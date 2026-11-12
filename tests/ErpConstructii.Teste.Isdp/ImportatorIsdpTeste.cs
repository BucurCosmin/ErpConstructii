using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import;
using ErpConstructii.Devize.Import.Isdp;
using ErpConstructii.Devize.Import.Isdp.Excel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Teste.Isdp;

/// <summary>Importatorul ISDP (D-50, docs/import-isdp.md §7): exporturile reale → modelul canonic → versiune de deviz.</summary>
public class ImportatorIsdpTeste
{
    private static readonly FoaieCalcul Deviz = Fixturi.Incarca(Fixturi.DevizResurse);
    private static readonly FoaieCalcul F3 = Fixturi.Incarca(Fixturi.ListaF3);

    private static DevizCanonic Converteste(params FoaieCalcul[] foi)
    {
        var r = ConversieIsdp.Converteste(foi);
        Assert.DoesNotContain(r.Probleme, p => p.Severitate == Severitate.Eroare);
        return r.Valoare!;
    }

    private static VersiuneDeviz Versiune(DevizCanonic d) =>
        ConvertorDevizCanonic.CaVersiune(d, Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "isdp", null, d.Parametri!);

    [Fact]
    public void Devizul_cu_F3_devine_versiune_identica_cu_fisierul_cu_pretul_de_oferta_in_euro()
    {
        var d = Converteste(Deviz, F3);
        Assert.DoesNotContain(ValidatorDevizCanonic.Valideaza(d), p => p.Severitate == Severitate.Eroare); // totalurile declarate = recalculul
        var v = Versiune(d);

        Assert.Equal(("EUR", 5.23m, PoliticaRotunjire.Fara), (v.Moneda, v.Curs, v.PoliticaRotunjire));
        Assert.Equal(("UNITATEA 1 INVESTIGATII", "INVESTIGATII"), (v.Obiectiv, v.Obiect));
        Assert.Equal(15, v.Articole().Count());
        Assert.Equal(521134.09m, Math.Round(v.Recapitulatie().TotalFaraTva, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(630572.25m, Math.Round(v.Recapitulatie().TotalCuTva, 2, MidpointRounding.AwayFromZero));

        // F3 1.1 = TSA04G1: 131,37 EUR / mc, 42.145,45 EUR total (material 1,26 / 404,15 + manoperă 130,11 / 41.741,30).
        var a = v.Articole().Single(x => x.CodWbsSursa == "0002.1.1.1.1");
        Assert.Equal((131.37m, 42145.45m), (a.PretOfertaUnitar.Total, a.ValoareOferta.Total));
        Assert.Equal((1.26m, 404.15m), (a.PretOfertaUnitar.Material, a.ValoareOferta.Material));
        // Totalul ofertei = totalul general F3 (99.643,23 EUR).
        Assert.Equal(99643.23m, v.Articole().Sum(x => x.ValoareOferta.Total));
        Assert.NotNull(v.TotaluriDeclarateJson);
    }

    [Fact]
    public void Arborele_resursele_si_articolele_speciale_se_pastreaza()
    {
        var v = Versiune(Converteste(Deviz, F3));

        var stadiu = Assert.Single(v.Radacini);
        Assert.Equal((TipCapitol.StadiuFizic, "0002.1.1"), (stadiu.Tip, stadiu.CodWbs));
        Assert.Equal(3, v.Copii(stadiu.Id).Count());
        // 23 de resurse distincte; resursa directă (cherestea) e și resursa ei, cu consum 1.
        Assert.Equal(23, v.Articole().SelectMany(a => a.Consumuri).Select(c => c.CodResursa).Distinct().Count());
        var direct = v.Articole().Single(a => a.TipArticol == TipArticol.ResursaDirecta);
        Assert.Equal((1m, direct.Cantitate), (direct.Consumuri.Single().ConsumUnitar, direct.Consumuri.Single().CantitateTotala));
        Assert.Contains(v.Articole(), a => a.TipArticol == TipArticol.Asimilat && a.SimbolBaza == "TSF01A1");
        Assert.Contains(v.Articole().SelectMany(a => a.Consumuri), c => c.TipDedus);
    }

    [Fact]
    public void Fara_F3_pretul_de_oferta_se_calculeaza_in_lei_sau_la_cursul_dat()
    {
        var lei = Versiune(Converteste(Deviz));
        Assert.Equal(("RON", (decimal?)null), (lei.Moneda, lei.Curs));
        Assert.True(lei.Articole().All(a => a.ArePretOferta));

        var r = ConversieIsdp.Converteste([Deviz], curs: 5.23m, moneda: "EUR");
        var eur = Versiune(r.Valoare!);
        // Același curs ca F3 → aceleași prețuri ca în F3.
        Assert.Equal(("EUR", 5.23m), (eur.Moneda, eur.Curs));
        Assert.Equal(99643.23m, eur.Articole().Sum(x => x.ValoareOferta.Total));
    }

    [Fact]
    public void Lista_F3_singura_e_refuzata_cu_explicatie()
    {
        var r = ConversieIsdp.Converteste([F3]);

        Assert.Null(r.Valoare);
        Assert.Contains("nu se poate importa singură", Assert.Single(r.Probleme).Mesaj);
    }

    [Fact]
    public void Reimportul_aceluiasi_deviz_pastreaza_toate_cheile_stabile()
    {
        var vechi = Versiune(Converteste(Deviz, F3));
        var nou = Versiune(Converteste(Deviz, F3));

        var r = PotrivireReimport.Potriveste([.. nou.Articole()], [.. vechi.Articole()]);

        Assert.Equal(15, r.Chei);
        Assert.All(r.Diferente, d => Assert.Equal(StareArticolReimport.Neschimbat, d.Stare));
        Assert.Equal(vechi.Articole().Select(a => a.CheieStabila), nou.Articole().Select(a => a.CheieStabila));
    }

    [Fact]
    public void Reimportul_cu_cantitate_schimbata_articol_nou_si_WBS_renumerotat()
    {
        var vechi = Versiune(Converteste(Deviz, F3));
        var d = Converteste(Deviz, F3);
        // Cantitatea la 1.1 crește; articolul 0002.1.1.3.5 primește alt WBS (renumerotat); se adaugă un articol nou.
        var modificat = d with
        {
            Capitole = Inlocuieste(d.Capitole, a => a.CodWbs switch
            {
                "0002.1.1.1.1" => a with { Cantitate = 400m },
                "0002.1.1.3.5" => a with { CodWbs = "0002.1.1.3.9" },
                _ => a,
            }, nou: new ArticolCanonic("TSA99X1", "Articol nou", "mc", 2m, new ValoriResurse(0, 10m, 0, 0), [], CodWbs: "0002.1.1.3.6")),
        };
        var nou = Versiune(modificat);

        var r = PotrivireReimport.Potriveste([.. nou.Articole()], [.. vechi.Articole()]);

        Assert.Equal(15, r.Chei); // toate cele vechi, inclusiv cel renumerotat (potrivit după simbol + denumire + UM)
        var cantitate = r.Diferente.Single(x => x.CodWbs == "0002.1.1.1.1");
        Assert.Equal((StareArticolReimport.CantitateSchimbata, 320.82m, 400m), (cantitate.Stare, cantitate.CantitateVeche!.Value, cantitate.CantitateNoua!.Value));
        Assert.True(r.Diferente.Single(x => x.CodWbs == "0002.1.1.3.9").PotrivitDupaDenumire);
        Assert.Equal(StareArticolReimport.Nou, r.Diferente.Single(x => x.Simbol == "TSA99X1").Stare);
        Assert.Equal((1, 0), (r.Noi, r.Eliminate));
        var cheiaVeche = vechi.Articole().Single(a => a.CodWbsSursa == "0002.1.1.3.5").CheieStabila;
        Assert.Equal(cheiaVeche, nou.Articole().Single(a => a.CodWbsSursa == "0002.1.1.3.9").CheieStabila);
    }

    [FactCuFisiereIsdp]
    public async Task Importatorul_citeste_fisierele_reale_ca_fixture_urile()
    {
        var importator = new ImportatorIsdp();
        var deviz = new SursaImport("extras de resurse.xls", await File.ReadAllBytesAsync(FisiereIsdp.Deviz!));
        var f3 = new SursaImport("sapare chilleri.xlsx", await File.ReadAllBytesAsync(FisiereIsdp.F3!));

        Assert.True(importator.PoateCiti(deviz).Incredere >= AlegereImportator.PragIncredere);
        var r = await importator.CitesteAsync(deviz, new OptiuniImportDeviz([f3]));

        Assert.DoesNotContain(r.Probleme, p => p.Severitate == Severitate.Eroare);
        var v = Versiune(r.Valoare!);
        Assert.Equal(("EUR", 5.23m, 15), (v.Moneda, v.Curs, v.Articole().Count()));
        Assert.Equal(521134.09m, Math.Round(v.Recapitulatie().TotalFaraTva, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(99643.23m, v.Articole().Sum(x => x.ValoareOferta.Total));
    }

    private static IReadOnlyList<CapitolCanonic> Inlocuieste(IReadOnlyList<CapitolCanonic> capitole, Func<ArticolCanonic, ArticolCanonic> f, ArticolCanonic nou)
    {
        var ultimul = capitole.SelectMany(Toate).Last(c => c.Articole.Count > 0);
        return capitole.Select(c => Copie(c)).ToList();

        CapitolCanonic Copie(CapitolCanonic c) => c with
        {
            Articole = [.. c.Articole.Select(f), .. ReferenceEquals(c, ultimul) ? [nou] : Array.Empty<ArticolCanonic>()],
            Subcapitole = c.Subcapitole.Select(Copie).ToList(),
        };

        static IEnumerable<CapitolCanonic> Toate(CapitolCanonic c) => c.Subcapitole.SelectMany(Toate).Prepend(c);
    }
}
