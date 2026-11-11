using System.Globalization;

namespace ErpConstructii.Aprovizionare;

/// <summary>Starea mărfii unui aviz în stoc, pentru rapoarte.</summary>
public enum StareMarfaAviz
{
    DeReceptionat,
    ReceptionatFaraFactura,
    Facturat,
}

/// <summary>Un material primit pe o lucrare: cantitatea din toate avizele alese.</summary>
/// <param name="Denumire">Denumirea din primul aviz (în ordinea datelor); variantele scrise altfel se adună aici.</param>
/// <param name="Avize">Avizele (numărul furnizorului, cu cel de sistem între paranteze).</param>
public sealed record MaterialAvize(
    Guid ProiectId,
    string ProiectCod,
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    IReadOnlyList<string> Avize,
    IReadOnlyList<string> Furnizori,
    DateOnly PrimaData,
    DateOnly UltimaData);

/// <summary>Sinteza avizelor unui furnizor pe o lucrare: câte avize, câte poziții și unde e marfa (recepție, factură).</summary>
public sealed record SintezaAvize(
    string Furnizor,
    string ProiectCod,
    int Avize,
    int Pozitii,
    int DeReceptionat,
    int ReceptionateFaraFactura,
    int Facturate,
    DateOnly PrimaData,
    DateOnly UltimaData);

/// <summary>Rapoartele avizelor de însoțire (logică pură).</summary>
public static class RaportAvize
{
    /// <summary>Starea mărfii din recepțiile avizului (null = nicio recepție).</summary>
    public static StareMarfaAviz Stare(StareStocAviz? stare) =>
        stare is not { Receptii: > 0 } ? StareMarfaAviz.DeReceptionat
        : stare.Facturat ? StareMarfaAviz.Facturat
        : StareMarfaAviz.ReceptionatFaraFactura;

    /// <summary>„INTFSN/18794 (AV/2026/0003)”.</summary>
    public static string Cod(AvizInsotire a) => $"{a.NumarExtern} ({a.NumarAfisat})";

    /// <summary>
    /// Materialele primite pe fiecare proiect: aceeași denumire (fără diferențe de majuscule, diacritice sau spații) și aceeași
    /// UM se adună; o UM diferită rămâne pe linie separată.
    /// </summary>
    public static IReadOnlyList<MaterialAvize> Materiale(IEnumerable<AvizInsotire> avize) =>
        avize.OrderBy(a => a.Data).ThenBy(a => a.An).ThenBy(a => a.Numar)
            .SelectMany(a => a.Linii.Select(l => (Aviz: a, Linie: l)))
            .GroupBy(x => (x.Aviz.ProiectId, Material: RaportNoteComanda.CheieMaterial(x.Linie.Denumire), Um: RaportNoteComanda.CheieMaterial(x.Linie.UnitateMasura)))
            .Select(g => new MaterialAvize(
                g.Key.ProiectId,
                g.First().Aviz.ProiectCod,
                g.First().Linie.Denumire,
                g.First().Linie.UnitateMasura,
                g.Sum(x => x.Linie.Cantitate),
                g.Select(x => Cod(x.Aviz)).Distinct().ToList(),
                g.Select(x => x.Aviz.FurnizorDenumire).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                g.Min(x => x.Aviz.Data),
                g.Max(x => x.Aviz.Data)))
            .OrderBy(m => m.ProiectCod, StringComparer.Ordinal)
            .ThenBy(m => m.Denumire, StringComparer.Create(CultureInfo.GetCultureInfo("ro-RO"), ignoreCase: true))
            .ToList();

    /// <summary>Sinteza pe furnizor și lucrare (furnizorul după CUI, altfel după denumire), furnizorii în ordine alfabetică.</summary>
    public static IReadOnlyList<SintezaAvize> PeFurnizori(IEnumerable<AvizInsotire> avize, IReadOnlyDictionary<Guid, StareStocAviz> stari) =>
        avize
            .GroupBy(a => (Furnizor: a.FurnizorId?.ToString() ?? RaportNoteComanda.CheieMaterial(a.FurnizorDenumire), a.ProiectId))
            .Select(g =>
            {
                var s = g.Select(a => Stare(stari.GetValueOrDefault(a.Id))).ToList();
                return new SintezaAvize(
                    g.OrderBy(a => a.Data).First().FurnizorDenumire,
                    g.First().ProiectCod,
                    g.Count(),
                    g.Sum(a => a.Linii.Count),
                    s.Count(x => x == StareMarfaAviz.DeReceptionat),
                    s.Count(x => x == StareMarfaAviz.ReceptionatFaraFactura),
                    s.Count(x => x == StareMarfaAviz.Facturat),
                    g.Min(a => a.Data),
                    g.Max(a => a.Data));
            })
            .OrderBy(x => x.Furnizor, StringComparer.Create(CultureInfo.GetCultureInfo("ro-RO"), ignoreCase: true))
            .ThenBy(x => x.ProiectCod, StringComparer.Ordinal)
            .ToList();
}
