using System.Globalization;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Facturi.Infrastructure;

/// <param name="Id">Facturile din raport (cele filtrate sau bifate în listă).</param>
/// <param name="Tip">„Registru” (o linie pe factură) sau gruparea totalurilor: „Furnizor”, „Proiect”, „Luna”, „CotaTva”.</param>
/// <param name="Filtre">Descrierea filtrelor din ecran, tipărită pe raport.</param>
public sealed record RaportFacturiCerere(IReadOnlyList<Guid>? Id, string? Tip, string? Filtre);

/// <summary>
/// Rapoartele facturilor primite (PDF): registrul facturilor și totalurile pe furnizor, proiect, lună sau cotă TVA
/// (<see cref="RaportFacturi"/>), plus tipărirea originalelor alese într-un singur PDF. Facturile anulate apar în
/// registru, marcate, dar nu intră în totaluri. Proiectele inaccesibile utilizatorului se exclud.
/// </summary>
internal static class EndpointuriRapoarteFacturi
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");
    private const int MaximOriginale = 100;

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Financiar, Actiune.Vizualizare);
        g.MapPost("/primite/raport/pdf", Raport).WithName("RaportFacturiPrimite")
            .WithSummary("Raportul facturilor primite: registru sau totaluri pe furnizor / proiect / lună / cotă TVA")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status400BadRequest).CerePermisiune(vizualizare);
        g.MapPost("/primite/originale/pdf", Originale).WithName("OriginaleFacturiPrimite")
            .WithSummary("Originalele PDF ale facturilor alese, într-un singur PDF (tipărire)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status400BadRequest).CerePermisiune(vizualizare);
    }

    private static async Task<(List<FacturaPrimita> Facturi, HashSet<Guid> Anulate)?> IncarcaAsync(
        IReadOnlyList<Guid>? id, FacturiDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (id is not { Count: > 0 } ids || utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return null;
        var accesibile = (await permisiuni.PentruUtilizatorAsync(uid, ct)).ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Financiar, Actiune.Vizualizare), fid);
        var facturi = await db.FacturiPrimite.AsNoTracking().Where(f => ids.Contains(f.Id)).ToListAsync(ct);
        if (accesibile is not null)
            facturi = facturi.Where(f => accesibile.Contains(f.ProiectId)).ToList();
        var anulate = (await db.Anulari.AsNoTracking().Where(a => ids.Contains(a.FacturaId)).Select(a => a.FacturaId).ToListAsync(ct)).ToHashSet();
        return (facturi, anulate);
    }

    private static async Task<IResult> Raport(
        RaportFacturiCerere cerere, FacturiDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (await IncarcaAsync(cerere.Id, db, permisiuni, utilizator, firma, ct) is not { Facturi.Count: > 0 } date)
            return Results.BadRequest(new { mesaj = "Alege cel puțin o factură." });
        var (facturi, anulate) = date;
        var valide = facturi.Where(f => !anulate.Contains(f.Id)).ToList();

        var f0 = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f0?.Denumire ?? string.Empty, f0?.Cui, f0?.NrRegCom, f0?.Adresa?.Formatata());
        var campuri = new Dictionary<string, string>
        {
            ["firma_denumire"] = antet.Denumire,
            ["perioada"] = $"{Data(facturi.Min(f => f.DataEmitere))} – {Data(facturi.Max(f => f.DataEmitere))}",
            ["facturi"] = anulate.Count == 0 ? facturi.Count.ToString(Ro) : $"{facturi.Count} ({anulate.Count} anulate, neincluse în totaluri)",
        };

        // Totalurile pe monedă (valutele nu se adună cu leii).
        var peMoneda = valide.GroupBy(f => f.Moneda).OrderBy(g => g.Key == "RON" ? 0 : 1).ThenBy(g => g.Key).ToList();
        var ron = peMoneda.FirstOrDefault(g => g.Key == "RON");
        campuri["total_baza"] = Lei(ron?.Sum(f => f.TotalFaraTva) ?? 0);
        campuri["total_tva"] = Lei(ron?.Sum(f => f.TotalTva) ?? 0);
        campuri["total"] = Lei(ron?.Sum(f => f.TotalDePlata) ?? 0);
        var valute = peMoneda.Where(g => g.Key != "RON").Select(g => $"{g.Key}: bază {Lei(g.Sum(f => f.TotalFaraTva))}, TVA {Lei(g.Sum(f => f.TotalTva))}, total {Lei(g.Sum(f => f.TotalDePlata))}");
        campuri["observatii"] = string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(cerere.Filtre) ? null : $"Filtre: {cerere.Filtre.Trim()}",
            peMoneda.Any(g => g.Key != "RON") ? $"Facturi în valută (neincluse în totalul în lei): {string.Join("; ", valute)}" : null,
            valide.Where(f => !RaportFacturi.LiniiConcordante(f)).Select(f => f.Numar).ToList() is { Count: > 0 } deVerificat
                ? $"De verificat — liniile nu dau totalul facturii (la cote TVA intră cu totalurile ei): {string.Join(", ", deVerificat)}" : null,
        }.Where(x => x is not null));

        var totaluri = new List<ElementAntet> { new("Total bază impozabilă (lei):", "total_baza"), new("Total TVA (lei):", "total_tva"), new("Total de plată (lei):", "total") };
        string titlu;
        List<ColoanaPdf> coloane;
        List<IReadOnlyDictionary<string, string>> linii;

        if (Enum.TryParse<GrupareFacturi>(cerere.Tip, true, out var grupare))
        {
            var randuri = RaportFacturi.Totaluri(valide, grupare);
            titlu = grupare switch
            {
                GrupareFacturi.Furnizor => "Facturi primite — totaluri pe furnizori",
                GrupareFacturi.Proiect => "Facturi primite — totaluri pe proiecte",
                GrupareFacturi.Luna => "Facturi primite — totaluri pe luni",
                _ => "Facturi primite — defalcare pe cote TVA",
            };
            var eticheta = grupare switch { GrupareFacturi.Furnizor => "Furnizor", GrupareFacturi.Proiect => "Proiect", GrupareFacturi.Luna => "Luna", _ => "Cota TVA" };
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("grup", eticheta, 6.0), new("moneda", "Monedă", 0.8, AliniereColoana.Centru),
                new("facturi", "Facturi", 0.8, AliniereColoana.Dreapta), new("baza", "Bază impozabilă", 1.7, AliniereColoana.Dreapta),
                new("tva", "TVA", 1.5, AliniereColoana.Dreapta), new("total", "Total", 1.7, AliniereColoana.Dreapta), new("procent", "% din total", 1.0, AliniereColoana.Dreapta),
            ];
            var totalPeMoneda = randuri.GroupBy(r => r.Moneda).ToDictionary(g => g.Key, g => g.Sum(r => r.Total));
            linii = randuri.Select((r, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("grup", r.Eticheta), ("moneda", r.Moneda), ("facturi", r.Facturi.ToString(Ro)),
                ("baza", Lei(r.BazaImpozabila)), ("tva", Lei(r.Tva)), ("total", Lei(r.Total)),
                ("procent", totalPeMoneda[r.Moneda] == 0 ? string.Empty : (r.Total / totalPeMoneda[r.Moneda]).ToString("P1", Ro)))).ToList();
        }
        else
        {
            titlu = "Registrul facturilor primite";
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.55, AliniereColoana.Centru), new("numar", "Număr factură", 1.6), new("data", "Data", 0.95, AliniereColoana.Centru),
                new("scadenta", "Scadența", 0.95, AliniereColoana.Centru), new("furnizor", "Furnizor", 3.0), new("cui", "CUI", 1.1), new("proiect", "Proiect", 1.1),
                new("baza", "Bază", 1.3, AliniereColoana.Dreapta), new("tva", "TVA", 1.1, AliniereColoana.Dreapta), new("total", "Total", 1.3, AliniereColoana.Dreapta),
                new("moneda", "Mon.", 0.55, AliniereColoana.Centru), new("stare", "Stare", 0.9),
            ];
            linii = facturi.OrderBy(f => f.DataEmitere).ThenBy(f => f.Numar).Select((f, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("numar", f.Numar), ("data", Data(f.DataEmitere)), ("scadenta", f.DataScadenta is { } s ? Data(s) : string.Empty),
                ("furnizor", f.FurnizorDenumire), ("cui", f.FurnizorCui ?? string.Empty), ("proiect", f.ProiectCod),
                ("baza", Lei(f.TotalFaraTva)), ("tva", Lei(f.TotalTva)), ("total", Lei(f.TotalDePlata)), ("moneda", f.Moneda),
                ("stare", anulate.Contains(f.Id) ? "anulată" : f.EsteStorno ? "storno" : !RaportFacturi.LiniiConcordante(f) ? "de verificat" : string.Empty))).ToList();
        }

        var aspect = new AspectDocument(titlu, null, $"Întocmit la {Data(ceas.Azi)}",
            [new("Perioada", "perioada"), new("Facturi", "facturi")],
            "linii", coloane, totaluri, ["Întocmit"], "observatii", Peisaj: true);
        var date2 = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync("raport-facturi-primite", antet, aspect, date2, $"Facturi_primite_{ceas.Azi:yyyy-MM-dd}.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    /// <summary>Originalele PDF (scanate / digitale) ale facturilor alese, în ordinea datei; cele doar XML nu au PDF.</summary>
    private static async Task<IResult> Originale(
        RaportFacturiCerere cerere, FacturiDbContext db, IServiciuDocumente documente, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator,
        IFirmaCurenta firma, CancellationToken ct)
    {
        if (cerere.Id is { Count: > MaximOriginale })
            return Results.BadRequest(new { mesaj = $"Se pot tipări cel mult {MaximOriginale} facturi deodată." });
        if (await IncarcaAsync(cerere.Id, db, permisiuni, utilizator, firma, ct) is not { Facturi.Count: > 0 } date)
            return Results.BadRequest(new { mesaj = "Alege cel puțin o factură." });

        var ordine = date.Facturi.OrderBy(f => f.DataEmitere).ThenBy(f => f.Numar).Where(f => f.DocumentPdfId is not null).ToList();
        var idDoc = ordine.Select(f => f.DocumentPdfId!.Value).ToList();
        var docs = await db.Documente.AsNoTracking().Where(d => idDoc.Contains(d.Id) && d.TipContinut == "application/pdf").ToDictionaryAsync(d => d.Id, ct);
        var pdf = ordine.Where(f => docs.ContainsKey(f.DocumentPdfId!.Value))
            .Select(f => new DocumentGenerat(docs[f.DocumentPdfId!.Value].Continut, "application/pdf", docs[f.DocumentPdfId!.Value].NumeFisier, false)).ToList();
        if (pdf.Count == 0)
            return Results.BadRequest(new { mesaj = "Facturile alese nu au original PDF (sunt doar din e-Factura, XML)." });

        var doc = documente.Combina(pdf, $"Facturi_{pdf.Count}.pdf");
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private static IReadOnlyDictionary<string, string> Rand(params (string Camp, string Valoare)[] valori) => valori.ToDictionary(v => v.Camp, v => v.Valoare);

    private static string Data(DateOnly d) => d.ToString("dd.MM.yyyy", Ro);

    private static string Lei(decimal v) => v.ToString("N2", Ro);
}
