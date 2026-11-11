using System.Globalization;
using ErpConstructii.Aprovizionare;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

public sealed record RenumerotareBonCerere(string? NumarNou, string? Motiv);

/// <summary>Primul număr de hârtie liber din seria unui proiect (proiect + an).</summary>
public sealed record NumarLiberBonDto(long Numar, int An);

public sealed record RenumerotareBonDto(Guid Id, Guid DocumentId, string DocumentNumar, string? NumarVechi, string NumarNou, string Motiv, string Utilizator, DateTimeOffset La);

/// <param name="Tip">registru (o linie pe bon) | detaliat (o linie pe material din fiecare bon) | materiale (cantitățile adunate pe lucrare).</param>
/// <param name="Filtre">Descrierea filtrelor din ecran, tipărită pe raport.</param>
public sealed record RaportBonuriCerere(IReadOnlyList<Guid>? Id, string? Tip, string? Filtre);

/// <summary>
/// Numerele de pe hârtie ale bonurilor de consum (D-48): verificarea pe ani (suprapuneri, numere sărite), renumerotarea cu
/// motiv și istoric, plus rapoartele bonurilor alese. Numărul de sistem BC/AAAA/NNNN și mișcările de stoc nu se schimbă.
/// </summary>
internal static class EndpointuriNumereBonuri
{
    private const int MaximRaport = 5000;
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Editare);
        g.MapGet("/consumuri/numere", Verifica).WithName("VerificaNumereBonuri")
            .WithSummary("Numerele de pe hârtie ale bonurilor de consum, pe ani: suprapuneri, numere sărite, primul număr liber")
            .Produces<IReadOnlyList<VerificareNumereAn>>().CerePermisiune(vizualizare);
        g.MapGet("/proiecte/{proiectId:guid}/consumuri/numar-liber", NumarLiber).WithName("NumarLiberBonConsum")
            .WithSummary("Primul număr de hârtie liber pe proiect, în anul dat (implicit anul curent): propunerea pentru un bon fără număr")
            .Produces<NumarLiberBonDto>().CerePermisiune(vizualizare);
        g.MapGet("/consumuri/renumerotari", Istoric).WithName("RenumerotariBonuri")
            .WithSummary("Istoricul renumerotărilor bonurilor de consum").Produces<IReadOnlyList<RenumerotareBonDto>>().CerePermisiune(vizualizare);
        g.MapPost("/proiecte/{proiectId:guid}/consumuri/{id:guid}/renumerotare", Renumeroteaza).WithName("RenumeroteazaBonConsum")
            .WithSummary("Schimbă numărul de pe hârtie al unui bon de consum (cu motiv); numărul de sistem rămâne")
            .Produces<BonConsumDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).CerePermisiune(editare);
        g.MapPost("/consumuri/raport/pdf", Raport).WithName("RaportBonuriConsum")
            .WithSummary("Raportul bonurilor de consum alese: registru, detaliat pe materiale sau materialele consumate (adunate pe lucrare)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status400BadRequest).CerePermisiune(vizualizare);
    }

    /// <summary>Bonurile cu număr de hârtie, opțional dintr-o serie: proiectul și anul datei bonului.</summary>
    internal static async Task<List<DocumentCuNumarExtern>> DocumenteAsync(StocuriDbContext db, Guid? proiectId, int? an, CancellationToken ct)
    {
        var q = db.BonuriConsum.AsNoTracking().Where(b => b.NumarExtern != null);
        if (proiectId is { } pid)
            q = q.Where(b => b.ProiectId == pid);
        if (an is { } a)
            q = q.Where(b => b.Data >= new DateOnly(a, 1, 1) && b.Data <= new DateOnly(a, 12, 31));
        return (await q.Select(b => new { b.Id, b.ProiectId, b.An, b.Numar, b.NumarExtern, b.Data, b.ProiectCod }).ToListAsync(ct))
            .Select(b => new DocumentCuNumarExtern(b.Id, b.ProiectId, $"BC/{b.An}/{b.Numar:D4}", b.NumarExtern, b.Data, b.ProiectCod))
            .ToList();
    }

    /// <summary>Bonul înregistrat deja cu același număr de hârtie și aceeași dată (alt fișier, aceeași hârtie).</summary>
    internal static async Task<string?> BonDublatAsync(StocuriDbContext db, string? numarExtern, DateOnly? data, CancellationToken ct)
    {
        if (ClasificarePagini.CheieNumar(numarExtern) is null || data is null)
            return null;
        var existente = await db.BonuriConsum.AsNoTracking().Where(b => b.NumarExtern != null && b.Data == data)
            .Select(b => new { b.An, b.Numar, b.NumarExtern, b.Data, b.ProiectCod }).ToListAsync(ct);
        return DocumentDublat.DupaNumarSiData(numarExtern, data,
            existente.Select(b => new DocumentImportat($"BC/{b.An}/{b.Numar:D4} pe proiectul {b.ProiectCod}", b.NumarExtern, b.Data)))?.Descriere;
    }

    private static async Task<IResult> Verifica(
        StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (await AccesibileAsync(permisiuni, utilizator, firma, ct) is not { } acces)
            return Results.Unauthorized();
        var verificare = NumerotareExterna.Verifica(await DocumenteAsync(db, null, null, ct));
        return Results.Ok(acces.Toate ? verificare : verificare.Where(v => acces.Proiecte.Contains(v.ProiectId)).ToList());
    }

    private static async Task<IResult> NumarLiber(Guid proiectId, int? an, StocuriDbContext db, ICeas ceas, CancellationToken ct)
    {
        var a = an is >= 2000 and <= 2100 ? an.Value : ceas.Azi.Year;
        var serie = await DocumenteAsync(db, proiectId, a, ct);
        return Results.Ok(new NumarLiberBonDto(NumerotareExterna.NumarLiber(serie.Select(d => d.NumarExtern)), a));
    }

    private static async Task<IResult> Istoric(StocuriDbContext db, CancellationToken ct) =>
        Results.Ok((await db.Renumerotari.AsNoTracking().OrderByDescending(r => r.La).Take(500).ToListAsync(ct))
            .Select(r => new RenumerotareBonDto(r.Id, r.DocumentId, r.DocumentNumar, r.NumarVechi, r.NumarNou, r.Motiv, r.UtilizatorNume, r.La)).ToList());

    private static async Task<IResult> Renumeroteaza(
        Guid proiectId, Guid id, RenumerotareBonCerere cerere, StocuriDbContext db, IServiciuPermisiuni permisiuni, IFirmaCurenta firma,
        IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid)
            return Results.Unauthorized();
        var bon = await db.BonuriConsum.FirstOrDefaultAsync(b => b.Id == id && b.ProiectId == proiectId, ct);
        if (bon is null)
            return Results.NotFound();

        var r = RenumerotareDocument.Creeaza(bon.Id, bon.NumarAfisat, bon.NumarExtern, cerere.NumarNou, cerere.Motiv, uid, utilizator.Nume, ceas.Acum);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var folosit = NumerotareExterna.Folosit(r.Valoare.NumarNou, bon.ProiectId, bon.Data.Year, await DocumenteAsync(db, bon.ProiectId, bon.Data.Year, ct), bon.Id);
        if (folosit.Count > 0)
        {
            return Results.Conflict(new
            {
                mesaj = $"Numărul {r.Valoare.NumarNou} e folosit deja pe proiectul {bon.ProiectCod} în {bon.Data.Year} de " +
                    $"{string.Join(", ", folosit.Select(d => $"{d.NumarSistem} din {d.Data:dd.MM.yyyy}"))}.",
            });
        }

        var schimbare = bon.Renumeroteaza(r.Valoare);
        if (schimbare.Esec)
            return Results.BadRequest(new { mesaj = schimbare.Eroare.Mesaj });
        db.Renumerotari.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(EndpointuriConsum.Dto(bon, await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct)));
    }

    private static async Task<IResult> Raport(
        RaportBonuriCerere cerere, StocuriDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (cerere.Id is not { Count: > 0 and <= MaximRaport } ids)
            return Results.BadRequest(new { mesaj = $"Alege între 1 și {MaximRaport} bonuri de consum." });
        var tip = (cerere.Tip ?? "registru").Trim().ToLowerInvariant();
        if (tip is not ("registru" or "detaliat" or "materiale"))
            return Results.BadRequest(new { mesaj = "Tipul raportului poate fi: registru, detaliat sau materiale." });
        if (await AccesibileAsync(permisiuni, utilizator, firma, ct) is not { } acces)
            return Results.Unauthorized();
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);

        var alese = ids.Distinct().ToList();
        var q = db.BonuriConsum.AsNoTracking().Where(b => alese.Contains(b.Id));
        if (!acces.Toate)
            q = q.Where(b => acces.Proiecte.Contains(b.ProiectId));
        var bonuri = (await q.ToListAsync(ct))
            .OrderBy(b => b.ProiectCod, StringComparer.Ordinal).ThenBy(b => b.Data).ThenBy(b => b.An).ThenBy(b => b.Numar).ToList();
        if (bonuri.Count == 0)
            return Results.BadRequest(new { mesaj = "Bonurile de consum alese nu există." });

        var f = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa?.Formatata());
        var proiecte = bonuri.Select(b => b.ProiectCod).Distinct().ToList();
        var campuri = new Dictionary<string, string>
        {
            ["perioada"] = $"{Data(bonuri.Min(b => b.Data))} – {Data(bonuri.Max(b => b.Data))}",
            ["proiecte"] = proiecte.Count <= 6 ? string.Join(", ", proiecte) : $"{proiecte.Count} lucrări",
            ["bonuri"] = $"{bonuri.Count} ({bonuri.Count(b => b.NumarExtern != null)} cu număr de hârtie)",
            ["total_valoare"] = preturi ? Lei(bonuri.Sum(b => b.Valoare)) : string.Empty,
            ["observatii"] = string.IsNullOrWhiteSpace(cerere.Filtre) ? string.Empty : $"Filtre: {cerere.Filtre.Trim()}",
        };
        ElementAntet[] elemente = [new("Perioada", "perioada"), new("Lucrări", "proiecte"), new("Bonuri de consum", "bonuri")];

        List<IReadOnlyDictionary<string, string>> linii;
        List<ColoanaPdf> coloane;
        string titlu;
        if (tip == "registru")
        {
            titlu = "Registrul bonurilor de consum";
            linii = bonuri.Select((b, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("proiect", b.ProiectCod), ("numar", b.NumarAfisat), ("numar_extern", b.NumarExtern ?? string.Empty),
                ("data", Data(b.Data)), ("materiale", b.Linii.Count.ToString(Ro)), ("valoare", preturi ? Lei(b.Valoare) : string.Empty),
                ("emis", b.EmisDeNume), ("observatii_bon", b.Observatii ?? string.Empty))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.2), new("numar", "Bon", 1.4),
                new("numar_extern", "Nr. hârtie", 0.9), new("data", "Data", 1.0, AliniereColoana.Centru), new("materiale", "Poz.", 0.6, AliniereColoana.Dreapta),
                new("valoare", "Valoare (lei)", 1.3, AliniereColoana.Dreapta), new("emis", "Emis de", 1.7), new("observatii_bon", "Observații", 3.6),
            ];
        }
        else if (tip == "detaliat")
        {
            titlu = "Bonurile de consum — materialele din fiecare bon";
            var i = 0;
            linii = bonuri.SelectMany(b => b.Linii.Select(l => Rand(
                ("numar_crt", (++i).ToString(Ro)), ("proiect", b.ProiectCod), ("numar", b.NumarAfisat), ("numar_extern", b.NumarExtern ?? string.Empty),
                ("data", Data(b.Data)), ("cod", l.ArticolCod), ("material", l.Denumire), ("um", l.UnitateMasura), ("cantitate", Cant(l.Cantitate)),
                ("valoare", preturi ? Lei(l.Valoare) : string.Empty),
                ("articol", l.ArticolDevizSimbol is { } s ? s : "chelt. generale")))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.1), new("numar", "Bon", 1.3),
                new("numar_extern", "Nr. hârtie", 0.8), new("data", "Data", 0.9, AliniereColoana.Centru), new("cod", "Cod", 1.0), new("material", "Material", 3.4),
                new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.0, AliniereColoana.Dreapta),
                new("valoare", "Valoare (lei)", 1.1, AliniereColoana.Dreapta), new("articol", "Art. deviz", 1.2),
            ];
        }
        else
        {
            titlu = "Materiale consumate pe lucrări (cantități adunate)";
            linii = RaportBonuriConsum.Materiale(bonuri).Select((m, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("proiect", m.ProiectCod), ("cod", m.Cod), ("material", m.Denumire), ("um", m.UnitateMasura),
                ("cantitate", Cant(m.Cantitate)), ("valoare", preturi ? Lei(m.Valoare) : string.Empty), ("documente", string.Join(", ", m.Documente)),
                ("perioada", m.PrimaData == m.UltimaData ? Data(m.PrimaData) : $"{Data(m.PrimaData)} – {Data(m.UltimaData)}"))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.1), new("cod", "Cod", 1.0), new("material", "Material", 3.2),
                new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate totală", 1.2, AliniereColoana.Dreapta),
                new("valoare", "Valoare (lei)", 1.2, AliniereColoana.Dreapta), new("documente", "Bonuri", 3.0), new("perioada", "Perioada", 1.7),
            ];
        }

        ElementAntet[] totaluri = preturi ? [new("Total valoare (lei):", "total_valoare")] : [];
        var aspect = new AspectDocument(titlu, null, $"Întocmit la {Data(ceas.Azi)}", elemente, "linii", coloane, totaluri, ["Întocmit"], "observatii", Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync($"raport-bonuri-consum-{tip}", antet, aspect, date, $"Bonuri_consum_{tip}.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private sealed record Acces(bool Toate, List<Guid> Proiecte);

    private static async Task<Acces?> AccesibileAsync(IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return null;
        var accesibile = (await permisiuni.PentruUtilizatorAsync(uid, ct)).ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);
        return accesibile is null ? new Acces(true, []) : new Acces(false, accesibile.ToList());
    }

    private static IReadOnlyDictionary<string, string> Rand(params (string Camp, string Valoare)[] valori) => valori.ToDictionary(v => v.Camp, v => v.Valoare);

    private static string Data(DateOnly d) => d.ToString("dd.MM.yyyy", Ro);

    private static string Lei(decimal v) => v.ToString("N2", Ro);

    private static string Cant(decimal v) => v.ToString("#,##0.####", Ro);
}
