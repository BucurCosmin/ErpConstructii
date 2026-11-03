using ErpConstructii.Contracte;

namespace ErpConstructii.Teste.Unitare.Contracte;

public class CalculatorSituatieTeste
{
    private static readonly ParametriSituatie Parametri = new(
        ProcentRecuperareAvans: 0.10m,
        ProcentRetinereGarantie: 0.05m,
        CotaTva: 0.21m,
        TaxareInversa: false);

    [Fact]
    public void Situatie_cu_ajustare_avans_si_garantie()
    {
        var r = CalculatorSituatie.Calculeaza(new IntrariSituatie(100_000m, 2_500m, 30_000m), Parametri);

        Assert.Equal(102_500m, r.ValoareSituatie);
        Assert.Equal(10_250m, r.RecuperareAvans);      // 10% din 102.500, sub avansul rămas
        Assert.Equal(92_250m, r.BazaImpozabila);
        Assert.Equal(19_372.50m, r.Tva);
        Assert.Equal(111_622.50m, r.TotalFactura);
        Assert.Equal(5_125m, r.RetinereGarantie);      // 5% din 102.500
        Assert.Equal(106_497.50m, r.DeIncasat);
        Assert.Equal(19_750m, r.AvansRamasDupa);
    }

    [Fact]
    public void Taxarea_inversa_nu_pune_TVA()
    {
        var r = CalculatorSituatie.Calculeaza(new IntrariSituatie(100_000m, 0m, 0m), Parametri with { TaxareInversa = true });
        Assert.Equal(0m, r.Tva);
        Assert.Equal(100_000m, r.TotalFactura);
    }

    [Fact]
    public void Recuperarea_nu_depaseste_avansul_ramas()
    {
        var r = CalculatorSituatie.Calculeaza(new IntrariSituatie(100_000m, 0m, 3_000m), Parametri);
        Assert.Equal(3_000m, r.RecuperareAvans);
        Assert.Equal(0m, r.AvansRamasDupa);
    }
}

public class FormulaAjustareTeste
{
    private static readonly FormulaAjustare Formula = new(0.15m,
    [
        new TermenAjustare("MAT", 0.50m, 100m),
        new TermenAjustare("MAN", 0.35m, 200m),
    ]);

    [Fact]
    public void Coeficient_si_diferenta()
    {
        var indici = new Dictionary<string, decimal> { ["MAT"] = 110m, ["MAN"] = 210m };

        // 0,15 + 0,50 × 1,10 + 0,35 × 1,05 = 1,0675
        Assert.Equal(1.0675m, Formula.Coeficient(indici).Valoare);
        Assert.Equal(6_750m, Formula.Diferenta(100_000m, indici).Valoare);
    }

    [Fact]
    public void Ponderile_care_nu_dau_1_sunt_respinse()
    {
        var gresita = Formula with { ParteFixa = 0.20m };
        Assert.True(gresita.Valideaza().Esec);
    }

    [Fact]
    public void Indicele_curent_lipsa_este_eroare_nu_zero()
    {
        var r = Formula.Coeficient(new Dictionary<string, decimal> { ["MAT"] = 110m });
        Assert.True(r.Esec);
        Assert.Contains("MAN", r.Eroare.Mesaj);
    }
}
