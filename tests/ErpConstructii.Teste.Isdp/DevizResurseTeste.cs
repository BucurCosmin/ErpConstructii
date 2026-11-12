using ErpConstructii.Devize.Import.Isdp;

namespace ErpConstructii.Teste.Isdp;

/// <summary>„Deviz ofertă cu resurse” real: șanț investigații utilități (chilleri), 15 articole, 3 capitole.</summary>
public class DevizResurseTeste
{
    private static DevizIsdp Deviz(out IReadOnlyList<ProblemaIsdp> probleme)
    {
        var r = ParserDevizResurse.Parseaza(Fixturi.Incarca(Fixturi.DevizResurse));
        probleme = r.Probleme;
        Assert.NotNull(r.Valoare);
        return r.Valoare!;
    }

    private static NodIsdp Nod(DevizIsdp d, string wbs) => d.Toate().Single(n => n.Wbs == wbs);

    [Fact]
    public void Antetul_si_moneda()
    {
        var d = Deviz(out _);
        Assert.Equal("UNITATEA 1 INVESTIGATII", d.Antet.Obiectiv);
        Assert.Equal("INVESTIGATII", d.Antet.Obiect);
        Assert.Equal("SANT INVESTIGATII UTILITATI (CHILLERI)25M X 2M X1M", d.Antet.StadiuFizic);
        Assert.Equal("", d.Antet.Beneficiar);           // „______” din șablonul gol
        Assert.Equal("RON", d.Moneda);
    }

    [Fact]
    public void Arborele_WBS_stadiu_capitole_articole_resurse()
    {
        var d = Deviz(out _);

        var stadiu = Assert.Single(d.Radacini);
        Assert.Equal("0002.1.1", stadiu.Wbs);
        Assert.Equal(TipNodIsdp.Grup, stadiu.Tip);

        var capitole = stadiu.Copii;
        Assert.Equal(new[] { "SAPATURA SI EVACUARE", "SPARGERE BETON,sprijiniri utilitati", "ADUCERE LA STAREA INITIALA" },
            capitole.Select(c => c.Denumire).ToArray());
        Assert.All(capitole, c => Assert.Equal(TipNodIsdp.Grup, c.Tip));

        Assert.Equal(15, d.Articole().Count());
        Assert.Equal(new[] { 5, 5, 5 }, capitole.Select(c => c.Copii.Count).ToArray());
    }

    [Fact]
    public void Articolul_normat_are_resursele_cu_tip_si_consum_unitar()
    {
        var d = Deviz(out _);
        var a = Nod(d, "0002.1.1.1.1");

        Assert.Equal(TipNodIsdp.Articol, a.Tip);
        Assert.Equal(TipArticolIsdp.Normat, a.TipArticol);
        Assert.Equal("TSA04G1", a.Simbol);
        Assert.Equal("Sapatura manuala de pamant in teren tare", a.Denumire);
        Assert.Equal("mc", a.UnitateMasura);
        Assert.Equal(320.82m, a.Cantitate);
        Assert.Equal(6, a.Copii.Count);

        var lemn = a.Copii.Single(r => r.Simbol == "2900888");
        Assert.Equal(TipResursaIsdp.Material, lemn.TipResursa);
        Assert.Equal(0.224574m, lemn.Cantitate);          // cantitate TOTALĂ pe articol
        Assert.Equal(0.0007m, lemn.ConsumUnitar);         // 0,224574 / 320,82

        var sapator = a.Copii.Single(r => r.Simbol == "19621");
        Assert.Equal(TipResursaIsdp.Manopera, sapator.TipResursa);
        Assert.Equal("ora", sapator.UnitateMasura);
        Assert.Equal(1745.2608m, sapator.Cantitate);
        Assert.Equal(111m, sapator.PretUnitar.Manopera);
    }

    [Fact]
    public void Articol_asimilat_si_resursa_directa()
    {
        var d = Deviz(out _);

        var asimilat = Nod(d, "0002.1.1.3.1");
        Assert.Equal(TipArticolIsdp.Asimilat, asimilat.TipArticol);
        Assert.Equal("TSF01A1 asim", asimilat.Simbol);
        Assert.Equal("TSF01A1", asimilat.SimbolBaza);

        var cherestea = Nod(d, "0002.1.1.1.3");
        Assert.Equal(TipArticolIsdp.ResursaDirecta, cherestea.TipArticol);
        Assert.Equal("20011961", cherestea.Simbol);
        Assert.Empty(cherestea.Copii);
        Assert.Equal(7500m, cherestea.Valoare.Material);
    }

    [Fact]
    public void Resursa_de_transport_cu_acelasi_cod_ca_articolul()
    {
        var d = Deviz(out _);
        var a = Nod(d, "0002.1.1.2.3");
        Assert.Equal("TRA01A20P", a.Simbol);

        var transport = a.Copii.Single(r => r.Simbol == "TRA01A20P");
        Assert.Equal(TipResursaIsdp.Transport, transport.TipResursa);
        Assert.Equal(200m, transport.PretUnitar.Transport);

        var muncitor = a.Copii.Single(r => r.Simbol == "29931");
        Assert.Equal(0.000024m, muncitor.Cantitate);      // 2,4e-05 ore: precizia se păstrează
    }

    [Fact]
    public void Resursa_fara_pret_primeste_tip_dedus_si_avertisment()
    {
        var d = Deviz(out var probleme);
        var apa = Nod(d, "0002.1.1.3.5.1");

        Assert.Equal("6202806", apa.Simbol);
        Assert.True(apa.TipResursaDedus);
        Assert.Equal(TipResursaIsdp.Material, apa.TipResursa);
        Assert.Contains(probleme, p => p.Severitate == SeveritateIsdp.Avertisment && p.Mesaj.Contains("6202806"));
        Assert.DoesNotContain(probleme, p => p.Severitate == SeveritateIsdp.Eroare);
    }

    [Fact]
    public void Recapitulatia_declarata_este_citita()
    {
        var r = Deviz(out _).Recapitulatie!;

        Assert.Equal(2.25m, r.ProcentCam);
        Assert.Equal(7m, r.ProcentIndirecte);
        Assert.Equal(3m, r.ProcentProfit);
        Assert.Equal(21m, r.CotaTva);
        Assert.Equal(462673.86m, Math.Round(r.T0ValoareDirecta!.Total, 2));
        Assert.Equal(480m, r.T0ValoareDirecta.Transport);
        Assert.Equal(521134.09m, Math.Round(r.T3CuProfit!.Total, 2));
        Assert.Equal(630572.25m, Math.Round(r.ValoareCuTva!.Value, 2));
    }

    [Fact]
    public void Recalculul_iese_identic_cu_fisierul()
    {
        var (calc, probleme) = VerificatorDeviz.Verifica(Deviz(out _));

        Assert.DoesNotContain(probleme, p => p.Severitate == SeveritateIsdp.Eroare);
        Assert.Equal(9674.65m, Math.Round(calc!.T0.Material, 2));
        Assert.Equal(452519.21m, Math.Round(calc.T0.Manopera, 2));
        Assert.Equal(10181.68m, Math.Round(calc.Cam, 2));              // 452.519,21 × 2,25%
        Assert.Equal(33099.89m, Math.Round(calc.Indirecte.Total, 2));  // 472.855,54 × 7%
        Assert.Equal(15178.66m, Math.Round(calc.Profit.Total, 2));     // 505.955,43 × 3%
        Assert.Equal(109438.16m, Math.Round(calc.Tva, 2));
        Assert.Equal(630572.25m, Math.Round(calc.TotalCuTva, 2));
    }

    [Fact]
    public void Un_total_modificat_in_fisier_este_prins()
    {
        // T3 (Total inclusiv Beneficiu) schimbat din 521.134,09 în 521.000,00.
        var foaie = Fixturi.Incarca(Fixturi.DevizResurse,
            (_, _, v) => v is double d && Math.Abs(d - 521134.0947392823) < 0.001 ? 521000.0 : v);
        var deviz = ParserDevizResurse.Parseaza(foaie).Valoare!;

        var (_, probleme) = VerificatorDeviz.Verifica(deviz);

        Assert.Contains(probleme, p => p.Severitate == SeveritateIsdp.Eroare && p.Mesaj.StartsWith("T3 total", StringComparison.Ordinal));
    }

    [Fact]
    public void O_foaie_care_nu_e_deviz_cu_resurse_este_respinsa()
    {
        var r = ParserDevizResurse.Parseaza(Fixturi.Incarca(Fixturi.ListaF3));
        Assert.Null(r.Valoare);
        Assert.True(r.AreErori);
    }
}
