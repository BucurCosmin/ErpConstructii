using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Teste.Unitare.Devize;

/// <summary>„Actualizează prețurile” (D-55, etapa 3): diferențele deviz / achiziții și aplicarea lor în ciornă.</summary>
public class ActualizarePreturiTeste
{
    /// <summary>
    /// Două articole cu betonul „2100133” (400 lei/mc în deviz) și un săpător (111 lei/oră, nelegat):
    /// CB1: 10 mc, consum beton 1,02 mc/mc → PU material 408 (1,02 × 400), manoperă 2 ore × 111 = 222;
    /// CB2: 5 mc, consum beton 1,05 mc/mc → PU material 420 (1,05 × 400).
    /// </summary>
    private static VersiuneDeviz Deviz()
    {
        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "Test");
        var c = v.AdaugaCapitol("1", "Fundații", TipCapitol.Capitol);
        var a1 = c.AdaugaArticol("CB1", "Beton fundații", "mc", 10m, new ValoriResurse(408m, 222m, 0, 0));
        a1.AdaugaConsum("2100133", "Beton C16/20", TipResursa.Material, "mc", 1.02m, 400m);
        a1.AdaugaConsum("19621", "Săpător", TipResursa.Manopera, "ora", 2m, 111m);
        var a2 = c.AdaugaArticol("CB2", "Beton elevații", "mc", 5m, new ValoriResurse(420m, 0, 0, 0));
        a2.AdaugaConsum("2100133", "Beton C16/20", TipResursa.Material, "mc", 1.05m, 400m);
        return v;
    }

    private static StatisticaAchizitie Achizitii(decimal ultimul, decimal? medie, decimal? minim) =>
        new("2100133", "mc", 3, minim ?? ultimul, ultimul, ultimul, new DateOnly(2026, 10, 1), "Beton SRL", "NIR/2026/0001", medie, medie is null ? 0 : 2, minim);

    [Fact]
    public void Raportul_arata_diferenta_si_impactul_pe_costul_direct()
    {
        var r = ActualizarePreturi.Propune(Deviz(), new Dictionary<string, StatisticaAchizitie> { ["2100133"] = Achizitii(460m, 450m, 440m) },
            new HashSet<string> { "2100133" }, PoliticaPret.MedieInterval);

        var beton = r.Resurse.Single(x => x.Cod == "2100133");
        // Cantitatea în deviz: 1,02 × 10 + 1,05 × 5 = 15,45 mc; impact: 15,45 × (450 − 400) = 772,50 lei.
        Assert.Equal((15.45m, 400m, 450m, 772.5m, 2), (beton.CantitateDeviz, beton.PretDeviz, beton.PretPropus!.Value, beton.Impact, beton.Articole));
        Assert.Equal((StarePropunere.Diferit, 0.125m), (beton.Stare, beton.Diferenta!.Value)); // (450 − 400) / 400
        Assert.Equal(StarePropunere.Nelegata, r.Resurse.Single(x => x.Cod == "19621").Stare);
        Assert.Equal(772.5m, r.ImpactTotal);
        // Cost direct actual: 10 × (408 + 222) + 5 × 420 = 6.300 + 2.100 = 8.400.
        Assert.Equal(8400m, r.CostDirectActual);
        Assert.Empty(r.ArticoleModificateManual);
    }

    [Theory]
    [InlineData(PoliticaPret.Ultimul, 460)]
    [InlineData(PoliticaPret.MinimInterval, 440)]
    public void Politica_alege_pretul_propus(PoliticaPret politica, decimal asteptat)
    {
        var r = ActualizarePreturi.Propune(Deviz(), new Dictionary<string, StatisticaAchizitie> { ["2100133"] = Achizitii(460m, 450m, 440m) },
            new HashSet<string> { "2100133" }, politica);

        Assert.Equal(asteptat, r.Resurse.Single(x => x.Cod == "2100133").PretPropus);
    }

    [Fact]
    public void Fara_achizitii_recente_media_lipseste_si_resursa_e_semnalata()
    {
        var r = ActualizarePreturi.Propune(Deviz(), new Dictionary<string, StatisticaAchizitie> { ["2100133"] = Achizitii(460m, null, null) },
            new HashSet<string> { "2100133" }, PoliticaPret.MedieInterval);

        var beton = r.Resurse.Single(x => x.Cod == "2100133");
        Assert.Equal((StarePropunere.FaraAchizitiiRecente, (decimal?)null, 0m), (beton.Stare, beton.PretPropus, beton.Impact));
    }

    [Fact]
    public void Aplicarea_ajusteaza_doar_componenta_resursei_cu_diferenta()
    {
        var v = Deviz();
        var a1 = v.Articole().Single(a => a.Simbol == "CB1");
        // Manopera lui CB1 modificată manual la 250 (resursele dau 222): după aplicare rămâne 250.
        Assert.True(v.ModificaArticol(a1.Id, new DateArticol("CB1", "Beton fundații", "mc", 10m, new ValoriResurse(408m, 250m, 0, 0))).Succes);
        Assert.Single(ActualizarePreturi.Propune(v, new Dictionary<string, StatisticaAchizitie>(), new HashSet<string>(), PoliticaPret.MedieInterval).ArticoleModificateManual);

        var r = v.ActualizeazaPreturiResurse(new Dictionary<string, decimal> { ["2100133"] = 450m });

        Assert.Equal(2, r.Valoare);
        // CB1: material 408 + 1,02 × (450 − 400) = 459; manopera rămâne 250. CB2: 420 + 1,05 × 50 = 472,50.
        Assert.Equal((459m, 250m), (a1.PretUnitar.Material, a1.PretUnitar.Manopera));
        Assert.Equal(472.5m, v.Articole().Single(a => a.Simbol == "CB2").PretUnitar.Material);
        Assert.All(v.Articole().SelectMany(a => a.Consumuri).Where(c => c.CodResursa == "2100133"), c => Assert.Equal(450m, c.PretUnitar));
        // Cost direct nou: 10 × (459 + 250) + 5 × 472,50 = 7.090 + 2.362,50 = 9.452,50.
        Assert.Equal(9452.5m, v.Recapitulatie().CheltuieliDirecteResurse.Total);
    }

    [Fact]
    public void Versiunea_aprobata_nu_primeste_preturi()
    {
        var v = Deviz();
        Assert.True(v.Aproba().Succes);

        Assert.True(v.ActualizeazaPreturiResurse(new Dictionary<string, decimal> { ["2100133"] = 450m }).Esec);
    }
}
