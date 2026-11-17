using ErpConstructii.Devize.Calcul;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

public sealed record VersiuneBugetDto(Guid Id, string Tip, int Numar, string Denumire, string Stare);

/// <summary>Devizul de referință al unui obiectiv (D-59).</summary>
public sealed record ReferintaBugetDto(Guid ObiectivId, string Obiectiv, VersiuneBugetDto Versiune);

/// <summary>
/// Bugetul proiectului (sau al unui obiectiv, <paramref name="ObiectivId"/>) față de costurile reale; <paramref name="Versiune"/> null =
/// fără deviz. Pe un obiectiv intră doar costurile pe articolele lui: cheltuielile generale, serviciile și materialele cumpărate
/// sunt ale proiectului.
/// </summary>
public sealed record BugetProiectDto(VersiuneBugetDto? Versiune, IReadOnlyList<RandBuget> Articole, RezumatBuget Rezumat,
    IReadOnlyList<ReferintaBugetDto>? Referinte = null, IReadOnlyList<ObiectivDto>? Obiective = null, Guid? ObiectivId = null,
    IReadOnlyList<DevizBugetDto>? Devize = null, Guid? VersiuneId = null);

/// <summary>O versiune de deviz a proiectului, de ales în ecranul bugetului (oricare, nu doar cea de referință).</summary>
public sealed record DevizBugetDto(Guid Id, Guid ObiectivId, string Tip, int Numar, string Denumire, string Stare);

/// <summary>Un document care a adus costuri (pentru detaliul unui rând al bugetului).</summary>
public sealed record CostDto(DateOnly Data, string Natura, string? Componenta, string Document, Guid? DocumentId, string Descriere, decimal Valoare);

/// <summary>
/// Bugetul proiectului (fișa proiectului → Buget): devizul de referință cu bugetul pe articole și resurse, față de costurile
/// reale aduse de celelalte module prin <see cref="ISursaCosturi"/> (Stocuri: materialele consumate pe articole; Facturi:
/// materialele cumpărate și serviciile facturate pe proiect). Calculul: <see cref="CalculBuget"/> (pur).
/// </summary>
internal static class EndpointuriBuget
{
    public static void Mapeaza(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/proiecte/{proiectId:guid}/buget").WithTags("Buget");
        var vizualizare = Permisiuni.Pentru(ModulErp.Buget, Actiune.Vizualizare);

        g.MapGet("/", Buget).WithName("BugetProiect")
            .WithSummary("Bugetul pe articolele devizului de referință față de costurile reale (consum pe articole, cheltuieli generale, servicii, materiale cumpărate)")
            .Produces<BugetProiectDto>().CerePermisiune(vizualizare).CerePermisiune(Permisiuni.PreturiCost);
        g.MapGet("/costuri", Costuri).WithName("CosturiBuget")
            .WithSummary("Documentele cu costuri: ?articol=<cheie stabilă> | generale | servicii | materiale (fără parametru: toate)")
            .Produces<IReadOnlyList<CostDto>>().CerePermisiune(vizualizare).CerePermisiune(Permisiuni.PreturiCost);
    }

    private static async Task<IReadOnlyList<CostRealizat>> CosturiAsync(IEnumerable<ISursaCosturi> surse, Guid proiectId, CancellationToken ct)
    {
        var toate = new List<CostRealizat>();
        foreach (var s in surse)
            toate.AddRange(await s.CosturiAsync(proiectId, ct));
        return toate;
    }

    /// <param name="obiectivId">Doar obiectivul acesta; fără el, toate obiectivele proiectului (D-59).</param>
    /// <param name="versiuneId">Un deviz anume al proiectului (oricare versiune), în locul referinței.</param>
    private static async Task<IResult> Buget(Guid proiectId, Guid? obiectivId, Guid? versiuneId, DevizeDbContext db, IEnumerable<ISursaCosturi> surse,
        CancellationToken ct)
    {
        var costuri = await CosturiAsync(surse, proiectId, ct);
        var obiective = await EndpointuriObiective.ObiectiveAsync(db, proiectId, ct);
        var ordine = obiective.Select((o, i) => (o.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var devize = (await db.Versiuni.AsNoTracking().Where(v => v.ProiectId == proiectId)
                .Select(v => new { v.Id, v.ObiectivId, v.Tip, v.Numar, v.Denumire, v.Stare }).ToListAsync(ct))
            .OrderBy(v => ordine.GetValueOrDefault(v.ObiectivId, int.MaxValue)).ThenByDescending(v => v.Numar)
            .Select(v => new DevizBugetDto(v.Id, v.ObiectivId, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString())).ToList();

        IReadOnlyList<(ObiectivDeviz Obiectiv, VersiuneDeviz Versiune)> referinte;
        if (versiuneId is { } vid)
        {
            var aleasa = await ModulDevize.IncarcaAsync(db, vid, ct);
            var obiectiv = aleasa is null ? null : await db.Obiective.AsNoTracking().FirstOrDefaultAsync(o => o.Id == aleasa.ObiectivId, ct);
            if (aleasa is null || aleasa.ProiectId != proiectId || obiectiv is null)
                return Results.NotFound(new { mesaj = "Devizul nu există în proiect." });
            referinte = [(obiectiv, aleasa)];
            obiectivId = obiectiv.Id;
        }
        else
        {
            referinte = await InterogareDevize.ReferinteAsync(db, proiectId, obiectivId, ct);
        }

        if (referinte.Count == 0)
            return Results.Ok(new BugetProiectDto(null, [], CalculBuget.Calculeaza([], obiectivId is null ? costuri : []).Rezumat, [], obiective, obiectivId,
                devize, versiuneId));

        // Bugetul articolului = valoarea liniei, cu rotunjirea versiunii (ca în recapitulație). Cu mai multe obiective, capitolul
        // începe cu obiectivul.
        var multe = obiectivId is null && referinte.Count > 1;
        var deviz = new List<ArticolBugetDeviz>();
        foreach (var (o, v) in referinte)
        {
            var p = v.Parametri();
            var capitole = v.Capitole.ToDictionary(c => c.Id);
            deviz.AddRange(v.Articole().Select(a =>
            {
                // Calea în arbore: obiectivul (când sunt mai multe), apoi capitolele de la rădăcină.
                var cale = (multe ? [o.Denumire] : Array.Empty<string>()).Concat(v.Cale(capitole[a.CapitolId]).Select(c => c.Denumire)).ToList();
                return new ArticolBugetDeviz(a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, string.Join(" / ", cale),
                    CalculatorDeviz.ValoareLinie(a.CaLinie(), p.RotunjireLinie), cale);
            }));
        }

        // Pe un obiectiv: doar costurile pe articolele lui.
        var chei = deviz.Select(a => a.CheieStabila).ToHashSet();
        var alese = obiectivId is null ? costuri : costuri.Where(c => c.ArticolDevizCheie is { } k && chei.Contains(k)).ToList();
        var buget = CalculBuget.Calculeaza(deviz, alese);
        var dto = referinte.Select(r => new ReferintaBugetDto(r.Obiectiv.Id, r.Obiectiv.Denumire,
            new VersiuneBugetDto(r.Versiune.Id, r.Versiune.Tip.ToString(), r.Versiune.Numar, r.Versiune.Denumire, r.Versiune.Stare.ToString()))).ToList();
        return Results.Ok(new BugetProiectDto(dto[0].Versiune, buget.Articole, buget.Rezumat, dto, obiective, obiectivId, devize, versiuneId));
    }

    private static async Task<IResult> Costuri(Guid proiectId, string? articol, IEnumerable<ISursaCosturi> surse, CancellationToken ct)
    {
        var costuri = await CosturiAsync(surse, proiectId, ct);
        IEnumerable<CostRealizat> alese = articol switch
        {
            null or "" => costuri,
            "generale" => costuri.Where(c => c.Natura == NaturaCost.Consum && c.ArticolDevizCheie is null),
            "servicii" => costuri.Where(c => c.Natura == NaturaCost.Serviciu),
            "materiale" => costuri.Where(c => c.Natura == NaturaCost.AchizitieMateriale),
            _ when Guid.TryParse(articol, out var cheie) => costuri.Where(c => c.ArticolDevizCheie == cheie),
            _ => [],
        };
        return Results.Ok(alese.OrderByDescending(c => c.Data)
            .Select(c => new CostDto(c.Data, c.Natura.ToString(), c.Componenta?.ToString(), c.Document, c.DocumentId, c.Descriere, c.Valoare)).ToList());
    }
}
