using ErpConstructii.Devize;
using ErpConstructii.Devize.Analize;
using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Teste.Unitare.Devize;

public class AnalizeDevizTeste
{
    [Fact]
    public void Copierea_tonelor_in_alt_capitol_al_stadiului_propune_volumul_de_referinta()
    {
        var deviz = Deviz();
        var stadiu = deviz.AdaugaCapitol("1", "Stadiu", TipCapitol.StadiuFizic);
        var sapaturi = deviz.AdaugaCapitol("1", "Săpătură", TipCapitol.Capitol, stadiu);
        var umpluturi = deviz.AdaugaCapitol("2", "Umpluturi", TipCapitol.Capitol, stadiu);
        var referinta = sapaturi.AdaugaArticol("S", "Săpătură", "mc", 320.82m, ValoriResurse.Zero);
        var masa = sapaturi.AdaugaArticol("T", "Transport", "tona", 577.47m, ValoriResurse.Zero);
        var gresit = umpluturi.AdaugaArticol("U", "Umplutură", "mc", 577.47m, new(0, 100, 0, 0));
        gresit.SeteazaPretOferta(new(0, 110, 0, 0), new(0, 63521.7m, 0, 0));

        var rezultat = new AnalizorAmprentaNumerelor().Analizeaza(new(deviz, new()));
        var constatare = Assert.Single(rezultat.Constatari, c => c.Cod == "A2.UNITATE_COPIATA");

        Assert.Equal("A2.UNITATE_COPIATA", constatare.Cod);
        Assert.Equal(SeveritateAnaliza.Info, constatare.Severitate);
        Assert.Equal(new CorecturaCantitate(gresit.CheieStabila, 577.47m, 320.82m), constatare.Corectura);
        // (320,82 - 577,47) × 100 = -25.665 lei; la ofertă × 110 = -28.231,50 lei.
        Assert.Equal(-25665m, constatare.ImpactCostDirect);
        Assert.Equal(-28231.50m, constatare.ImpactOferta);
        Assert.Contains(referinta.CheieStabila, constatare.Articole);
        Assert.Contains(masa.CheieStabila, constatare.Articole);
    }

    [Fact]
    public void Nu_compara_articole_din_stadii_diferite_si_semnaleaza_unitatile_necunoscute()
    {
        var deviz = Deviz();
        var a = deviz.AdaugaCapitol("1", "A", TipCapitol.StadiuFizic);
        var b = deviz.AdaugaCapitol("2", "B", TipCapitol.StadiuFizic);
        a.AdaugaArticol("S", "Săpătură", "mc", 577.47m, ValoriResurse.Zero);
        b.AdaugaArticol("T", "Transport", "tona", 577.47m, ValoriResurse.Zero);
        b.AdaugaArticol("X", "Necunoscut", "necunoscut", 10m, ValoriResurse.Zero);

        var rezultat = new AnalizorAmprentaNumerelor().Analizeaza(new(deviz, new()));

        Assert.Empty(rezultat.Constatari);
        Assert.Contains(rezultat.Limitari, x => x.Contains("necunoscut", StringComparison.Ordinal));
    }

    [Fact]
    public void Mai_multe_volume_plauzibile_nu_produc_o_corectura_arbitrara()
    {
        var deviz = Deviz();
        var c = deviz.AdaugaCapitol("1", "Stadiu", TipCapitol.StadiuFizic);
        c.AdaugaArticol("T", "Transport", "tona", 180m, ValoriResurse.Zero);
        c.AdaugaArticol("A", "A", "mc", 180m, ValoriResurse.Zero);
        c.AdaugaArticol("B", "B", "mc", 100m, ValoriResurse.Zero);
        c.AdaugaArticol("C", "C", "mc", 75m, ValoriResurse.Zero);

        var rezultat = new AnalizorAmprentaNumerelor().Analizeaza(new(deviz, new()));

        Assert.Null(Assert.Single(rezultat.Constatari, c => c.Cod == "A2.UNITATE_COPIATA").Corectura);
    }

    [Fact]
    public void Converteste_unitatile_inainte_de_raport_si_nu_inventeaza_impact_in_lei_fara_curs()
    {
        var deviz = Deviz();
        deviz.SeteazaMoneda("EUR", null);
        var c = deviz.AdaugaCapitol("1", "Stadiu", TipCapitol.StadiuFizic);
        c.AdaugaArticol("T", "Transport", "kg", 180m, ValoriResurse.Zero);
        c.AdaugaArticol("S", "Săpătură", "mc", 0.1m, ValoriResurse.Zero);
        var gresit = c.AdaugaArticol("U", "Umplutură", "mc", 180m, ValoriResurse.Zero);
        gresit.SeteazaPretOferta(new(1, 0, 0, 0), new(180, 0, 0, 0));

        var rezultat = new AnalizorAmprentaNumerelor().Analizeaza(new(deviz, new(CantitateMinima: 0)));
        var constatare = Assert.Single(rezultat.Constatari, c => c.Cod == "A2.UNITATE_COPIATA");
        Assert.Contains(rezultat.Constatari, c => c.Cod == "A2.FACTOR_CONVERSIE");

        // 180 kg = 0,18 t; 0,18 t / 0,1 mc = 1,8 t/mc.
        Assert.Equal(0.1m, constatare.Corectura!.CantitateNoua);
        Assert.Null(constatare.ImpactOferta);
    }

    [Fact]
    public void Amprenta_este_stabila_la_reanaliza_si_se_schimba_cand_dovezile_se_schimba()
    {
        var deviz = Deviz();
        var a = deviz.AdaugaObiect("1", "Obiect").AdaugaArticol("A", "Articol", "mc", 10, ValoriResurse.Zero);
        a.AdaugaConsum("apa", "Apă", TipResursa.Material, "mc", 1, 0);
        var analizor = new AnalizorPreturiSpeciale();
        var context = new ContextAnaliza(deviz, new());

        var prima = Assert.Single(analizor.Analizeaza(context).Constatari);
        var aDoua = Assert.Single(analizor.Analizeaza(context).Constatari);
        Assert.Equal(prima.Amprenta(), aDoua.Amprenta());
        var schimbata = prima with { Dovezi = [new("resursa", "alta")] };
        Assert.NotEqual(prima.Amprenta(), schimbata.Amprenta());
    }

    [Fact]
    public void Pret_zero_si_tarif_uniform_se_raporteaza_fara_istoric()
    {
        var deviz = Deviz();
        var c = deviz.AdaugaObiect("1", "Obiect");
        var a = c.AdaugaArticol("A", "Articol", "mc", 10, ValoriResurse.Zero);
        a.AdaugaConsum("apa", "Apă", TipResursa.Material, "mc", 1, 0);
        a.AdaugaConsum("M1", "Muncitor 1", TipResursa.Manopera, "ora", 1, 111);
        a.AdaugaConsum("M2", "Muncitor 2", TipResursa.Manopera, "ora", 1, 111);
        a.AdaugaConsum("M3", "Fără consum", TipResursa.Manopera, "ora", 0, 0);

        var rezultat = new AnalizorPreturiSpeciale().Analizeaza(new(deviz, new()));

        Assert.Equal(2, rezultat.Constatari.Count);
        Assert.Single(rezultat.Constatari, x => x.Cod == "A5.PRET_ZERO");
        Assert.Single(rezultat.Constatari, x => x.Cod == "A5.TARIF_UNIFORM");
        Assert.NotEmpty(rezultat.Limitari);
    }

    [Fact]
    public void Manopera_cu_tarif_zero_nu_este_ignorata_la_verificarea_tarifului_uniform()
    {
        var deviz = Deviz();
        var a = deviz.AdaugaObiect("1", "Obiect").AdaugaArticol("A", "Articol", "mc", 10, ValoriResurse.Zero);
        a.AdaugaConsum("M1", "Muncitor 1", TipResursa.Manopera, "ora", 1, 111);
        a.AdaugaConsum("M2", "Muncitor 2", TipResursa.Manopera, "ora", 1, 111);
        a.AdaugaConsum("M3", "Muncitor 3", TipResursa.Manopera, "ora", 1, 0);

        var rezultat = new AnalizorPreturiSpeciale().Analizeaza(new(deviz, new()));

        Assert.Single(rezultat.Constatari, x => x.Cod == "A5.PRET_ZERO");
        Assert.DoesNotContain(rezultat.Constatari, x => x.Cod == "A5.TARIF_UNIFORM");
    }

    [Fact]
    public void Registrul_ruleaza_doar_analizorii_activi_si_respinge_codurile_necunoscute()
    {
        var registru = new RegistruAnalizori([new AnalizorAmprentaNumerelor(), new AnalizorPreturiSpeciale()]);
        var context = new ContextAnaliza(Deviz(), new());

        Assert.Equal("A5", Assert.Single(registru.Analizeaza(context, new HashSet<string> { "A5" })).Cod);
        Assert.Throws<ArgumentException>(() => registru.Analizeaza(context, new HashSet<string> { "A9" }));
    }

    private static VersiuneDeviz Deviz() => new(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "Deviz");
}
