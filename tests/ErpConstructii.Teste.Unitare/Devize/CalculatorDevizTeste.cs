using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Teste.Unitare.Devize;

public class CalculatorDevizTeste
{
    private static readonly ParametriRecapitulatie Parametri = new(
        CotaContributiiManopera: 0.0225m,
        CotaCheltuieliIndirecte: 0.10m,
        CotaProfit: 0.05m,
        CotaTva: 0.21m);

    // Valorile așteptate sunt calculate de mână (vezi comentariile), la ban.
    [Fact]
    public void Recapitulatie_doua_linii_iese_la_ban()
    {
        LinieDeviz[] linii =
        [
            // 10 × (100; 50; 20; 5) = 1000; 500; 200; 50
            new(10m, new ValoriResurse(100m, 50m, 20m, 5m)),
            // 2,5 × (40,123; 10,555; 0; 1,111) = 100,3075→100,31; 26,3875→26,39; 0; 2,7775→2,78
            new(2.5m, new ValoriResurse(40.123m, 10.555m, 0m, 1.111m)),
        ];

        var r = CalculatorDeviz.Calculeaza(linii, Parametri);

        Assert.Equal(new ValoriResurse(1100.31m, 526.39m, 200m, 52.78m), r.CheltuieliDirecteResurse);
        Assert.Equal(11.84m, r.ContributiiManopera);       // 526,39 × 2,25% = 11,843775
        Assert.Equal(1891.32m, r.TotalCheltuieliDirecte);  // 1879,48 + 11,84
        Assert.Equal(189.13m, r.CheltuieliIndirecte);      // 1891,32 × 10% = 189,132
        Assert.Equal(104.02m, r.Profit);                   // 2080,45 × 5% = 104,0225
        Assert.Equal(2184.47m, r.TotalFaraTva);
        Assert.Equal(458.74m, r.Tva);                      // 2184,47 × 21% = 458,7387
        Assert.Equal(2643.21m, r.TotalCuTva);
    }

    [Fact]
    public void Rotunjirea_este_jumatate_departe_de_zero()
    {
        // 0,125 → 0,13 (rotunjirea bancară ar da 0,12)
        var valoare = CalculatorDeviz.ValoareLinie(new LinieDeviz(1m, new ValoriResurse(0.125m, 0, 0, 0)), SharedKernel.Rotunjire.Lei);
        Assert.Equal(0.13m, valoare.Material);
    }

    [Fact]
    public void Cotele_date_in_procente_in_loc_de_fractii_sunt_respinse()
    {
        var gresit = Parametri with { CotaTva = 21m };
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculatorDeviz.Calculeaza([], gresit));
    }

    [Fact]
    public void Deviz_gol_are_totaluri_zero()
    {
        var r = CalculatorDeviz.Calculeaza([], Parametri);
        Assert.Equal(0m, r.TotalCuTva);
    }
}
