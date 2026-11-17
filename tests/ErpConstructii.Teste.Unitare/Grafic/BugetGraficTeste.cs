using ErpConstructii.Grafic;

namespace ErpConstructii.Teste.Unitare.Grafic;

public class BugetGraficTeste
{
    private static readonly DateOnly Zi = new(2026, 11, 13);

    private static ActivitateCanonica Activitate(string cod, string wbs, decimal plan, decimal procent) =>
        new(cod, $"Activitatea {cod}", wbs, Zi, Zi, 5, false, [], new Dictionary<string, string>(), Procent: procent,
            Resurse: [new ResursaActivitate("R", "resursă", TipResursaGrafic.Material, null, 1, 0, 1, plan, plan, 0, plan)]);

    private static RaportBugetGrafic Raport()
    {
        NodWbsCanonic[] noduri = [new("B5", "B5", "Clădirea 5", null, 0), new("B5.FUND", "FUND", "Fundații", "B5", 1), new("M", "M", "Jaloane", null, 2)];
        ActivitateCanonica[] activitati =
        [
            Activitate("A1", "B5.FUND", 1000m, 50m),
            Activitate("A2", "B5.FUND", 3000m, 0m),
            Activitate("M1", "M", 0m, 0m),
        ];
        // Costurile ERP (în EUR): A1 600; 200 alocați direct pe B5; M1 50 (fără valoare în grafic).
        var costuri = new RezultatCosturiGrafic(
            new Dictionary<string, CostTinta> { ["A1"] = new(3000m, 600m, 1, 0), ["M1"] = new(250m, 50m, 1, 0) },
            new Dictionary<string, CostTinta> { ["B5.FUND"] = new(3000m, 600m, 1, 0), ["B5"] = new(4000m, 800m, 2, 0), ["M"] = new(250m, 50m, 1, 0) },
            new CostTinta(4250m, 850m, 3, 0), new CostTinta(0, 0, 0, 0), [], []);
        return BugetGrafic.Construieste(noduri, activitati, costuri);
    }

    [Fact]
    public void Randurile_urmeaza_arborele_cu_activitatile_sub_nodul_lor()
    {
        Assert.Equal(["B5", "B5.FUND", "A1", "A2", "M", "M1"], Raport().Randuri.Select(r => r.Cheie));
        Assert.Equal([0, 1, 2, 2, 0, 1], Raport().Randuri.Select(r => r.Adancime));
    }

    [Fact]
    public void Activitatea_are_valoarea_executata_abaterea_indicele_si_prognoza()
    {
        var a1 = Raport().Randuri.Single(r => r.Cheie == "A1");

        Assert.Equal(500m, a1.ValoareExecutata);           // 1.000 × 50%
        Assert.Equal(-100m, a1.Abatere);                   // 500 − 600
        Assert.Equal(0.833m, a1.IndiceCost);               // 500 / 600
        Assert.Equal(1100m, a1.Prognoza);                  // 600 + (1.000 − 500)
        Assert.Equal(60m, a1.ProcentConsumat);             // 600 / 1.000
        Assert.Equal("costul depășește valoarea executată cu 20%", a1.Alerta);   // 600 > 500 × 1,1
    }

    [Fact]
    public void Nodul_aduna_valorile_activitatilor_si_ia_costul_totalizat()
    {
        var r = Raport().Randuri;
        var fund = r.Single(x => x.Cheie == "B5.FUND");
        var b5 = r.Single(x => x.Cheie == "B5");

        Assert.Equal((4000m, 500m, 12.5m), (fund.ValoarePlanificata, fund.ValoareExecutata, fund.Procent));   // 500 / 4.000
        Assert.Equal(4100m, fund.Prognoza);                // 600 + 3.500
        Assert.Equal((800m, -300m, 0.625m), (b5.CostErp, b5.Abatere, b5.IndiceCost));   // 200 direct pe B5; 500 / 800
        Assert.Null(r.Single(x => x.Cheie == "A2").Alerta);
    }

    [Fact]
    public void Costul_fara_valoare_planificata_e_semnalat_iar_totalul_ia_toate_costurile()
    {
        var raport = Raport();

        Assert.Equal("cost fără valoare planificată în grafic", raport.Randuri.Single(x => x.Cheie == "M1").Alerta);
        Assert.Equal((4000m, 500m, 850m), (raport.Total.ValoarePlanificata, raport.Total.ValoareExecutata, raport.Total.CostErp));
    }
}
