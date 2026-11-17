using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Grafic.Infrastructure;

/// <param name="Surse">Ce module dau documente (Factura, BonConsum); lipsă = modulul nu e instalat.</param>
public sealed record CosturiGraficDto(
    string Moneda, Guid? VersiuneId, CostTinta Total, CostTinta FaraTinta, IReadOnlyDictionary<string, CostTinta> PeActivitate,
    IReadOnlyDictionary<string, CostTinta> PeNod, IReadOnlyList<StareDocumentCost> Documente, IReadOnlyList<CostAlocat> Alocari,
    IReadOnlyList<SursaDocumentCost> Surse);

public sealed record AlocareDocumentCerere(SursaDocumentCost Sursa, Guid DocumentId, IReadOnlyList<AlocareCeruta> Alocari);

/// <summary>Raportul „valoare în grafic vs cost ERP” (D-57, etapa 3) pe actualizarea curentă.</summary>
public sealed record BugetGraficDto(string Moneda, string Grafic, int? Actualizare, DateOnly? DataActualizare, RaportBugetGrafic Raport);

public sealed record DocumentAles(SursaDocumentCost Sursa, Guid DocumentId);

/// <summary>Tot documentul (liniile lui de cost direct, cât nu e încă alocat) pe o singură țintă, pentru mai multe documente odată.</summary>
public sealed record AlocareInMasaCerere(IReadOnlyList<DocumentAles> Documente, TipTinta Tip, string Cheie);

/// <summary>
/// Costurile ERP pe graficul de execuție (D-57, etapa 2): documentele proiectului (facturi, bonuri de consum, prin
/// <see cref="ISursaDocumenteCost"/>), alocările lor pe activități / noduri WBS, valorile în lei și în moneda graficului la
/// cursul BNR din data documentului (<see cref="ICursValutar"/>), totalizate pe arbore (<see cref="CalculCosturiGrafic"/>).
/// Drepturi: <c>buget.vizualizare</c> / <c>buget.editare</c> pe proiect, plus <c>date.preturi_cost</c>.
/// </summary>
internal static class EndpointuriCosturi
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Buget, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Buget, Actiune.Editare);

        g.MapGet("/{graficId:guid}/costuri", Costuri).WithName("CosturiGrafic")
            .WithSummary("Costurile ERP alocate pe activitățile / nodurile graficului (lei și moneda graficului), documentele proiectului cu starea alocării și propunerile")
            .Produces<CosturiGraficDto>().Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare).CerePermisiune(Permisiuni.PreturiCost);
        g.MapPut("/{graficId:guid}/costuri/alocari", Aloca).WithName("AlocaDocumentGrafic")
            .WithSummary("Înlocuiește alocările unui document (pe linii, procentual) pe activități / noduri WBS")
            .Produces<StareDocumentCost>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare).CerePermisiune(Permisiuni.PreturiCost);
        g.MapPost("/{graficId:guid}/costuri/alocare-in-masa", AlocaInMasa).WithName("AlocaInMasaGrafic")
            .WithSummary("Alocă restul nealocat al documentelor alese pe o singură activitate / nod WBS; întoarce câte documente s-au schimbat")
            .Produces<int>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare).CerePermisiune(Permisiuni.PreturiCost);

        // Valoarea din grafic față de costurile ERP (D-57, etapa 3).
        g.MapGet("/{graficId:guid}/costuri/buget", Buget).WithName("BugetGrafic")
            .WithSummary("Valoarea planificată / executată din grafic față de costurile ERP alocate, pe arborele WBS: abatere, indice de cost, prognoză, alerte")
            .Produces<BugetGraficDto>().Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare).CerePermisiune(Permisiuni.PreturiCost);
        g.MapGet("/{graficId:guid}/costuri/buget.xlsx", BugetExcel).WithName("BugetGraficExcel")
            .WithSummary("Raportul buget / cost ca fișier Excel (arborele cu niveluri de grupare)")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare).CerePermisiune(Permisiuni.PreturiCost);
        g.MapGet("/{graficId:guid}/costuri/buget.pdf", BugetPdf).WithName("BugetGraficPdf")
            .WithSummary("Raportul buget / cost în PDF: nodurile până la nivelul ales (fără nivel: tot, cu activitățile), opțional doar alertele")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare).CerePermisiune(Permisiuni.PreturiCost);
    }

    private sealed record ContextBuget(GraficExecutie Grafic, VersiuneGrafic? Versiune, RaportBugetGrafic Raport);

    private static async Task<ContextBuget?> BugetAsync(
        Guid proiectId, Guid graficId, decimal? prag, GraficDbContext db, IEnumerable<ISursaDocumenteCost> surse, ICursValutar curs, CancellationToken ct)
    {
        if (await ContextAsync(proiectId, graficId, db, surse, ct) is not { } c)
            return null;
        var costuri = await CalculeazaAsync(c, db, curs, ct);
        var vid = c.Grafic.VersiuneCurentaId;
        var versiune = vid is null ? null : await db.Versiuni.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vid, ct);
        var noduri = await db.Noduri.AsNoTracking().Where(n => n.VersiuneId == vid).OrderBy(n => n.Ordine).ToListAsync(ct);
        var activitati = await db.Activitati.AsNoTracking().Where(a => a.VersiuneId == vid).OrderBy(a => a.Start).ThenBy(a => a.Cod).ToListAsync(ct);
        var p = prag is > 0 and < 10 ? prag.Value : BugetGrafic.PragImplicit;
        return new ContextBuget(c.Grafic, versiune,
            BugetGrafic.Construieste(noduri.Select(n => n.Canonic()).ToList(), activitati.Select(a => a.Canonic()).ToList(), costuri, p));
    }

    private static string Actualizare(VersiuneGrafic? v) => v is null ? "—" : $"actualizarea nr. {v.Numar} din {v.DataActualizare:dd.MM.yyyy}";

    private static string Fisier(GraficExecutie g, string extensie) =>
        $"Buget_cost_{new string((g.CodSursa ?? g.Denumire).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray())}.{extensie}";

    private static async Task<IResult> Buget(
        Guid proiectId, Guid graficId, decimal? prag, GraficDbContext db, IEnumerable<ISursaDocumenteCost> surse, ICursValutar curs, CancellationToken ct) =>
        await BugetAsync(proiectId, graficId, prag, db, surse, curs, ct) is { } b
            ? Results.Ok(new BugetGraficDto(b.Grafic.Moneda ?? "RON", b.Grafic.Denumire, b.Versiune?.Numar, b.Versiune?.DataActualizare, b.Raport))
            : Results.NotFound();

    private static async Task<IResult> BugetExcel(
        Guid proiectId, Guid graficId, decimal? prag, GraficDbContext db, IEnumerable<ISursaDocumenteCost> surse, ICursValutar curs, ICeas ceas,
        CancellationToken ct)
    {
        if (await BugetAsync(proiectId, graficId, prag, db, surse, curs, ct) is not { } b)
            return Results.NotFound();
        var continut = ExportBugetGrafic.Excel(b.Raport, b.Grafic.Denumire, Actualizare(b.Versiune), b.Grafic.Moneda ?? "RON", ceas.Acum);
        return Results.File(continut, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Fisier(b.Grafic, "xlsx"));
    }

    private static async Task<IResult> BugetPdf(
        Guid proiectId, Guid graficId, decimal? prag, int? nivel, bool? doarAlerte, GraficDbContext db, IEnumerable<ISursaDocumenteCost> surse,
        ICursValutar curs, IServiciuDocumente documente, IInterogareFirma firme, ErpConstructii.Proiecte.IInterogareProiecte proiecte, ICeas ceas,
        CancellationToken ct)
    {
        if (await BugetAsync(proiectId, graficId, prag, db, surse, curs, ct) is not { } b)
            return Results.NotFound();
        var randuri = ExportBugetGrafic.Filtreaza(b.Raport, nivel is > 0 ? nivel : null, doarAlerte == true);
        var selectie = (nivel is > 0 ? $"structura WBS până la nivelul {nivel}" : "tot graficul, cu activitățile") + (doarAlerte == true ? ", doar alertele" : string.Empty);
        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        var f = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa.Formatata());
        var (aspect, date) = ExportBugetGrafic.Pdf(b.Raport, randuri, b.Grafic.Denumire, proiect is null ? string.Empty : $"{proiect.Cod} — {proiect.Denumire}",
            Actualizare(b.Versiune), b.Grafic.Moneda ?? "RON", selectie, ceas.Acum);
        var doc = await documente.PdfAsync("raport-buget-grafic", antet, aspect, date, Fisier(b.Grafic, "pdf"), ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    /// <summary>Graficul, structura actualizării curente și documentele proiectului.</summary>
    private sealed record Context(GraficExecutie Grafic, StructuraGrafic Structura, IReadOnlyList<DocumentCost> Documente, IReadOnlyList<SursaDocumentCost> Surse);

    private static async Task<Context?> ContextAsync(Guid proiectId, Guid graficId, GraficDbContext db, IEnumerable<ISursaDocumenteCost> surse, CancellationToken ct)
    {
        var grafic = await db.Grafice.AsNoTracking().FirstOrDefaultAsync(x => x.Id == graficId && x.ProiectId == proiectId, ct);
        if (grafic is null)
            return null;
        var versiune = grafic.VersiuneCurentaId;
        var noduri = await db.Noduri.AsNoTracking().Where(n => n.VersiuneId == versiune).Select(n => new { n.Cheie, n.CheieParinte, n.Denumire }).ToListAsync(ct);
        var activitati = await db.Activitati.AsNoTracking().Where(a => a.VersiuneId == versiune).Select(a => new { a.Cod, a.CheieWbs, a.Denumire }).ToListAsync(ct);
        var structura = new StructuraGrafic(
            noduri.ToDictionary(n => n.Cheie, n => n.CheieParinte, StringComparer.Ordinal),
            activitati.ToDictionary(a => a.Cod, a => a.CheieWbs, StringComparer.Ordinal),
            noduri.Select(n => ("w:" + n.Cheie, n.Denumire)).Concat(activitati.Select(a => ("a:" + a.Cod, a.Denumire)))
                .ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal));
        var documente = new List<DocumentCost>();
        var listaSurse = new List<SursaDocumentCost>();
        foreach (var s in surse)
        {
            documente.AddRange(await s.DocumenteAsync(proiectId, ct));
            listaSurse.Add(s.Sursa);
        }

        return new Context(grafic, structura, documente.OrderByDescending(d => d.Data).ThenBy(d => d.Numar).ToList(), listaSurse);
    }

    private static async Task<RezultatCosturiGrafic> CalculeazaAsync(Context c, GraficDbContext db, ICursValutar curs, CancellationToken ct)
    {
        var alocari = (await db.Alocari.AsNoTracking().Where(a => a.GraficId == c.Grafic.Id).ToListAsync(ct)).Select(a => a.Linie()).ToList();
        var moneda = c.Grafic.Moneda ?? "RON";
        // Cursurile BNR la data fiecărui document: moneda documentului și moneda graficului (cache pe monedă și zi).
        var cache = new Dictionary<(string, DateOnly), CursValutar?>();
        async Task<CursValutar?> Curs(string m, DateOnly d)
        {
            if (!cache.TryGetValue((m, d), out var v))
                cache[(m, d)] = v = await curs.CursAsync(m, d, ct);
            return v;
        }

        var cursuri = new Dictionary<Guid, CursuriDocument>();
        foreach (var d in c.Documente)
            cursuri[d.Id] = new CursuriDocument(await Curs(d.Moneda, d.Data), await Curs(moneda, d.Data));
        return CalculCosturiGrafic.Calculeaza(c.Structura, moneda, c.Documente, alocari, cursuri);
    }

    private static async Task<IResult> Costuri(
        Guid proiectId, Guid graficId, GraficDbContext db, IEnumerable<ISursaDocumenteCost> surse, ICursValutar curs, CancellationToken ct)
    {
        if (await ContextAsync(proiectId, graficId, db, surse, ct) is not { } c)
            return Results.NotFound();
        var r = await CalculeazaAsync(c, db, curs, ct);
        return Results.Ok(new CosturiGraficDto(c.Grafic.Moneda ?? "RON", c.Grafic.VersiuneCurentaId, r.Total, r.FaraTinta, r.PeActivitate, r.PeNod,
            r.Documente, r.Alocari, c.Surse));
    }

    private static async Task<IResult> Aloca(
        Guid proiectId, Guid graficId, AlocareDocumentCerere cerere, GraficDbContext db, IEnumerable<ISursaDocumenteCost> surse, ICursValutar curs,
        IUtilizatorCurent utilizator, CancellationToken ct)
    {
        if (await ContextAsync(proiectId, graficId, db, surse, ct) is not { } c)
            return Results.NotFound();
        var document = c.Documente.FirstOrDefault(d => d.Sursa == cerere.Sursa && d.Id == cerere.DocumentId);
        if (document is null)
            return Results.NotFound(new { mesaj = "Documentul nu e pe proiect." });
        var alocari = cerere.Alocari ?? [];
        if (CalculCosturiGrafic.Valideaza(document, alocari, c.Structura) is { Esec: true } v)
            return Results.BadRequest(new { mesaj = v.Eroare.Mesaj });

        await InlocuiesteAsync(db, c, document, alocari, utilizator.Nume ?? string.Empty, ct);
        await db.SaveChangesAsync(ct);
        var r = await CalculeazaAsync(c, db, curs, ct);
        return Results.Ok(r.Documente.First(d => d.Document.Sursa == document.Sursa && d.Document.Id == document.Id));
    }

    private static async Task<IResult> AlocaInMasa(
        Guid proiectId, Guid graficId, AlocareInMasaCerere cerere, GraficDbContext db, IEnumerable<ISursaDocumenteCost> surse, IUtilizatorCurent utilizator,
        CancellationToken ct)
    {
        if (await ContextAsync(proiectId, graficId, db, surse, ct) is not { } c)
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(cerere.Cheie) || !c.Structura.Exista(cerere.Tip, cerere.Cheie))
            return Results.BadRequest(new { mesaj = "Alege o activitate sau un nod WBS din actualizarea curentă a graficului." });
        var existente = (await db.Alocari.AsNoTracking().Where(a => a.GraficId == graficId).ToListAsync(ct)).Select(a => a.Linie()).ToLookup(a => (a.Sursa, a.DocumentId));
        var schimbate = 0;
        foreach (var ales in (cerere.Documente ?? []).Distinct())
        {
            var document = c.Documente.FirstOrDefault(d => d.Sursa == ales.Sursa && d.Id == ales.DocumentId);
            if (document is null)
                return Results.NotFound(new { mesaj = "Unul dintre documente nu e pe proiect." });
            var vechi = existente[(ales.Sursa, ales.DocumentId)].ToList();
            var noi = CalculCosturiGrafic.CompleteazaPe(document, vechi, cerere.Tip, cerere.Cheie);
            if (noi.Count == vechi.Count && noi.Zip(vechi).All(x => x.First.Procent == x.Second.Procent))
                continue;
            if (CalculCosturiGrafic.Valideaza(document, noi, c.Structura) is { Esec: true } v)
                return Results.BadRequest(new { mesaj = $"{document.Numar}: {v.Eroare.Mesaj}" });
            await InlocuiesteAsync(db, c, document, noi, utilizator.Nume ?? string.Empty, ct);
            schimbate++;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(schimbate);
    }

    /// <summary>Alocările documentului se înlocuiesc: cele care rămân neschimbate (aceeași linie, țintă și procent) nu se ating.</summary>
    private static async Task InlocuiesteAsync(GraficDbContext db, Context c, DocumentCost document, IReadOnlyList<AlocareCeruta> alocari, string utilizator,
        CancellationToken ct)
    {
        var vechi = await db.Alocari.Where(a => a.GraficId == c.Grafic.Id && a.Sursa == document.Sursa && a.DocumentId == document.Id).ToListAsync(ct);
        var linii = document.Linii.ToDictionary(l => l.Index);
        foreach (var a in vechi.Where(v => !alocari.Any(n => n.LinieIndex == v.LinieIndex && n.Tip == v.TipTinta && n.Cheie == v.Cheie && n.Procent == v.Procent)))
            db.Alocari.Remove(a);
        foreach (var n in alocari.Where(n => !vechi.Any(v => n.LinieIndex == v.LinieIndex && n.Tip == v.TipTinta && n.Cheie == v.Cheie && n.Procent == v.Procent)))
            db.Alocari.Add(AlocareCost.Creeaza(c.Grafic.Id, document, linii[n.LinieIndex], n.Tip, n.Cheie, c.Structura.Denumire(n.Tip, n.Cheie) ?? n.Cheie,
                Math.Round(n.Procent, 4, MidpointRounding.AwayFromZero), utilizator));
    }
}
