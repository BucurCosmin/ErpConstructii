using System.Globalization;

namespace ErpConstructii.Stocuri;

/// <summary>Un material consumat pe o lucrare: cantitatea și valoarea din toate bonurile alese.</summary>
/// <param name="Documente">Numerele bonurilor (de sistem, cu numărul de hârtie între paranteze).</param>
public sealed record MaterialBonuri(
    Guid ProiectId,
    string ProiectCod,
    string Cod,
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    decimal Valoare,
    IReadOnlyList<string> Documente,
    DateOnly PrimaData,
    DateOnly UltimaData);

/// <summary>Rapoartele bonurilor de consum (logică pură).</summary>
public static class RaportBonuriConsum
{
    /// <summary>Numărul de sistem, cu numărul de pe hârtie: „BC/2026/0007 (nr. 245)”.</summary>
    public static string Cod(BonConsum b) => b.NumarExtern is { } e ? $"{b.NumarAfisat} (nr. {e})" : b.NumarAfisat;

    /// <summary>Materialele consumate pe fiecare proiect, adunate pe material de stoc (același material pe mai multe articole de deviz = un rând).</summary>
    public static IReadOnlyList<MaterialBonuri> Materiale(IEnumerable<BonConsum> bonuri) =>
        bonuri.OrderBy(b => b.Data).ThenBy(b => b.An).ThenBy(b => b.Numar)
            .SelectMany(b => b.Linii.Select(l => (Bon: b, Linie: l)))
            .GroupBy(x => (x.Bon.ProiectId, x.Linie.ArticolId))
            .Select(g => new MaterialBonuri(
                g.Key.ProiectId,
                g.First().Bon.ProiectCod,
                g.First().Linie.ArticolCod,
                g.First().Linie.Denumire,
                g.First().Linie.UnitateMasura,
                g.Sum(x => x.Linie.Cantitate),
                g.Sum(x => x.Linie.Valoare),
                g.Select(x => Cod(x.Bon)).Distinct().ToList(),
                g.Min(x => x.Bon.Data),
                g.Max(x => x.Bon.Data)))
            .OrderBy(m => m.ProiectCod, StringComparer.Ordinal)
            .ThenBy(m => m.Denumire, StringComparer.Create(CultureInfo.GetCultureInfo("ro-RO"), ignoreCase: true))
            .ToList();
}
