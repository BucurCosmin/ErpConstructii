namespace ErpConstructii.Grafic;

/// <summary>
/// Legătura dintre o activitate a graficului și un articol de deviz (REQ-GRF-02).
/// Un articol poate fi împărțit între mai multe activități prin <see cref="CantitateAlocata"/>.
/// </summary>
/// <param name="CheieArticol">CheieStabila a articolului de deviz (rezistă la reversionarea devizului).</param>
/// <param name="PretUnitar">Prețul unitar total al articolului (pentru ponderarea valorică).</param>
public sealed record LegaturaArticol(string CodActivitate, Guid CheieArticol, decimal CantitateAlocata, decimal PretUnitar);

/// <summary>Cantitate raportată din șantier într-o zi pe un articol (din jurnalul zilnic).</summary>
public sealed record CantitateRaportata(DateOnly Data, Guid CheieArticol, decimal Cantitate);

/// <summary>Progresul calculat al unei activități la data de raportare.</summary>
/// <param name="Progres">0..1, ponderat valoric; null pentru activitățile fără articole (progres manual).</param>
public sealed record ProgresActivitate(string CodActivitate, decimal? Progres, DateOnly? StartReal, DateOnly? SfarsitReal);

/// <summary>
/// Calculează progresul activităților din cantitățile raportate (REQ-GRF-03, pasul 2–3).
///
/// Când un articol este legat de mai multe activități, cantitatea executată se împarte
/// PROPORȚIONAL cu cantitățile alocate. (Alternativa — umplerea activităților în ordinea
/// planificată — se poate adăuga ca opțiune, dacă pilotul o cere.)
/// </summary>
public static class CalculatorProgres
{
    public static IReadOnlyList<ProgresActivitate> Calculeaza(
        IEnumerable<string> coduriActivitati,
        IReadOnlyList<LegaturaArticol> legaturi,
        IReadOnlyList<CantitateRaportata> raportari,
        DateOnly dataRaportare)
    {
        var pana = raportari.Where(r => r.Data <= dataRaportare).ToList();

        var executatPeArticol = pana
            .GroupBy(r => r.CheieArticol)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Cantitate));

        var primaZiPeArticol = pana
            .Where(r => r.Cantitate > 0)
            .GroupBy(r => r.CheieArticol)
            .ToDictionary(g => g.Key, g => g.Min(r => r.Data));

        var totalAlocatPeArticol = legaturi
            .GroupBy(l => l.CheieArticol)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.CantitateAlocata));

        var rezultat = new List<ProgresActivitate>();
        foreach (var cod in coduriActivitati)
        {
            var aleActivitatii = legaturi.Where(l => l.CodActivitate == cod).ToList();
            if (aleActivitatii.Count == 0)
            {
                rezultat.Add(new ProgresActivitate(cod, null, null, null));
                continue;
            }

            decimal valoarePlanificata = 0, valoareExecutata = 0;
            DateOnly? start = null;
            foreach (var l in aleActivitatii)
            {
                valoarePlanificata += l.CantitateAlocata * l.PretUnitar;

                var totalAlocat = totalAlocatPeArticol[l.CheieArticol];
                var executatArticol = executatPeArticol.GetValueOrDefault(l.CheieArticol);
                var cotaActivitatii = totalAlocat == 0 ? 0 : executatArticol * l.CantitateAlocata / totalAlocat;
                valoareExecutata += Math.Min(cotaActivitatii, l.CantitateAlocata) * l.PretUnitar;

                if (primaZiPeArticol.TryGetValue(l.CheieArticol, out var zi) && (start is null || zi < start))
                {
                    start = zi;
                }
            }

            decimal? progres = valoarePlanificata == 0 ? null : Math.Clamp(valoareExecutata / valoarePlanificata, 0m, 1m);
            DateOnly? sfarsit = progres == 1m ? UltimaZiCuCantitati(pana, aleActivitatii) : null;
            rezultat.Add(new ProgresActivitate(cod, progres, start, sfarsit));
        }

        return rezultat;
    }

    private static DateOnly? UltimaZiCuCantitati(List<CantitateRaportata> raportari, List<LegaturaArticol> legaturi)
    {
        var chei = legaturi.Select(l => l.CheieArticol).ToHashSet();
        var zile = raportari.Where(r => chei.Contains(r.CheieArticol) && r.Cantitate > 0).Select(r => r.Data).ToList();
        return zile.Count == 0 ? null : zile.Max();
    }
}
