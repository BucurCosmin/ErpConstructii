using System.Text;
using ErpConstructii.Facturi;
using ErpConstructii.Facturi.Import;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Teste.Unitare.Facturi;

/// <summary>CUI-uri sintetice valide: furnizor 18547290, cumpărător (firma activă) 23456783. Fără date reale.</summary>
internal static class DateTest
{
    public const string CuiFurnizor = "18547290";
    public const string CuiFirma = "23456783";
}

public class TextNumericTeste
{
    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("70", 70)]
    [InlineData("0,5", 0.5)]
    [InlineData("-190,00", -190)]
    [InlineData("(25,00)", -25)]
    public void Numerele_din_documente_se_citesc_in_ambele_formate(string text, decimal asteptat)
    {
        Assert.True(TextNumeric.ParseazaNumar(text, out var v));
        Assert.Equal(asteptat, v);
    }

    [Fact]
    public void Un_singur_separator_urmat_de_trei_cifre_e_ambiguu_si_se_decide_explicit()
    {
        TextNumeric.ParseazaNumar("1.250", out var zecimal);
        TextNumeric.ParseazaNumar("1.250", out var mii, miiImplicit: true);
        Assert.Equal((1.25m, 1250m), (zecimal, mii));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("12a")]
    public void Textul_care_nu_e_numar_e_respins(string text) => Assert.False(TextNumeric.ParseazaNumar(text, out _));
}

public class InterpretorEFacturaTeste
{
    private static byte[] Xml(string radacina = "Invoice", string linii = "", string extra = "") => Encoding.UTF8.GetBytes($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <{radacina} xmlns="urn:oasis:names:specification:ubl:schema:xsd:{radacina}-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:CustomizationID>urn:cen.eu:en16931:2017#compliant#urn:efactura.mfinante.ro:CIUS-RO:1.0.1</cbc:CustomizationID>
          <cbc:ID>MEX 1234</cbc:ID>
          <cbc:IssueDate>2026-10-15</cbc:IssueDate>
          <cbc:DueDate>2026-11-14</cbc:DueDate>
          <cbc:DocumentCurrencyCode>RON</cbc:DocumentCurrencyCode>
          <cac:OrderReference><cbc:ID>265</cbc:ID></cac:OrderReference>
          {extra}
          <cac:AccountingSupplierParty><cac:Party>
            <cac:PartyTaxScheme><cbc:CompanyID>RO{DateTest.CuiFurnizor}</cbc:CompanyID><cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:PartyTaxScheme>
            <cac:PartyLegalEntity><cbc:RegistrationName>MATERIALE EXEMPLU SRL</cbc:RegistrationName></cac:PartyLegalEntity>
          </cac:Party></cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty><cac:Party>
            <cac:PartyLegalEntity><cbc:RegistrationName>CONSTRUCT TEST SRL</cbc:RegistrationName><cbc:CompanyID>{DateTest.CuiFirma}</cbc:CompanyID></cac:PartyLegalEntity>
          </cac:Party></cac:AccountingCustomerParty>
          <cac:TaxTotal><cbc:TaxAmount currencyID="RON">231.00</cbc:TaxAmount></cac:TaxTotal>
          <cac:LegalMonetaryTotal>
            <cbc:LineExtensionAmount currencyID="RON">1100.00</cbc:LineExtensionAmount>
            <cbc:TaxExclusiveAmount currencyID="RON">1100.00</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="RON">1331.00</cbc:TaxInclusiveAmount>
            <cbc:PayableAmount currencyID="RON">1331.00</cbc:PayableAmount>
          </cac:LegalMonetaryTotal>
          {linii}
        </{radacina}>
        """);

    private static string Linie(string element, string cantitateEl, string id, string cod, string cant, string valoare, string nume, string pret, string baza = "") => $"""
        <cac:{element}><cbc:ID>{id}</cbc:ID>
          <cbc:{cantitateEl} unitCode="{cod}">{cant}</cbc:{cantitateEl}>
          <cbc:LineExtensionAmount currencyID="RON">{valoare}</cbc:LineExtensionAmount>
          <cac:Item><cbc:Name>{nume}</cbc:Name><cac:ClassifiedTaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>21</cbc:Percent><cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:ClassifiedTaxCategory></cac:Item>
          <cac:Price><cbc:PriceAmount currencyID="RON">{pret}</cbc:PriceAmount>{baza}</cac:Price>
        </cac:{element}>
        """;

    private static readonly string DouaLinii =
        Linie("InvoiceLine", "InvoicedQuantity", "1", "H87", "70", "700.00", "Plasa sudata 2x1 m", "10.00") +
        Linie("InvoiceLine", "InvoicedQuantity", "2", "KGM", "500", "400.00", "Sarma neagra", "80.00", "<cbc:BaseQuantity unitCode=\"KGM\">100</cbc:BaseQuantity>");

    [Fact]
    public void Factura_eFactura_se_citeste_integral_si_sigur()
    {
        var r = InterpretorEFactura.Interpreteaza(Xml(linii: DouaLinii));
        var f = r.Valoare!;

        Assert.False(r.AreErori);
        Assert.Equal(SursaFactura.EFactura, f.Sursa);
        Assert.Equal("MEX 1234", f.Numar.Valoare);
        Assert.Equal((new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 14)), (f.DataEmitere.Valoare, f.DataScadenta.Valoare));
        Assert.Equal(("MATERIALE EXEMPLU SRL", DateTest.CuiFurnizor, DateTest.CuiFirma), (f.FurnizorDenumire.Valoare, f.FurnizorCui.Valoare, f.CumparatorCui.Valoare));
        Assert.Equal((1100m, 231m, 1331m), (f.TotalFaraTva.Valoare, f.TotalTva.Valoare, f.TotalDePlata.Valoare));
        Assert.Equal("265", f.Referinta);
        Assert.Equal(2, f.Linii.Count);
        Assert.All(f.Linii, l => Assert.Equal(Certitudine.Sigur, l.Denumire.Certitudine));
    }

    [Fact]
    public void Unitatile_UN_ECE_si_pretul_pe_cantitatea_de_baza_se_convertesc()
    {
        var l = InterpretorEFactura.Interpreteaza(Xml(linii: DouaLinii)).Valoare!.Linii;

        Assert.Equal(("buc", 70m, 10m, 0.21m), (l[0].UnitateMasura.Valoare, l[0].Cantitate.Valoare, l[0].PretUnitar.Valoare, l[0].CotaTva.Valoare));
        // 80 lei / 100 kg → 0,80 lei/kg; 500 kg × 0,80 = 400,00 ✓
        Assert.Equal(("kg", 0.8m, 400m), (l[1].UnitateMasura.Valoare, l[1].PretUnitar.Valoare, l[1].ValoareFaraTva.Valoare));
    }

    [Fact]
    public void Nota_de_credit_se_inregistreaza_cu_valori_negative()
    {
        var r = InterpretorEFactura.Interpreteaza(Xml("CreditNote",
            Linie("CreditNoteLine", "CreditedQuantity", "1", "H87", "10", "100.00", "Plasa sudata 2x1 m", "10.00")));

        var f = r.Valoare!;
        Assert.Equal((-1331m, -10m, -100m), (f.TotalDePlata.Valoare, f.Linii[0].Cantitate.Valoare, f.Linii[0].ValoareFaraTva.Valoare));
    }

    [Fact]
    public void Taxarea_inversa_e_semnalata()
    {
        var xml = Encoding.UTF8.GetString(Xml(linii: DouaLinii)).Replace("<cbc:ID>S</cbc:ID><cbc:Percent>21</cbc:Percent>", "<cbc:ID>AE</cbc:ID><cbc:Percent>0</cbc:Percent>");
        var r = InterpretorEFactura.Interpreteaza(Encoding.UTF8.GetBytes(xml));
        Assert.Contains(r.Probleme, p => p.Mesaj.Contains("taxare inversă"));
        Assert.Equal(0m, r.Valoare!.Linii[0].CotaTva.Valoare);
    }

    [Fact]
    public void XML_invalid_sau_care_nu_e_factura_e_respins_fara_exceptie()
    {
        Assert.True(InterpretorEFactura.Interpreteaza("<nu-e-xml"u8.ToArray()).AreErori);
        Assert.True(InterpretorEFactura.Interpreteaza("<Comanda/>"u8.ToArray()).AreErori);
        Assert.False(InterpretorEFactura.EsteEFactura("<Comanda/>"u8.ToArray()));
        Assert.True(InterpretorEFactura.EsteEFactura(Xml()));
    }

    [Fact]
    public void XML_cu_DTD_e_refuzat_protectie_XXE()
    {
        var xml = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///c:/windows/win.ini">]><Invoice>&e;</Invoice>"""u8.ToArray();
        Assert.True(InterpretorEFactura.Interpreteaza(xml).AreErori);
    }
}

public class InterpretorFacturaPdfTeste
{
    private static RandText R(int y, params (string Text, int X0)[] cuvinte) =>
        new(string.Join(' ', cuvinte.Select(c => c.Text)),
            cuvinte.Select(c => new CuvantText(c.Text, c.X0, c.X0 + c.Text.Length * 9, 100)).ToList(), 1, y, y + 14);

    /// <summary>Factură cu furnizorul și cumpărătorul pe două coloane, ca majoritatea facturilor românești.</summary>
    private static TextDocument FacturaTipica(string cantitate2 = "500") => new(
    [
        R(40, ("FACTURA", 300), ("FISCALA", 380)),
        R(60, ("Seria", 280), ("MEX", 330), ("nr.", 370), ("1234", 400)),
        R(80, ("Data", 280), ("facturii:", 320), ("15.10.2026", 400)),
        R(120, ("Furnizor:", 20), ("MATERIALE", 100), ("EXEMPLU", 190), ("SRL", 270), ("Cumpărător:", 420), ("CONSTRUCT", 520), ("TEST", 610), ("SRL", 660)),
        R(140, ("CIF:", 20), ("RO18547290", 60), ("CIF:", 420), ("RO23456783", 460)),
        R(160, ("Nr.", 20), ("Reg.", 50), ("Com.:", 90), ("J13/100/2020", 140), ("Termen", 420), ("de", 480), ("plată:", 500), ("14.11.2026", 560)),
        R(220, ("Nr.", 20), ("Denumirea", 60), ("produselor", 160), ("U.M.", 400), ("Cant.", 460), ("Preț", 530), ("unitar", 570), ("Valoarea", 640), ("Valoarea", 730), ("TVA", 810)),
        R(236, ("(fără", 530), ("TVA)", 580), ("-lei-", 650), ("-lei-", 740)),
        R(260, ("1", 25), ("Plasă", 60), ("sudată", 115), ("2x1", 175), ("m", 210), ("buc", 405), ("70", 470), ("10,00", 545), ("700,00", 650), ("147,00", 740)),
        R(280, ("2", 25), ("Sârmă", 60), ("neagră", 115), ("recoaptă", 180), ("kg", 405), (cantitate2, 465), ("0,80", 550), ("400,00", 650), ("84,00", 745)),
        R(296, ("pentru", 60), ("legături", 125)),
        R(330, ("TOTAL", 400), ("1.100,00", 645), ("231,00", 740)),
        R(350, ("Total", 400), ("de", 450), ("plată:", 475), ("1.331,00", 645)),
        R(380, ("Conform", 20), ("comenzii", 90), ("nr.", 170), ("265", 200)),
    ], [], DinOcr: false);

    [Fact]
    public void Factura_PDF_tipica_se_citeste_corect()
    {
        var f = InterpretorFacturaPdf.Interpreteaza(FacturaTipica(), DateTest.CuiFirma);

        Assert.Equal(SursaFactura.Pdf, f.Sursa);
        Assert.Equal("MEX 1234", f.Numar.Valoare);
        Assert.Equal(new DateOnly(2026, 10, 15), f.DataEmitere.Valoare);
        Assert.Equal(new DateOnly(2026, 11, 14), f.DataScadenta.Valoare);
        Assert.Equal((DateTest.CuiFurnizor, Certitudine.Sigur), (f.FurnizorCui.Valoare, f.FurnizorCui.Certitudine));
        Assert.Equal((DateTest.CuiFirma, Certitudine.Sigur), (f.CumparatorCui.Valoare, f.CumparatorCui.Certitudine));
        Assert.Equal("MATERIALE EXEMPLU SRL", f.FurnizorDenumire.Valoare); // până la eticheta „Cumpărător”
        Assert.Equal((1100m, 231m, 1331m), (f.TotalFaraTva.Valoare, f.TotalTva.Valoare, f.TotalDePlata.Valoare));
        Assert.Equal(Certitudine.Sigur, f.TotalDePlata.Certitudine); // 1.100 + 231 = 1.331 ✓
        Assert.Equal("comenzii nr. 265", f.Referinta);
    }

    [Fact]
    public void Liniile_se_impart_pe_coloane_si_se_verifica_cantitate_ori_pret()
    {
        var l = InterpretorFacturaPdf.Interpreteaza(FacturaTipica(), DateTest.CuiFirma).Linii;

        Assert.Equal(2, l.Count);
        Assert.Equal(("Plasă sudată 2x1 m", "buc", 70m, 10m, 700m), (l[0].Denumire.Valoare, l[0].UnitateMasura.Valoare, l[0].Cantitate.Valoare, l[0].PretUnitar.Valoare, l[0].ValoareFaraTva.Valoare));
        // Denumirea continuă pe rândul următor; 500 × 0,80 = 400,00 ✓ → sigur
        Assert.Equal(("Sârmă neagră recoaptă pentru legături", 500m, Certitudine.Sigur), (l[1].Denumire.Valoare, l[1].Cantitate.Valoare, l[1].Cantitate.Certitudine));
        // Cota din TVA-ul liniei: 147 / 700 = 0,21 (dedusă → de verificat)
        Assert.Equal((0.21m, Certitudine.Nesigur), (l[0].CotaTva.Valoare, l[0].CotaTva.Certitudine));
    }

    [Fact]
    public void Cifrele_din_denumire_raman_in_denumire_iar_continuarea_la_distanta_de_rand_se_uneste()
    {
        // „40 kg” e în stânga coloanei U.M. → denumire. „pentru exterior” începe la 26 px sub produs
        // (rândurile au 14 px înălțime: 300 − 274 = 26 < 2,5 × 14 = 35) → continuarea denumirii.
        var doc = new TextDocument(
        [
            R(220, ("Nr.", 20), ("Denumirea", 60), ("U.M.", 400), ("Cant.", 460), ("Preț", 530), ("Valoarea", 640), ("Valoarea", 730), ("TVA", 810)),
            R(260, ("1", 25), ("Ciment", 60), ("CEM", 120), ("II", 160), ("40", 190), ("kg", 215), ("sac", 405), ("120", 465), ("32,50", 545), ("3.900,00", 640), ("819,00", 740)),
            R(300, ("pentru", 60), ("exterior,", 125), ("gri", 210)),
            R(340, ("TOTAL", 400), ("3.900,00", 640), ("819,00", 740)),
        ], [], DinOcr: false);

        var l = Assert.Single(InterpretorFacturaPdf.Interpreteaza(doc, DateTest.CuiFirma).Linii);

        Assert.Equal(("Ciment CEM II 40 kg pentru exterior, gri", 120m, Certitudine.Sigur), (l.Denumire.Valoare, l.Cantitate.Valoare, l.Cantitate.Certitudine));
    }

    [Fact]
    public void Linia_cu_cantitate_x_pret_diferit_de_valoare_e_de_verificat()
    {
        var f = InterpretorFacturaPdf.Interpreteaza(FacturaTipica(cantitate2: "50"), DateTest.CuiFirma);

        Assert.Equal(Certitudine.Nesigur, f.Linii[1].Cantitate.Certitudine); // 50 × 0,80 = 40 ≠ 400
        Assert.Contains(f.Probleme, p => p.Locatie == "linia 2");
    }

    [Fact]
    public void Factura_emisa_catre_alt_CUI_e_semnalata()
    {
        // Firma activă are alt CUI (valid): ambii CUI din document sunt „străini”.
        var f = InterpretorFacturaPdf.Interpreteaza(FacturaTipica(), "34567892");
        Assert.Contains(f.Probleme, p => p.Locatie == "cumpărător");
        Assert.Equal(DateTest.CuiFurnizor, f.FurnizorCui.Valoare);
    }

    [Fact]
    public void Documentul_fara_tabel_cere_liniile_manual()
    {
        var f = InterpretorFacturaPdf.Interpreteaza(new TextDocument([R(10, ("Factura", 10), ("nr.", 80), ("77", 110))], [], false), DateTest.CuiFirma);
        Assert.Equal("77", f.Numar.Valoare);
        Assert.Empty(f.Linii);
        Assert.Contains(f.Probleme, p => p.Locatie == "linii");
    }

    [Fact]
    public void Termenul_in_zile_da_scadenta_de_verificat()
    {
        var f = InterpretorFacturaPdf.Interpreteaza(new TextDocument(
        [
            R(10, ("Data", 10), ("facturii:", 50), ("01.10.2026", 130)),
            R(30, ("Termen", 10), ("de", 70), ("plată:", 95), ("30", 150), ("zile", 175)),
        ], [], false), DateTest.CuiFirma);

        Assert.Equal((new DateOnly(2026, 10, 31), Certitudine.Nesigur), (f.DataScadenta.Valoare, f.DataScadenta.Certitudine));
    }
}

/// <summary>Formular de comandă cu 3 coloane (Beneficiar | COMANDA | Furnizor), ca formularul GC 007; date sintetice.</summary>
public class ComandaCaFacturaTeste
{
    private static RandText R(int y, params (string Text, int X0)[] cuvinte) =>
        new(string.Join(' ', cuvinte.Select(c => c.Text)),
            cuvinte.Select(c => new CuvantText(c.Text, c.X0, c.X0 + c.Text.Length * 20, 100)).ToList(), 1, y, y + 30);

    private static TextDocument Comanda() => new(
    [
        R(150, ("Beneficiar:", 270)),
        R(190, ("COMANDA", 1130)),
        R(207, ("Furnizor:", 1600), ("ALFA", 1800), ("TRADE", 1900)),
        R(226, ("SC", 320), ("CONSTRUCT", 390), ("TEST", 600), ("SRL", 700)),
        R(250, ("INDUSTRY", 1600)),
        R(292, ("Nr.", 1090), ("7/09.01.2026", 1160)),
        R(330, ("ADRESA", 1600), (":", 1750), ("Str.", 1780), ("Exemplu", 1860)),
        R(415, ("CUI:", 270), ("RO", 360), (DateTest.CuiFirma, 430), ("Obiect/", 940), ("Lucrare", 1100)),
        R(1380, ("MATERIAL", 717), ("/", 944), ("PRODUS", 969), ("U.M", 1547), ("CANTITATE", 1702), ("TERMEN", 2040)),
        R(1460, ("1.", 296), ("MANUSI", 397), ("PROTECTIE", 561), ("MARIMEA", 800), ("9", 1000), ("BUC", 1517), ("60", 1800)),
        R(1507, ("2.", 296), ("PRAF", 397), ("CRETA", 513), ("BUC", 1517), ("1", 1811)),
        R(2120, ("APROBAT,", 249), ("AVIZAT,", 953), ("INTOCMIT,", 1799)),
    ], [], DinOcr: false);

    [Fact]
    public void CUI_ul_beneficiarului_nu_ajunge_niciodata_la_furnizor()
    {
        // Firma activă nu e cea din document → beneficiarul e semnalat, dar furnizorul rămâne fără CUI (nu se ia al beneficiarului).
        var f = InterpretorFacturaPdf.Interpreteaza(Comanda(), "34567892");
        Assert.Equal(Certitudine.Lipsa, f.FurnizorCui.Certitudine);
        Assert.Equal(DateTest.CuiFirma, f.CumparatorCui.Valoare);
    }

    [Fact]
    public void Furnizorul_se_citeste_din_coloana_lui_chiar_pe_doua_randuri()
    {
        var f = InterpretorFacturaPdf.Interpreteaza(Comanda(), DateTest.CuiFirma);
        Assert.Equal("ALFA TRADE INDUSTRY", f.FurnizorDenumire.Valoare); // nu „SC CONSTRUCT TEST SRL” din coloana beneficiarului
        Assert.Equal((DateTest.CuiFirma, Certitudine.Sigur), (f.CumparatorCui.Valoare, f.CumparatorCui.Certitudine));
    }

    [Fact]
    public void Numarul_cu_data_se_imparte_corect()
    {
        var f = InterpretorFacturaPdf.Interpreteaza(Comanda(), DateTest.CuiFirma);
        Assert.Equal(("7", new DateOnly(2026, 1, 9)), (f.Numar.Valoare, f.DataEmitere.Valoare)); // „7/09.01.2026”, nu 2001
    }

    [Fact]
    public void Tabelul_MATERIAL_PRODUS_fara_preturi_se_citeste_iar_documentul_e_semnalat_ca_comanda()
    {
        var f = InterpretorFacturaPdf.Interpreteaza(Comanda(), DateTest.CuiFirma);

        Assert.Equal(2, f.Linii.Count);
        Assert.Equal(("MANUSI PROTECTIE MARIMEA 9", "buc", 60m, Certitudine.Sigur), (f.Linii[0].Denumire.Valoare, f.Linii[0].UnitateMasura.Valoare, f.Linii[0].Cantitate.Valoare, f.Linii[0].Cantitate.Certitudine));
        Assert.Contains(f.Probleme, p => p.Severitate == Severitate.Eroare && p.Locatie == "document");
    }
}

public class FacturaPrimitaTeste
{
    private static readonly LinieFactura Linie = new("Plasă sudată", "buc", 70m, 10m, 700m, 0.21m, 147m);

    private static DateFacturaPrimita Date(string? cui = DateTest.CuiFurnizor, Guid? proiect = null) => new(
        "MEX 1234", new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 14), "ron", null, "Materiale Exemplu SRL", cui,
        proiect ?? Guid.NewGuid(), "P-2026-001", [Linie], 700m, 147m, 847m, "265", null);

    [Fact]
    public void Factura_valida_se_inregistreaza_imuabila()
    {
        var f = FacturaPrimita.Inregistreaza(Date(), SursaFactura.EFactura, Guid.NewGuid(), null, Guid.NewGuid(), "Elena Dumitru").Valoare;

        Assert.Equal(("RON", true), (f.Moneda, f.EsteBlocat));
        Assert.False(f.EsteStorno);
    }

    [Fact]
    public void Proiectul_e_obligatoriu_orice_cost_se_leaga_de_un_proiect()
    {
        var r = FacturaPrimita.Inregistreaza(Date(proiect: Guid.Empty), SursaFactura.Pdf, null, Guid.NewGuid(), Guid.NewGuid(), "Elena");
        Assert.False(r.Succes);
        Assert.Contains("Proiectul", r.Eroare.Mesaj);
    }

    [Fact]
    public void CUI_ul_furnizorului_invalid_si_lipsa_originalului_sunt_respinse()
    {
        Assert.False(FacturaPrimita.Inregistreaza(Date(cui: "18547291"), SursaFactura.Pdf, null, Guid.NewGuid(), Guid.NewGuid(), "E").Succes);
        Assert.False(FacturaPrimita.Inregistreaza(Date(), SursaFactura.Pdf, null, null, Guid.NewGuid(), "E").Succes);
    }

    [Fact]
    public void Anularea_e_o_entitate_separata_cu_motiv_obligatoriu()
    {
        var f = FacturaPrimita.Inregistreaza(Date(), SursaFactura.Pdf, null, Guid.NewGuid(), Guid.NewGuid(), "E").Valoare;

        Assert.False(AnulareFacturaPrimita.Creeaza(f.Id, " ", Guid.NewGuid(), "Elena", DateTimeOffset.UtcNow).Succes);
        var a = AnulareFacturaPrimita.Creeaza(f.Id, " Proiect greșit ", Guid.NewGuid(), "Elena", DateTimeOffset.UtcNow);
        Assert.Equal((f.Id, "Proiect greșit"), (a.Valoare.FacturaId, a.Valoare.Motiv));
    }

    [Fact]
    public void Diferentele_de_totaluri_se_semnaleaza_fara_sa_blocheze()
    {
        // Linii: 700 + TVA 147 = 847; declarat: 700 / 147 / 800 → avans dedus sau greșeală
        var diferente = VerificareTotaluriFactura.Diferente([Linie], 700m, 147m, 800m);
        Assert.Single(diferente);
        Assert.Contains("avans", diferente[0]);
        Assert.Empty(VerificareTotaluriFactura.Diferente([Linie], 700m, 147m, 847m));
    }

    [Fact]
    public void TVA_ul_liniei_se_rotunjeste_la_ban()
    {
        // 333,33 × 0,21 = 69,9993 → 70,00
        Assert.Equal(70.00m, VerificareTotaluriFactura.TvaLinie(333.33m, 0.21m));
    }
}
