using ErpConstructii.Facturi.Import;
using ErpConstructii.SharedKernel.Import;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Facturi;

/// <summary>Rânduri cu poziții, ca după OCR (X în pixeli la 300 DPI; lățimea unui cuvânt ≈ 20 px pe caracter).</summary>
internal static class Pagini
{
    public static RandText R(int pagina, int y, params (string Text, int X0)[] cuvinte) =>
        new(string.Join(' ', cuvinte.Select(c => c.Text)),
            cuvinte.Select(c => new CuvantText(c.Text, c.X0, c.X0 + c.Text.Length * 20, 95)).ToList(), pagina, y, y + 30);

    public static RandText T(int pagina, string text) => new(text, [], pagina);
}

/// <summary>Dosarele de factură scanate: factura, e-Factura, nota de comandă, avizul și certificatele în același PDF.</summary>
public class ClasificarePaginiTeste
{
    private static TextDocument Dosar() => new(
    [
        // 1–2: vizualizarea e-Factura (2 pagini)
        Pagini.T(1, "VANZATOR CUMPARATOR"), Pagini.T(1, "Nume ALFA TRADE SRL RO eFactura Nume CONSTRUCT TEST SRL"), Pagini.T(1, "Pagina 1 din 2"),
        Pagini.T(2, "Instructiuni de plata"), Pagini.T(2, "Nr. cont de plata RO49AAAA1B31007593840000"), Pagini.T(2, "Pagina 2 din 2"),
        // 3: factura furnizorului, care pomenește e-Factura în subsol
        Pagini.T(3, "FACTURA"), Pagini.T(3, "Numar 23558"), Pagini.T(3, "Furnizor Client"), Pagini.T(3, "Factura transmisa in RO e-Factura"),
        // 4: nota de comandă (textul ei pomenește certificatele și declarațiile de conformitate)
        Pagini.T(4, "Beneficiar"), Pagini.T(4, "COMANDA Furnizor"), Pagini.T(4, "SC CONSTRUCT TEST SRL Nr. 496 /08.09.2026 ALFA TRADE"),
        Pagini.T(4, "Obiect/ Lucrare ADRESA: Str. Exemplu"), Pagini.T(4, "Va rugam sa ne expediati, insotite de actele de certificare a calitatii (certificate de calitate, declaratii de conformitate)"),
        // 5: avizul, cu același antet ca factura
        Pagini.T(5, "AVIZ DE INSOTIRE A MARFII"), Pagini.T(5, "Numar 23558 Data 10.09.2026"), Pagini.T(5, "Furnizor Client"),
        // 6–7: declarații de conformitate (a doua pagină fără titlu)
        Pagini.T(6, "DECLARATIA DE CONFORMITATE"), Pagini.T(6, "Articol: ORIONE_24"),
        Pagini.T(7, "Articolo: ORIONE_24"),
    ], [], DinOcr: true);

    [Fact]
    public void Fiecare_pagina_primeste_tipul_documentului_ei()
    {
        var tipuri = ClasificarePagini.Clasifica(Dosar()).Select(p => p.Tip).ToList();
        Assert.Equal(
        [
            TipPagina.EFactura, TipPagina.EFactura, TipPagina.Factura, TipPagina.NotaComanda, TipPagina.Aviz, TipPagina.Certificat, TipPagina.Certificat,
        ], tipuri);
    }

    [Fact]
    public void Nota_de_comanda_isi_pastreaza_numarul_si_data()
    {
        var nota = ClasificarePagini.Clasifica(Dosar()).Single(p => p.Tip == TipPagina.NotaComanda);
        Assert.Equal(("496", new DateOnly(2026, 9, 8)), (nota.Numar, nota.Data));
        Assert.Equal("Notă de comandă nr. 496 din 08.09.2026", nota.Descriere);
    }

    [Fact]
    public void Interpretorul_primeste_doar_paginile_tipului_cerut()
    {
        var (document, clasificare) = ClasificarePagini.PentruTip(Dosar(), TipPagina.NotaComanda);
        Assert.Equal(7, clasificare.Count);
        Assert.All(document.Randuri, r => Assert.Equal(4, r.Pagina));
    }

    [Fact]
    public void Un_document_de_o_pagina_ramane_intreg()
    {
        var doc = new TextDocument([Pagini.T(1, "NECESAR MATERIALE"), Pagini.T(1, "Nr. 81")], [], DinOcr: false);
        var (document, _) = ClasificarePagini.PentruTip(doc, TipPagina.NotaComanda); // tip diferit: nu se taie nimic
        Assert.Same(doc, document);
    }

    [Fact]
    public void Factura_cu_mentiunea_avizului_ramane_factura()
    {
        // „FACTURA FISCALA … Nr. aviz insotire a marfii”: avizul e doar pomenit în factură.
        var doc = new TextDocument([Pagini.T(1, "FACTURA FISCALA"), Pagini.T(1, "Nr. aviz insotire a marfii:"), Pagini.T(1, "Furnizor: ALFA SRL Cumparator: TEST SRL")], [], true);
        Assert.Equal(TipPagina.Factura, ClasificarePagini.Clasifica(doc).Single().Tip);
    }
}

/// <summary>Vizualizarea e-Factura din SPV, scanată (același format la toți furnizorii).</summary>
public class VizualizareEFacturaTeste
{
    private static RandText R(int y, params (string, int)[] c) => Pagini.R(1, y, c);

    private static TextDocument Vizualizare(string valoareLinia2 = "300.00") => new(
    [
        R(150, ("VANZATOR", 150), ("CUMPARATOR", 2180)),
        R(192, ("Nume", 150), ("ALFA", 470), ("TRADE", 590), ("SRL", 700), ("Nume", 2180), ("CONSTRUCT", 2500), ("TEST", 2700), ("SRL", 2800)),
        R(229, ("Nr.", 150), ("inregistrare", 200), ("J13/1/2014", 470), ("RO", 1550), ("eFactura", 1640), ("Nr.", 2180), ("inregistrare", 2230), ("J13/2/1994", 2500)),
        R(308, ("Identificator", 150), (DateTest.CuiFurnizor, 370), ("Identificator", 2180), ("RO" + DateTest.CuiFirma, 2440)),
        R(327, ("Nr.", 1370), ("factura", 1420), ("ATI", 1660), ("778", 1740)),
        R(348, ("Identificatorul", 150), ("TVA", 340), ("RO" + DateTest.CuiFurnizor, 500)),
        R(373, ("Codul", 1370), ("tipului", 1460), ("380", 1660)),
        R(425, ("Data", 1370), ("emitere", 1450), ("2026-09-10", 1660)),
        R(468, ("Data", 1370), ("scadenta", 1450), ("2026-11-09", 1660)),
        R(509, ("Moneda", 1370), ("facturii", 1490), ("RON", 1660)),
        R(776, ("TOTAL", 170), ("NET", 290), ("VALOARE", 700), ("TOTALA", 870), ("fara", 1010), ("TVA", 1080), ("VALOARE", 1240), ("TOTALA", 1400), ("cu", 1540), ("TOTAL", 1670), ("DEDUCERI", 1790)),
        R(859, ("450.00", 170), ("450.00", 700), ("544.50", 1240)),
        R(908, ("TOTAL", 170), ("PLATA", 290), ("544.50", 700)),
        R(992, ("TOTAL", 170), ("TVA", 290), ("94.50", 470), ("RON", 575)),
        R(1222, ("Linia", 150), ("Nume", 290), ("articol/Descriere", 370), ("articol", 600), ("Tara", 1520), ("Pretul", 1700), ("net", 1790), ("Moneda", 1930),
            ("Cantitate", 2050), ("de", 2180), ("baza", 2220), ("Cantitate", 2320), ("UM", 2500), ("Cota", 2600), ("Valoare", 2710), ("neta", 2820)),
        R(1255, ("provenient", 1520), ("articolului", 1700), ("facturata", 2310), ("TVA", 2600)),
        R(1303, ("1", 150), ("CIMENT", 290), ("CEM", 420), ("II", 500), ("40KG", 540), ("15.0000", 1700), ("RON", 1930), ("1", 2050), ("10.000", 2310), ("H87", 2500), ("21", 2600), ("150.00", 2700)),
        R(1344, ("2", 150), ("PLASA", 290), ("SUDATA", 400), ("6MM", 540), ("60.00", 1700), ("RON", 1930), ("1", 2050), ("5.000", 2310), ("MTK", 2500), ("21", 2600), (valoareLinia2, 2700), ("OY", 3400)),
        R(1384, ("100X100", 290)),
        R(1578, ("Referinta", 160), ("comanda", 280), ("articol", 400), ("pentru", 490), ("linia", 575), ("1", 630), (":260900100", 660)),
        R(2123, ("Nr.", 160), ("comanda", 200), ("260900100", 520)),
        R(2300, ("Pagina", 1800), ("1", 2030), ("din", 2140), ("1", 2280)),
    ], [], DinOcr: true);

    [Fact]
    public void Antetul_se_citeste_pe_cele_trei_coloane()
    {
        var f = InterpretorVizualizareEFactura.Interpreteaza(Vizualizare(), DateTest.CuiFirma);
        Assert.Equal("ATI 778", f.Numar.Valoare);
        Assert.Equal(("ALFA TRADE SRL", DateTest.CuiFurnizor, DateTest.CuiFirma), (f.FurnizorDenumire.Valoare, f.FurnizorCui.Valoare, f.CumparatorCui.Valoare));
        Assert.Equal((new DateOnly(2026, 9, 10), new DateOnly(2026, 11, 9)), (f.DataEmitere.Valoare, f.DataScadenta.Valoare));
        Assert.Equal("comanda 260900100", f.Referinta);
    }

    [Fact]
    public void Liniile_si_totalurile_se_verifica_incrucisat()
    {
        // 10 × 15,00 = 150,00; 5 × 60,00 = 300,00 → net 450,00; TVA 21% = 94,50; total 544,50.
        var f = InterpretorVizualizareEFactura.Interpreteaza(Vizualizare(), DateTest.CuiFirma);
        Assert.True(InterpretorVizualizareEFactura.EsteVizualizare(Vizualizare()));
        Assert.Equal(2, f.Linii.Count);
        Assert.Equal(("PLASA SUDATA 6MM 100X100", "mp", 5m, 60m, 300m, 0.21m),
            (f.Linii[1].Denumire.Valoare, f.Linii[1].UnitateMasura.Valoare, f.Linii[1].Cantitate.Valoare, f.Linii[1].PretUnitar.Valoare,
             f.Linii[1].ValoareFaraTva.Valoare, f.Linii[1].CotaTva.Valoare)); // „OY” (zgomot OCR) ignorat; denumirea continuă pe rândul următor
        Assert.Equal((450m, 94.5m, 544.5m), (f.TotalFaraTva.Valoare, f.TotalTva.Valoare, f.TotalDePlata.Valoare));
        Assert.Equal(Certitudine.Sigur, f.TotalDePlata.Certitudine);
    }

    [Fact]
    public void Doua_cifre_gresite_intr_o_linie_strica_verificarea_totalurilor()
    {
        // Linia 2 citită 380,50: 5 × 60 = 300 ≠ 380,50 (două cifre diferite, nu se corectează) → avertisment pe linie;
        // 150 + 380,50 ≠ 450 → totaluri de verificat.
        var f = InterpretorVizualizareEFactura.Interpreteaza(Vizualizare("380.50"), DateTest.CuiFirma);
        Assert.Contains(f.Probleme, p => p.Locatie == "linia 2");
        Assert.Equal(Certitudine.Nesigur, f.TotalDePlata.Certitudine);
    }

    [Fact]
    public void O_cifra_confundata_in_valoarea_liniei_se_corecteaza_si_ramane_de_verificat()
    {
        // Linia 2 citită 360,00 (0 citit 6): 5 × 60,00 = 300,00 diferă printr-o cifră → 300,00, de verificat.
        var f = InterpretorVizualizareEFactura.Interpreteaza(Vizualizare("360.00"), DateTest.CuiFirma);
        Assert.Equal((300m, Certitudine.Nesigur), (f.Linii[1].ValoareFaraTva.Valoare, f.Linii[1].ValoareFaraTva.Certitudine));
        Assert.Contains(f.Probleme, p => p.Locatie == "linia 2" && p.Mesaj.Contains("o cifră", StringComparison.Ordinal));
    }
}
