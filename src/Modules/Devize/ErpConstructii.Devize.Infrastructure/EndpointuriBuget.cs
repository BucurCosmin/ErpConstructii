using ErpConstructii.Devize.Calcul;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ErpConstructii.Devize.Infrastructure;

public sealed record VersiuneBugetDto(Guid Id, string Tip, int Numar, string Denumire, string Stare);

/// <summary>Bugetul proiectului față de costurile reale; <paramref name="Versiune"/> null = proiectul nu are încă deviz.</summary>
public sealed record BugetProiectDto(VersiuneBugetDto? Versiune, IReadOnlyList<RandBuget> Articole, RezumatBuget Rezumat);

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

    private static async Task<IResult> Buget(Guid proiectId, DevizeDbContext db, IEnumerable<ISursaCosturi> surse, CancellationToken ct)
    {
        var costuri = await CosturiAsync(surse, proiectId, ct);
        var v = await InterogareDevize.ReferintaAsync(db, proiectId, ct);
        if (v is null)
            return Results.Ok(new BugetProiectDto(null, [], CalculBuget.Calculeaza([], costuri).Rezumat));

        // Bugetul articolului = valoarea liniei, cu rotunjirea versiunii (ca în recapitulație).
        var p = v.Parametri();
        var capitole = v.Capitole.ToDictionary(c => c.Id);
        var deviz = v.Articole().Select(a => new ArticolBugetDeviz(a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate,
            string.Join(" / ", v.Cale(capitole[a.CapitolId]).Select(c => c.Denumire)), CalculatorDeviz.ValoareLinie(a.CaLinie(), p.RotunjireLinie))).ToList();
        var buget = CalculBuget.Calculeaza(deviz, costuri);
        return Results.Ok(new BugetProiectDto(new VersiuneBugetDto(v.Id, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString()), buget.Articole, buget.Rezumat));
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
