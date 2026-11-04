using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

/// <param name="BugetMaterial">Cantitatea din deviz × prețul materialelor din deviz.</param>
public sealed record ArticolBuget(Guid Cheie, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, decimal BugetMaterial);

public sealed record ConsumMaterial(Guid? ArticolDevizCheie, string MaterialCod, string MaterialDenumire, string UnitateMasura, decimal Cantitate, decimal Valoare);

public sealed record MaterialConsumat(string Cod, string Denumire, string UnitateMasura, decimal Cantitate, decimal Valoare);

/// <param name="Cheie">Null pentru „Cheltuieli generale de șantier” (consum fără articol de deviz).</param>
/// <param name="Diferenta">Consumat − buget material (pozitiv = depășire).</param>
public sealed record RandConsumArticol(
    Guid? Cheie, string Simbol, string Denumire, decimal BugetMaterial, decimal Consumat, decimal Diferenta, decimal? ProcentConsumat,
    IReadOnlyList<MaterialConsumat> Materiale);

/// <summary>
/// Consumul real de materiale pe articolele devizului, față de bugetul de materiale al articolului. Articolele fără consum
/// apar cu 0 (se vede ce nu s-a început); consumurile pe articole care nu mai sunt în devizul de referință și cele fără
/// articol apar separat, la final. Funcție pură.
/// </summary>
public static class RaportConsum
{
    public static IReadOnlyList<RandConsumArticol> PeArticole(IReadOnlyList<ArticolBuget> deviz, IReadOnlyList<ConsumMaterial> consumuri)
    {
        var dupaCheie = consumuri.GroupBy(c => c.ArticolDevizCheie ?? Guid.Empty).ToDictionary(g => g.Key, g => g.ToList());
        var randuri = deviz.Select(a => Rand(a.Cheie, a.Simbol, a.Denumire, a.BugetMaterial, dupaCheie.GetValueOrDefault(a.Cheie) ?? [])).ToList();

        var cunoscute = deviz.Select(a => a.Cheie).ToHashSet();
        foreach (var (cheie, lista) in dupaCheie.Where(x => x.Key != Guid.Empty && !cunoscute.Contains(x.Key)))
            randuri.Add(Rand(cheie, "—", "Articol care nu mai e în devizul de referință", 0, lista));
        if (dupaCheie.TryGetValue(Guid.Empty, out var generale))
            randuri.Add(Rand(null, "—", "Cheltuieli generale de șantier (fără articol de deviz)", 0, generale));
        return randuri;
    }

    private static RandConsumArticol Rand(Guid? cheie, string simbol, string denumire, decimal buget, List<ConsumMaterial> consum)
    {
        var consumat = consum.Sum(c => c.Valoare);
        var materiale = consum.GroupBy(c => (c.MaterialCod, c.MaterialDenumire, c.UnitateMasura))
            .Select(g => new MaterialConsumat(g.Key.MaterialCod, g.Key.MaterialDenumire, g.Key.UnitateMasura, g.Sum(x => x.Cantitate), g.Sum(x => x.Valoare)))
            .OrderByDescending(m => m.Valoare).ToList();
        var bugetLei = Rotunjire.Lei.Aplica(buget);
        decimal? procent = bugetLei > 0 ? Math.Round(consumat / bugetLei * 100, 1, MidpointRounding.AwayFromZero) : null;
        return new RandConsumArticol(cheie, simbol, denumire, bugetLei, consumat, consumat - bugetLei, procent, materiale);
    }
}
