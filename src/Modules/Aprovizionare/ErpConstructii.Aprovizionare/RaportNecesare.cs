using System.Globalization;

namespace ErpConstructii.Aprovizionare;

/// <summary>Un material cerut pe o lucrare: cantitatea din toate necesarele alese.</summary>
/// <param name="Denumire">Denumirea din primul necesar (în ordinea datelor); variantele scrise altfel se adună aici.</param>
/// <param name="Documente">Numerele necesarelor (de sistem, cu numărul de hârtie între paranteze).</param>
public sealed record MaterialNecesar(
    Guid ProiectId,
    string ProiectCod,
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    IReadOnlyList<string> Documente,
    DateOnly PrimaData,
    DateOnly UltimaData);

/// <summary>Rapoartele necesarelor de materiale (logică pură).</summary>
public static class RaportNecesare
{
    /// <summary>Numărul de sistem, cu numărul de pe hârtie: „NM/2026/0015 (nr. 81)”.</summary>
    public static string Cod(NecesarMateriale n) => n.NumarExtern is { } e ? $"{n.NumarAfisat} (nr. {e})" : n.NumarAfisat;

    /// <summary>
    /// Materialele cerute pe fiecare proiect: aceeași denumire (fără diferențe de majuscule, diacritice sau spații) și aceeași UM
    /// se adună; o UM diferită rămâne pe linie separată.
    /// </summary>
    public static IReadOnlyList<MaterialNecesar> Materiale(IEnumerable<NecesarMateriale> necesare) =>
        necesare.OrderBy(n => n.Data).ThenBy(n => n.An).ThenBy(n => n.Numar)
            .SelectMany(n => n.Linii.Select(l => (Necesar: n, Linie: l)))
            .GroupBy(x => (x.Necesar.ProiectId, Material: RaportNoteComanda.CheieMaterial(x.Linie.Denumire), Um: RaportNoteComanda.CheieMaterial(x.Linie.UnitateMasura)))
            .Select(g => new MaterialNecesar(
                g.Key.ProiectId,
                g.First().Necesar.ProiectCod,
                g.First().Linie.Denumire,
                g.First().Linie.UnitateMasura,
                g.Sum(x => x.Linie.Cantitate),
                g.Select(x => Cod(x.Necesar)).Distinct().ToList(),
                g.Min(x => x.Necesar.Data),
                g.Max(x => x.Necesar.Data)))
            .OrderBy(m => m.ProiectCod, StringComparer.Ordinal)
            .ThenBy(m => m.Denumire, StringComparer.Create(CultureInfo.GetCultureInfo("ro-RO"), ignoreCase: true))
            .ToList();
}
