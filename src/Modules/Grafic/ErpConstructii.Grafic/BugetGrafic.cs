namespace ErpConstructii.Grafic;

/// <summary>
/// Un rând al raportului „valoare în grafic vs cost ERP” (nod WBS sau activitate), în moneda graficului.
/// </summary>
/// <param name="ValoarePlanificata">Costul planificat din grafic (resurse + cheltuieli din Primavera): bugetul la terminare.</param>
/// <param name="Procent">Execuția: la activitate cea din grafic; la nod, valoarea executată / valoarea planificată.</param>
/// <param name="ValoareExecutata">Valoarea planificată × procentul de execuție (valoarea câștigată).</param>
/// <param name="CostErp">Costurile ERP alocate (facturi, bonuri de consum), în moneda graficului, la cursul BNR din data documentelor.</param>
/// <param name="Abatere">Valoarea executată − costul ERP: negativ = s-a cheltuit mai mult decât valoarea lucrărilor executate.</param>
/// <param name="IndiceCost">Valoarea executată / costul ERP (sub 1 = depășire); null fără costuri.</param>
/// <param name="Prognoza">Costul prognozat la terminare: costul ERP + valoarea rămasă de executat (restul la valoarea planificată).</param>
/// <param name="ProcentConsumat">Costul ERP / valoarea planificată, în procente; null fără valoare planificată.</param>
/// <param name="Alerta">Costul depășește valoarea executată peste prag, sau există cost fără valoare planificată.</param>
public sealed record RandBugetGrafic(
    int Adancime, bool EsteNod, string Cheie, string Cod, string Denumire, StadiuActivitate? Stadiu,
    decimal ValoarePlanificata, decimal Procent, decimal ValoareExecutata,
    decimal CostErp, decimal CostErpLei, int Documente, int FaraCurs,
    decimal Abatere, decimal? IndiceCost, decimal Prognoza, decimal? ProcentConsumat, string? Alerta);

/// <param name="CostFaraTinta">Costuri alocate pe ținte care nu mai sunt în actualizarea curentă (intră în total, nu în arbore).</param>
public sealed record RaportBugetGrafic(IReadOnlyList<RandBugetGrafic> Randuri, RandBugetGrafic Total, CostTinta CostFaraTinta, decimal PragAlerta);

/// <summary>
/// Valoarea din grafic față de costurile ERP alocate (D-57, etapa 3), pe arborele WBS: valoarea planificată și executată vin
/// din grafic (activitățile cu procentul lor), costul din alocări (<see cref="CalculCosturiGrafic"/>). Indicatorii, ca la
/// metoda valorii câștigate: abaterea de cost, indicele de cost și prognoza la terminare. Logică pură.
/// </summary>
public static class BugetGrafic
{
    /// <summary>Pragul implicit al alertei: costul peste valoarea executată cu mai mult de 10%.</summary>
    public const decimal PragImplicit = 0.10m;

    private static readonly System.Globalization.CultureInfo Ro = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

    public static RaportBugetGrafic Construieste(
        IReadOnlyList<NodWbsCanonic> noduri, IReadOnlyList<ActivitateCanonica> activitati, RezultatCosturiGrafic costuri, decimal pragAlerta = PragImplicit)
    {
        var copii = noduri.ToLookup(n => n.CheieParinte);
        var peNod = activitati.ToLookup(a => a.CodWbs);
        var randuri = new List<RandBugetGrafic>();
        var gol = new CostTinta(0, 0, 0, 0);

        // Parcurgerea în ordinea arborelui; valorile nodului = suma activităților de sub el (toate nivelurile).
        (decimal Plan, decimal Executat) Nod(NodWbsCanonic n, int adancime)
        {
            var index = randuri.Count;
            randuri.Add(null!);
            decimal plan = 0, executat = 0;
            foreach (var c in copii[n.Cheie].OrderBy(x => x.Ordine))
            {
                var (p, e) = Nod(c, adancime + 1);
                plan += p;
                executat += e;
            }

            foreach (var a in peNod[n.Cheie])
            {
                var (p, e) = Activitate(a, adancime + 1);
                plan += p;
                executat += e;
            }

            randuri[index] = Rand(adancime, true, n.Cheie, n.Cod, n.Denumire, null, plan, null, executat,
                costuri.PeNod.GetValueOrDefault(n.Cheie) ?? gol, pragAlerta);
            return (plan, executat);
        }

        (decimal Plan, decimal Executat) Activitate(ActivitateCanonica a, int adancime)
        {
            var plan = a.CostPlanificat;
            var executat = Rotunjit(plan * a.Procent / 100m);
            randuri.Add(Rand(adancime, false, a.Cod, a.Cod, a.Denumire, a.Stadiu, plan, a.Procent, executat,
                costuri.PeActivitate.GetValueOrDefault(a.Cod) ?? gol, pragAlerta));
            return (plan, executat);
        }

        decimal planTotal = 0, executatTotal = 0;
        foreach (var n in copii[null].OrderBy(x => x.Ordine))
        {
            var (p, e) = Nod(n, 0);
            planTotal += p;
            executatTotal += e;
        }

        // Activitățile fără nod WBS (sau cu un nod necunoscut) stau la rădăcină.
        var chei = noduri.Select(n => n.Cheie).ToHashSet(StringComparer.Ordinal);
        foreach (var a in activitati.Where(a => a.CodWbs is null || !chei.Contains(a.CodWbs)))
        {
            var (p, e) = Activitate(a, 0);
            planTotal += p;
            executatTotal += e;
        }

        var total = Rand(0, true, string.Empty, string.Empty, "Total grafic", null, planTotal, null, executatTotal, costuri.Total, pragAlerta);
        return new RaportBugetGrafic(randuri, total, costuri.FaraTinta, pragAlerta);
    }

    private static RandBugetGrafic Rand(
        int adancime, bool esteNod, string cheie, string cod, string denumire, StadiuActivitate? stadiu, decimal plan, decimal? procent, decimal executat,
        CostTinta cost, decimal prag)
    {
        plan = Rotunjit(plan);
        executat = Rotunjit(executat);
        var ac = Rotunjit(cost.MonedaGrafic ?? 0);
        var procentRand = procent ?? (plan == 0 ? 0 : Math.Round(executat / plan * 100m, 2, MidpointRounding.AwayFromZero));
        string? alerta = null;
        if (ac > 0 && plan == 0)
            alerta = "cost fără valoare planificată în grafic";
        else if (ac > 0 && ac > executat * (1 + prag))
            alerta = executat == 0
                ? "cost fără execuție raportată în grafic"
                : $"costul depășește valoarea executată cu {((ac / executat - 1) * 100).ToString("0.#", Ro)}%";
        return new RandBugetGrafic(
            adancime, esteNod, cheie, cod, denumire, stadiu, plan, procentRand, executat, ac, Rotunjit(cost.Lei), cost.Documente, cost.FaraCurs,
            executat - ac, ac == 0 ? null : Math.Round(executat / ac, 3, MidpointRounding.AwayFromZero),
            ac + Math.Max(plan - executat, 0), plan == 0 ? null : Math.Round(ac / plan * 100m, 2, MidpointRounding.AwayFromZero), alerta);
    }

    private static decimal Rotunjit(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
