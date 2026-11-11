using ErpConstructii.Aprovizionare;
using ErpConstructii.Aprovizionare.Import;
using ErpConstructii.SharedKernel.Import;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Avizul de însoțire a mărfii (D-40): citirea scanării și înregistrarea.</summary>
public class AvizTeste
{
    private const string CuiFirma = "RO5160991";

    private static RandText R(int pagina, params (string Text, int X0)[] cuvinte) =>
        new(string.Join(' ', cuvinte.Select(c => c.Text)),
            cuvinte.Select(c => new CuvantText(c.Text, c.X0, c.X0 + c.Text.Length * 20, 95)).ToList(), pagina);

    /// <summary>Ca avizul INTERTRANSCOM scanat: antet în dreapta, furnizorul în stânga, clientul în dreapta, tabel Denumire | UM | Cantitate.</summary>
    private static readonly RandText[] AvizOtel =
    [
        R(1, ("Aviz", 1475), ("expeditie", 1617)),
        R(1, ("Serie/Numar:", 1477), ("INTFSN/20488", 1678)),
        R(1, ("Data:", 1476), ("14.08.2026", 1564)),
        R(1, ("Client:", 1475), ("GENERAL", 1653), ("CONCRETE", 1935)),
        R(1, ("Furnizor:", 277), ("INTERTRANSCOM", 413), ("IMPEX", 705), ("SRL", 812)),
        R(1, ("CUL:", 275), ("RO5169494", 349)),
        R(1, ("Cod", 1475), ("fiscal:", 1560), (CuiFirma, 1700)),
        R(1, ("Nr", 160), ("Denumire", 700), ("produse", 900), ("UM", 1500), ("Cantitate", 1800)),
        R(1, ("OTEL", 170), ("BETON", 280), ("B500C", 400), ("8MM", 520), ("Kg", 1500), ("11,90", 1850)),
        R(1, ("OTEL", 170), ("BETON", 280), ("B500C", 400), ("10MM", 520), ("Kg", 1500), ("11.002,34", 1800)),
        R(1, ("OTEL", 170), ("BETON", 280), ("B500C", 400), ("12MM", 520), ("Ka", 1500), ("1.001.66", 1800)),
        R(1, ("TOTAL", 160), ("GENERAL:", 300), ("11.015,90", 1800)),
        R(1, ("Observatii:", 160)),
        R(1, ("SANTIER", 160), ("RETEHNOLOGIZARE", 400), ("CLADIRE", 800), ("U1", 1000)),
    ];

    /// <summary>Rând cu poziție verticală (Y sus / jos, înălțime 30 px), ca în textul OCR sau cel digital.</summary>
    private static RandText RY(int y, params (string Text, int X0)[] cuvinte) => R(1, cuvinte) with { Y0 = y, Y1 = y + 30 };

    private static RandText[] AntetAviz(int yTabel) =>
    [
        RY(100, ("AVIZ", 175), ("DE", 300), ("INSOTIRE", 400), ("A", 600), ("MARFII", 650)),
        RY(150, ("Serie/Numar:", 175), ("ABC/123", 450)),
        RY(200, ("Data:", 175), ("03.06.2026", 300)),
        RY(250, ("Furnizor:", 175), ("Client:", 1312)),
        RY(300, ("SC", 175), ("FURNIZOR", 240), ("SRL", 450), ("General", 1312), ("Concrete", 1450)),
        RY(350, ("CIF:", 175), ("RO47884086", 260), ("CIF:", 1312), (CuiFirma, 1400)),
        RY(yTabel, ("Nr.", 200), ("Denumire", 600), ("produse", 800), ("UM", 1500), ("Cantitate", 1800)),
        RY(yTabel + 30, ("crt.", 200)),
    ];

    private static string Linii(AvizCitit a) =>
        string.Join(" ; ", a.Linii.Select(l => $"{l.Denumire.Valoare}|{l.UnitateMasura.Valoare}|{l.Cantitate.Valoare.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

    [Fact]
    public void Denumirea_lunga_continuata_sub_cantitate_ramane_la_materialul_ei()
    {
        // Celule aliniate sus: denumirea începe pe rândul cu cantitatea și continuă dedesubt (spațiu de 10 px între rândurile
        // celulei, 40 px între materiale). Rândul de continuare nu e material nou și nu trece la materialul următor.
        RandText[] randuri =
        [
            .. AntetAviz(500),
            RY(600, ("1", 210), ("OTEL", 300), ("BETON", 400), ("B500C", 520), ("16MM", 640), ("FASONAT", 740), ("Kg", 1500), ("16.486,80", 1800)),
            RY(640, ("CONFORM", 300), ("EXTRAS", 460), ("ARMARE", 600), ("POZ.", 740), ("12-18", 820)),
            RY(710, ("2", 210), ("PLASA", 300), ("SUDATA", 420), ("STNB", 560), ("6MM", 660), ("buc", 1500), ("40", 1800)),
            RY(780, ("TOTAL", 175), ("GENERAL", 300), ("16.526,80", 1800)),
        ];

        var a = InterpretorAviz.Interpreteaza(new TextDocument(randuri, [], DinOcr: false), CuiFirma);

        Assert.Equal("OTEL BETON B500C 16MM FASONAT CONFORM EXTRAS ARMARE POZ. 12-18|kg|16486.80 ; PLASA SUDATA STNB 6MM|buc|40", Linii(a));
        Assert.Equal(Certitudine.Nesigur, a.Linii[0].Denumire.Certitudine); // adunată din două rânduri: de verificat
    }

    [Fact]
    public void Denumirea_pe_trei_randuri_cu_cantitatea_centrata_e_un_singur_material()
    {
        // Celule centrate vertical: numărul, UM-ul și cantitatea stau pe rândul din mijloc al denumirii.
        RandText[] randuri =
        [
            .. AntetAviz(500),
            RY(600, ("TEAVA", 300), ("PEHD", 420), ("PE100", 520), ("SDR17", 640)),
            RY(640, ("1", 210), ("D=110MM", 300), ("PN10", 460), ("BARE", 560), ("12M", 660), ("ml", 1500), ("120", 1800)),
            RY(680, ("PENTRU", 300), ("APA", 440), ("POTABILA", 520)),
            RY(760, ("2", 210), ("COT", 300), ("PEHD", 380), ("D110", 480), ("buc", 1500), ("6", 1800)),
            RY(840, ("Observatii:", 175), ("SANTIER", 400)),
        ];

        var a = InterpretorAviz.Interpreteaza(new TextDocument(randuri, [], DinOcr: false), CuiFirma);

        Assert.Equal("TEAVA PEHD PE100 SDR17 D=110MM PN10 BARE 12M PENTRU APA POTABILA|ml|120 ; COT PEHD D110|buc|6", Linii(a));
    }

    [Fact]
    public void Randul_cu_cantitate_fara_denumire_ia_denumirea_din_celula_lui_fara_detalii()
    {
        // Ca avizele AVSOV: denumirea deasupra, „1 TONE 24,920” sub ea, „Cod NC” dedesubt; apoi datele expediției, care nu
        // țin de material (etichete, spațiu mare).
        RandText[] randuri =
        [
            .. AntetAviz(500),
            RY(600, ("V_PIATRA", 341), ("22.4/63", 553)),
            RY(630, ("1", 232), ("TONE", 1500), ("24,920", 1800)),
            RY(660, ("Cod", 341), ("NC:", 435), ("25171010", 523)),
            RY(900, ("Emis", 175), ("de", 300), ("Date", 1312), ("privind", 1437), ("expeditia", 1617)),
            RY(950, ("NIVACI", 175), ("OANA", 322), ("Numele", 1312), ("delegatului:", 1478), ("IONESCU", 1725)),
        ];

        var a = InterpretorAviz.Interpreteaza(new TextDocument(randuri, [], DinOcr: false), CuiFirma);

        Assert.Equal("V_PIATRA 22.4/63|tone|24.920", Linii(a));
    }

    [Fact]
    public void Liniile_tabelului_delimiteaza_celula_si_cand_randurile_sunt_departate()
    {
        // Cu liniile orizontale ale tabelului: tot textul dintre două linii e o celulă, oricât de mare e spațiul dintre rânduri.
        RandText[] randuri =
        [
            .. AntetAviz(500),
            RY(600, ("1", 210), ("CABLU", 300), ("CYY-F", 420), ("3X2,5", 560), ("m", 1500), ("500", 1800)),
            RY(700, ("CUPRU,", 300), ("TAMBUR", 440), ("500M", 600)),
            RY(800, ("2", 210), ("DOZA", 300), ("ETANSA", 420), ("buc", 1500), ("30", 1800)),
            RY(900, ("TOTAL", 175), ("530", 1800)),
        ];
        LinieOrizontala[] linii =
        [
            new(1, 150, 580, 2200, 580), new(1, 150, 760, 2200, 760), new(1, 150, 860, 2200, 860),
        ];

        var a = InterpretorAviz.Interpreteaza(new TextDocument(randuri, [], DinOcr: false, linii), CuiFirma);

        Assert.Equal("CABLU CYY-F 3X2,5 CUPRU, TAMBUR 500M|m|500 ; DOZA ETANSA|buc|30", Linii(a));
    }

    [Fact]
    public void Fara_pozitii_randurile_de_dinaintea_cantitatii_tin_de_ea()
    {
        // Text fără poziții (Word): rândurile de text de dinaintea rândului cu cantitate sunt denumirea lui.
        RandText[] randuri =
        [
            RandText.DinText("AVIZ DE INSOTIRE A MARFII"),
            RandText.DinText("Serie/Numar: ABC/77"),
            RandText.DinText("Data: 03.06.2026"),
            RandText.DinText("Nr. Denumire produse UM Cantitate"),
            RandText.DinText("MORTAR ADEZIV"),
            RandText.DinText("1 PENTRU POLISTIREN 25KG sac 40"),
            RandText.DinText("2 DIBLURI 10CM buc 500"),
            RandText.DinText("TOTAL 540"),
        ];

        var a = InterpretorAviz.Interpreteaza(new TextDocument(randuri, [], DinOcr: false), CuiFirma);

        Assert.Equal("MORTAR ADEZIV PENTRU POLISTIREN 25KG|sac|40 ; DIBLURI 10CM|buc|500", Linii(a));
    }

    [Fact]
    public void Avizul_scanat_da_numarul_data_furnizorul_si_materialele()
    {
        var a = InterpretorAviz.Interpreteaza(new TextDocument(AvizOtel, [], DinOcr: true), CuiFirma);

        Assert.Equal(("INTFSN/20488", new DateOnly(2026, 8, 14)), (a.NumarExtern.Valoare, a.Data.Valoare));
        Assert.Equal(("INTERTRANSCOM IMPEX SRL", "5169494"), (a.FurnizorDenumire.Valoare, a.FurnizorCui.Valoare)); // „CUL” = CUI citit greșit; CUI-ul se păstrează fără „RO”
        Assert.Equal(
            ["OTEL BETON B500C 8MM|kg|11.90", "OTEL BETON B500C 10MM|kg|11002.34", "OTEL BETON B500C 12MM|kg|1001.66"],
            a.Linii.Select(l => $"{l.Denumire.Valoare}|{l.UnitateMasura.Valoare}|{l.Cantitate.Valoare.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        // „Ka” e „Kg” citit greșit: de verificat.
        Assert.Equal(Certitudine.Nesigur, a.Linii[2].UnitateMasura.Certitudine);
        Assert.StartsWith("SANTIER RETEHNOLOGIZARE", a.Lucrare.Valoare);
        Assert.DoesNotContain(a.Probleme, p => p.Locatie == "client");
    }

    [Fact]
    public void Din_dosarul_cu_avize_reavizate_se_citeste_avizul_catre_firma()
    {
        // Pagina 1: avizul transportatorului către firmă (denumirea pe un rând, detaliile pe următoarele, cantitatea pe ultimul);
        // pagina 2: avizul carierei către transportator (alt client), lăsat deoparte.
        RandText[] randuri =
        [
            R(1, ("AVIZ", 1211), ("DE", 1336), ("ÎNSOŢIRE", 1422), ("A", 1676), ("MĂRFII", 1727)),
            R(1, ("Client:", 1266)),
            R(1, ("Furnizor:", 240)),
            R(1, ("SC", 239), ("ONE", 302), ("TRANS", 393), ("LOAD", 532), ("S.R.L", 645), ("General", 1267), ("Concrete", 1409), ("S.R.L.", 1765)),
            R(1, ("CIF:", 239), ("RO47884086", 303), ("CIF:", 1266), (CuiFirma, 1330)),
            R(1, ("PIATRA", 304), ("SPARTA", 446), ("SORT", 589), ("22,4/63", 694)),
            R(1, ("REAVIZARE", 305), ("AVIZ", 511), ("AVSOV6786", 597), ("-", 1008), ("24,92", 1030), ("to", 1127)),
            R(1, ("REAVIZARE", 304), ("AVIZ", 511), ("AVSOV6787", 598), ("-", 1009), ("25,38", 1031), ("to", 1128), ("3", 1985), ("to", 2029), ("101.7", 2114)),
            R(1, ("LUCRARE:", 222), ("REFURBISMENT", 409), ("PACKAGE", 693), ("A", 869)),
            R(1, ("CF", 222), ("COMANDA", 277), ("244", 463), ("/", 540), ("02.06.2026", 560)),
            R(2, ("AVIZ", 397), ("DE", 514), ("INSOTIRE", 588), ("A", 810), ("MARFII", 857)),
            R(2, ("Serie", 399), ("AVSOV", 490), ("Numar", 627), ("6786", 743)),
            R(2, ("S.C.", 319), ("SOV", 394), ("ETRAC", 475), ("S.R.L.", 599), ("ONE", 1267), ("TRANS", 1349), ("LOAD", 1474), ("S.R.L.", 1578)),
            R(2, ("CIF", 321), ("RO26007819", 386), ("CIF", 1269), ("RO47884086", 1333)),
            R(2, ("1", 330), ("V_PIATRA", 360), ("22.4/63", 560), ("TONE", 1500), ("24.920", 1600)),
        ];

        var a = InterpretorAviz.Interpreteaza(new TextDocument(randuri, [], DinOcr: true), CuiFirma);

        Assert.Equal([1], a.Pagini);
        Assert.Equal(("SC ONE TRANS LOAD S.R.L", "47884086"), (a.FurnizorDenumire.Valoare, a.FurnizorCui.Valoare));
        var linie = Assert.Single(a.Linii);
        Assert.Equal(("PIATRA SPARTA SORT 22,4/63", "to", 101.7m), (linie.Denumire.Valoare, linie.UnitateMasura.Valoare, linie.Cantitate.Valoare));
        Assert.Equal(("REFURBISMENT PACKAGE A", "244"), (a.Lucrare.Valoare, a.ReferintaComanda));
        // Numărul nu s-a citit (scanare ștearsă): de completat; data se propune din text, de verificat.
        Assert.Equal(Certitudine.Lipsa, a.NumarExtern.Certitudine);
        Assert.Contains(a.Probleme, p => p.Locatie == "pagini");
    }

    [Fact]
    public void Avizul_catre_alt_client_e_semnalat()
    {
        RandText[] randuri =
        [
            R(1, ("AVIZ", 397), ("DE", 514), ("INSOTIRE", 588)),
            R(1, ("Furnizor", 322), ("Client", 1257)),
            R(1, ("S.C.", 319), ("SOV", 394), ("ETRAC", 475), ("S.R.L.", 599), ("ONE", 1267), ("TRANS", 1349)),
            R(1, ("CIF", 321), ("RO26007819", 386), ("CIF", 1269), ("RO47884086", 1333)),
        ];

        var a = InterpretorAviz.Interpreteaza(new TextDocument(randuri, [], DinOcr: true), CuiFirma);

        Assert.Equal("S.C. SOV ETRAC S.R.L", a.FurnizorDenumire.Valoare);
        Assert.Contains(a.Probleme, p => p.Locatie == "client");
    }

    private static DateAviz Date(params LinieAviz[] linii) => new(
        "INTFSN/20488", new DateOnly(2026, 8, 14), Guid.NewGuid(), "P-07", "Retehnologizare U1", Guid.NewGuid(), "INTERTRANSCOM IMPEX SRL", "RO5169494",
        linii, null);

    [Fact]
    public void Avizul_importat_retine_nota_de_comanda_si_originalul()
    {
        var nota = Guid.NewGuid();
        var r = AvizInsotire.Importa(2026, 1, Date(new LinieAviz(" Otel beton 8mm ", "kg", 11.9m)), nota, "NC/2026/0012", notaGenerata: true,
            Guid.NewGuid(), Guid.NewGuid(), "Ana");

        Assert.True(r.Succes);
        Assert.Equal(("AV/2026/0001", nota, true), (r.Valoare.NumarAfisat, r.Valoare.NotaComandaId, r.Valoare.NotaComandaGenerata));
        Assert.Equal("Otel beton 8mm", r.Valoare.Linii[0].Denumire);
        Assert.True(r.Valoare.EsteBlocat);
    }

    [Fact]
    public void Avizul_fara_materiale_numar_sau_nota_de_comanda_e_refuzat()
    {
        var r = AvizInsotire.Importa(2026, 1, Date() with { NumarExtern = " " }, Guid.Empty, "", false, Guid.NewGuid(), Guid.NewGuid(), "Ana");

        Assert.True(r.Esec);
        Assert.Contains("Numărul de pe aviz este obligatoriu", r.Eroare.Mesaj);
        Assert.Contains("materiale cu denumire, UM și cantitate pozitivă", r.Eroare.Mesaj);
        Assert.Contains("Nota de comandă a avizului este obligatorie", r.Eroare.Mesaj);
    }

    [Fact]
    public void Fisierul_doar_cu_avize_le_propune_ca_documente_iar_avizul_de_langa_factura_ramane_al_ei()
    {
        var doarAvize = ImpartireDocumente.Imparte([
            new PaginaClasificata(1, TipPagina.Aviz, "220", null), new PaginaClasificata(2, TipPagina.Aviz, "6786", null),
            new PaginaClasificata(3, TipPagina.Certificat, null, null),
        ]);
        var cuFactura = ImpartireDocumente.Imparte([
            new PaginaClasificata(1, TipPagina.Factura, "77", null), new PaginaClasificata(2, TipPagina.Aviz, "220", null),
        ]);

        Assert.Equal(["Aviz:1", "Aviz:2,3"], doarAvize.Select(d => $"{d.Tip}:{string.Join(",", d.Pagini)}"));
        Assert.Equal(["Factura:1,2"], cuFactura.Select(d => $"{d.Tip}:{string.Join(",", d.Pagini)}"));
    }

    [Fact]
    public void Avizele_reavizate_catre_alt_client_sunt_anexele_avizului_catre_firma()
    {
        // Pagina 1: avizul transportatorului către firmă (CUI-ul ei); paginile 2-3: avizele carierei către transportator
        // (alt client), reavizate pe primul; pagina 4: un alt aviz către firmă, document separat.
        RandText[] text =
        [
            R(1, ("CIF:", 175), ("RO47884086", 260), ("CIF:", 1312), (CuiFirma, 1400)),
            R(2, ("CIF:", 175), ("RO26007819", 260), ("CIF:", 1312), ("RO47884086", 1400)),
            R(3, ("CIF:", 175), ("RO26007819", 260), ("CIF:", 1312), ("RO47884086", 1400)),
            R(4, ("CUI:", 175), ("RO5169494", 260), ("Cod", 1312), ("fiscal:", 1400), ("RO5160991", 1500)),
        ];
        PaginaClasificata[] pagini =
        [
            new(1, TipPagina.Aviz, "OTL-220", null), new(2, TipPagina.Aviz, "AVSOV 6786", null),
            new(3, TipPagina.Aviz, "AVSOV 6787", null), new(4, TipPagina.Aviz, "INTFSN/18794", null),
        ];

        var documente = ImpartireDocumente.Imparte(pagini, new TextDocument(text, [], DinOcr: true), CuiFirma);

        Assert.Equal(["Aviz:1,2,3:OTL-220", "Aviz:4:INTFSN/18794"], documente.Select(d => $"{d.Tip}:{string.Join(",", d.Pagini)}:{d.Numar}"));
        // Fără CUI-ul firmei, fiecare aviz rămâne document separat.
        Assert.Equal(4, ImpartireDocumente.Imparte(pagini, new TextDocument(text, [], DinOcr: true)).Count);
    }
}
