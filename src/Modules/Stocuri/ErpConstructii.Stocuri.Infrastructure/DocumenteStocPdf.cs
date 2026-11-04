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
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        g.MapGet("/nir/{id:guid}/pdf", Nir).WithName("PdfNir").WithSummary("NIR-ul în PDF").Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
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
        var (antet, campuri) = await AntetAsync(firme, ct);

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
        return await Pdf(documente, CatalogFormulare.Nir.Cod, antet, aspect, campuri, linii, n.NumarAfisat, ct);
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
