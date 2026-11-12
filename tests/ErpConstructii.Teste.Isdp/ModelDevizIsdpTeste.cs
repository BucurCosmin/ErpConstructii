using ErpConstructii.Devize;
using ErpConstructii.Devize.Analize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import.Isdp;

namespace ErpConstructii.Teste.Isdp;

/// <summary>
/// Test de acceptanță al modelului de deviz (D-50, docs/import-isdp.md §6): devizul real ISDP, pus în modelul ERP (capitole
/// arborescente, cinci resurse, cantități totale pe consum, rotunjirea „Fara”), se recalculează identic cu recapitulația
/// declarată în fișier. Maparea de aici e minimă; importatorul complet (§7) o înlocuiește.
/// </summary>
public class ModelDevizIsdpTeste
{
    private static (DevizIsdp Isdp, VersiuneDeviz Versiune) Versiune()
    {
        var isdp = ParserDevizResurse.Parseaza(Fixturi.Incarca(Fixturi.DevizResurse)).Valoare!;
        var r = isdp.Recapitulatie!;
        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, isdp.Antet.Titlu);
        // ISDP dă procentele (2,25); modelul ține fracții (0,0225).
        v.SeteazaParametri(new ParametriRecapitulatie(r.ProcentCam!.Value / 100, r.ProcentIndirecte!.Value / 100, r.ProcentProfit!.Value / 100, r.CotaTva!.Value / 100));
        v.SeteazaPoliticaRotunjire(PoliticaRotunjire.Fara);
        v.SeteazaAntet(isdp.Antet.Obiectiv, isdp.Antet.Obiect, isdp.Antet.StadiuFizic, null);
        foreach (var radacina in isdp.Radacini)
        {
            Adauga(v, radacina, null);
        }

        return (isdp, v);
    }

    private static void Adauga(VersiuneDeviz v, NodIsdp grup, CapitolDeviz? parinte)
    {
        var tip = parinte is null ? TipCapitol.StadiuFizic : TipCapitol.Capitol;
        var capitol = v.AdaugaCapitol(grup.Wbs.Split('.')[^1], grup.Denumire, tip, parinte, grup.Wbs);
        foreach (var n in grup.Copii)
        {
            if (n.Tip == TipNodIsdp.Grup)
            {
                Adauga(v, n, capitol);
                continue;
            }

            var p = n.PretUnitar;
            var a = capitol.AdaugaArticol(n.Simbol, n.Denumire, n.UnitateMasura, n.Cantitate,
                new ValoriResurse(p.Material, p.Manopera, p.Utilaj, p.Transport, p.Echipament));
            a.SeteazaSursa((TipArticol)(int)n.TipArticol, n.SimbolBaza, n.Wbs);
            // Resursa pusă direct ca articol devine și resursă a lui (consum 1), ca să apară în extras și în nomenclator.
            if (n.TipArticol == TipArticolIsdp.ResursaDirecta && n.Copii.Count == 0)
            {
                var tipDirect = ValoriIsdp.Tipuri.FirstOrDefault(t => p[t] != 0);
                a.AdaugaConsum(n.Simbol, n.Denumire, (TipResursa)(int)tipDirect, n.UnitateMasura, 1, p.Total, n.Cantitate);
            }

            foreach (var res in n.Copii)
            {
                a.AdaugaConsum(res.Simbol, res.Denumire, (TipResursa)(int)res.TipResursa, res.UnitateMasura, res.ConsumUnitar,
                    res.PretUnitar.Total, res.Cantitate, res.TipResursaDedus);
            }
        }
    }

    [Fact]
    public void Devizul_real_in_modelul_ERP_se_recalculeaza_identic_cu_fisierul()
    {
        var (isdp, v) = Versiune();
        var declarat = isdp.Recapitulatie!;

        var r = v.Recapitulatie();

        Assert.Equal(15, v.Articole().Count());
        // Fără rotunjiri intermediare; comparația la ban se face pe valorile finale.
        Assert.Equal(Bani(declarat.T0ValoareDirecta!.Total), Bani(r.CheltuieliDirecteResurse.Total)); // 462.673,86
        Assert.Equal(Bani(declarat.T1CuCam!.Total), Bani(r.TotalCheltuieliDirecte));                   // 472.855,54
        Assert.Equal(Bani(declarat.T2CuIndirecte!.Total), Bani(r.TotalCheltuieliDirecte + r.CheltuieliIndirecte)); // 505.955,43
        Assert.Equal(521134.09m, Bani(r.TotalFaraTva));                                                // T3
        Assert.Equal(Bani(declarat.ValoareTva!.Value), Bani(r.Tva));                                   // 109.438,16
        Assert.Equal(630572.25m, Bani(r.TotalCuTva));
    }

    [Fact]
    public void Arborele_pastreaza_stadiul_capitolele_si_codurile_WBS()
    {
        var (_, v) = Versiune();

        var stadiu = Assert.Single(v.Radacini);
        Assert.Equal((TipCapitol.StadiuFizic, "0002.1.1"), (stadiu.Tip, stadiu.CodWbs));
        Assert.Equal(["SAPATURA SI EVACUARE", "SPARGERE BETON,sprijiniri utilitati", "ADUCERE LA STAREA INITIALA"], v.Copii(stadiu.Id).Select(c => c.Denumire));
        var primul = v.Articole().First();
        Assert.Equal(("TSA04G1", "0002.1.1.1.1", TipArticol.Normat), (primul.Simbol, primul.CodWbsSursa, primul.TipArticol));
        Assert.Equal(["0002.1.1", "0002.1.1.1"], v.Cale(v.Capitole.Single(c => c.Id == primul.CapitolId)).Select(c => c.CodWbs));
        Assert.Contains(v.Articole(), a => a.TipArticol == TipArticol.Asimilat && a.SimbolBaza == "TSF01A1");
    }

    [Fact]
    public void Consumul_tine_cantitatea_totala_si_consumul_unitar()
    {
        var (_, v) = Versiune();
        var articol = v.Articole().Single(a => a.CodWbsSursa == "0002.1.1.1.1"); // TSA04G1, 320,82 mc
        var sapator = articol.Consumuri.Single(c => c.CodResursa == "19621");

        Assert.Equal(TipResursa.Manopera, sapator.Tip);
        // Consumul unitar = cantitatea totală / cantitatea articolului (ISDP exportă cantitatea totală).
        Assert.Equal(sapator.CantitateTotala / articol.Cantitate, sapator.ConsumUnitar, 10);
        Assert.Contains(v.Articole().SelectMany(a => a.Consumuri), c => c.TipDedus); // apa industrială, fără preț
    }

    [Fact]
    public void Nomenclatorul_primeste_cele_23_de_resurse_iar_al_doilea_import_nu_creeaza_nimic_nou()
    {
        var (_, v1) = Versiune();
        var p1 = CatalogResurse.Potriveste("isdp", v1.Articole().SelectMany(a => a.Consumuri), []);

        // 23 de coduri distincte, ca extrasul de resurse; săpătorul (19621) e o singură resursă, deși apare în 3 articole.
        Assert.Equal(23, p1.Noi.Count);
        Assert.All(p1.Noi, r => Assert.False(r.EsteMapata));
        Assert.Equal(3, v1.Articole().SelectMany(a => a.Consumuri).Count(c => c.ResursaCatalogId == p1.Noi.Single(r => r.Cod == "19621").Id));
        Assert.True(p1.Noi.Single(r => r.Cod == "6202806").TipDedus); // apa industrială, fără preț

        // Reimportul aceluiași deviz: toate codurile se găsesc în nomenclator (cu maparea lor), nimic nou.
        var (_, v2) = Versiune();
        var p2 = CatalogResurse.Potriveste("isdp", v2.Articole().SelectMany(a => a.Consumuri), p1.Noi);
        Assert.Empty(p2.Noi);
        Assert.Empty(p2.Probleme);
        Assert.All(v2.Articole().SelectMany(a => a.Consumuri), c => Assert.Contains(p1.Noi, r => r.Id == c.ResursaCatalogId));
    }

    [Fact]
    public void Analiza_numerelor_identifica_volumele_posibil_copiate_din_tone_in_acelasi_stadiu()
    {
        var (_, deviz) = Versiune();
        var rezultat = new AnalizorAmprentaNumerelor().Analizeaza(new(deviz, new()));
        var articole = deviz.Articole().Where(a => a.Simbol is "TSD01C1" or "TSD04A1").ToArray();

        Assert.Equal(2, articole.Length);
        foreach (var articol in articole)
        {
            var constatare = Assert.Single(rezultat.Constatari, c => c.Corectura?.Articol == articol.CheieStabila);
            Assert.Equal(320.82m, constatare.Corectura!.CantitateNoua);
            Assert.Equal(SeveritateAnaliza.Info, constatare.Severitate);
        }
        Assert.Equal(2, rezultat.Constatari.Count(c => c.Corectura is not null));
        // Cele două reduceri ale costului direct însumează aproximativ 37.035 lei.
        Assert.InRange(-rezultat.Constatari.Where(c => c.Corectura is not null).Sum(c => c.ImpactCostDirect!.Value), 37030m, 37040m);
    }

    [Fact]
    public void Analiza_preturilor_speciale_gaseste_apa_si_tariful_uniform_pe_devizul_isdp()
    {
        var (_, deviz) = Versiune();
        var rezultat = new AnalizorPreturiSpeciale().Analizeaza(new(deviz, new()));

        Assert.Contains(rezultat.Constatari, c => c.Cod == "A5.PRET_ZERO" && c.Dovezi.Contains(new("resursa", "6202806")));
        Assert.Contains(rezultat.Constatari, c => c.Cod == "A5.TARIF_UNIFORM" && c.Dovezi.Contains(new("tarif", "111")));
    }

    private static decimal Bani(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
