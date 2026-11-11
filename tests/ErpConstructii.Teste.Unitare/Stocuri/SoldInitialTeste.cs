using ErpConstructii.Stocuri;
using ErpConstructii.Stocuri.Import;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Stocuri;

/// <summary>Preluarea stocului din balanța programului de gestiune anterior (D-42): citirea fișierului, potrivirea, documentul SI.</summary>
public class SoldInitialTeste
{
    // Structura exportului Nexus „Balanta stoc”: antet firmă, titlu, perioadă, antetul tabelului, blocuri „Gestiune: …”.
    // Coloanele ca în fișierul „Materii prime”: Stoc ini. în E (4), Intrari în G (6), Iesiri în J (9), Stoc final în M (12), cont în P (15).
    private static string?[] R(params (int Col, string Val)[] celule)
    {
        var r = new string?[17];
        foreach (var (c, v) in celule) r[c] = v;
        return r;
    }

    private static IReadOnlyList<IReadOnlyList<string?>> Fisier() =>
    [
        R((0, "FIRMA TEST SRL\nC.I.F.: RO5160991, Numar inreg. Registrul Comertului: J1994000726136")),
        [],
        R((0, "Balanta stoc - GCC: Materii prime")),
        R((0, "Perioada: 01.09.2026 - 30.09.2026")),
        R((0, "FIRMA TEST SRL * Balanta stoc - GCC *")),
        R((0, "Nr. crt."), (1, "Denumire produs"), (2, "Cod extern"), (3, "U.M."), (4, "Stoc ini. cantitate"), (6, "Intrari"), (9, "Iesiri"), (12, "Stoc final cantitate"), (15, "Denumire cont produs")),
        R((0, "Gestiune: 01-G5")),
        R((0, "1"), (1, "ALICE SFERICE  1035215"), (2, "1035215             "), (3, "KG      "), (4, "1200"), (6, "0"), (9, "0"), (12, "1200"), (15, "MATERII PRIME")),
        R((0, "2"), (1, "TUB APA D.32MM"), (2, "                    "), (3, "ML      "), (4, "0"), (6, "56"), (9, "56"), (12, "0"), (15, "MATERII PRIME")),
        R((0, "Gestiune: 08-G6")),
        R((0, "1"), (1, "ANTIFREEZE G12+ -20LT"), (3, "BUC"), (4, "25.5"), (6, "0"), (9, "12"), (12, "13.5"), (15, "3022.01")),
        R((0, "\n\nSemnaturi,\n\n")),
        R((0, "Nexus ERP v.26.7.266")),
    ];

    [Fact]
    public void Balanta_se_citeste_pe_gestiuni_cu_antetul_firmei_si_perioada()
    {
        var b = InterpretorBalantaStoc.Interpreteaza(Fisier());

        Assert.False(b.AreErori);
        Assert.Equal(("FIRMA TEST SRL", "RO5160991", "Materii prime"), (b.Firma, b.Cif, b.Categorie));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (b.PerioadaDe!.Value, b.PerioadaPanaLa!.Value));
        Assert.Equal(["01-G5", "08-G6"], b.Gestiuni.Select(g => g.Cod));
        Assert.Equal(2, b.Gestiuni[0].Linii.Count);
        var antigel = Assert.Single(b.Gestiuni[1].Linii);
        // 25,5 + 0 − 12 = 13,5: balanța se închide, deci fără avertisment.
        Assert.Equal((25.5m, 0m, 12m, 13.5m, "3022.01"), (antigel.StocInitial, antigel.Intrari, antigel.Iesiri, antigel.StocFinal, antigel.Cont));
        Assert.Empty(b.Probleme);
    }

    [Fact]
    public void Codul_lipit_la_sfarsitul_denumirii_se_scoate_iar_UM_si_codul_se_curata_de_spatii()
    {
        var alice = InterpretorBalantaStoc.Interpreteaza(Fisier()).Gestiuni[0].Linii[0];

        Assert.Equal(("ALICE SFERICE", "1035215", "KG"), (alice.Denumire, alice.CodExtern, alice.UnitateMasura));
        Assert.Null(InterpretorBalantaStoc.Interpreteaza(Fisier()).Gestiuni[0].Linii[1].CodExtern); // doar spații
    }

    [Fact]
    public void Coloanele_se_gasesc_dupa_antet_si_cand_valoarea_sta_in_alta_celula_a_coloanei_unite()
    {
        // Ca în fișierul „Obiecte de inventar”: Stoc ini. în F (5), Iesiri în K (10), Stoc final în N (13), fără cont.
        IReadOnlyList<IReadOnlyList<string?>> f =
        [
            R((0, "Balanta stoc: Ob.inventar in magazie")),
            R((0, "Nr. crt."), (1, "Denumire produs"), (2, "Cod extern"), (3, "U.M."), (5, "Stoc ini. cantitate"), (6, "Intrari"), (10, "Iesiri"), (13, "Stoc final cantitate")),
            R((0, "Gestiune: 09-G7")),
            // Intrările scrise în H (7), nu în G: tot ale coloanei „Intrari” (G..J).
            R((0, "1"), (1, "BOCANCI ORIONE 20 S3 SRC"), (3, "PER"), (5, "0"), (7, "35"), (10, "0"), (13, "35")),
        ];

        var b = InterpretorBalantaStoc.Interpreteaza(f);

        Assert.Equal("Obiecte de inventar în magazie", b.Categorie);
        var l = Assert.Single(Assert.Single(b.Gestiuni).Linii);
        Assert.Equal((0m, 35m, 0m, 35m), (l.StocInitial, l.Intrari, l.Iesiri, l.StocFinal));
        Assert.Contains(b.Probleme, p => !p.Eroare && p.Mesaj.Contains("perioada")); // fără perioadă: avertisment, nu eroare
    }

    [Fact]
    public void Randul_care_nu_se_inchide_da_avertisment_si_se_ia_stocul_final()
    {
        IReadOnlyList<IReadOnlyList<string?>> f =
        [
            .. Fisier().Take(7),
            R((0, "1"), (1, "BARITINA 4/16"), (3, "T"), (4, "4"), (6, "2"), (9, "1"), (12, "6")),
        ];

        var b = InterpretorBalantaStoc.Interpreteaza(f);

        // 4 + 2 − 1 = 5 ≠ 6.
        var p = Assert.Single(b.Probleme);
        Assert.False(p.Eroare);
        Assert.Contains("= 5", p.Mesaj);
        Assert.Equal(6m, b.Gestiuni[0].Linii[0].StocFinal);
    }

    [Fact]
    public void Stocul_final_negativ_da_avertisment_nu_eroare()
    {
        IReadOnlyList<IReadOnlyList<string?>> f =
        [
            .. Fisier().Take(7),
            R((0, "1"), (1, "TEAVA ROT ZINCATA 2X2.9"), (3, "BUC"), (4, "0"), (6, "0"), (9, "1"), (12, "-1")),
        ];

        var b = InterpretorBalantaStoc.Interpreteaza(f);

        Assert.False(b.AreErori);
        Assert.Contains("nu se preia", Assert.Single(b.Probleme).Mesaj);
    }

    [Fact]
    public void Fisierul_fara_antet_sau_gestiuni_da_eroare()
    {
        var b = InterpretorBalantaStoc.Interpreteaza([R((0, "Altceva")), R((0, "1"), (1, "X"))]);

        Assert.True(b.AreErori);
        Assert.Empty(b.Gestiuni);
    }

    [Fact]
    public void Materialul_se_gaseste_intai_dupa_codul_extern_apoi_dupa_denumire_cu_aceeasi_UM()
    {
        var dupaCod = new ArticolExistent(Guid.NewGuid(), "MAT-00001", "Alice sferice (alt nume)", "kg", "1035215");
        var dupaNume = new ArticolExistent(Guid.NewGuid(), "MAT-00002", "Tub apă D.32mm", "ml", null);
        var altaUm = new ArticolExistent(Guid.NewGuid(), "MAT-00003", "Antifreeze G12+ -20LT", "l", null);
        ArticolExistent[] articole = [dupaCod, dupaNume, altaUm];

        Assert.Equal((dupaCod.Id, SursaPotrivireSold.CodExtern), Rezultat(PotrivireSoldInitial.Propune("ALICE SFERICE", "1035215", "KG", articole)));
        Assert.Equal((dupaNume.Id, SursaPotrivireSold.Denumire), Rezultat(PotrivireSoldInitial.Propune("TUB APA D.32MM", null, "ML", articole)));
        Assert.Null(PotrivireSoldInitial.Propune("ANTIFREEZE G12+ -20LT", null, "BUC", articole)); // aceeași denumire, altă UM → material nou

        static (Guid, SursaPotrivireSold)? Rezultat((ArticolExistent Articol, SursaPotrivireSold Sursa)? p) => p is { } x ? (x.Articol.Id, x.Sursa) : null;
    }

    [Theory]
    [InlineData("01-G5", "G5")]
    [InlineData("g7", "G7")]
    [InlineData("01-", "01-")]
    public void Codul_depozitului_e_partea_de_dupa_ultima_cratima(string externa, string asteptat) =>
        Assert.Equal(asteptat, PotrivireSoldInitial.CodDepozit(externa));

    [Fact]
    public void Documentul_SI_aduna_acelasi_material_si_cere_un_depozit()
    {
        var depozit = Gestiune.CreeazaInDepozit(Depozit.Central(), "G5", "Gestiune G5", null, null).Valoare;
        var alice = ArticolStoc.Creeaza("MAT-00001", "ALICE SFERICE", "KG").Valoare;
        var var_ = ArticolStoc.Creeaza("MAT-00002", "VAR", "sac").Valoare;

        var r = SoldInitialStoc.Emite(2026, 1, new DateOnly(2026, 9, 30), depozit, "01-G5", "Materii prime", "balanta.xlsx", "abc",
            [new(alice, 1200, 0, "1035215"), new(var_, 3, 0, null), new(alice, 50, 0, "1035215")], Guid.NewGuid(), "Ana");

        Assert.True(r.Succes);
        Assert.Equal("SI/2026/0001", r.Valoare.NumarAfisat);
        // ALICE: 1.200 + 50 = 1.250, o singură linie.
        Assert.Equal([("ALICE SFERICE", 1250m), ("VAR", 3m)], r.Valoare.Linii.Select(l => (l.Denumire, l.Cantitate)));
        Assert.Equal(0m, r.Valoare.Valoare);

        var santier = Gestiune.Santier(Guid.NewGuid(), "P-1", "Proiect");
        Assert.True(SoldInitialStoc.Emite(2026, 2, new DateOnly(2026, 9, 30), santier, "01-G5", null, "b.xlsx", "abc", [new(alice, 1, 0, null)], Guid.NewGuid(), "Ana").Esec);
    }

    // „Raport rapid stoc” (Nexus): un rând pe lot de preț, gestiunea pe rând, fără U.M. Coloanele ca în fișierul „Materii prime”.
    private static IReadOnlyList<IReadOnlyList<string?>> RaportRapid() =>
    [
        R((0, "FIRMA TEST SRL"), (6, "Materii prime  - Sep.2026")),
        R((0, "C.I.F.: RO5160991, Numar inreg. Registrul Comertului: J1994000726136")),
        R((0, "Telefon: 0241238069")),
        [],
        R((0, "Cod extern"), (2, "Denumire"), (9, "Cantitate"), (12, "Pret unitar"), (14, "Valoare"), (15, "Gestiune")),
        R((2, "ADAPTOR FLANSA SDR17 DE 110 "), (9, "60"), (12, "26.8"), (14, "1608"), (15, "08-G6")),
        R((0, "1035215             "), (2, "ALICE SFERICE  1035215 "), (9, "1200"), (12, "3.6"), (14, "4320"), (15, "01-G5")),
        R((2, "WATERBAR D-19 (15M) "), (9, "60"), (12, "62.5"), (14, "3750"), (15, "01-G5")),
        R((2, "WATERBAR D-19 (15M) "), (9, "120"), (12, "81.66"), (14, "9799.2000000000007"), (15, "01-G5")),
        R((2, "CIZME RYDER 42 "), (9, "2"), (12, "71.305000000000007"), (14, "142.61000000000001"), (15, "01-G5")),
        R((2, "VOPSEA PITURA "), (9, "3"), (12, "10"), (14, "25"), (15, "01-G5")),
        R((0, "Data: 09.11.2026 12:14   (Serban Madalina)"), (7, "Nexus ERP v.26.7.266")),
    ];

    [Fact]
    public void Raportul_rapid_se_citeste_cu_valori_pe_gestiunile_din_randuri_si_luna_din_titlu()
    {
        var b = InterpretorBalantaStoc.Interpreteaza(RaportRapid());

        Assert.False(b.AreErori);
        Assert.True(b.CuValori);
        Assert.Equal(("FIRMA TEST SRL", "RO5160991", "Materii prime"), (b.Firma, b.Cif, b.Categorie));
        // „Sep.2026” → 01.09.2026 – 30.09.2026 (stocul la sfârșitul lunii).
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (b.PerioadaDe!.Value, b.PerioadaPanaLa!.Value));
        Assert.Equal(["08-G6", "01-G5"], b.Gestiuni.Select(g => g.Cod)); // ordinea primei apariții; subsolul nu e material
        var g5 = b.Gestiuni[1].Linii;
        Assert.Equal(("ALICE SFERICE", "1035215", "", 1200m, 3.6m, 4320m), (g5[0].Denumire, g5[0].CodExtern, g5[0].UnitateMasura, g5[0].StocFinal, g5[0].PretUnitar, g5[0].Valoare));
        // Același material pe două loturi de preț rămâne pe două rânduri (documentul SI le adună): 60 × 62,5 = 3.750; 120 × 81,66 = 9.799,20.
        Assert.Equal([(60m, 3750m), (120m, 9799.20m)], g5.Where(l => l.Denumire.StartsWith("WATERBAR", StringComparison.Ordinal)).Select(l => (l.StocFinal, l.Valoare!.Value)));
        // 2 × 71,305 = 142,61: se închide. 3 × 10 = 30 ≠ 25: avertisment, se ia valoarea din fișier.
        var problema = Assert.Single(b.Probleme);
        Assert.Equal((false, 11), (problema.Eroare, problema.Rand!.Value));
        Assert.Contains("cantitate × preț = 30", problema.Mesaj);
        Assert.Equal(25m, g5[^1].Valoare);
    }

    [Fact]
    public void Fara_UM_in_fisier_materialul_se_gaseste_dupa_denumire_doar_daca_e_unul_singur()
    {
        var waterbar = new ArticolExistent(Guid.NewGuid(), "MAT-00001", "WATERBAR D-19 (15M)", "ml", null);
        var antigelL = new ArticolExistent(Guid.NewGuid(), "MAT-00002", "ANTIFREEZE G12+ -20LT", "l", null);
        var antigelBuc = new ArticolExistent(Guid.NewGuid(), "MAT-00003", "ANTIFREEZE G12+ -20LT", "buc", null);
        ArticolExistent[] articole = [waterbar, antigelL, antigelBuc];

        Assert.Equal(waterbar.Id, PotrivireSoldInitial.Propune("WATERBAR D-19 (15M)", null, "", articole)?.Articol.Id);
        Assert.Null(PotrivireSoldInitial.Propune("ANTIFREEZE G12+ -20LT", null, "", articole)); // două U.M.: nu se ghicește
    }

    [Fact]
    public void Soldul_initial_se_anuleaza_cat_timp_materialele_lui_nu_au_alte_miscari()
    {
        var depozit = Gestiune.CreeazaInDepozit(Depozit.Central(), "G5", "Gestiune G5", null, null).Valoare;
        var alice = ArticolStoc.Creeaza("MAT-00001", "ALICE SFERICE", "KG").Valoare;
        var var_ = ArticolStoc.Creeaza("MAT-00002", "VAR", "sac").Valoare;
        var si = SoldInitialStoc.Emite(2026, 1, new DateOnly(2026, 9, 30), depozit, "01-G5", "Materii prime", "balanta.xlsx", "abc",
            [new(alice, 1200, 0, "1035215"), new(var_, 3, 30, null)], Guid.NewGuid(), "Ana").Valoare;
        // ALICE: 1.200 din acest SI + 100 din altul (aceeași gestiune) → rămân 100; VAR iese tot.
        var solduri = new Dictionary<Guid, Sold> { [alice.Id] = new(1300, 0), [var_.Id] = new(3, 30) };

        var r = AnulareSoldInitial.Calculeaza(si, solduri, new HashSet<Guid>());

        Assert.True(r.Succes);
        Assert.Equal([("ALICE SFERICE", new Sold(100, 0)), ("VAR", Sold.Zero)], r.Valoare.Select(x => (x.Linie.Denumire, x.Ramas)));

        var blocat = AnulareSoldInitial.Calculeaza(si, solduri, new HashSet<Guid> { var_.Id });
        Assert.True(blocat.Esec);
        Assert.Contains("1 material are și alte mișcări", blocat.Eroare.Mesaj);
        Assert.Contains("VAR", blocat.Eroare.Mesaj);
    }
}
