using ErpConstructii.Devize.Import.Isdp;

namespace ErpConstructii.Teste.Isdp;

/// <summary>Extrasul agregat de resurse = necesarul pentru aprovizionare și planificarea manoperei.</summary>
public class ExtrasResurseTeste
{
    private static IReadOnlyList<LinieExtras> Extras()
    {
        var deviz = ParserDevizResurse.Parseaza(Fixturi.Incarca(Fixturi.DevizResurse)).Valoare!;
        var (linii, probleme) = ExtrasResurse.Agrega(deviz);
        Assert.DoesNotContain(probleme, p => p.Severitate == SeveritateIsdp.Eroare);
        return linii;
    }

    [Fact]
    public void Douazeci_si_trei_de_resurse_distincte()
    {
        Assert.Equal(23, Extras().Count);
    }

    [Fact]
    public void Orele_de_sapator_se_aduna_din_toate_articolele()
    {
        var sapator = Extras().Single(l => l.Cod == "19621");

        Assert.Equal(TipResursaIsdp.Manopera, sapator.Tip);
        Assert.Equal(2397.8019m, sapator.CantitateTotala);          // 1745,2608 + 248,3121 + 404,229
        Assert.Equal(266156.01m, Math.Round(sapator.ValoareTotala, 2));
        Assert.Equal(new[] { "0002.1.1.1.1", "0002.1.1.3.4", "0002.1.1.3.5" }, sapator.Utilizari.Select(u => u.WbsArticol).ToArray());
    }

    [Fact]
    public void Materialul_din_mai_multe_articole_si_resursa_directa()
    {
        var linii = Extras();

        var dulap = linii.Single(l => l.Cod == "2917685");
        Assert.Equal(TipResursaIsdp.Material, dulap.Tip);
        Assert.Equal(0.457066m, dulap.CantitateTotala);              // 0,417066 + 0 + 0,04 + 0
        Assert.Equal(4, dulap.Utilizari.Count);

        var cherestea = linii.Single(l => l.Cod == "20011961");
        Assert.Equal(5m, cherestea.CantitateTotala);
        Assert.Equal(1m, Assert.Single(cherestea.Utilizari).ConsumUnitar);
    }

    [Fact]
    public void Valoarea_extrasului_este_valoarea_directa_a_devizului()
    {
        Assert.Equal(462673.86m, Math.Round(Extras().Sum(l => l.ValoareTotala), 2));
    }
}
