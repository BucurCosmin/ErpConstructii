using System.Globalization;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Aprovizionare.Infrastructure;

/// <param name="Tip">sintetic (pe furnizor și lucrare) | materiale (adunate pe lucrare) | registru (un rând pe aviz) | detaliat (avizele cu materialele lor).</param>
/// <param name="Filtre">Descrierea filtrelor din ecran, tipărită pe raport.</param>
public sealed record RaportAvizeCerere(IReadOnlyList<Guid>? Id, string? Tip, string? Filtre);

/// <summary>Rapoartele PDF ale avizelor de însoțire alese (cele filtrate în ecran), D-40.</summary>
internal static class RapoarteAvize
{
    private const int MaximRaport = 5000;
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static void Mapeaza(RouteGroupBuilder g)
    {
        g.MapPost("/avize/raport/pdf", Raport).WithName("RaportAvize")
            .WithSummary("Raportul avizelor alese: sintetic pe furnizori și lucrări, materiale primite (adunate), registru sau detaliat")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status400BadRequest)
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare));
    }

    private static async Task<IResult> Raport(
        RaportAvizeCerere cerere, AprovizionareDbContext db, IEnumerable<IInterogareReceptiiAviz> receptii, IServiciuDocumente documente,
        IInterogareFirma firme, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (cerere.Id is not { Count: > 0 and <= MaximRaport } ids)
            return Results.BadRequest(new { mesaj = $"Alege între 1 și {MaximRaport} avize." });
        var tip = (cerere.Tip ?? "sintetic").Trim().ToLowerInvariant();
        if (tip is not ("sintetic" or "materiale" or "registru" or "detaliat"))
            return Results.BadRequest(new { mesaj = "Tipul raportului poate fi: sintetic, materiale, registru sau detaliat." });
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();

        var accesibile = (await permisiuni.PentruUtilizatorAsync(uid, ct)).ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);
        var alese = ids.Distinct().ToList();
        var q = db.Avize.AsNoTracking().Where(a => alese.Contains(a.Id));
        if (accesibile is not null)
        {
            var proiecteAccesibile = accesibile.ToList();
            q = q.Where(a => proiecteAccesibile.Contains(a.ProiectId));
        }

        var avize = (await q.ToListAsync(ct))
            .OrderBy(a => a.ProiectCod, StringComparer.Ordinal).ThenBy(a => a.Data).ThenBy(a => a.An).ThenBy(a => a.Numar).ToList();
        if (avize.Count == 0)
            return Results.BadRequest(new { mesaj = "Avizele alese nu există." });
        var stari = receptii.FirstOrDefault() is { } r ? await r.StariAsync(ct) : new Dictionary<Guid, StareStocAviz>();
        var stareAviz = avize.ToDictionary(a => a.Id, a => RaportAvize.Stare(stari.GetValueOrDefault(a.Id)));

        var f = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa.Formatata());
        var proiecte = avize.Select(a => a.ProiectCod).Distinct().ToList();
        var furnizori = avize.Select(a => a.FurnizorDenumire).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var campuri = new Dictionary<string, string>
        {
            ["perioada"] = $"{Data(avize.Min(a => a.Data))} – {Data(avize.Max(a => a.Data))}",
            ["proiecte"] = proiecte.Count <= 6 ? string.Join(", ", proiecte) : $"{proiecte.Count} lucrări",
            ["avize"] = $"{avize.Count} de la {furnizori} {(furnizori == 1 ? "furnizor" : "furnizori")}: " +
                        $"{stareAviz.Values.Count(s => s == StareMarfaAviz.DeReceptionat)} de recepționat, " +
                        $"{stareAviz.Values.Count(s => s == StareMarfaAviz.ReceptionatFaraFactura)} recepționate fără factură, " +
                        $"{stareAviz.Values.Count(s => s == StareMarfaAviz.Facturat)} facturate",
            ["observatii"] = string.IsNullOrWhiteSpace(cerere.Filtre) ? string.Empty : $"Filtre: {cerere.Filtre.Trim()}",
        };
        ElementAntet[] elemente = [new("Perioada", "perioada"), new("Lucrări", "proiecte"), new("Avize", "avize")];

        List<IReadOnlyDictionary<string, string>> linii;
        List<ColoanaPdf> coloane;
        string titlu;
        switch (tip)
        {
            case "sintetic":
                titlu = "Avizele de însoțire — sinteză pe furnizori și lucrări";
                linii = RaportAvize.PeFurnizori(avize, stari).Select((s, i) => Rand(
                    ("numar_crt", (i + 1).ToString(Ro)), ("furnizor", s.Furnizor), ("proiect", s.ProiectCod), ("avize", s.Avize.ToString(Ro)),
                    ("pozitii", s.Pozitii.ToString(Ro)), ("de_receptionat", Zero(s.DeReceptionat)), ("fara_factura", Zero(s.ReceptionateFaraFactura)),
                    ("facturate", Zero(s.Facturate)),
                    ("perioada", s.PrimaData == s.UltimaData ? Data(s.PrimaData) : $"{Data(s.PrimaData)} – {Data(s.UltimaData)}"))).ToList();
                coloane =
                [
                    new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("furnizor", "Furnizor", 3.4), new("proiect", "Proiect", 1.2),
                    new("avize", "Avize", 0.8, AliniereColoana.Dreapta), new("pozitii", "Poziții", 0.8, AliniereColoana.Dreapta),
                    new("de_receptionat", "De recepționat", 1.2, AliniereColoana.Dreapta), new("fara_factura", "Recepționate fără factură", 1.6, AliniereColoana.Dreapta),
                    new("facturate", "Facturate", 1.0, AliniereColoana.Dreapta), new("perioada", "Perioada", 1.9),
                ];
                break;
            case "materiale":
                titlu = "Materiale primite cu aviz, pe lucrări (cantități adunate)";
                linii = RaportAvize.Materiale(avize).Select((m, i) => Rand(
                    ("numar_crt", (i + 1).ToString(Ro)), ("proiect", m.ProiectCod), ("material", m.Denumire), ("um", m.UnitateMasura),
                    ("cantitate", Cant(m.Cantitate)), ("furnizori", string.Join(", ", m.Furnizori)), ("avize", string.Join(", ", m.Avize)),
                    ("perioada", m.PrimaData == m.UltimaData ? Data(m.PrimaData) : $"{Data(m.PrimaData)} – {Data(m.UltimaData)}"))).ToList();
                coloane =
                [
                    new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.0), new("material", "Material", 3.2),
                    new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate totală", 1.2, AliniereColoana.Dreapta),
                    new("furnizori", "Furnizori", 2.0), new("avize", "Avize", 3.0), new("perioada", "Perioada", 1.7),
                ];
                break;
            case "registru":
                titlu = "Registrul avizelor de însoțire";
                linii = avize.Select((a, i) => Rand(
                    ("numar_crt", (i + 1).ToString(Ro)), ("proiect", a.ProiectCod), ("numar_extern", a.NumarExtern), ("numar", a.NumarAfisat),
                    ("data", Data(a.Data)), ("furnizor", a.FurnizorDenumire), ("nota", a.NotaComandaNumar ?? (a.DinUrma ? "din urmă" : string.Empty)),
                    ("pozitii", a.Linii.Count.ToString(Ro)), ("stare", Stare(stareAviz[a.Id], stari.GetValueOrDefault(a.Id))))).ToList();
                coloane =
                [
                    new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.0), new("numar_extern", "Nr. aviz", 1.4),
                    new("numar", "Înregistrat", 1.3), new("data", "Data", 0.9, AliniereColoana.Centru), new("furnizor", "Furnizor", 2.8),
                    new("nota", "Notă de comandă", 1.4), new("pozitii", "Poz.", 0.5, AliniereColoana.Dreapta), new("stare", "Marfa", 2.0),
                ];
                break;
            default:
                titlu = "Avizele de însoțire — materialele din fiecare aviz";
                var k = 0;
                linii = avize.SelectMany(a => a.Linii.Select(l => Rand(
                    ("numar_crt", (++k).ToString(Ro)), ("proiect", a.ProiectCod), ("numar_extern", a.NumarExtern), ("data", Data(a.Data)),
                    ("furnizor", a.FurnizorDenumire), ("material", l.Denumire), ("um", l.UnitateMasura), ("cantitate", Cant(l.Cantitate)),
                    ("stare", Stare(stareAviz[a.Id], stari.GetValueOrDefault(a.Id)))))).ToList();
                coloane =
                [
                    new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.0), new("numar_extern", "Nr. aviz", 1.3),
                    new("data", "Data", 0.9, AliniereColoana.Centru), new("furnizor", "Furnizor", 2.2), new("material", "Material", 3.2),
                    new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.0, AliniereColoana.Dreapta), new("stare", "Marfa", 1.6),
                ];
                break;
        }

        var aspect = new AspectDocument(titlu, null, $"Întocmit la {Data(ceas.Azi)}", elemente, "linii", coloane, [], ["Întocmit"], "observatii", Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync($"raport-avize-{tip}", antet, aspect, date, $"Avize_{tip}.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private static string Stare(StareMarfaAviz stare, StareStocAviz? detalii) => stare switch
    {
        StareMarfaAviz.Facturat => detalii?.FacturaNumar is { } fa ? $"facturat ({fa})" : "facturat",
        StareMarfaAviz.ReceptionatFaraFactura => "recepționat, fără factură",
        _ => "de recepționat",
    };

    private static string Zero(int n) => n == 0 ? "—" : n.ToString(Ro);

    private static IReadOnlyDictionary<string, string> Rand(params (string Camp, string Valoare)[] valori) => valori.ToDictionary(v => v.Camp, v => v.Valoare);

    private static string Data(DateOnly d) => d.ToString("dd.MM.yyyy", Ro);

    private static string Cant(decimal v) => v.ToString("#,##0.####", Ro);
}
