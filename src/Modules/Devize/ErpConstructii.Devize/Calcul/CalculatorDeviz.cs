using ErpConstructii.SharedKernel;

namespace ErpConstructii.Devize.Calcul;

/// <summary>O linie de deviz pentru calcul: cantitate × preț unitar pe resurse.</summary>
public readonly record struct LinieDeviz(decimal Cantitate, ValoriResurse PretUnitar);

/// <summary>
/// Parametrii recapitulației. NU au valori implicite legale în cod: cotele (contribuții pe
/// manoperă, cheltuieli indirecte, profit, TVA) vin din setările firmei sau din devizul importat,
/// pentru că se schimbă prin lege și diferă de la ofertă la ofertă (vezi cotele TVA schimbate în 2025).
/// </summary>
public sealed record ParametriRecapitulatie(
    decimal CotaContributiiManopera,
    decimal CotaCheltuieliIndirecte,
    decimal CotaProfit,
    decimal CotaTva)
{
    /// <summary>Rotunjirea valorii fiecărei linii pe fiecare resursă.</summary>
    public Rotunjire RotunjireLinie { get; init; } = Rotunjire.Lei;

    /// <summary>Rotunjirea fiecărui rând din recapitulație.</summary>
    public Rotunjire RotunjireRecapitulatie { get; init; } = Rotunjire.Lei;

    public void Valideaza()
    {
        foreach (var (nume, valoare) in new[]
                 {
                     (nameof(CotaContributiiManopera), CotaContributiiManopera),
                     (nameof(CotaCheltuieliIndirecte), CotaCheltuieliIndirecte),
                     (nameof(CotaProfit), CotaProfit),
                     (nameof(CotaTva), CotaTva),
                 })
        {
            if (valoare is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nume, valoare, "Cotele se dau ca fracție între 0 și 1 (ex. 0,21 pentru 21%).");
            }
        }
    }
}

/// <summary>Recapitulația unui deviz-ofertă, în ordinea formularului uzual.</summary>
public sealed record RecapitulatieDeviz(
    ValoriResurse CheltuieliDirecteResurse,
    decimal ContributiiManopera,
    decimal TotalCheltuieliDirecte,
    decimal CheltuieliIndirecte,
    decimal Profit,
    decimal TotalFaraTva,
    decimal Tva,
    decimal TotalCuTva);

/// <summary>
/// Motorul de calcul al devizului. Funcție pură: aceleași intrări → același rezultat, la ban.
///
/// Ordinea calculului (recapitulația uzuală a devizului-ofertă):
///   I.   Cheltuieli directe pe resurse = Σ rotunjit(cantitate × preț unitar resursă)
///   II.  Contribuții asupra manoperei   = rotunjit(manoperă × cotă)
///        Total cheltuieli directe        = I + II
///   III. Cheltuieli indirecte            = rotunjit(total directe × cotă indirecte)
///   IV.  Profit                          = rotunjit((total directe + indirecte) × cotă profit)
///        Total fără TVA                  = total directe + indirecte + profit
///        TVA                             = rotunjit(total fără TVA × cotă TVA)
///
/// ATENȚIE: înainte de a declara importatorul WinDoc gata, ordinea și regulile de rotunjire
/// se confirmă pe devize reale (testul de regresie trebuie să iasă la ban față de totalurile
/// din fișier). Dacă programul sursă rotunjește altfel, se adaugă opțiuni în
/// <see cref="ParametriRecapitulatie"/>, nu cazuri speciale în cod.
/// </summary>
public static class CalculatorDeviz
{
    public static ValoriResurse ValoareLinie(LinieDeviz linie, Rotunjire rotunjire) =>
        (linie.PretUnitar * linie.Cantitate).Rotunjeste(rotunjire);

    public static RecapitulatieDeviz Calculeaza(IEnumerable<LinieDeviz> linii, ParametriRecapitulatie p)
    {
        p.Valideaza();
        var r = p.RotunjireRecapitulatie;

        var directe = linii.Aggregate(ValoriResurse.Zero, (acc, l) => acc + ValoareLinie(l, p.RotunjireLinie));
        var contributii = r.Aplica(directe.Manopera * p.CotaContributiiManopera);
        var totalDirecte = directe.Total + contributii;
        var indirecte = r.Aplica(totalDirecte * p.CotaCheltuieliIndirecte);
        var profit = r.Aplica((totalDirecte + indirecte) * p.CotaProfit);
        var totalFaraTva = totalDirecte + indirecte + profit;
        var tva = r.Aplica(totalFaraTva * p.CotaTva);

        return new RecapitulatieDeviz(
            directe,
            contributii,
            totalDirecte,
            indirecte,
            profit,
            totalFaraTva,
            tva,
            totalFaraTva + tva);
    }
}
