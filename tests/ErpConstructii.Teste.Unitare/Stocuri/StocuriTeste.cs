using ErpConstructii.SharedKernel;
using ErpConstructii.Stocuri;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Stocuri;

public class CalculStocTeste
{
    [Fact]
    public void Intrarile_succesive_dau_costul_mediu_ponderat()
    {
        // 100 buc × 10,00 = 1.000,00; apoi 50 buc × 13,00 = 650,00 → 150 buc, 1.650,00, CMP = 1.650 / 150 = 11,00.
        var s = CalculStoc.Intrare(Sold.Zero, 100, 1000m).Valoare;
        s = CalculStoc.Intrare(s, 50, 650m).Valoare;
        Assert.Equal((150m, 1650m, 11m), (s.Cantitate, s.Valoare, s.Cmp));
    }

    [Fact]
    public void Iesirea_pleaca_la_CMP_rotunjit_la_ban()
    {
        // 3 buc, 10,00 lei (CMP 3,3333): ieșire 1 buc = 10 × 1/3 = 3,333… → 3,33; rămân 2 buc, 6,67.
        var r = CalculStoc.Iesire(new Sold(3, 10m), 1, "Diblu").Valoare;
        Assert.Equal((3.33m, 2m, 6.67m), (r.ValoareIesire, r.Ramas.Cantitate, r.Ramas.Valoare));
    }

    [Fact]
    public void Iesirea_intregii_cantitati_ia_toata_valoarea_ramasa()
    {
        // 2 buc, 6,67 (după exemplul de mai sus): ieșirea ambelor ia exact 6,67, nu 2 × 3,335 rotunjit; soldul rămâne 0 / 0,00.
        var r = CalculStoc.Iesire(new Sold(2, 6.67m), 2, "Diblu").Valoare;
        Assert.Equal((6.67m, Sold.Zero), (r.ValoareIesire, r.Ramas));
    }

    [Fact]
    public void Stocul_nu_poate_deveni_negativ()
    {
        var r = CalculStoc.Iesire(new Sold(5, 50m), 6, "Ciment CEM II 40kg");
        Assert.True(r.Esec);
        Assert.Contains("Stoc insuficient pentru „Ciment CEM II 40kg”: disponibil 5, cerut 6", r.Eroare.Mesaj);
    }

    [Fact]
    public void Intrarea_cu_cantitate_zero_e_refuzata() =>
        Assert.True(CalculStoc.Intrare(Sold.Zero, 0, 10m).Esec);
}

public class PotrivireArticolTeste
{
    private static readonly Guid Furnizor = Guid.NewGuid();
    private static readonly ArticolCandidat Ciment = new(Guid.NewGuid(), "MAT-00001", "Ciment CEM II/A-S 42,5R sac 40 kg", "sac");
    private static readonly ArticolCandidat Lavete = new(Guid.NewGuid(), "MAT-00002", "Lavete bumbac colorate", "buc");

    [Fact]
    public void Cheia_ignora_diacriticele_si_punctuatia() =>
        Assert.Equal("CIMENT CEM II A S 42 5R SAC 40 KG", PotrivireArticol.Cheie("Ciment CEM II/A-S 42,5R – sac 40 kg"));

    [Fact]
    public void Textul_furnizorului_se_potriveste_dupa_alegerea_anterioara()
    {
        // „LAVETE BUMBAC 40X40 COLORATE” de la acest furnizor a fost legat de MAT-00002 la o recepție anterioară.
        var aliasuri = new[] { new AliasCandidat(Furnizor, PotrivireArticol.Cheie("LAVETE BUMBAC 40X40 COLORATE"), Lavete.Id) };
        var p = PotrivireArticol.Propune("Lavete bumbac 40x40 colorate", Furnizor, aliasuri, [Ciment, Lavete]);
        Assert.Equal((Lavete.Id, SursaPotrivire.AliasFurnizor), (p!.Articol.Id, p.Sursa));
    }

    [Fact]
    public void Fara_alias_se_potriveste_dupa_denumirea_articolului()
    {
        var p = PotrivireArticol.Propune("CIMENT CEM II/A-S 42.5R SAC 40 KG", Furnizor, [], [Ciment, Lavete]);
        Assert.Equal((Ciment.Id, SursaPotrivire.Denumire), (p!.Articol.Id, p.Sursa));
    }

    [Fact]
    public void Materialul_nou_nu_primeste_propunere() =>
        Assert.Null(PotrivireArticol.Propune("Plasă sudată 6 mm", Furnizor, [], [Ciment, Lavete]));
}

public class DocumenteStocTeste
{
    private static readonly Gestiune Depozit = Gestiune.DepozitCentral(ErpConstructii.Stocuri.Depozit.Central());
    private static readonly Guid Articol = Guid.NewGuid();

    private static LinieNirCerere Linie(decimal primit, string? observatii = null) =>
        new(0, Articol, "MAT-00001", "Lavete bumbac", "buc", 1500, primit, 0.975m, observatii);

    [Fact]
    public void NIR_ul_valoreaza_cantitatea_receptionata_la_pretul_facturii()
    {
        // 1.500 buc × 0,975 = 1.462,50 lei.
        var r = Nir.Emite(2026, 1, new DateOnly(2026, 9, 11), Depozit, Guid.NewGuid(), "23558", new DateOnly(2026, 9, 10), null, "GLOBAL TRADE INDUSTRY",
            Guid.NewGuid(), "P-2026-001", [Linie(1500)], null, Guid.NewGuid(), "Ion Gestionar");
        Assert.True(r.Succes, r.Eroare.Mesaj);
        Assert.Equal(("NIR/2026/0001", 1462.50m, false), (r.Valoare.NumarAfisat, r.Valoare.Valoare, r.Valoare.AreDiferente));
    }

    [Fact]
    public void Lipsa_la_receptie_cere_observatie()
    {
        var fara = Nir.Emite(2026, 1, new DateOnly(2026, 9, 11), Depozit, Guid.NewGuid(), "23558", new DateOnly(2026, 9, 10), null, "GTI",
            Guid.NewGuid(), "P-2026-001", [Linie(1400)], null, Guid.NewGuid(), "Ion Gestionar");
        Assert.True(fara.Esec);

        // Cu observație: intră doar ce s-a primit, 1.400 × 0,975 = 1.365,00; NIR-ul arată diferența (−100).
        var cu = Nir.Emite(2026, 1, new DateOnly(2026, 9, 11), Depozit, Guid.NewGuid(), "23558", new DateOnly(2026, 9, 10), null, "GTI",
            Guid.NewGuid(), "P-2026-001", [Linie(1400, "100 buc lipsă la livrare")], null, Guid.NewGuid(), "Ion Gestionar");
        Assert.Equal((1365m, -100m, true), (cu.Valoare.Valoare, cu.Valoare.Linii[0].Diferenta, cu.Valoare.AreDiferente));
    }

    [Fact]
    public void Receptia_nu_se_face_inainte_de_factura_sau_intr_un_santier()
    {
        var santier = Gestiune.Santier(Guid.NewGuid(), "P-2026-001", "Hală");
        var r = Nir.Emite(2026, 1, new DateOnly(2026, 9, 9), santier, Guid.NewGuid(), "23558", new DateOnly(2026, 9, 10), null, "GTI",
            Guid.NewGuid(), "P-2026-001", [Linie(1500)], null, Guid.NewGuid(), "Ion Gestionar");
        Assert.True(r.Esec);
        Assert.Contains("depozit", r.Eroare.Mesaj);
        Assert.Contains("înaintea datei facturii", r.Eroare.Mesaj);
    }

    [Fact]
    public void Transferul_merge_din_depozit_in_santierul_proiectului()
    {
        var proiect = Guid.NewGuid();
        var santier = Gestiune.Santier(proiect, "P-2026-001", "Hală");
        var linii = new[] { new LinieTransfer(Articol, "MAT-00001", "Lavete", "buc", 100, 0.975m, 97.5m) };
        var r = TransferStoc.Emite(2026, 4, new DateOnly(2026, 9, 12), Depozit, santier, "P-2026-001", linii, null, Guid.NewGuid(), "Ion Gestionar");
        Assert.Equal(("BT/2026/0004", proiect, 97.5m), (r.Valoare.NumarAfisat, r.Valoare.ProiectId, r.Valoare.Valoare));

        var invers = TransferStoc.Emite(2026, 5, new DateOnly(2026, 9, 12), santier, Depozit, "P-2026-001", linii, null, Guid.NewGuid(), "Ion Gestionar");
        Assert.True(invers.Esec);
    }
}

public class ConsumSiReturTeste
{
    private static readonly Guid Proiect = Guid.NewGuid();
    private static readonly Gestiune Santier = Gestiune.Santier(Proiect, "P-2026-001", "Hală");
    private static readonly Gestiune Depozit = Gestiune.DepozitCentral(ErpConstructii.Stocuri.Depozit.Central());
    private static readonly Guid Ciment = Guid.NewGuid();
    private static readonly Guid Fundatii = Guid.NewGuid();

    [Fact]
    public void Bonul_de_consum_se_face_din_santier_pe_articole_de_deviz()
    {
        // 20 saci × 11,50 = 230,00 pe fundații + 5 saci × 11,50 = 57,50 fără articol → total 287,50.
        LinieConsum[] linii =
        [
            new(Ciment, "MAT-00001", "Ciment", "sac", 20, 11.5m, 230m, Fundatii, "CA01A1", "Beton în fundații"),
            new(Ciment, "MAT-00001", "Ciment", "sac", 5, 11.5m, 57.5m, null, null, null),
        ];
        var r = BonConsum.Emite(2026, 2, new DateOnly(2026, 9, 15), Santier, "P-2026-001", linii, null, Guid.NewGuid(), "Șef șantier");
        Assert.True(r.Succes, r.Eroare.Mesaj);
        Assert.Equal(("BC/2026/0002", Proiect, 287.5m), (r.Valoare.NumarAfisat, r.Valoare.ProiectId, r.Valoare.Valoare));
        Assert.True(BonConsum.Emite(2026, 3, new DateOnly(2026, 9, 15), Depozit, "P-2026-001", linii, null, Guid.NewGuid(), "x").Esec);
    }

    [Fact]
    public void Acelasi_material_pe_acelasi_articol_de_doua_ori_e_refuzat()
    {
        LinieConsum[] linii =
        [
            new(Ciment, "MAT-00001", "Ciment", "sac", 2, 11.5m, 23m, Fundatii, "CA01A1", "Beton"),
            new(Ciment, "MAT-00001", "Ciment", "sac", 3, 11.5m, 34.5m, Fundatii, "CA01A1", "Beton"),
        ];
        Assert.True(BonConsum.Emite(2026, 1, new DateOnly(2026, 9, 15), Santier, "P", linii, null, Guid.NewGuid(), "x").Esec);
    }

    [Fact]
    public void Returul_merge_din_santier_in_depozit()
    {
        var linii = new[] { new LinieTransfer(Ciment, "MAT-00001", "Ciment", "sac", 10, 11.5m, 115m) };
        var r = TransferStoc.Emite(2026, 7, new DateOnly(2026, 9, 20), Santier, Depozit, "P-2026-001", linii, null, Guid.NewGuid(), "x", DirectieTransfer.Retur);
        Assert.True(r.Succes, r.Eroare.Mesaj);
        Assert.Equal((DirectieTransfer.Retur, Proiect), (r.Valoare.Directie, r.Valoare.ProiectId));
        Assert.True(TransferStoc.Emite(2026, 8, new DateOnly(2026, 9, 20), Depozit, Santier, "P", linii, null, Guid.NewGuid(), "x", DirectieTransfer.Retur).Esec);
    }
}

public class RaportConsumTeste
{
    private static readonly Guid Fundatii = Guid.NewGuid();
    private static readonly Guid Zidarie = Guid.NewGuid();
    private static readonly Guid Vechi = Guid.NewGuid();

    private static readonly ArticolBuget[] Deviz =
    [
        new(Fundatii, "CA01A1", "Beton în fundații", "mc", 10, 1000m), // 10 mc × 100 lei material
        new(Zidarie, "CG01A", "Zidărie BCA", "mc", 20, 4000m),
    ];

    [Fact]
    public void Consumul_se_compara_cu_bugetul_de_materiale_al_articolului()
    {
        // Fundații: 230,00 + 920,00 = 1.150,00 consumat față de 1.000,00 buget → depășire 150,00 (115%).
        ConsumMaterial[] consum =
        [
            new(Fundatii, "MAT-00001", "Ciment", "sac", 20, 230m),
            new(Fundatii, "MAT-00003", "Agregate", "t", 40, 920m),
        ];
        var r = RaportConsum.PeArticole(Deviz, consum);
        Assert.Equal((1150m, 150m, 115.0m, 2), (r[0].Consumat, r[0].Diferenta, r[0].ProcentConsumat, r[0].Materiale.Count));
        Assert.Equal((0m, -4000m, 0.0m), (r[1].Consumat, r[1].Diferenta, r[1].ProcentConsumat)); // zidăria nu e începută
    }

    [Fact]
    public void Consumurile_fara_articol_si_pe_articole_scoase_din_deviz_apar_separat()
    {
        ConsumMaterial[] consum =
        [
            new(null, "MAT-00009", "Lavete", "buc", 100, 97.5m),
            new(Vechi, "MAT-00001", "Ciment", "sac", 2, 23m),
        ];
        var r = RaportConsum.PeArticole(Deviz, consum);
        Assert.Equal(4, r.Count);
        Assert.Equal((Vechi, 23m), (r[2].Cheie!.Value, r[2].Consumat));
        Assert.Equal(((Guid?)null, 97.5m, (decimal?)null), (r[3].Cheie, r[3].Consumat, r[3].ProcentConsumat));
    }
}

public class StornoNirTeste
{
    [Fact]
    public void Stornarea_scoate_exact_valoarea_intrata_nu_CMP_ul()
    {
        // Sold 240 buc, 2.760,00 (120 × 10 + 120 × 13). Stornez NIR-ul cu 120 × 13 = 1.560,00 → rămân 120 buc, 1.200,00 (CMP 10).
        var r = CalculStoc.Storno(new Sold(240, 2760m), 120, 1560m, "Material A");
        Assert.Equal(new Sold(120, 1200m), r.Valoare);
    }

    [Fact]
    public void Stornarea_e_refuzata_daca_materialul_a_plecat_din_gestiune()
    {
        var r = CalculStoc.Storno(new Sold(60, 690m), 120, 1200m, "Material A");
        Assert.True(r.Esec);
        Assert.Contains("Fă întâi returul", r.Eroare.Mesaj);
    }

    [Fact]
    public void Stornarea_intregului_stoc_ia_toata_valoarea_ramasa()
    {
        // 120 buc rămase, 1.200,03 (rotunjiri din ieșiri anterioare): stornarea celor 120 buc golește complet soldul.
        Assert.Equal(Sold.Zero, CalculStoc.Storno(new Sold(120, 1200.03m), 120, 1200m, "Material A").Valoare);
    }

    [Fact]
    public void Documentul_de_storno_cere_motiv_si_preia_liniile_receptionate()
    {
        var nir = Nir.Emite(2026, 5, new DateOnly(2026, 9, 11), Gestiune.DepozitCentral(ErpConstructii.Stocuri.Depozit.Central()), Guid.NewGuid(), "GTX 901", new DateOnly(2026, 9, 10), null, "GAMA",
            Guid.NewGuid(), "P-2026-001",
            [new LinieNirCerere(0, Guid.NewGuid(), "MAT-00001", "Ciment", "sac", 120, 120, 10m, null),
             new LinieNirCerere(2, Guid.NewGuid(), "MAT-00002", "Adeziv", "sac", 40, 0, 45m, "nelivrat")],
            null, Guid.NewGuid(), "Gestionar").Valoare;
        Assert.True(StornoNir.Emite(nir, new DateOnly(2026, 9, 12), " ", Guid.NewGuid(), "Gestionar").Esec);
        var s = StornoNir.Emite(nir, new DateOnly(2026, 9, 12), "Factura anulată de furnizor", Guid.NewGuid(), "Gestionar").Valoare;
        Assert.Equal((1, 1200m, "NIR/2026/0005"), (s.Linii.Count, s.Valoare, s.NirNumar)); // adezivul nerecepționat nu are ce storna
    }
}

public class InventarTeste
{
    private static readonly Gestiune Depozit = Gestiune.DepozitCentral(ErpConstructii.Stocuri.Depozit.Central());
    private static readonly ArticolCandidat Ciment = new(Guid.NewGuid(), "MAT-00001", "Ciment", "sac");
    private static readonly ArticolCandidat Adeziv = new(Guid.NewGuid(), "MAT-00002", "Adeziv", "sac");
    private static readonly ArticolCandidat Diblu = new(Guid.NewGuid(), "MAT-00003", "Diblu", "buc");

    private static Inventar Deschis() => Inventar.Deschide(2026, 1, new DateOnly(2026, 12, 31), Depozit,
        [(Ciment, new Sold(100, 1150m)), (Adeziv, new Sold(40, 1800m))], "Ion, Maria", Guid.NewGuid(), "Gestionar").Valoare;

    private static Dictionary<Guid, Sold> Solduri() => new() { [Ciment.Id] = new Sold(100, 1150m), [Adeziv.Id] = new Sold(40, 1800m) };

    [Fact]
    public void Inventarul_preia_stocul_scriptic_si_nu_se_finalizeaza_nenumarat()
    {
        var inv = Deschis();
        Assert.Equal(("INV/2026/0001", 2, false), (inv.NumarAfisat, inv.Linii.Count, inv.EsteBlocat));
        var r = inv.Finalizeaza(Solduri(), Guid.NewGuid(), "Director");
        Assert.True(r.Esec);
        Assert.Contains("nenumărate", r.Eroare.Mesaj);
    }

    [Fact]
    public void Plusul_intra_la_CMP_si_minusul_iese_la_CMP()
    {
        // Ciment: numărat 102 față de 100 → plus 2 × 11,50 = 23,00. Adeziv: numărat 38 față de 40 → minus 2 × 45,00 = 90,00.
        var inv = Deschis();
        inv.Numara([new(Ciment.Id, 102, null), new(Adeziv.Id, 38, null)], new Dictionary<Guid, ArticolCandidat>());
        var r = inv.Finalizeaza(Solduri(), Guid.NewGuid(), "Director");
        Assert.True(r.Succes, r.Eroare.Mesaj);
        var (plus, minus) = (r.Valoare.Single(x => x.Linie.ArticolId == Ciment.Id), r.Valoare.Single(x => x.Linie.ArticolId == Adeziv.Id));
        Assert.Equal((23m, new Sold(102, 1173m)), (plus.Linie.ValoareDiferenta!.Value, plus.SoldNou));
        Assert.Equal((-90m, new Sold(38, 1710m)), (minus.Linie.ValoareDiferenta!.Value, minus.SoldNou));
        Assert.True(inv.EsteBlocat);
        Assert.True(inv.Numara([new(Ciment.Id, 1, null)], new Dictionary<Guid, ArticolCandidat>()).Esec); // finalizat → imuabil
    }

    [Fact]
    public void Materialul_gasit_fara_evidenta_cere_pret()
    {
        // Diblu: 50 buc găsite, fără stoc scriptic (fără CMP): fără preț → eroare; cu 0,40 lei → plus 50 × 0,40 = 20,00.
        var inv = Deschis();
        var noi = new Dictionary<Guid, ArticolCandidat> { [Diblu.Id] = Diblu };
        inv.Numara([new(Ciment.Id, 100, null), new(Adeziv.Id, 40, null), new(Diblu.Id, 50, null)], noi);
        Assert.Contains("prețul unitar", inv.Finalizeaza(Solduri(), Guid.NewGuid(), "Director").Eroare.Mesaj);

        inv.Numara([new(Diblu.Id, 50, 0.40m)], noi);
        var r = inv.Finalizeaza(Solduri(), Guid.NewGuid(), "Director");
        Assert.Equal((1, 20m, new Sold(50, 20m)), (r.Valoare.Count, r.Valoare[0].Linie.ValoareDiferenta!.Value, r.Valoare[0].SoldNou)); // doar diblul are diferență
    }
}
