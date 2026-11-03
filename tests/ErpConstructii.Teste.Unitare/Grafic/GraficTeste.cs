using System.Text;
using ErpConstructii.Grafic;
using ErpConstructii.Grafic.Import.Primavera;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Teste.Unitare.Grafic;

public class CalculatorProgresTeste
{
    private static readonly Guid X = Guid.NewGuid();
    private static readonly Guid Y = Guid.NewGuid();
    private static readonly DateOnly Z1 = new(2026, 3, 2);

    [Fact]
    public void Progresul_este_ponderat_valoric()
    {
        LegaturaArticol[] legaturi =
        [
            new("A", X, CantitateAlocata: 100m, PretUnitar: 10m),  // plan 1.000
            new("A", Y, CantitateAlocata: 50m, PretUnitar: 40m),   // plan 2.000
        ];
        CantitateRaportata[] raportari =
        [
            new(Z1, X, 30m), new(Z1.AddDays(2), X, 20m),           // X: 50 → 500
            new(Z1.AddDays(1), Y, 25m),                            // Y: 25 → 1.000
        ];

        var p = Assert.Single(CalculatorProgres.Calculeaza(["A"], legaturi, raportari, Z1.AddDays(5)));

        Assert.Equal(0.5m, p.Progres);                             // 1.500 / 3.000
        Assert.Equal(Z1, p.StartReal);
        Assert.Null(p.SfarsitReal);
    }

    [Fact]
    public void Articolul_impartit_intre_activitati_se_aloca_proportional()
    {
        LegaturaArticol[] legaturi = [new("A", X, 60m, 10m), new("B", X, 40m, 10m)];
        CantitateRaportata[] raportari = [new(Z1, X, 50m)];

        var r = CalculatorProgres.Calculeaza(["A", "B"], legaturi, raportari, Z1);

        Assert.All(r, p => Assert.Equal(0.5m, p.Progres));         // A: 30/60, B: 20/40
    }

    [Fact]
    public void Activitatea_fara_articole_are_progres_manual()
    {
        var p = Assert.Single(CalculatorProgres.Calculeaza(["AVIZE"], [], [], Z1));
        Assert.Null(p.Progres);
    }

    [Fact]
    public void Raportarile_de_dupa_data_de_raportare_sunt_ignorate_iar_100_procent_da_sfarsit_real()
    {
        LegaturaArticol[] legaturi = [new("A", X, 10m, 1m)];
        CantitateRaportata[] raportari = [new(Z1, X, 4m), new(Z1.AddDays(3), X, 6m), new(Z1.AddDays(10), X, 5m)];

        var laZ2 = Assert.Single(CalculatorProgres.Calculeaza(["A"], legaturi, raportari, Z1.AddDays(1)));
        Assert.Equal(0.4m, laZ2.Progres);

        var laZ5 = Assert.Single(CalculatorProgres.Calculeaza(["A"], legaturi, raportari, Z1.AddDays(5)));
        Assert.Equal(1m, laZ5.Progres);
        Assert.Equal(Z1.AddDays(3), laZ5.SfarsitReal);
    }
}

public class PrognozaTermeneTeste
{
    private static readonly DateOnly Start = new(2026, 3, 2);

    private static StareActivitate A(decimal progres = 0, DateOnly? startReal = null) =>
        new("A", Start, 10, [], progres, startReal, null);

    private static readonly StareActivitate B = new("B", Start, 5, [new Dependenta("A", TipDependenta.FS, 2)], 0, null, null);
    private static readonly StareActivitate C = new("C", Start.AddDays(3), 4, [new Dependenta("A", TipDependenta.SS, 3)], 0, null, null);

    [Fact]
    public void Nimic_inceput_urmeaza_dependentele()
    {
        var t = PrognozaTermene.Calculeaza([A(), B, C], Start.AddDays(-1)).Valoare.ToDictionary(x => x.Cod);

        Assert.Equal(new DateOnly(2026, 3, 12), t["A"].Sfarsit);
        Assert.Equal(new DateOnly(2026, 3, 14), t["B"].Start);    // FS + 2
        Assert.Equal(new DateOnly(2026, 3, 19), t["B"].Sfarsit);
        Assert.Equal(new DateOnly(2026, 3, 5), t["C"].Start);     // SS + 3
    }

    [Fact]
    public void Activitatea_inceputa_se_termina_dupa_durata_ramasa_si_impinge_succesorii()
    {
        var dataRaportare = new DateOnly(2026, 3, 10);
        var t = PrognozaTermene.Calculeaza([A(0.5m, Start), B, C], dataRaportare).Valoare.ToDictionary(x => x.Cod);

        Assert.Equal(new DateOnly(2026, 3, 15), t["A"].Sfarsit);  // 10 × 50% = 5 zile după 10.03
        Assert.Equal(new DateOnly(2026, 3, 17), t["B"].Start);
        Assert.Equal(dataRaportare, t["C"].Start);                // nu poate începe în trecut
    }

    [Fact]
    public void Metoda_ritm_extrapoleaza_din_zilele_scurse()
    {
        var dataRaportare = new DateOnly(2026, 3, 10);
        var t = PrognozaTermene.Calculeaza([A(0.5m, Start)], dataRaportare, MetodaDurataRamasa.Ritm).Valoare;

        Assert.Equal(new DateOnly(2026, 3, 18), t[0].Sfarsit);     // 8 zile pentru 50% → încă 8
    }

    [Fact]
    public void Dependenta_FF_aliniaza_sfarsiturile()
    {
        var d = new StareActivitate("D", Start, 3, [new Dependenta("A", TipDependenta.FF)], 0, null, null);
        var t = PrognozaTermene.Calculeaza([A(), d], Start.AddDays(-1)).Valoare.ToDictionary(x => x.Cod);
        Assert.Equal(t["A"].Sfarsit, t["D"].Sfarsit);
    }

    [Fact]
    public void Ciclul_si_predecesorul_lipsa_sunt_erori()
    {
        var x = new StareActivitate("X", Start, 1, [new Dependenta("Y", TipDependenta.FS)], 0, null, null);
        var y = new StareActivitate("Y", Start, 1, [new Dependenta("X", TipDependenta.FS)], 0, null, null);
        Assert.True(PrognozaTermene.Calculeaza([x, y], Start).Esec);

        var orfan = new StareActivitate("Z", Start, 1, [new Dependenta("NU", TipDependenta.FS)], 0, null, null);
        Assert.True(PrognozaTermene.Calculeaza([orfan], Start).Esec);
    }
}

public class DetectarePrimaveraTeste
{
    [Fact]
    public void Recunoaste_antetul_XER()
    {
        var xer = new SursaImport("grafic.xer", Encoding.UTF8.GetBytes("ERMHDR\t19.12\t2026-03-01"));
        Assert.True(new ImportatorPrimavera().PoateCiti(xer).Incredere >= AlegereImportator.PragIncredere);
    }

    [Fact]
    public void Respinge_alte_fisiere()
    {
        var alt = new SursaImport("grafic.xlsx", [0x50, 0x4B]);
        Assert.Equal(0, new ImportatorPrimavera().PoateCiti(alt).Incredere);
    }
}
