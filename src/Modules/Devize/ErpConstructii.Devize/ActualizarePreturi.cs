using ErpConstructii.Devize.Calcul;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Devize;

/// <summary>Ce preț de achiziție se propune pentru o resursă (D-55, docs/preturi-resurse.md, etapa 3).</summary>
public enum PoliticaPret
{
    /// <summary>Media ponderată cu cantitățile pe ultimele luni (implicit 6).</summary>
    MedieInterval,

    /// <summary>Ultimul preț de achiziție.</summary>
    Ultimul,

    /// <summary>Cel mai mic preț de achiziție din ultimele luni.</summary>
    MinimInterval,
}

public enum StarePropunere
{
    /// <summary>Resursa nu e legată de un material din stoc (sau nu e material).</summary>
    Nelegata,

    /// <summary>Materialul legat nu are achiziții cu preț.</summary>
    FaraAchizitii,

    /// <summary>Are achiziții, dar niciuna în intervalul cerut (pentru medie / minim).</summary>
    FaraAchizitiiRecente,

    /// <summary>Prețul propus e același cu cel din deviz (la 1 ban).</summary>
    Egal,
    Diferit,
}

/// <param name="PretDeviz">Prețul din deviz (media ponderată cu cantitățile, dacă resursa are prețuri diferite în articole).</param>
/// <param name="Impact">Cu cât se schimbă costul direct al devizului la prețul propus (cantitatea din deviz × diferența).</param>
public sealed record PropunerePret(
    string Cod, string Denumire, string UnitateMasura, TipResursa Tip, decimal CantitateDeviz, decimal PretDeviz, bool PreturiDiferiteInDeviz,
    int Articole, StarePropunere Stare, decimal? PretPropus, decimal? Diferenta, decimal Impact, StatisticaAchizitie? Achizitii);

/// <param name="ArticoleModificateManual">Articolele al căror preț nu mai e suma resurselor (modificat manual): primesc doar diferența.</param>
public sealed record RaportActualizarePreturi(
    IReadOnlyList<PropunerePret> Resurse, decimal ImpactTotal, decimal CostDirectActual, IReadOnlyList<string> ArticoleModificateManual);

/// <summary>
/// Diferențele dintre prețurile resurselor din deviz și prețurile de achiziție (recepții = prețul facturilor), cu prețul
/// propus după politică și impactul pe costul direct. Resursele se recunosc după codul din nomenclator. Logică pură.
/// </summary>
public static class ActualizarePreturi
{
    public static RaportActualizarePreturi Propune(VersiuneDeviz v, IReadOnlyDictionary<string, StatisticaAchizitie> achizitii,
        IReadOnlySet<string> legate, PoliticaPret politica)
    {
        var consumuri = v.Articole().SelectMany(a => a.Consumuri.Select(c => (Articol: a, Consum: c))).ToList();
        var resurse = consumuri
            .GroupBy(x => ResursaCatalog.CheieCod(x.Consum.CodResursa))
            .Select(g =>
            {
                var cod = g.Key;
                var primul = g.First().Consum;
                // Cantitatea resursei în deviz = consum unitar × cantitatea articolului (ca la recalcul).
                var cantitate = g.Sum(x => x.Consum.ConsumUnitar * x.Articol.Cantitate);
                var preturi = g.Select(x => x.Consum.PretUnitar).Distinct().ToList();
                var pretDeviz = preturi.Count == 1 || cantitate == 0
                    ? preturi[0]
                    : CatalogPreturi.RotunjirePret.Aplica(g.Sum(x => x.Consum.PretUnitar * x.Consum.ConsumUnitar * x.Articol.Cantitate) / cantitate);

                achizitii.TryGetValue(cod, out var info);
                var stare = !legate.Contains(cod) ? StarePropunere.Nelegata : info is null ? StarePropunere.FaraAchizitii : StarePropunere.Diferit;
                decimal? propus = null;
                if (info is not null)
                {
                    propus = politica switch
                    {
                        PoliticaPret.Ultimul => info.Ultimul,
                        PoliticaPret.MinimInterval => info.MinimInterval,
                        _ => info.MedieInterval,
                    };
                    if (propus is null)
                        stare = StarePropunere.FaraAchizitiiRecente;
                }

                var impact = propus is { } p ? Rotunjire.Lei.Aplica(g.Sum(x => x.Consum.ConsumUnitar * x.Articol.Cantitate * (p - x.Consum.PretUnitar))) : 0m;
                if (propus is { } q && g.All(x => Math.Abs(x.Consum.PretUnitar - q) < 0.01m))
                    stare = StarePropunere.Egal;
                decimal? diferenta = propus is { } r && pretDeviz > 0 ? Math.Round((r - pretDeviz) / pretDeviz, 4, MidpointRounding.AwayFromZero) : null;
                return new PropunerePret(cod, primul.Denumire, primul.UnitateMasura, primul.Tip, cantitate, pretDeviz, preturi.Count > 1,
                    g.Select(x => x.Articol.Id).Distinct().Count(), stare, propus, diferenta, impact, info);
            })
            .OrderByDescending(x => Math.Abs(x.Impact)).ThenBy(x => x.Tip).ThenBy(x => x.Denumire, StringComparer.Ordinal)
            .ToList();

        var manuale = v.Articole().Where(a => a.DiferaDeResurse() is not null).Select(a => $"{a.Simbol} — {a.Denumire}").ToList();
        var impactTotal = resurse.Where(r => r.Stare == StarePropunere.Diferit).Sum(r => r.Impact);
        return new RaportActualizarePreturi(resurse, impactTotal, Rotunjire.Lei.Aplica(v.Recapitulatie().CheltuieliDirecteResurse.Total), manuale);
    }
}
