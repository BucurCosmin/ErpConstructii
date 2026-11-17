namespace ErpConstructii.Grafic;

/// <summary>Totalurile unui nod WBS (sau ale întregului grafic), adunate din activitățile de sub el.</summary>
/// <param name="Procent">Procentul de execuție ponderat cu costul planificat; fără costuri, cu durata.</param>
public sealed record TotalWbs(
    DateOnly? Start, DateOnly? Sfarsit, DateOnly? StartPlanificat, DateOnly? SfarsitPlanificat,
    decimal CostPlanificat, decimal CostRealizat, decimal CostRamas, decimal Procent,
    int Activitati, int Finalizate, int InCurs, int Critice);

/// <summary>
/// Totalizarea graficului pe structura WBS: datele (cel mai devreme început, cel mai târziu sfârșit), costurile
/// (planificat / realizat / rămas) și procentul de execuție. Procentul se ponderează cu costul planificat al activităților;
/// dacă nodul nu are costuri, cu durata planificată (jaloanele nu cântăresc). Activitățile rezumat (WBS Summary) și
/// de nivel (LOE) nu intră în procent și în numărători: ele doar repetă activitățile pe care le acoperă.
/// </summary>
public static class AnalizaGrafic
{
    public static IReadOnlyDictionary<string, TotalWbs> Totalizeaza(IReadOnlyList<NodWbsCanonic> noduri, IReadOnlyList<ActivitateCanonica> activitati)
    {
        var parinti = noduri.ToDictionary(n => n.Cheie, n => n.CheieParinte, StringComparer.Ordinal);
        var peNod = new Dictionary<string, List<ActivitateCanonica>>(StringComparer.Ordinal);
        foreach (var a in activitati)
        {
            // Activitatea intră în nodul ei și în toți strămoșii lui.
            for (var cheie = a.CodWbs; cheie is not null && parinti.ContainsKey(cheie); cheie = parinti[cheie])
            {
                if (!peNod.TryGetValue(cheie, out var l))
                    peNod[cheie] = l = [];
                l.Add(a);
            }
        }

        return noduri.ToDictionary(n => n.Cheie, n => Total(peNod.GetValueOrDefault(n.Cheie) ?? []), StringComparer.Ordinal);
    }

    public static TotalWbs Total(IReadOnlyList<ActivitateCanonica> activitati)
    {
        var lucrari = activitati.Where(a => a.Tip is not (TipActivitate.Rezumat or TipActivitate.Nivel)).ToList();
        var costPlan = activitati.Sum(a => a.CostPlanificat);
        var costPlanLucrari = lucrari.Sum(a => a.CostPlanificat);
        decimal procent;
        if (costPlanLucrari > 0)
            procent = lucrari.Sum(a => a.CostPlanificat * a.Procent) / costPlanLucrari;
        else if (lucrari.Sum(a => a.DurataZile) is > 0 and var durata)
            procent = lucrari.Sum(a => a.DurataZile * a.Procent) / durata;
        else
            procent = lucrari.Count > 0 ? lucrari.Average(a => a.Procent) : 0;

        return new TotalWbs(
            Min(activitati.Select(a => (DateOnly?)(a.Start ?? a.StartPlanificat))),
            Max(activitati.Select(a => (DateOnly?)(a.Sfarsit ?? a.SfarsitPlanificat))),
            Min(activitati.Select(a => (DateOnly?)a.StartPlanificat)),
            Max(activitati.Select(a => (DateOnly?)a.SfarsitPlanificat)),
            costPlan, activitati.Sum(a => a.CostRealizat), activitati.Sum(a => a.CostRamas),
            Math.Round(procent, 2, MidpointRounding.AwayFromZero),
            lucrari.Count,
            lucrari.Count(a => a.Stadiu == StadiuActivitate.Finalizata),
            lucrari.Count(a => a.Stadiu == StadiuActivitate.InCurs),
            lucrari.Count(a => a.Critica));
    }

    private static DateOnly? Min(IEnumerable<DateOnly?> d) => d.Where(x => x is not null).DefaultIfEmpty().Min();

    private static DateOnly? Max(IEnumerable<DateOnly?> d) => d.Where(x => x is not null).DefaultIfEmpty().Max();
}

/// <summary>Ce s-a schimbat la o activitate între două actualizări ale graficului.</summary>
/// <param name="DecalajStartZile">Zile calendaristice cu care s-a mutat începutul (pozitiv = mai târziu); null = neschimbat.</param>
/// <param name="DecalajSfarsitZile">Zile calendaristice cu care s-a mutat sfârșitul (pozitiv = întârziere); null = neschimbat.</param>
public sealed record ModificareActivitate(
    string Cod, string Denumire, string? DenumireAnterioara,
    int? DecalajStartZile, int? DecalajSfarsitZile,
    decimal ProcentAnterior, decimal Procent,
    StadiuActivitate StadiuAnterior, StadiuActivitate Stadiu,
    string? CodWbsAnterior, string? CodWbs);

public sealed record ActivitateComparata(string Cod, string Denumire, string? CodWbs);

/// <summary>Comparația a două versiuni (actualizări) ale aceluiași grafic, după codul activității.</summary>
/// <param name="DecalajSfarsitProiectZile">Cu câte zile s-a mutat sfârșitul întregului grafic.</param>
public sealed record ComparatieGrafic(
    IReadOnlyList<ActivitateComparata> Noi,
    IReadOnlyList<ActivitateComparata> Sterse,
    IReadOnlyList<ModificareActivitate> Modificate,
    DateOnly? SfarsitAnterior, DateOnly? Sfarsit, int? DecalajSfarsitProiectZile,
    decimal ProcentAnterior, decimal Procent,
    int Finalizate)
{
    public int Intarziate => Modificate.Count(m => m.DecalajSfarsitZile > 0);
}

public static class ComparareGrafice
{
    public static ComparatieGrafic Compara(IReadOnlyList<ActivitateCanonica> anterioare, IReadOnlyList<ActivitateCanonica> actuale)
    {
        var vechi = anterioare.GroupBy(a => a.Cod, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var noi = new List<ActivitateComparata>();
        var modificate = new List<ModificareActivitate>();
        var finalizate = 0;
        var vazute = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in actuale)
        {
            vazute.Add(a.Cod);
            if (!vechi.TryGetValue(a.Cod, out var v))
            {
                noi.Add(new ActivitateComparata(a.Cod, a.Denumire, a.CodWbs));
                continue;
            }

            var decStart = Decalaj(v.Start ?? v.StartPlanificat, a.Start ?? a.StartPlanificat);
            var decSfarsit = Decalaj(v.Sfarsit ?? v.SfarsitPlanificat, a.Sfarsit ?? a.SfarsitPlanificat);
            var denumireSchimbata = !string.Equals(v.Denumire.Trim(), a.Denumire.Trim(), StringComparison.Ordinal);
            var wbsSchimbat = !string.Equals(v.CodWbs, a.CodWbs, StringComparison.Ordinal);
            if (v.Stadiu != StadiuActivitate.Finalizata && a.Stadiu == StadiuActivitate.Finalizata)
                finalizate++;
            if (decStart is null && decSfarsit is null && v.Procent == a.Procent && v.Stadiu == a.Stadiu && !denumireSchimbata && !wbsSchimbat)
                continue;
            modificate.Add(new ModificareActivitate(
                a.Cod, a.Denumire, denumireSchimbata ? v.Denumire : null, decStart, decSfarsit, v.Procent, a.Procent, v.Stadiu, a.Stadiu,
                wbsSchimbat ? v.CodWbs : null, a.CodWbs));
        }

        var sterse = anterioare.Where(v => !vazute.Contains(v.Cod)).Select(v => new ActivitateComparata(v.Cod, v.Denumire, v.CodWbs)).ToList();
        var totalVechi = AnalizaGrafic.Total(anterioare);
        var totalNou = AnalizaGrafic.Total(actuale);
        return new ComparatieGrafic(noi, sterse, modificate, totalVechi.Sfarsit, totalNou.Sfarsit,
            Decalaj(totalVechi.Sfarsit, totalNou.Sfarsit), totalVechi.Procent, totalNou.Procent, finalizate);
    }

    private static int? Decalaj(DateOnly? vechi, DateOnly? nou) =>
        vechi is { } v && nou is { } n && v != n ? n.DayNumber - v.DayNumber : null;
}
