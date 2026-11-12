using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Teste.Unitare.Devize;

/// <summary>Modelul de deviz (D-50): capitole arborescente, a cincea resursă, două prețuri pe articol, monedă și rotunjire.</summary>
public class ModelDevizTeste
{
    private static readonly ParametriRecapitulatie Cote = new(0.10m, 0.10m, 0.05m, 0.21m);

    [Fact]
    public void Capitolele_pe_mai_multe_niveluri_dau_articolele_in_ordinea_arborelui()
    {
        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "Test");
        var obiect = v.AdaugaObiect("01", "Bloc A");
        var stadiu = v.AdaugaCapitol("01.1", "Infrastructură", TipCapitol.StadiuFizic, obiect);
        var sapaturi = v.AdaugaCapitol("01.1.1", "Săpături", TipCapitol.Capitol, stadiu);
        var betoane = v.AdaugaCapitol("01.1.2", "Betoane", TipCapitol.Capitol, stadiu);
        betoane.AdaugaArticol("CA02", "Beton", "mc", 10, new ValoriResurse(400, 30, 20, 0));
        sapaturi.AdaugaArticol("TS01", "Săpătură", "mc", 50, new ValoriResurse(0, 40, 0, 0));
        obiect.AdaugaArticol("X1", "Articol direct sub obiect", "buc", 1, new ValoriResurse(1, 0, 0, 0));

        // Întâi articolele capitolului, apoi subcapitolele în ordinea lor.
        Assert.Equal(["X1", "TS01", "CA02"], v.Articole().Select(a => a.Simbol));
        Assert.Equal(["01", "01.1", "01.1.2"], v.Cale(betoane).Select(c => c.Cod));
        Assert.Equal([0, 1], v.Copii(stadiu.Id).Select(c => c.Ordine));
    }

    [Fact]
    public void Echipamentul_intra_in_cheltuielile_directe_si_in_recapitulatie()
    {
        // 2 buc × (100 material + 50 echipament) = 200 + 100 = 300 directe; manopera 0 → CAM 0;
        // indirecte 10% = 30; profit 5% din 330 = 16,50; total fără TVA 346,50.
        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "Test");
        v.SeteazaParametri(Cote);
        v.AdaugaObiect("01", "Obiect").AdaugaArticol("E1", "Pompă", "buc", 2, new ValoriResurse(100, 0, 0, 0, Echipament: 50));

        var r = v.Recapitulatie();

        Assert.Equal(100m, r.CheltuieliDirecteResurse.Echipament);
        Assert.Equal(300m, r.CheltuieliDirecteResurse.Total);
        Assert.Equal(346.50m, r.TotalFaraTva);
    }

    [Fact]
    public void Politica_Fara_nu_rotunjeste_pe_parcurs_iar_Standard_rotunjeste_la_ban()
    {
        // 3 × 0,3333 = 0,9999 lei material: Standard → 1,00 pe linie; Fara → 0,9999.
        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "Test");
        v.SeteazaParametri(new ParametriRecapitulatie(0, 0, 0, 0));
        v.AdaugaObiect("01", "Obiect").AdaugaArticol("A", "A", "buc", 3, new ValoriResurse(0.3333m, 0, 0, 0));

        Assert.Equal(1.00m, v.Recapitulatie().TotalFaraTva);
        v.SeteazaPoliticaRotunjire(PoliticaRotunjire.Fara);
        Assert.Equal(0.9999m, v.Recapitulatie().TotalFaraTva);
    }

    [Fact]
    public void Articolul_tine_costul_direct_si_pretul_de_oferta_separat()
    {
        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "Test");
        Assert.True(v.SeteazaMoneda("eur", 5.23m).Succes);
        var a = v.AdaugaObiect("01", "Obiect").AdaugaArticol("TSA04G1", "Săpătură", "mc", 320.82m, new ValoriResurse(5.9781m, 659.4m, 0, 0));
        Assert.False(a.ArePretOferta);

        a.SeteazaPretOferta(new ValoriResurse(1.26m, 130.11m, 0, 0), new ValoriResurse(404.15m, 41741.30m, 0, 0));

        Assert.Equal(("EUR", 5.23m), (v.Moneda, v.Curs));
        Assert.Equal(665.3781m, a.PretUnitar.Total);       // cost direct, lei
        Assert.Equal(131.37m, a.PretOfertaUnitar.Total);   // preț de ofertă, EUR (F3 1.1)
        Assert.Equal(42145.45m, a.ValoareOferta.Total);
        Assert.True(v.SeteazaMoneda("EURO", null).Esec);
    }

    [Fact]
    public void Consumul_fara_cantitate_totala_o_calculeaza_din_consumul_unitar()
    {
        var a = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "Test")
            .AdaugaObiect("01", "Obiect").AdaugaArticol("CA02", "Beton", "mc", 12.5m, ValoriResurse.Zero);

        // 1,02 mc beton / mc × 12,5 mc = 12,75 mc pe articol.
        var c = a.AdaugaConsum("6000123", "Beton C16/20", TipResursa.Material, "mc", 1.02m, 410m);

        Assert.Equal((12.75m, (Guid?)null), (c.CantitateTotala, c.ResursaCatalogId));
    }

    [Fact]
    public void Versiunea_aprobata_nu_mai_primeste_capitole_si_nu_se_aproba_goala()
    {
        var goala = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "Test");
        goala.AdaugaObiect("01", "Obiect");
        Assert.True(goala.Aproba().Esec); // fără articole

        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 2, "Test");
        v.AdaugaObiect("01", "Obiect").AdaugaArticol("A", "A", "buc", 1, new ValoriResurse(1, 0, 0, 0));
        Assert.True(v.Aproba().Succes);
        Assert.Throws<InvalidOperationException>(() => v.AdaugaCapitol("02", "Altul", TipCapitol.Obiect));
        Assert.Throws<InvalidOperationException>(() => v.SeteazaPoliticaRotunjire(PoliticaRotunjire.Fara));
    }
}
