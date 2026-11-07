using System.Globalization;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Facturi;

/// <summary>Criteriul de grupare al raportului de totaluri.</summary>
public enum GrupareFacturi
{
    Furnizor,
    Proiect,
    Luna,
    CotaTva,
}

/// <summary>O linie a raportului de totaluri: un grup (furnizor, proiect, lună sau cotă TVA) într-o monedă.</summary>
/// <param name="Facturi">Numărul de facturi distincte din grup (la cota TVA, o factură cu mai multe cote apare la fiecare).</param>
public sealed record TotalGrupFacturi(string Cheie, string Eticheta, string Moneda, int Facturi, decimal BazaImpozabila, decimal Tva, decimal Total);

/// <summary>
/// Totalurile facturilor primite pe furnizor, proiect, lună sau cotă TVA, separat pe monedă (sumele în valute diferite
/// nu se adună). Facturile storno (valori negative) se scad singure; cele anulate nu intră (le exclude apelantul).
/// </summary>
public static class RaportFacturi
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static IReadOnlyList<TotalGrupFacturi> Totaluri(IEnumerable<FacturaPrimita> facturi, GrupareFacturi grupare)
    {
        var lista = facturi.ToList();
        if (grupare == GrupareFacturi.CotaTva)
        {
            // Pe cote: din liniile facturii (baza și TVA-ul fiecărei cote). O factură ale cărei linii nu dau totalurile ei
            // (citire greșită) intră cu totalurile ei, pe cota rezultată din ele, ca raportul să dea aceleași sume ca registrul.
            return lista
                .SelectMany(f => LiniiConcordante(f)
                    ? f.Linii.Select(l => (Factura: f, Cota: l.CotaTva, Baza: l.ValoareFaraTva, Tva: l.ValoareTva))
                    : [(Factura: f, Cota: CotaEfectiva(f), Baza: f.TotalFaraTva, Tva: f.TotalTva)])
                .GroupBy(x => (x.Factura.Moneda, x.Cota))
                .OrderBy(g => g.Key.Moneda).ThenByDescending(g => g.Key.Cota)
                .Select(g =>
                {
                    var baza = Rotunjire.Lei.Aplica(g.Sum(x => x.Baza));
                    var tva = Rotunjire.Lei.Aplica(g.Sum(x => x.Tva));
                    var procent = (g.Key.Cota * 100).ToString("0.##", Ro);
                    return new TotalGrupFacturi(procent, $"TVA {procent}%", g.Key.Moneda, g.Select(x => x.Factura.Id).Distinct().Count(), baza, tva, baza + tva);
                })
                .ToList();
        }

        return lista
            .GroupBy(f => (f.Moneda, Cheie: Cheie(f, grupare)))
            .Select(g => new TotalGrupFacturi(
                g.Key.Cheie, Eticheta(g.First(), grupare), g.Key.Moneda, g.Count(),
                g.Sum(f => f.TotalFaraTva), g.Sum(f => f.TotalTva), g.Sum(f => f.TotalDePlata)))
            .OrderBy(t => t.Moneda)
            .ThenBy(t => grupare == GrupareFacturi.Luna ? t.Cheie : string.Empty, StringComparer.Ordinal)
            .ThenByDescending(t => grupare == GrupareFacturi.Luna ? 0 : t.Total)
            .ToList();
    }

    /// <summary>Toleranța la compararea sumei liniilor cu totalurile facturii (rotunjiri pe linie).</summary>
    private const decimal Toleranta = 1m;

    /// <summary>Suma liniilor (baza și TVA-ul) dă totalurile facturii; altfel liniile au fost citite / introduse greșit.</summary>
    public static bool LiniiConcordante(FacturaPrimita f) =>
        f.Linii.Count > 0
        && Math.Abs(f.Linii.Sum(l => l.ValoareFaraTva) - f.TotalFaraTva) <= Toleranta
        && Math.Abs(f.Linii.Sum(l => l.ValoareTva) - f.TotalTva) <= Toleranta;

    /// <summary>Cota rezultată din totalurile facturii (TVA / bază), rotunjită la procente întregi (0,21; 0,09).</summary>
    public static decimal CotaEfectiva(FacturaPrimita f) =>
        f.TotalFaraTva == 0 ? 0 : Math.Round(f.TotalTva / f.TotalFaraTva, 2, MidpointRounding.AwayFromZero);

    private static string Cheie(FacturaPrimita f, GrupareFacturi grupare) => grupare switch
    {
        // Furnizorul: după CUI dacă există (aceeași firmă scrisă diferit), altfel după denumire.
        GrupareFacturi.Furnizor => string.IsNullOrWhiteSpace(f.FurnizorCui) ? f.FurnizorDenumire.Trim().ToUpperInvariant() : f.FurnizorCui.Trim().ToUpperInvariant(),
        GrupareFacturi.Proiect => f.ProiectId.ToString(),
        GrupareFacturi.Luna => f.DataEmitere.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private static string Eticheta(FacturaPrimita f, GrupareFacturi grupare) => grupare switch
    {
        GrupareFacturi.Furnizor => string.IsNullOrWhiteSpace(f.FurnizorCui) ? f.FurnizorDenumire : $"{f.FurnizorDenumire} (CUI {f.FurnizorCui})",
        GrupareFacturi.Proiect => f.ProiectCod,
        GrupareFacturi.Luna => f.DataEmitere.ToString("MMMM yyyy", Ro),
        _ => string.Empty,
    };
}
