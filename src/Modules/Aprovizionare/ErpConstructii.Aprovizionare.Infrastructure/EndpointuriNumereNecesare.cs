using System.Globalization;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Aprovizionare.Infrastructure;

public sealed record RenumerotareCerere(string? NumarNou, string? Motiv);

public sealed record RenumerotareDto(Guid Id, Guid DocumentId, string DocumentNumar, string? NumarVechi, string NumarNou, string Motiv, string Utilizator, DateTimeOffset La);

/// <param name="Tip">registru (o linie pe necesar) | detaliat (o linie pe material din fiecare necesar) | materiale (cantitățile adunate pe lucrare).</param>
/// <param name="Filtre">Descrierea filtrelor din ecran, tipărită pe raport.</param>
public sealed record RaportNecesareCerere(IReadOnlyList<Guid>? Id, string? Tip, string? Filtre);

/// <summary>
/// Numerele de pe hârtie ale necesarelor importate (D-48): verificarea pe ani (suprapuneri, numere sărite, documente fără număr),
/// renumerotarea cu motiv și istoric, plus rapoartele necesarelor alese (registru, detaliat, materiale adunate).
/// Numărul de sistem NM/AAAA/NNNN e dat automat și nu se schimbă.
/// </summary>
internal static class EndpointuriNumereNecesare
{
    private const int MaximRaport = 5000;
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Editare);
        g.MapGet("/necesare-materiale/numere", Verifica).WithName("VerificaNumereNecesare")
            .WithSummary("Numerele de pe hârtie ale necesarelor importate, pe ani: suprapuneri, numere sărite, fără număr, primul număr liber")
            .Produces<IReadOnlyList<VerificareNumereAn>>().CerePermisiune(vizualizare);
        g.MapGet("/necesare-materiale/renumerotari", Istoric).WithName("RenumerotariNecesare")
            .WithSummary("Istoricul renumerotărilor numerelor de pe hârtie").Produces<IReadOnlyList<RenumerotareDto>>().CerePermisiune(vizualizare);
        g.MapPost("/proiecte/{proiectId:guid}/necesare-materiale/{id:guid}/renumerotare", Renumeroteaza).WithName("RenumeroteazaNecesar")
            .WithSummary("Schimbă numărul de pe hârtie al unui necesar importat (cu motiv); numărul de sistem rămâne")
            .Produces<NecesarMaterialeDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).CerePermisiune(editare);
        g.MapPost("/necesare-materiale/raport/pdf", Raport).WithName("RaportNecesare")
            .WithSummary("Raportul necesarelor alese: registru, detaliat pe materiale sau materialele cerute (adunate pe lucrare)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status400BadRequest).CerePermisiune(vizualizare);
    }

    /// <summary>Necesarele importate (cu sau fără număr de hârtie), opțional dintr-o serie: proiectul și anul datei documentului.</summary>
    internal static async Task<List<DocumentCuNumarExtern>> DocumenteAsync(AprovizionareDbContext db, Guid? proiectId, int? an, CancellationToken ct)
    {
        var q = db.NecesareMateriale.AsNoTracking().Where(n => n.Sursa == SursaDocument.Import);
        if (proiectId is { } pid)
            q = q.Where(n => n.ProiectId == pid);
        if (an is { } a)
            q = q.Where(n => n.Data >= new DateOnly(a, 1, 1) && n.Data <= new DateOnly(a, 12, 31));
        return (await q.Select(n => new { n.Id, n.ProiectId, n.An, n.Numar, n.NumarExtern, n.Data, n.ProiectCod }).ToListAsync(ct))
            .Select(n => new DocumentCuNumarExtern(n.Id, n.ProiectId, $"NM/{n.An}/{n.Numar:D4}", n.NumarExtern, n.Data, n.ProiectCod))
            .ToList();
    }

    private static async Task<IResult> Verifica(
        AprovizionareDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (await AccesibileAsync(permisiuni, utilizator, firma, ct) is not { } acces)
            return Results.Unauthorized();
        // Seriile sunt pe proiect: utilizatorul vede doar seriile proiectelor lui.
        var verificare = NumerotareExterna.Verifica(await DocumenteAsync(db, null, null, ct));
        return Results.Ok(acces.Toate ? verificare : verificare.Where(v => acces.Proiecte.Contains(v.ProiectId)).ToList());
    }

    private static async Task<IResult> Istoric(AprovizionareDbContext db, CancellationToken ct) =>
        Results.Ok((await db.Renumerotari.AsNoTracking().OrderByDescending(r => r.La).Take(500).ToListAsync(ct)).Select(Dto).ToList());

    private static async Task<IResult> Renumeroteaza(
        Guid proiectId, Guid id, RenumerotareCerere cerere, AprovizionareDbContext db, IFirmaCurenta firma, IUtilizatorCurent utilizator, ICeas ceas,
        CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid)
            return Results.Unauthorized();
        var necesar = await db.NecesareMateriale.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (necesar is null)
            return Results.NotFound();

        var r = RenumerotareDocument.Creeaza(necesar.Id, necesar.NumarAfisat, necesar.NumarExtern, cerere.NumarNou, cerere.Motiv, uid, utilizator.Nume, ceas.Acum);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulAprovizionare.BlocheazaSecventaAsync(db, tranzactie, fid, necesar.Data.Year, ct);
        var folosit = NumerotareExterna.Folosit(r.Valoare.NumarNou, necesar.ProiectId, necesar.Data.Year,
            await DocumenteAsync(db, necesar.ProiectId, necesar.Data.Year, ct), necesar.Id);
        if (folosit.Count > 0)
        {
            return Results.Conflict(new
            {
                mesaj = $"Numărul {r.Valoare.NumarNou} e folosit deja pe proiectul {necesar.ProiectCod} în {necesar.Data.Year} de " +
                    $"{string.Join(", ", folosit.Select(d => $"{d.NumarSistem} din {d.Data:dd.MM.yyyy}"))}.",
            });
        }

        var schimbare = necesar.Renumeroteaza(r.Valoare);
        if (schimbare.Esec)
            return Results.BadRequest(new { mesaj = schimbare.Eroare.Mesaj });
        db.Renumerotari.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(MapareNecesarMateriale.LaDto(necesar));
    }

    private static async Task<IResult> Raport(
        RaportNecesareCerere cerere, AprovizionareDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (cerere.Id is not { Count: > 0 and <= MaximRaport } ids)
            return Results.BadRequest(new { mesaj = $"Alege între 1 și {MaximRaport} necesare." });
        var tip = (cerere.Tip ?? "registru").Trim().ToLowerInvariant();
        if (tip is not ("registru" or "detaliat" or "materiale"))
            return Results.BadRequest(new { mesaj = "Tipul raportului poate fi: registru, detaliat sau materiale." });
        if (await AccesibileAsync(permisiuni, utilizator, firma, ct) is not { } acces)
            return Results.Unauthorized();

        var alese = ids.Distinct().ToList();
        var q = db.NecesareMateriale.AsNoTracking().Where(n => alese.Contains(n.Id));
        if (!acces.Toate)
            q = q.Where(n => acces.Proiecte.Contains(n.ProiectId));
        var necesare = (await q.ToListAsync(ct))
            .OrderBy(n => n.ProiectCod, StringComparer.Ordinal).ThenBy(n => n.Data).ThenBy(n => n.An).ThenBy(n => n.Numar).ToList();
        if (necesare.Count == 0)
            return Results.BadRequest(new { mesaj = "Necesarele alese nu există." });

        var f = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa.Formatata());
        var proiecte = necesare.Select(n => n.ProiectCod).Distinct().ToList();
        var campuri = new Dictionary<string, string>
        {
            ["perioada"] = $"{Data(necesare.Min(n => n.Data))} – {Data(necesare.Max(n => n.Data))}",
            ["proiecte"] = proiecte.Count <= 6 ? string.Join(", ", proiecte) : $"{proiecte.Count} lucrări",
            ["necesare"] = $"{necesare.Count} ({necesare.Count(n => n.Sursa == SursaDocument.Sistem)} emise în aplicație, {necesare.Count(n => n.Sursa == SursaDocument.Import)} de pe hârtie)",
            ["observatii"] = string.IsNullOrWhiteSpace(cerere.Filtre) ? string.Empty : $"Filtre: {cerere.Filtre.Trim()}",
        };
        ElementAntet[] elemente = [new("Perioada", "perioada"), new("Lucrări", "proiecte"), new("Necesare", "necesare")];

        List<IReadOnlyDictionary<string, string>> linii;
        List<ColoanaPdf> coloane;
        string titlu;
        if (tip == "registru")
        {
            titlu = "Registrul necesarelor de materiale";
            linii = necesare.Select((n, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("proiect", n.ProiectCod), ("numar", n.NumarAfisat), ("numar_extern", n.NumarExtern ?? string.Empty),
                ("data", Data(n.Data)), ("santier", n.CompartimentSantier), ("stare", Stare(n)), ("materiale", n.Linii.Count.ToString(Ro)),
                ("intocmit", n.EmitentNume), ("aprobat", n.Stare == StareNecesarMateriale.Aprobata ? n.DecisaDeNume ?? string.Empty : string.Empty))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.2), new("numar", "Necesar", 1.4),
                new("numar_extern", "Nr. hârtie", 0.9), new("data", "Data", 1.0, AliniereColoana.Centru), new("santier", "Șantier", 2.0),
                new("stare", "Stare", 1.7), new("materiale", "Poz.", 0.6, AliniereColoana.Dreapta), new("intocmit", "Întocmit", 1.7), new("aprobat", "Aprobat", 1.7),
            ];
        }
        else if (tip == "detaliat")
        {
            titlu = "Necesarele de materiale — materialele din fiecare document";
            var i = 0;
            linii = necesare.SelectMany(n => n.Linii.Select(l => Rand(
                ("numar_crt", (++i).ToString(Ro)), ("proiect", n.ProiectCod), ("numar", n.NumarAfisat), ("numar_extern", n.NumarExtern ?? string.Empty),
                ("data", Data(n.Data)), ("material", l.Denumire), ("um", l.UnitateMasura), ("cantitate", Cant(l.Cantitate)),
                ("observatii_linie", l.Observatii ?? string.Empty)))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.1), new("numar", "Necesar", 1.3),
                new("numar_extern", "Nr. hârtie", 0.8), new("data", "Data", 0.9, AliniereColoana.Centru), new("material", "Material", 4.0),
                new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.0, AliniereColoana.Dreapta), new("observatii_linie", "Observații", 2.2),
            ];
        }
        else
        {
            titlu = "Materiale cerute pe lucrări (cantități adunate)";
            linii = RaportNecesare.Materiale(necesare).Select((m, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("proiect", m.ProiectCod), ("material", m.Denumire), ("um", m.UnitateMasura),
                ("cantitate", Cant(m.Cantitate)), ("documente", string.Join(", ", m.Documente)),
                ("perioada", m.PrimaData == m.UltimaData ? Data(m.PrimaData) : $"{Data(m.PrimaData)} – {Data(m.UltimaData)}"))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.1), new("material", "Material", 3.6),
                new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate totală", 1.2, AliniereColoana.Dreapta),
                new("documente", "Necesare", 3.6), new("perioada", "Perioada", 1.8),
            ];
        }

        var aspect = new AspectDocument(titlu, null, $"Întocmit la {Data(ceas.Azi)}", elemente, "linii", coloane, [], ["Întocmit"], "observatii", Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync($"raport-necesare-{tip}", antet, aspect, date, $"Necesare_{tip}.pdf", ct);
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

    internal static RenumerotareDto Dto(RenumerotareDocument r) =>
        new(r.Id, r.DocumentId, r.DocumentNumar, r.NumarVechi, r.NumarNou, r.Motiv, r.UtilizatorNume, r.La);

    private static string Stare(NecesarMateriale n) => n.Stare switch
    {
        StareNecesarMateriale.Aprobata => n.DinUrma ? "aprobat (din urmă)" : "aprobat",
        StareNecesarMateriale.Respinsa => "respins",
        _ => "în așteptare",
    };

    private static IReadOnlyDictionary<string, string> Rand(params (string Camp, string Valoare)[] valori) => valori.ToDictionary(v => v.Camp, v => v.Valoare);

    private static string Data(DateOnly d) => d.ToString("dd.MM.yyyy", Ro);

    private static string Cant(decimal v) => v.ToString("#,##0.####", Ro);
}
