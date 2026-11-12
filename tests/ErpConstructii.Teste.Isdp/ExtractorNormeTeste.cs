using ErpConstructii.Devize.Import.Isdp;

namespace ErpConstructii.Teste.Isdp;

/// <summary>Popularea bibliotecii de norme din devizul real.</summary>
public class ExtractorNormeTeste
{
    private static (IReadOnlyList<NormaExtrasa> Norme, IReadOnlyList<ProblemaIsdp> Probleme) Extrage(Func<int, int, object?, object?>? transforma = null)
    {
        var foaie = transforma is null ? Fixturi.Incarca(Fixturi.DevizResurse) : Fixturi.Incarca(Fixturi.DevizResurse, transforma);
        return ExtractorNorme.Extrage(ParserDevizResurse.Parseaza(foaie).Valoare!);
    }

    [Fact]
    public void Unsprezece_norme_fara_resursa_directa()
    {
        var (norme, probleme) = Extrage();

        // 15 articole: TRI1AA01C2 apare de 3 ori și TRB01A11 de 2 ori cu aceleași consumuri,
        // iar 20011961 (Cherestea) e resursă directă, nu normă → 15 − 2 − 1 − 1 = 11.
        Assert.Equal(11, norme.Count);
        Assert.DoesNotContain(norme, n => n.Simbol == "20011961");
        Assert.Empty(probleme);
    }

    [Fact]
    public void Aparitiile_identice_se_comaseaza_cu_toate_WBS_urile()
    {
        var incarcare = Extrage().Norme.Single(n => n.Simbol == "TRI1AA01C2");

        Assert.Equal(new[] { "0002.1.1.1.4", "0002.1.1.2.2", "0002.1.1.3.2" }, incarcare.WbsSursa.ToArray());
        var muncitor = Assert.Single(incarcare.Consumuri);
        Assert.Equal("319721", muncitor.CodResursa);
        Assert.Equal(0.35m, muncitor.ConsumUnitar);       // 202,1145 ore / 577,47 t
    }

    [Fact]
    public void Consumurile_unitare_ale_sapaturii()
    {
        var sapatura = Extrage().Norme.Single(n => n.Simbol == "TSA04G1");

        Assert.Equal("mc", sapatura.UnitateMasura);
        Assert.Equal(6, sapatura.Consumuri.Count);
        Assert.Equal(0.0007m, sapatura.Consumuri.Single(c => c.CodResursa == "2900888").ConsumUnitar);
        Assert.Equal(5.44m, sapatura.Consumuri.Single(c => c.CodResursa == "19621").ConsumUnitar);   // 1745,2608 / 320,82
        Assert.Equal(TipResursaIsdp.Manopera, sapatura.Consumuri.Single(c => c.CodResursa == "19621").Tip);
    }

    [Fact]
    public void Norma_asimilata_pastreaza_simbolul_de_baza_si_resursele_cu_consum_zero()
    {
        var asim = Extrage().Norme.Single(n => n.Simbol == "TSF01A1 asim");

        Assert.Equal(TipArticolIsdp.Asimilat, asim.TipArticol);
        Assert.Equal("TSF01A1", asim.SimbolBaza);
        Assert.Equal(0m, asim.Consumuri.Single(c => c.CodResursa == "2917685").ConsumUnitar);
    }

    [Fact]
    public void Acelasi_simbol_cu_consumuri_diferite_devine_doua_variante()
    {
        // A doua „Incarcare in roaba” (2,4 t): 0,84 ore → 1,0 ore, adică 0,4167 ore/t în loc de 0,35.
        var (norme, probleme) = Extrage((_, _, v) => v is double d && d == 0.84 ? 1.0 : v);

        var variante = norme.Where(n => n.Simbol == "TRI1AA01C2").ToList();
        Assert.Equal(2, variante.Count);
        Assert.NotEqual(variante[0].Amprenta, variante[1].Amprenta);
        Assert.Contains(probleme, p => p.Severitate == SeveritateIsdp.Avertisment && p.Locatie == "norma TRI1AA01C2");
    }

    [Fact]
    public void Amprenta_nu_depinde_de_pret()
    {
        var a = new ConsumNorma("19621", "Sapator", "ora", TipResursaIsdp.Manopera, false, 5.44m, 111m);
        var b = a with { PretUnitar = 130m };

        Assert.Equal(
            NormaExtrasa.CalculeazaAmprenta("TSA04G1", "mc", [a]),
            NormaExtrasa.CalculeazaAmprenta("TSA04G1", "mc", [b]));
    }
}
