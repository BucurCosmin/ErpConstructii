using System.Globalization;

namespace ErpConstructii.Devize.Import.Isdp;

/// <summary>Consumul unei resurse pe UNITATEA de articol, așa cum rezultă dintr-un deviz importat.</summary>
public sealed record ConsumNorma(
    string CodResursa,
    string Denumire,
    string UnitateMasura,
    TipResursaIsdp Tip,
    bool TipDedus,
    decimal ConsumUnitar,
    decimal PretUnitar);

/// <summary>
/// O normă (articol de deviz cu descompunerea pe resurse) extrasă dintr-un deviz importat, pentru
/// biblioteca de norme a firmei (docs/editor-deviz.md, §4).
///
/// ATENȚIE: este norma AȘA CUM A FOLOSIT-O FIRMA în acel deviz (devizierul poate modifica consumurile
/// în ISDP), nu neapărat norma oficială din indicator. De aceea biblioteca păstrează variante, cu proveniența.
/// </summary>
public sealed record NormaExtrasa(
    string Simbol,
    string SimbolBaza,
    TipArticolIsdp TipArticol,
    string Denumire,
    string UnitateMasura,
    IReadOnlyList<ConsumNorma> Consumuri,
    IReadOnlyList<string> WbsSursa)
{
    /// <summary>
    /// Identifică varianta: simbol + UM + consumurile (cod, consum unitar rotunjit la 8 zecimale), sortate.
    /// Două extrageri cu aceeași amprentă sunt aceeași variantă de normă (prețurile NU intră în amprentă:
    /// prețurile se țin în catalogul de prețuri al resurselor, nu în normă).
    /// </summary>
    public string Amprenta { get; } = CalculeazaAmprenta(Simbol, UnitateMasura, Consumuri);

    public static string CalculeazaAmprenta(string simbol, string um, IEnumerable<ConsumNorma> consumuri) =>
        string.Join('|', consumuri
            .OrderBy(c => c.CodResursa, StringComparer.Ordinal)
            .Select(c => $"{c.CodResursa}:{Math.Round(c.ConsumUnitar, 8, MidpointRounding.AwayFromZero).ToString("0.########", CultureInfo.InvariantCulture)}")
            .Prepend($"{TextIsdp.Normalizeaza(simbol)}@{TextIsdp.UnitateMasura(um)}"));
}

/// <summary>
/// Extrage normele dintr-un deviz ISDP, ca să populeze biblioteca de norme a firmei la fiecare import.
///
/// Reguli:
///  - doar articolele cu descompunere (Normat, Asimilat) devin norme; „resursa directă” (ex. 20011961
///    Cherestea) NU e normă — merge în catalogul de resurse;
///  - consum unitar = cantitate resursă / cantitate articol (vezi NodIsdp.ConsumUnitar), rotunjit la 10 zecimale;
///  - același simbol de mai multe ori în deviz cu aceleași consumuri → o singură normă (cu toate WBS-urile);
///  - același simbol cu consumuri DIFERITE → variante separate + avertisment (devizierul a modificat norma);
///  - resursele cu consum 0 se păstrează (fac parte din normă, chiar dacă devizierul le-a anulat aici).
/// </summary>
public static class ExtractorNorme
{
    public static (IReadOnlyList<NormaExtrasa> Norme, IReadOnlyList<ProblemaIsdp> Probleme) Extrage(DevizIsdp deviz)
    {
        var probleme = new List<ProblemaIsdp>();
        var dupaAmprenta = new Dictionary<string, (NormaExtrasa Norma, List<string> Wbs)>(StringComparer.Ordinal);

        foreach (var a in deviz.Articole().Where(a => a.TipArticol != TipArticolIsdp.ResursaDirecta))
        {
            var consumuri = a.Copii
                .Select(r => new ConsumNorma(
                    r.Simbol,
                    r.Denumire,
                    r.UnitateMasura,
                    r.TipResursa,
                    r.TipResursaDedus,
                    Math.Round(r.ConsumUnitar, 10, MidpointRounding.AwayFromZero),
                    r.PretUnitar[r.TipResursa]))
                .ToList();

            var amprenta = NormaExtrasa.CalculeazaAmprenta(a.Simbol, a.UnitateMasura, consumuri);
            if (dupaAmprenta.TryGetValue(amprenta, out var existent))
            {
                existent.Wbs.Add(a.Wbs);
                continue;
            }

            var wbs = new List<string> { a.Wbs };
            dupaAmprenta[amprenta] = (new NormaExtrasa(a.Simbol, a.SimbolBaza, a.TipArticol, a.Denumire, a.UnitateMasura, consumuri, wbs), wbs);
        }

        var norme = dupaAmprenta.Values.Select(v => v.Norma).ToList();

        foreach (var g in norme.GroupBy(n => n.Simbol, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            probleme.Add(new(SeveritateIsdp.Avertisment, $"norma {g.Key}",
                $"Simbolul apare cu {g.Count()} seturi diferite de consumuri în același deviz (WBS {string.Join(", ", g.SelectMany(n => n.WbsSursa))}); se păstrează ca variante."));
        }

        return (norme.OrderBy(n => n.Simbol, StringComparer.Ordinal).ToList(), probleme);
    }
}
