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
    private static readonly Gestiune Depozit = Gestiune.DepozitCentral();
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
