using ErpConstructii.Aprovizionare;
using ErpConstructii.Aprovizionare.Import;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Date sintetice, așezate ca în formularul „Necesar materiale” (fără date reale de la clienți).</summary>
public class InterpretareNecesarDinWordTeste
{
    private static TextDocument Word(IReadOnlyList<string> randuri, params string[][] tabel) =>
        new(randuri.Select(RandText.DinText).ToList(), [new TabelText(tabel)], DinOcr: false);

    private static readonly string[] Antet = ["Nr. Crt.", "Denumire material", "UM", "Cantitate", "Observatii"];

    [Fact]
    public void Documentul_Word_completat_se_citeste_sigur()
    {
        var doc = Word(
            [
                "S.C. EXEMPLU CONSTRUCT S.R.L. | Nr. 15",
                "Compartiment/Santier: Santier Medgidia | Data: 05.11.2026",
                "NECESAR MATERIALE",
                "Viza Comp. Tehnic-ProductieLucrare: Hala depozitare Medgidia",
            ],
            Antet,
            ["1", "Plasa sudata 2x1 m", "buc", "70", ""],
            ["2", "Ciment CEM II 40 kg", "sac", "1.250,5", "urgent"],
            ["3", "", "", "", ""]);

        var nota = InterpretorNecesarMateriale.Interpreteaza(doc);

        Assert.Equal(new CampCitit<string>("15", Certitudine.Sigur, "Nr. 15"), nota.NumarExtern);
        Assert.Equal(new DateOnly(2026, 11, 5), nota.Data.Valoare);
        Assert.Equal("Santier Medgidia", nota.CompartimentSantier.Valoare);
        Assert.Equal("Hala depozitare Medgidia", nota.Lucrare.Valoare);
        Assert.Equal(2, nota.Linii.Count); // rândul 3 e gol

        var l2 = nota.Linii[1];
        Assert.Equal("Ciment CEM II 40 kg", l2.Denumire.Valoare);
        Assert.Equal("sac", l2.UnitateMasura.Valoare);
        Assert.Equal(1250.5m, l2.Cantitate.Valoare); // „1.250,5”: punct = mii, virgulă = zecimale
        Assert.Equal("urgent", l2.Observatii.Valoare);
        Assert.All(nota.Linii, l => Assert.False(l.AreIncertitudini));
    }

    [Fact]
    public void Sablonul_necompletat_are_toate_campurile_lipsa()
    {
        var doc = Word(
            [
                "S.C. EXEMPLU CONSTRUCT S.R.L. | Nr. {{NR}}",
                "{{Compartiment/Santier}} | Data: {{DATA}}",
                "Viza Comp. Tehnic-ProductieLucrare: {{PROIECT}}",
            ],
            Antet,
            ["1", "", "", "", ""]);

        var nota = InterpretorNecesarMateriale.Interpreteaza(doc);

        Assert.Equal(Certitudine.Lipsa, nota.NumarExtern.Certitudine);
        Assert.Equal(Certitudine.Lipsa, nota.Data.Certitudine);
        Assert.Equal(Certitudine.Lipsa, nota.CompartimentSantier.Certitudine);
        Assert.Equal(Certitudine.Lipsa, nota.Lucrare.Certitudine);
        Assert.Empty(nota.Linii);
        Assert.Contains(nota.Probleme, p => p.Severitate == Severitate.Avertisment && p.Locatie == "tabel");
    }

    [Fact]
    public void Compartimentul_lasat_cu_puncte_de_completare_e_lipsa()
    {
        var doc = Word(["Compartiment/Santier ......................................"], Antet);
        Assert.Equal(Certitudine.Lipsa, InterpretorNecesarMateriale.Interpreteaza(doc).CompartimentSantier.Certitudine);
    }

    [Fact]
    public void Unitatea_necunoscuta_si_cantitatea_invalida_se_semnaleaza()
    {
        var doc = Word([], Antet, ["1", "Diblu", "cutiute", "doua", ""]);

        var linie = Assert.Single(InterpretorNecesarMateriale.Interpreteaza(doc).Linii);

        Assert.Equal(Certitudine.Nesigur, linie.UnitateMasura.Certitudine);
        Assert.Equal(Certitudine.Lipsa, linie.Cantitate.Certitudine);
        Assert.Equal("doua", linie.Cantitate.TextSursa);
    }

    [Theory]
    [InlineData("BUC", "buc")]
    [InlineData("m2", "mp")]
    [InlineData("Buc.", "buc")]
    [InlineData("to", "t")]
    public void Unitatile_uzuale_se_normalizeaza(string scris, string asteptat)
    {
        var doc = Word([], Antet, ["1", "Material", scris, "1", ""]);
        var um = Assert.Single(InterpretorNecesarMateriale.Interpreteaza(doc).Linii).UnitateMasura;
        Assert.Equal((asteptat, Certitudine.Sigur), (um.Valoare, um.Certitudine));
    }
}

public class InterpretareNecesarScanatTeste
{
    // Pozițiile (în pixeli) imită scanarea la 300 DPI a formularului: antetul tabelului dă limitele coloanelor.
    private static RandText Rand(params (string Text, int X0, int X1, float Incredere)[] cuvinte) =>
        new(string.Join(' ', cuvinte.Select(c => c.Text)), cuvinte.Select(c => new CuvantText(c.Text, c.X0, c.X1, c.Incredere)).ToList());

    private static RandText Rand(string text, float incredere = 95) => Rand((text, 100, 100 + text.Length * 10, incredere));

    private static readonly RandText AntetTabel = Rand(
        ("Nr.", 105, 130, 96), ("Denumire", 205, 300, 96), ("material", 305, 380, 96),
        ("UM", 490, 512, 96), ("Cantitate", 540, 610, 96), ("Observatii", 690, 770, 96));

    private static TextDocument Scanare(params RandText[] randuri) => new(randuri, [], DinOcr: true);

    [Fact]
    public void Scanarea_se_imparte_pe_coloane_dupa_antetul_tabelului()
    {
        var doc = Scanare(
            Rand("S.C. EXEMPLU CONSTRUCT S.R.L."),
            Rand(("Nr.", 650, 680, 93), ("81", 690, 710, 91)),
            Rand(("Compartiment/Santier", 100, 330, 90), ("........................", 335, 600, 40)),
            Rand(("Data:", 650, 700, 92), ("08.10.2026", 705, 820, 88)),
            Rand("NECESAR MATERIALE"),
            Rand(("Lucrare", 100, 170, 94), (":", 172, 176, 90), ("Hala", 180, 220, 93), ("depozitare", 225, 330, 92)),
            Rand("Contract nr. E25-S040-000", 90),
            AntetTabel,
            Rand(("Crt.", 108, 130, 90)),
            Rand(("1", 118, 125, 95), ("Plasa", 152, 190, 94), ("expandata", 193, 260, 93), ("2x1", 263, 285, 90), ("m", 288, 298, 92), ("buc", 486, 510, 91), ("70", 566, 582, 93)),
            Rand(("2", 118, 125, 95), ("Tub", 152, 180, 92), ("vaselina", 183, 250, 90), ("buc", 486, 510, 90), ("2", 570, 578, 88)),
            Rand(("3", 118, 125, 95), ("—", 200, 600, 30)), // rând barat
            Rand(("4", 118, 125, 95), ("/", 520, 540, 20)),
            Rand(("Compartiment", 100, 230, 90), ("Aprovizionare", 235, 360, 90), ("-", 365, 370, 90), ("depozit", 375, 450, 90)));

        var nota = InterpretorNecesarMateriale.Interpreteaza(doc);

        Assert.Equal("81", nota.NumarExtern.Valoare);
        Assert.Equal(new DateOnly(2026, 10, 8), nota.Data.Valoare);
        Assert.Equal(Certitudine.Lipsa, nota.CompartimentSantier.Certitudine); // doar puncte de completare
        Assert.Equal("Hala depozitare Contract nr. E25-S040-000", nota.Lucrare.Valoare);
        Assert.True(nota.DinOcr);

        Assert.Equal(2, nota.Linii.Count); // „Crt.”, rândurile barate și subsolul nu devin materiale
        Assert.Equal(("Plasa expandata 2x1 m", "buc", 70m), (nota.Linii[0].Denumire.Valoare, nota.Linii[0].UnitateMasura.Valoare, nota.Linii[0].Cantitate.Valoare));
        Assert.Equal(("Tub vaselina", "buc", 2m), (nota.Linii[1].Denumire.Valoare, nota.Linii[1].UnitateMasura.Valoare, nota.Linii[1].Cantitate.Valoare));
        Assert.All(nota.Linii, l => Assert.False(l.AreIncertitudini));
    }

    [Fact]
    public void Cifrele_citite_ca_litere_se_corecteaza_dar_raman_de_verificat()
    {
        var doc = Scanare(
            AntetTabel,
            Rand(("1", 118, 125, 95), ("Nisip", 152, 200, 94), ("mc", 488, 508, 92), ("1O", 566, 582, 90)));

        var cantitate = Assert.Single(InterpretorNecesarMateriale.Interpreteaza(doc).Linii).Cantitate;

        Assert.Equal((10m, Certitudine.Nesigur, "1O"), (cantitate.Valoare, cantitate.Certitudine, cantitate.TextSursa));
    }

    [Fact]
    public void Cuvintele_cu_incredere_OCR_mica_sunt_de_verificat()
    {
        var doc = Scanare(
            AntetTabel,
            Rand(("1", 118, 125, 95), ("Teava", 152, 200, 45), ("PVC", 205, 240, 50), ("ml", 488, 508, 92), ("12", 566, 582, 93)));

        var linie = Assert.Single(InterpretorNecesarMateriale.Interpreteaza(doc).Linii);

        Assert.Equal(Certitudine.Nesigur, linie.Denumire.Certitudine); // 45 < pragul de 75
        Assert.Equal(Certitudine.Sigur, linie.Cantitate.Certitudine);
    }

    [Fact]
    public void Denumirea_pe_doua_randuri_se_uneste()
    {
        var doc = Scanare(
            AntetTabel,
            Rand(("1", 118, 125, 95), ("Adeziv", 152, 210, 94), ("placi", 215, 260, 93), ("buc", 486, 510, 91), ("5", 570, 578, 93)),
            Rand(("ceramice", 152, 230, 92), ("exterior", 235, 300, 92)));

        var linie = Assert.Single(InterpretorNecesarMateriale.Interpreteaza(doc).Linii);

        Assert.Equal("Adeziv placi ceramice exterior", linie.Denumire.Valoare);
    }

    private static RandText RandY(int y0, int y1, params (string Text, int X0, int X1, float Incredere)[] cuvinte) =>
        Rand(cuvinte) with { Y0 = y0, Y1 = y1 };

    [Fact]
    public void Observatiile_pe_mai_multe_randuri_raman_la_materialul_lor()
    {
        // Pozițiile din formularul GC-043 tipărit: antetul „Observații” e centrat (X 1793), textul observațiilor începe la
        // stânga coloanei (X 1575), mai aproape de antetul „Cantitate” (1330–1530) decât de al lui. Materialul stă pe rândul
        // din mijloc al celulei, cu un rând de observație deasupra și unul dedesubt.
        var antet = RandY(1251, 1275, ("Denumire", 498, 680, 96), ("material", 741, 880, 96), ("UM", 1172, 1215, 96),
            ("Cantitate", 1330, 1530, 96), ("Observații", 1793, 2010, 96));
        var doc = Scanare(
            antet,
            RandY(1345, 1365, ("Livrat", 1575, 1670, 99), ("29.10.2026;", 1686, 1890, 99), ("FURNIZOR-A.", 1912, 2140, 99)),
            RandY(1365, 1385, ("1", 254, 266, 99), ("Sârmă", 350, 470, 99), ("fierar", 483, 580, 99), ("kg", 1184, 1215, 99), ("175", 1395, 1460, 99)),
            RandY(1389, 1409, ("Com.", 1575, 1660, 99), ("391;", 1678, 1750, 99), ("fact.", 1769, 1840, 99), ("65327/29.07.2026.", 1859, 2140, 99)),
            RandY(1458, 1478, ("Livrat", 1575, 1670, 99), ("29.10.2026;", 1686, 1890, 99), ("FURNIZOR-B.", 1912, 2060, 99), ("Fact.", 2078, 2160, 99)),
            RandY(1477, 1497, ("2", 254, 266, 99), ("Saci", 350, 425, 99), ("rafie", 439, 520, 99), ("buc", 1174, 1220, 99), ("3", 1417, 1435, 99)),
            RandY(1502, 1522, ("23744/15.10.2026.", 1575, 1860, 99)),
            RandY(1570, 1590, ("Livrat", 1575, 1670, 99), ("29.10.2026;", 1686, 1890, 99), ("FURNIZOR-C.", 1912, 2050, 99), ("Aviz", 2068, 2140, 99)),
            RandY(1613, 1633, ("3", 254, 266, 99), ("Foarfecă", 350, 510, 99), ("buc", 1174, 1220, 99), ("1", 1417, 1430, 99),
                ("310008065/21.10.2026", 1575, 1990, 99), ("[număr", 2003, 2130, 99), ("de", 2144, 2180, 99)),
            RandY(1658, 1678, ("verificat].", 1575, 1740, 99)),
            RandY(2267, 2287, ("4", 254, 266, 99)), // rând gol al formularului
            RandY(2893, 2913, ("Compartiment", 204, 470, 99), ("Aprovizionare", 487, 740, 99), ("-", 757, 770, 99), ("depozit", 783, 920, 99)));

        var nota = InterpretorNecesarMateriale.Interpreteaza(doc);

        Assert.DoesNotContain(nota.Probleme, p => p.Mesaj.Contains("Cantitatea", StringComparison.Ordinal));
        Assert.Equal(
            [
                ("Sârmă fierar", "kg", 175m, "Livrat 29.10.2026; FURNIZOR-A. Com. 391; fact. 65327/29.07.2026."),
                ("Saci rafie", "buc", 3m, "Livrat 29.10.2026; FURNIZOR-B. Fact. 23744/15.10.2026."),
                ("Foarfecă", "buc", 1m, "Livrat 29.10.2026; FURNIZOR-C. Aviz 310008065/21.10.2026 [număr de verificat]."),
            ],
            nota.Linii.Select(l => (l.Denumire.Valoare!, l.UnitateMasura.Valoare!, l.Cantitate.Valoare, l.Observatii.Valoare!)));
    }

    [Fact]
    public void Valorile_scrise_mai_jos_in_celula_se_unesc_cu_denumirea_iar_randul_urmator_e_material_nou()
    {
        // Înălțimea textului 35 px. „buc 70” e cu 10 px sub denumire (< 2 × 35) → același rând de tabel.
        // „Tub vaselina” (fără număr citit) e la 40 px sub „buc 70” (> 0,8 × 35) → material nou, nr. 2.
        var doc = Scanare(
            AntetTabel with { Y0 = 900, Y1 = 935 },
            RandY(1000, 1035, ("1", 118, 125, 95), ("Plasa", 152, 190, 94), ("expandata", 193, 260, 93)),
            RandY(1045, 1080, ("buc", 486, 510, 95), ("70", 566, 582, 97)),
            RandY(1120, 1155, ("Tub", 152, 180, 96), ("vaselina", 183, 250, 96)),
            RandY(1165, 1200, ("buc", 486, 510, 96), ("2", 570, 578, 96)));

        var linii = InterpretorNecesarMateriale.Interpreteaza(doc).Linii;

        Assert.Equal(2, linii.Count);
        Assert.Equal((1, "Plasa expandata", "buc", 70m), (linii[0].Rand, linii[0].Denumire.Valoare, linii[0].UnitateMasura.Valoare, linii[0].Cantitate.Valoare));
        Assert.Equal((2, "Tub vaselina", "buc", 2m), (linii[1].Rand, linii[1].Denumire.Valoare, linii[1].UnitateMasura.Valoare, linii[1].Cantitate.Valoare));
    }

    // Denumire pe două rânduri, cu numărul, UM și cantitatea centrate pe verticală: ajung pe al doilea rând de text,
    // iar primul rând are doar începutul denumirii. Înălțimea textului 35 px.
    private static RandText[] DenumirePeDouaRanduri() =>
    [
        AntetTabel with { Y0 = 900, Y1 = 935 },
        RandY(1000, 1035, ("1", 118, 125, 95), ("Plasa", 152, 190, 94), ("sudata", 193, 260, 93), ("buc", 486, 510, 95), ("70", 566, 582, 97)),
        RandY(1080, 1115, ("Teava", 152, 200, 95), ("rectangulara", 205, 320, 95)), // primul rând al celulei, fără date
        RandY(1123, 1158, ("2", 118, 125, 95), ("40x40x2", 152, 250, 95), ("ml", 486, 505, 95), ("120", 560, 590, 96)),
        RandY(1240, 1275, ("3", 118, 125, 95), ("Tub", 152, 180, 96), ("vaselina", 183, 250, 96), ("buc", 486, 510, 96), ("2", 570, 578, 96)),
    ];

    [Fact]
    public void Denumirea_pe_doua_randuri_cu_datele_pe_al_doilea_e_un_singur_material_intre_liniile_tabelului()
    {
        // Liniile orizontale ale tabelului: sub antet (970), după fiecare rând fizic (1060, 1190) și jos (1300).
        LinieOrizontala Linie(int y) => new(1, 100, y, 800, y + 2);
        var doc = Scanare(DenumirePeDouaRanduri()) with { Linii = [Linie(880), Linie(970), Linie(1060), Linie(1190), Linie(1300)] };

        var linii = InterpretorNecesarMateriale.Interpreteaza(doc).Linii;

        Assert.Equal(
            [(1, "Plasa sudata", "buc", 70m), (2, "Teava rectangulara 40x40x2", "ml", 120m), (3, "Tub vaselina", "buc", 2m)],
            linii.Select(l => (l.Rand, l.Denumire.Valoare!, l.UnitateMasura.Valoare!, l.Cantitate.Valoare)));
    }

    [Fact]
    public void Fara_linii_randul_doar_cu_denumire_tine_de_randul_cu_date_cel_mai_apropiat()
    {
        // „Teava rectangulara” e la 8 px deasupra rândului cu nr. 2 și la 45 px sub materialul 1 → începutul denumirii lui 2.
        var linii = InterpretorNecesarMateriale.Interpreteaza(Scanare(DenumirePeDouaRanduri())).Linii;

        Assert.Equal(
            [(1, "Plasa sudata"), (2, "Teava rectangulara 40x40x2"), (3, "Tub vaselina")],
            linii.Select(l => (l.Rand, l.Denumire.Valoare!)));
    }

    [Fact]
    public void Fara_linii_continuarea_denumirii_sub_randul_cu_date_ramane_la_el()
    {
        // „rectangulara 40x40x2” e la 8 px sub rândul cu date al materialului 2 și la 82 px deasupra materialului 3.
        var doc = Scanare(
            AntetTabel with { Y0 = 900, Y1 = 935 },
            RandY(1000, 1035, ("2", 118, 125, 95), ("Teava", 152, 200, 95), ("ml", 486, 505, 95), ("120", 560, 590, 96)),
            RandY(1043, 1078, ("rectangulara", 152, 270, 95), ("40x40x2", 275, 350, 95)),
            RandY(1160, 1195, ("3", 118, 125, 95), ("Tub", 152, 180, 96), ("vaselina", 183, 250, 96), ("buc", 486, 510, 96), ("2", 570, 578, 96)));

        var linii = InterpretorNecesarMateriale.Interpreteaza(doc).Linii;

        Assert.Equal([(2, "Teava rectangulara 40x40x2"), (3, "Tub vaselina")], linii.Select(l => (l.Rand, l.Denumire.Valoare!)));
    }

    [Fact]
    public void Punctele_de_completare_citite_ca_litere_nu_devin_valoare()
    {
        // Încrederea sub 50% (zgomot) → câmpul rămâne necompletat.
        var doc = Scanare(Rand(("Compartiment/Santier", 275, 650, 92), ("SSE", 691, 740, 0), ("LLL", 799, 830, 25), ("III", 1016, 1050, 44)));
        Assert.Equal(Certitudine.Lipsa, InterpretorNecesarMateriale.Interpreteaza(doc).CompartimentSantier.Certitudine);
    }

    [Fact]
    public void Compartimentul_completat_de_mana_si_citit_clar_se_pastreaza()
    {
        var doc = Scanare(Rand(("Compartiment/Santier", 275, 650, 92), ("Santier", 700, 790, 80), ("Medgidia", 800, 900, 82)));
        var c = InterpretorNecesarMateriale.Interpreteaza(doc).CompartimentSantier;
        Assert.Equal(("Santier Medgidia", Certitudine.Sigur), (c.Valoare, c.Certitudine));
    }

    [Fact]
    public void Data_cu_virgula_in_loc_de_punct_e_corecta_dar_de_verificat()
    {
        var data = InterpretorNecesarMateriale.Interpreteaza(Scanare(Rand(("Data:", 650, 700, 93), ("08.10,2026", 705, 820, 89)))).Data;
        Assert.Equal((new DateOnly(2026, 10, 8), Certitudine.Nesigur), (data.Valoare, data.Certitudine));
    }

    [Fact]
    public void Data_cu_litera_O_in_loc_de_zero_e_corectata_si_de_verificat()
    {
        var data = InterpretorNecesarMateriale.Interpreteaza(Scanare(Rand(("Data:", 650, 700, 92), ("O8.1O.2026", 705, 820, 90)))).Data;
        Assert.Equal((new DateOnly(2026, 10, 8), Certitudine.Nesigur), (data.Valoare, data.Certitudine));
    }
}

public class PotrivireProiectTeste
{
    private static readonly ProiectCandidat Hala = new(Guid.NewGuid(), "P-2026-007", "Hală depozitare Medgidia");
    private static readonly ProiectCandidat Bloc = new(Guid.NewGuid(), "P-2026-001", "Bloc de locuințe P+8, str. Fabricii 12");

    [Fact]
    public void Codul_proiectului_in_text_da_potrivire_sigura()
    {
        var p = PotrivireProiect.Propune("Lucrări la P-2026-001, etapa 2", [Hala, Bloc]);
        Assert.Equal((Bloc, Certitudine.Sigur), (p!.Proiect, p.Certitudine));
    }

    [Fact]
    public void Cuvintele_comune_din_denumire_dau_o_propunere_de_verificat()
    {
        // Denumirea proiectului: HALA, DEPOZITARE, MEDGIDIA → 3 din 3 în text (fără diacritice și fără majuscule).
        var p = PotrivireProiect.Propune("Constructie hala depozitare - Medgidia, contract 12/2026", [Hala, Bloc]);
        Assert.Equal((Hala, 1.0, Certitudine.Nesigur), (p!.Proiect, p.Scor, p.Certitudine));
    }

    [Fact]
    public void Fara_cuvinte_comune_suficiente_nu_se_propune_nimic()
    {
        // BLOC, LOCUINTE, P+8, FABRICII („DE”, „STR” ignorate, „12” prea scurt) → doar „Bloc” comun: 1/4 < 0,5
        Assert.Null(PotrivireProiect.Propune("Bloc administrativ Cernavoda", [Hala, Bloc]));
        Assert.Null(PotrivireProiect.Propune(null, [Hala, Bloc]));
    }
}

public class NecesarMaterialeImportatTeste
{
    private static readonly Guid Importator = Guid.NewGuid();
    private static readonly LinieNecesarMateriale[] Linii = [new("Plasa sudata", "buc", 70m, string.Empty, null)];

    private static ErpConstructii.SharedKernel.Rezultat<NecesarMateriale> Importa(string? numarExtern = "81", Guid? document = null) =>
        NecesarMateriale.Importa(2026, 12, numarExtern, Guid.NewGuid(), "P-2026-007", "Hală depozitare", "Șantier Medgidia",
            new DateOnly(2026, 10, 8), Importator, "Maria Ionescu", Guid.Empty, 0, Linii, document ?? Guid.NewGuid());

    [Fact]
    public void Nota_importata_primeste_numar_de_sistem_si_pastreaza_numarul_si_data_originalului()
    {
        var nota = Importa().Valoare;

        Assert.Equal("NM/2026/0012", nota.NumarAfisat);
        Assert.Equal("81", nota.NumarExtern);
        Assert.Equal(new DateOnly(2026, 10, 8), nota.Data);
        Assert.Equal(SursaDocument.Import, nota.Sursa);
        Assert.NotNull(nota.DocumentOriginalId);
    }

    [Fact]
    public void Nota_importata_asteapta_aprobarea_si_nu_o_poate_aproba_cine_a_importat_o()
    {
        var nota = Importa().Valoare;

        Assert.Equal(StareNecesarMateriale.InAsteptareAprobare, nota.Stare);
        Assert.False(nota.Aproba(Importator, "Maria Ionescu", DateTimeOffset.UtcNow).Succes);
        Assert.True(nota.Aproba(Guid.NewGuid(), "Radu Marin", DateTimeOffset.UtcNow).Succes);
    }

    [Fact]
    public void Nota_importata_nu_cere_sablon_dar_cere_documentul_original()
    {
        Assert.True(Importa().Succes); // șablon gol (Guid.Empty, versiunea 0)
        Assert.False(Importa(document: Guid.Empty).Succes);
    }

    [Fact]
    public void Numarul_extern_prea_lung_e_respins()
    {
        Assert.False(Importa(numarExtern: new string('9', 51)).Succes);
    }
}
