using System.Globalization;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <summary>
/// PDF-urile documentelor de stoc: NIR (14-3-1A), bon de consum (14-3-4A), bon de predare-transfer-restituire (14-3-3A).
/// Datele intră în schema formularului din catalog (<see cref="CatalogFormulare"/>): când firma încarcă propriul șablon Word
/// pentru tip, PDF-ul se face din el; până atunci, în formatul standard. Prețurile apar doar cu dreptul „prețuri de cost”.
/// </summary>
internal static class DocumenteStocPdf
{
    /// <param name="Filtre">Descrierea filtrelor din ecran, tipărită pe registru.</param>
    internal sealed record DocumenteAlese(IReadOnlyList<Guid>? Id, string? Filtre = null);

    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        g.MapGet("/nir/{id:guid}/pdf", Nir).WithName("PdfNir").WithSummary("NIR-ul în PDF").Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapPost("/nir/pdf", NirMultiple).WithName("PdfNirMultiple").WithSummary("Mai multe NIR-uri într-un singur PDF (tipărire)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status400BadRequest).CerePermisiune(vizualizare);
        g.MapGet("/raport-stoc/pdf", RaportStoc).WithName("PdfRaportStoc")
            .WithSummary("Raportul stocului (depozite / șantiere): sumar la CMP sau detaliat pe proveniență (factură → NIR)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").CerePermisiune(vizualizare);
        g.MapPost("/nir/registru/pdf", RegistruNir).WithName("PdfRegistruNir").WithSummary("Registrul NIR-urilor alese (raport)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status400BadRequest).CerePermisiune(vizualizare);
        g.MapGet("/consumuri/{id:guid}/pdf", BonConsum).WithName("PdfBonConsum").WithSummary("Bonul de consum în PDF")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapGet("/transferuri/{id:guid}/pdf", Transfer).WithName("PdfBonTransfer").WithSummary("Bonul de transfer / restituire în PDF")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
    }

    private static async Task<IResult> Nir(
        Guid id, StocuriDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator,
        IFirmaCurenta firma, CancellationToken ct)
    {
        var n = await db.Nir.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null)
            return Results.NotFound();
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var storno = await db.StornariNir.AsNoTracking().FirstOrDefaultAsync(s => s.NirId == id, ct);
        var doc = await NirPdfAsync(n, storno, preturi, await AntetAsync(firme, ct), documente, ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    /// <summary>NIR-urile alese, în ordinea numerelor, într-un singur PDF (fiecare pe paginile lui).</summary>
    private static async Task<IResult> NirMultiple(
        DocumenteAlese cerere, StocuriDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (cerere.Id is not { Count: > 0 and <= MaximTiparire } ids)
            return Results.BadRequest(new { mesaj = $"Alege între 1 și {MaximTiparire} NIR-uri." });
        var lista = await db.Nir.AsNoTracking().Where(n => ids.Contains(n.Id)).OrderBy(n => n.An).ThenBy(n => n.Numar).ToListAsync(ct);
        if (lista.Count == 0)
            return Results.BadRequest(new { mesaj = "NIR-urile alese nu există." });
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var stornari = await db.StornariNir.AsNoTracking().Where(s => ids.Contains(s.NirId)).ToDictionaryAsync(s => s.NirId, ct);
        var antet = await AntetAsync(firme, ct);
        var pdf = new List<DocumentGenerat>();
        foreach (var n in lista)
            pdf.Add(await NirPdfAsync(n, stornari.GetValueOrDefault(n.Id), preturi, antet, documente, ct));
        var doc = documente.Combina(pdf, lista.Count == 1 ? pdf[0].NumeFisier : $"NIR-uri_{lista.Count}.pdf");
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    /// <summary>Registrul NIR-urilor alese: o linie pe NIR (furnizor, factură, depozit, valoare, stare), cu totalul NIR-urilor active.</summary>
    private static async Task<IResult> RegistruNir(
        DocumenteAlese cerere, StocuriDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (cerere.Id is not { Count: > 0 } ids)
            return Results.BadRequest(new { mesaj = "Alege cel puțin un NIR." });
        var lista = await db.Nir.AsNoTracking().Where(n => ids.Contains(n.Id)).OrderBy(n => n.Data).ThenBy(n => n.An).ThenBy(n => n.Numar).ToListAsync(ct);
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var stornari = await db.StornariNir.AsNoTracking().Where(s => ids.Contains(s.NirId)).ToDictionaryAsync(s => s.NirId, ct);
        var (antet, campuri) = await AntetAsync(firme, ct);

        var active = lista.Where(n => !stornari.ContainsKey(n.Id)).ToList();
        campuri["perioada"] = lista.Count == 0 ? string.Empty : $"{Data(lista.Min(n => n.Data))} – {Data(lista.Max(n => n.Data))}";
        campuri["numar_nir"] = $"{lista.Count} ({active.Count} active, {lista.Count - active.Count} stornate)";
        campuri["total_valoare"] = preturi ? Lei(active.Sum(n => n.Valoare)) : string.Empty;
        campuri["observatii"] = string.IsNullOrWhiteSpace(cerere.Filtre) ? string.Empty : $"Filtre: {cerere.Filtre.Trim()}";
        var linii = lista.Select((n, i) => Rand(
            ("numar_crt", (i + 1).ToString(Ro)), ("numar", n.NumarAfisat), ("data", Data(n.Data)), ("furnizor", n.FurnizorDenumire),
            ("factura", $"{n.FacturaNumar} / {Data(n.FacturaData)}"), ("gestiune", n.GestiuneDenumire), ("proiect", n.ProiectCod),
            ("linii", n.Linii.Count.ToString(Ro)), ("valoare", preturi ? Lei(n.Valoare) : string.Empty),
            ("stare", stornari.TryGetValue(n.Id, out var s) ? $"stornat {Data(s.Data)}" : n.AreDiferente ? "cu diferențe" : string.Empty))).ToList();

        var aspect = new AspectDocument(
            "Registrul notelor de recepție (NIR)", null, $"Întocmit la {Data(ceas.Azi)}",
            [new("Perioada", "perioada"), new("NIR-uri", "numar_nir")],
            "linii",
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("numar", "NIR", 1.4), new("data", "Data", 1.0, AliniereColoana.Centru),
                new("furnizor", "Furnizor", 3.0), new("factura", "Factura / data", 2.2), new("gestiune", "Gestiunea", 2.0), new("proiect", "Proiect", 1.2),
                new("linii", "Linii", 0.6, AliniereColoana.Dreapta), new("valoare", "Valoare (lei)", 1.4, AliniereColoana.Dreapta), new("stare", "Stare", 1.5),
            ],
            [new("Total NIR-uri active (lei, fără TVA):", "total_valoare")],
            ["Întocmit"],
            "observatii",
            Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync("registru-nir", antet, aspect, date, "Registru_NIR.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    /// <param name="Detaliat">Câte o linie pe lot (factura și NIR-ul de origine, la prețul de intrare); altfel câte una pe material (CMP).</param>
    internal sealed record FiltreRaportStoc(string? Tip, Guid? GestiuneId, Guid? ProiectId, string? Material, bool Detaliat = false);

    /// <summary>
    /// Raportul stocului la zi: sumar (cantitate, CMP, valoare din evidență) sau detaliat pe proveniență (pentru fiecare
    /// material, cantitățile cu factura și NIR-ul pe care au intrat, din <see cref="TrasabilitateStoc"/>).
    /// </summary>
    private static async Task<IResult> RaportStoc(
        [AsParameters] FiltreRaportStoc filtre, StocuriDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        var preturi = evaluator.Are(Permisiuni.PreturiCost, new Domeniu(fid));
        var accesibile = evaluator.ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);
        var gestiuni = await db.Gestiuni.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var tip = Enum.TryParse<TipGestiune>(filtre.Tip, true, out var t) ? t : (TipGestiune?)null;
        bool GestiuneAleasa(Guid id) => gestiuni.TryGetValue(id, out var gst)
            && (filtre.GestiuneId is null || gst.Id == filtre.GestiuneId)
            && (tip is null || gst.Tip == tip)
            && (filtre.ProiectId is null || gst.ProiectId == filtre.ProiectId)
            && (gst.ProiectId is null || accesibile is null || accesibile.Contains(gst.ProiectId.Value));

        var (antet, campuri) = await AntetAsync(firme, ct);
        var gestiuniAlese = filtre.GestiuneId is { } gid && gestiuni.TryGetValue(gid, out var aleasa) ? aleasa.Denumire
            : tip == TipGestiune.Depozit ? "toate depozitele" : tip == TipGestiune.Santier ? "toate șantierele" : "toate gestiunile";
        campuri["gestiuni"] = gestiuniAlese;
        campuri["material"] = string.IsNullOrWhiteSpace(filtre.Material) ? "toate" : filtre.Material.Trim();
        List<IReadOnlyDictionary<string, string>> linii;
        List<ColoanaPdf> coloane;
        string total;

        if (filtre.Detaliat)
        {
            var loturi = (await EndpointuriTrasabilitate.LoturiAsync(db, null, null, filtre.Material, null, preturi, accesibile, ct))
                .Where(l => GestiuneAleasa(l.GestiuneId)).ToList();
            linii = loturi.Select((l, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("gestiune", l.Gestiune), ("cod", l.ArticolCod), ("denumire", l.Denumire), ("um", l.UnitateMasura),
                ("cantitate", Cant(l.Cantitate)),
                ("factura", l.FacturaNumar is null ? string.Empty : $"{l.FacturaNumar} / {(l.FacturaData is { } fd ? Data(fd) : string.Empty)}"),
                ("furnizor", l.FurnizorDenumire ?? string.Empty),
                ("nir", $"{l.DocumentNumar} / {Data(l.DataIntrare)}"),
                ("pret", l.PretIntrare is { } pi ? Pret(pi) : string.Empty), ("valoare", l.Valoare is { } v ? Lei(v) : string.Empty))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("gestiune", "Gestiunea", 1.7), new("cod", "Cod", 1.25), new("denumire", "Material", 2.85),
                new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.0, AliniereColoana.Dreapta),
                new("factura", "Factura / data", 2.0), new("furnizor", "Furnizor", 2.2), new("nir", "NIR / data intrării", 2.0),
                new("pret", "Preț intrare", 1.1, AliniereColoana.Dreapta), new("valoare", "Valoare (lei)", 1.2, AliniereColoana.Dreapta),
            ];
            total = preturi ? Lei(loturi.Sum(l => l.Valoare ?? 0)) : string.Empty;
            campuri["observatii"] = "Proveniența: ieșirile se atribuie FIFO (în stoc rămân cele mai recente intrări); valoarea e la prețul de intrare din NIR, nu la CMP.";
        }
        else
        {
            var articole = await db.Articole.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
            var termen = string.IsNullOrWhiteSpace(filtre.Material) ? null : Text.FaraDiacritice(filtre.Material.Trim());
            var solduri = (await db.Solduri.AsNoTracking().Where(x => x.Cantitate != 0).ToListAsync(ct))
                .Where(x => GestiuneAleasa(x.GestiuneId) && articole.ContainsKey(x.ArticolId))
                .Select(x => (Sold: x, Gestiune: gestiuni[x.GestiuneId], Articol: articole[x.ArticolId]))
                .Where(x => termen is null || Text.FaraDiacritice(x.Articol.Denumire).Contains(termen, StringComparison.Ordinal)
                    || x.Articol.Cod.Contains(termen, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Gestiune.Tip).ThenBy(x => x.Gestiune.Cod).ThenBy(x => x.Articol.Denumire).ToList();
            linii = solduri.Select((x, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("gestiune", x.Gestiune.Denumire), ("cod", x.Articol.Cod), ("denumire", x.Articol.Denumire),
                ("um", x.Articol.UnitateMasura), ("cantitate", Cant(x.Sold.Cantitate)),
                ("cmp", preturi && x.Sold.Sold.Cmp is { } cmp ? Pret(cmp) : string.Empty), ("valoare", preturi ? Lei(x.Sold.Valoare) : string.Empty))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("gestiune", "Gestiunea", 2.6), new("cod", "Cod", 1.2), new("denumire", "Material", 5.0),
                new("um", "UM", 0.7, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.3, AliniereColoana.Dreapta),
                new("cmp", "Preț mediu (CMP)", 1.5, AliniereColoana.Dreapta), new("valoare", "Valoare (lei)", 1.6, AliniereColoana.Dreapta),
            ];
            total = preturi ? Lei(solduri.Sum(x => x.Sold.Valoare)) : string.Empty;
            campuri["observatii"] = string.Empty;
        }

        campuri["total_valoare"] = total;
        var aspect = new AspectDocument(
            filtre.Detaliat ? "Stocul pe proveniență (factură → NIR)" : "Situația stocului", null, $"la {Data(ceas.Azi)}",
            [new("Gestiuni", "gestiuni"), new("Material", "material")],
            "linii", coloane,
            [new(filtre.Detaliat ? "Total (preț de intrare, lei):" : "Total valoare (CMP, lei):", "total_valoare")],
            ["Gestionar"],
            "observatii",
            Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync("raport-stoc", antet, aspect, date, $"Stoc_{ceas.Azi:yyyy-MM-dd}.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private const int MaximTiparire = 200;

    private static async Task<DocumentGenerat> NirPdfAsync(
        Nir n, StornoNir? storno, bool preturi, (AntetFirma Antet, Dictionary<string, string> Campuri) antetFirma, IServiciuDocumente documente,
        CancellationToken ct)
    {
        var antet = antetFirma.Antet;
        var campuri = new Dictionary<string, string>(antetFirma.Campuri);

        campuri["numar"] = n.NumarAfisat;
        campuri["data"] = Data(n.Data);
        campuri["gestiune"] = n.GestiuneDenumire;
        campuri["furnizor"] = n.FurnizorDenumire;
        campuri["factura"] = $"{n.FacturaNumar} din {Data(n.FacturaData)}";
        campuri["proiect_cod"] = n.ProiectCod;
        campuri["intocmit_de"] = n.ReceptionatDeNume;
        campuri["total_valoare"] = preturi ? Lei(n.Valoare) : string.Empty;
        campuri["observatii"] = string.Join(" ", new[] { n.Observatii, storno is null ? null : $"STORNAT la {Data(storno.Data)}: {storno.Motiv}" }.Where(x => x is not null));
        var linii = n.Linii.Select((l, i) => Rand(
            ("numar_crt", (i + 1).ToString(Ro)), ("cod", l.ArticolCod), ("denumire", l.Denumire), ("um", l.UnitateMasura),
            ("cantitate_factura", Cant(l.CantitateFactura)), ("cantitate_receptionata", Cant(l.CantitateReceptionata)),
            ("pret_unitar", preturi ? Pret(l.PretUnitar) : string.Empty), ("valoare", preturi ? Lei(l.Valoare) : string.Empty),
            ("diferenta", l.Diferenta == 0 ? string.Empty : Cant(l.Diferenta)), ("observatii", l.Observatii ?? string.Empty))).ToList();

        var aspect = new AspectDocument(
            "Notă de recepție și constatare de diferențe", "14-3-1A", $"Nr. {n.NumarAfisat} din {Data(n.Data)}",
            [new("Gestiunea", "gestiune"), new("Furnizor", "furnizor"), new("Document însoțitor", "factura"), new("Proiect", "proiect_cod"), new("Recepționat de", "intocmit_de")],
            "linii",
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("cod", "Cod", 1.1), new("denumire", "Denumirea bunurilor", 4.2),
                new("um", "UM", 0.7, AliniereColoana.Centru), new("cantitate_factura", "Cantitate cf. document", 1.3, AliniereColoana.Dreapta),
                new("cantitate_receptionata", "Cantitate recepționată", 1.3, AliniereColoana.Dreapta), new("pret_unitar", "Preț unitar (lei)", 1.2, AliniereColoana.Dreapta),
                new("valoare", "Valoare (lei)", 1.3, AliniereColoana.Dreapta), new("diferenta", "Diferențe", 1.0, AliniereColoana.Dreapta),
                new("observatii", "Observații", 2.2),
            ],
            [new("Total valoare (lei, fără TVA):", "total_valoare")],
            ["Comisia de recepție", "Primit în gestiune (gestionar)"],
            "observatii",
            Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        return await documente.PdfAsync(CatalogFormulare.Nir.Cod, antet, aspect, date, n.NumarAfisat.Replace('/', '_') + ".pdf", ct);
    }

    private static async Task<IResult> BonConsum(
        Guid id, StocuriDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator,
        IFirmaCurenta firma, CancellationToken ct)
    {
        var b = await db.BonuriConsum.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null)
            return Results.NotFound();
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var gestiune = await db.Gestiuni.AsNoTracking().Where(x => x.Id == b.GestiuneId).Select(x => x.Denumire).FirstOrDefaultAsync(ct);
        var (antet, campuri) = await AntetAsync(firme, ct);

        campuri["numar"] = b.NumarAfisat;
        campuri["data"] = Data(b.Data);
        campuri["gestiune"] = gestiune ?? b.ProiectCod;
        campuri["proiect_cod"] = b.ProiectCod;
        campuri["intocmit_de"] = b.EmisDeNume;
        campuri["total_valoare"] = preturi ? Lei(b.Valoare) : string.Empty;
        campuri["observatii"] = b.Observatii ?? string.Empty;
        var linii = b.Linii.Select((l, i) => Rand(
            ("numar_crt", (i + 1).ToString(Ro)), ("cod", l.ArticolCod), ("denumire", l.Denumire), ("um", l.UnitateMasura), ("cantitate", Cant(l.Cantitate)),
            ("pret_unitar", preturi ? Pret(l.PretUnitar) : string.Empty), ("valoare", preturi ? Lei(l.Valoare) : string.Empty),
            ("articol_deviz", l.ArticolDevizSimbol is null ? "Cheltuieli generale de șantier" : $"{l.ArticolDevizSimbol} — {l.ArticolDevizDenumire}"))).ToList();

        var aspect = new AspectDocument(
            "Bon de consum", "14-3-4A", $"Nr. {b.NumarAfisat} din {Data(b.Data)}",
            [new("Gestiunea", "gestiune"), new("Proiect", "proiect_cod"), new("Întocmit de", "intocmit_de")],
            "linii",
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("cod", "Cod", 1.1), new("denumire", "Denumirea materialelor", 3.4),
                new("um", "UM", 0.7, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.1, AliniereColoana.Dreapta),
                new("pret_unitar", "Preț unitar (lei)", 1.2, AliniereColoana.Dreapta), new("valoare", "Valoare (lei)", 1.3, AliniereColoana.Dreapta),
                new("articol_deviz", "Consumat pe articolul de deviz", 3.6),
            ],
            [new("Total valoare (lei):", "total_valoare")],
            ["Aprobat (șef de șantier)", "Predat (gestionar)", "Primit (consumator)"],
            "observatii",
            Peisaj: true);
        return await Pdf(documente, CatalogFormulare.BonConsum.Cod, antet, aspect, campuri, linii, b.NumarAfisat, ct);
    }

    private static async Task<IResult> Transfer(
        Guid id, StocuriDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator,
        IFirmaCurenta firma, CancellationToken ct)
    {
        var t = await db.Transferuri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
            return Results.NotFound();
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var (antet, campuri) = await AntetAsync(firme, ct);
        var retur = t.Directie == DirectieTransfer.Retur;

        campuri["numar"] = t.NumarAfisat;
        campuri["data"] = Data(t.Data);
        campuri["tip"] = retur ? "Restituire (retur din șantier)" : "Transfer în șantier";
        campuri["gestiune_predatoare"] = t.GestiuneSursaDenumire;
        campuri["gestiune_primitoare"] = t.GestiuneDestinatieDenumire;
        campuri["proiect_cod"] = t.ProiectCod;
        campuri["intocmit_de"] = t.EmisDeNume;
        campuri["total_valoare"] = preturi ? Lei(t.Valoare) : string.Empty;
        campuri["observatii"] = t.Observatii ?? string.Empty;
        var linii = t.Linii.Select((l, i) => Rand(
            ("numar_crt", (i + 1).ToString(Ro)), ("cod", l.ArticolCod), ("denumire", l.Denumire), ("um", l.UnitateMasura), ("cantitate", Cant(l.Cantitate)),
            ("pret_unitar", preturi ? Pret(l.PretUnitar) : string.Empty), ("valoare", preturi ? Lei(l.Valoare) : string.Empty))).ToList();

        var aspect = new AspectDocument(
            "Bon de predare-transfer-restituire", "14-3-3A", $"Nr. {t.NumarAfisat} din {Data(t.Data)}",
            [new("Tip", "tip"), new("Proiect", "proiect_cod"), new("Gestiunea predătoare", "gestiune_predatoare"), new("Gestiunea primitoare", "gestiune_primitoare"),
             new("Întocmit de", "intocmit_de")],
            "linii",
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("cod", "Cod", 1.2), new("denumire", "Denumirea materialelor", 4.4),
                new("um", "UM", 0.7, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.2, AliniereColoana.Dreapta),
                new("pret_unitar", "Preț unitar (lei)", 1.3, AliniereColoana.Dreapta), new("valoare", "Valoare (lei)", 1.4, AliniereColoana.Dreapta),
            ],
            [new("Total valoare (lei):", "total_valoare")],
            ["Predat (gestiunea predătoare)", "Primit (gestiunea primitoare)"],
            "observatii");
        return await Pdf(documente, CatalogFormulare.BonTransfer.Cod, antet, aspect, campuri, linii, t.NumarAfisat, ct);
    }

    private static async Task<(AntetFirma Antet, Dictionary<string, string> Campuri)> AntetAsync(IInterogareFirma firme, CancellationToken ct)
    {
        var f = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa?.Formatata());
        return (antet, new Dictionary<string, string> { ["firma_denumire"] = antet.Denumire, ["firma_cui"] = antet.Cui ?? string.Empty });
    }

    private static async Task<IResult> Pdf(
        IServiciuDocumente documente, string codTip, AntetFirma antet, AspectDocument aspect, Dictionary<string, string> campuri,
        List<IReadOnlyDictionary<string, string>> linii, string numar, CancellationToken ct)
    {
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var nume = numar.Replace('/', '_') + ".pdf";
        var doc = await documente.PdfAsync(codTip, antet, aspect, date, nume, ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private static IReadOnlyDictionary<string, string> Rand(params (string Camp, string Valoare)[] valori) => valori.ToDictionary(v => v.Camp, v => v.Valoare);

    private static string Data(DateOnly d) => d.ToString("dd.MM.yyyy", Ro);

    private static string Lei(decimal v) => v.ToString("N2", Ro);

    private static string Pret(decimal v) => v.ToString("#,##0.00##", Ro);

    private static string Cant(decimal v) => v.ToString("#,##0.####", Ro);
}
