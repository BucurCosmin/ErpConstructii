using ErpConstructii.Grafic;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Teste.Unitare.Grafic;

public class CursuriBnrTeste
{
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <DataSet xmlns="https://www.bnr.ro/xsd"><Header><Publisher>National Bank of Romania</Publisher></Header><Body><OrigCurrency>RON</OrigCurrency>
        <Cube date="2026-11-13"><Rate currency="EUR">5.0800</Rate><Rate currency="HUF" multiplier="100">1.4662</Rate></Cube>
        <Cube date="2026-11-16"><Rate currency="EUR">5.0900</Rate></Cube>
        </Body></DataSet>
        """;

    [Fact]
    public void Ziua_fara_curs_ia_ultimul_curs_publicat()
    {
        var c = CursuriBnr.Citeste(Xml);

        var duminica = c.Cauta("EUR", new DateOnly(2026, 11, 15));
        Assert.Equal(new CursValutar("EUR", new DateOnly(2026, 11, 13), 5.08m), duminica);
        Assert.Equal(5.09m, c.Cauta("eur", new DateOnly(2026, 11, 16))!.LeiPeUnitate);
        Assert.Null(c.Cauta("EUR", new DateOnly(2026, 11, 12)));        // înaintea primului curs din fișier
    }

    [Fact]
    public void Cursul_publicat_la_mai_multe_unitati_se_imparte_la_multiplicator_iar_leul_e_1()
    {
        var c = CursuriBnr.Citeste(Xml);

        Assert.Equal(0.014662m, c.Cauta("HUF", new DateOnly(2026, 11, 13))!.LeiPeUnitate);   // 1,4662 / 100
        Assert.Equal(1m, c.Cauta("RON", new DateOnly(2020, 1, 1))!.LeiPeUnitate);
    }
}

public class CalculCosturiGraficTeste
{
    private static readonly DateOnly Zi = new(2026, 11, 13);
    private static readonly Guid F1 = Guid.NewGuid(), BC1 = Guid.NewGuid(), F2 = Guid.NewGuid();

    private static readonly StructuraGrafic Structura = StructuraGrafic.Din(
        [new NodWbsCanonic("B5", "B5", "Clădirea 5", null, 0), new NodWbsCanonic("B5.FUND", "FUND", "Fundații", "B5", 1), new NodWbsCanonic("M", "M", "Jaloane", null, 2)],
        [Activitate("A1", "B5.FUND"), Activitate("A2", "B5.FUND"), Activitate("M1", "M")]);

    private static ActivitateCanonica Activitate(string cod, string wbs) =>
        new(cod, $"Activitatea {cod}", wbs, Zi, Zi, 1, false, [], new Dictionary<string, string>());

    private static readonly DocumentCost Factura = new(SursaDocumentCost.Factura, F1, "F1", Zi, "Subantreprenor SRL", "RON",
    [
        new LinieDocumentCost(0, "Manoperă cofraj", "ora", 100, 1000m, true, null, "contract:X"),
        new LinieDocumentCost(1, "Beton C25/30", "mc", 10, 5000m, false, "material de stoc", null),
    ], null);

    private static readonly DocumentCost Bon = new(SursaDocumentCost.BonConsum, BC1, "BC/2026/0001", Zi, "Șantier", "RON",
    [
        new LinieDocumentCost(0, "Oțel beton", "kg", 100, 500m, true, null, "articol:K"),
        new LinieDocumentCost(1, "Sârmă", "kg", 10, 300m, true, null, "articol:K"),
    ], null);

    private static readonly DocumentCost FacturaEur = new(SursaDocumentCost.Factura, F2, "F2", Zi, "Furnizor GmbH", "EUR",
        [new LinieDocumentCost(0, "Consultanță", "buc", 1, 200m, true, null, null)], null);

    private static readonly AlocareLinie[] Alocari =
    [
        new(SursaDocumentCost.Factura, F1, 0, TipTinta.Activitate, "A1", 60m),
        new(SursaDocumentCost.Factura, F1, 0, TipTinta.Wbs, "B5", 40m),
        new(SursaDocumentCost.BonConsum, BC1, 0, TipTinta.Activitate, "A2", 100m),
        new(SursaDocumentCost.Factura, F2, 0, TipTinta.Activitate, "X999", 100m),
    ];

    private static RezultatCosturiGrafic Calculeaza()
    {
        var eur = new CursValutar("EUR", Zi, 5m);
        var cursuri = new Dictionary<Guid, CursuriDocument>
        {
            [F1] = new(new CursValutar("RON", Zi, 1m), eur),
            [BC1] = new(new CursValutar("RON", Zi, 1m), eur),
            [F2] = new(eur, eur),
        };
        return CalculCosturiGrafic.Calculeaza(Structura, "EUR", [Factura, Bon, FacturaEur], Alocari, cursuri);
    }

    [Fact]
    public void Alocarile_se_valorizeaza_in_lei_si_in_moneda_graficului_la_cursul_documentului()
    {
        var r = Calculeaza();

        Assert.Equal(new CostTinta(600m, 120m, 1, 0), r.PeActivitate["A1"]);     // 1.000 × 60% = 600 lei / 5 = 120 EUR
        Assert.Equal(new CostTinta(500m, 100m, 1, 0), r.PeActivitate["A2"]);     // 500 lei / 5
        Assert.Equal(2500m, r.Total.Lei);                                         // 600 + 400 + 500 + 200 × 5
        Assert.Equal(500m, r.Total.MonedaGrafic);                                 // 120 + 80 + 100 + 200 (deja în EUR)
    }

    [Fact]
    public void Nodul_wbs_aduna_activitatile_lui_si_alocarile_directe_pe_nod()
    {
        var r = Calculeaza();

        Assert.Equal(1100m, r.PeNod["B5.FUND"].Lei);                             // A1 600 + A2 500
        Assert.Equal(1500m, r.PeNod["B5"].Lei);                                   // 1.100 + 400 alocat direct pe B5
        Assert.Equal(300m, r.PeNod["B5"].MonedaGrafic);
        Assert.Equal(2, r.PeNod["B5"].Documente);                                 // F1 și BC1
        Assert.False(r.PeNod.ContainsKey("M"));
    }

    [Fact]
    public void Tinta_care_nu_mai_e_in_grafic_ramane_doar_in_total_si_e_semnalata()
    {
        var r = Calculeaza();

        Assert.Equal(new CostTinta(1000m, 200m, 1, 0), r.FaraTinta);
        var f2 = r.Documente.Single(d => d.Document.Id == F2);
        Assert.Contains(f2.Probleme, p => p.Contains("X999"));
    }

    [Fact]
    public void Starea_documentului_tine_cont_doar_de_liniile_de_cost_direct()
    {
        var r = Calculeaza();

        var f1 = r.Documente.Single(d => d.Document.Id == F1);
        Assert.Equal((StareAlocare.Alocat, 1000m, 1000m), (f1.Stare, f1.ValoareLei, f1.AlocatLei));   // betonul (stoc) nu se alocă
        var bc = r.Documente.Single(d => d.Document.Id == BC1);
        Assert.Equal((StareAlocare.Partial, 800m, 500m, 62.5m), (bc.Stare, bc.ValoareLei, bc.AlocatLei, bc.ProcentAlocat));   // 500 / 800
    }

    [Fact]
    public void Linia_nealocata_primeste_propunerea_liniei_cu_aceeasi_grupare()
    {
        var bc = Calculeaza().Documente.Single(d => d.Document.Id == BC1);

        var p = Assert.Single(bc.Propuneri);                                      // sârma, același articol de deviz ca oțelul
        Assert.Equal((1, TipTinta.Activitate, "A2", 100m), (p.LinieIndex, p.Tip, p.Cheie, p.Procent));
    }

    [Fact]
    public void Validarea_refuza_peste_100_procent_linia_de_stoc_si_tinta_inexistenta()
    {
        Assert.True(CalculCosturiGrafic.Valideaza(Factura, [new(0, TipTinta.Activitate, "A1", 60), new(0, TipTinta.Activitate, "A2", 50)], Structura).Esec);
        Assert.True(CalculCosturiGrafic.Valideaza(Factura, [new(1, TipTinta.Activitate, "A1", 100)], Structura).Esec);
        Assert.True(CalculCosturiGrafic.Valideaza(Factura, [new(0, TipTinta.Wbs, "NU", 100)], Structura).Esec);
        Assert.True(CalculCosturiGrafic.Valideaza(Factura, [new(0, TipTinta.Activitate, "A1", 60), new(0, TipTinta.Wbs, "B5", 40)], Structura).Succes);
    }

    [Fact]
    public void Tot_documentul_pe_o_tinta_completeaza_doar_restul_nealocat()
    {
        var noi = CalculCosturiGrafic.CompleteazaPe(Bon, [Alocari[2]], TipTinta.Wbs, "M");

        Assert.Equal(2, noi.Count);
        Assert.Contains(new AlocareCeruta(0, TipTinta.Activitate, "A2", 100m), noi);   // linia alocată rămâne
        Assert.Contains(new AlocareCeruta(1, TipTinta.Wbs, "M", 100m), noi);
    }
}
