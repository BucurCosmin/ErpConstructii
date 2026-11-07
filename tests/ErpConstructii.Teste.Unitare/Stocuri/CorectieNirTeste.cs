using ErpConstructii.Stocuri;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Stocuri;

/// <summary>Corectura unui NIR recepționat greșit (D-35): NIR-ul, diferențele de stoc și proveniența.</summary>
public class CorectieNirTeste
{
    private static readonly Guid Ciment = Guid.NewGuid();
    private static readonly DateOnly Zi = new(2026, 11, 7);

    // Recepție greșită: 7.000 saci în loc de 7, la 30 lei → valoare 210.000.
    private static Nir NirGresit() => ErpConstructii.Stocuri.Nir.Emite(2026, 4, new DateOnly(2026, 11, 2), Gestiune.DepozitCentral(), Guid.NewGuid(), "F1",
        new DateOnly(2026, 11, 1), null, "Furnizor", Guid.NewGuid(), "P-1",
        [new LinieNirCerere(0, Ciment, "MAT-1", "Ciment", "sac", 7000, 7000, 30m, null), new LinieNirCerere(1, Guid.NewGuid(), "MAT-2", "Var", "sac", 10, 10, 12m, null)],
        null, Guid.NewGuid(), "Gestionar").Valoare;

    [Fact]
    public void Corectura_schimba_linia_si_valoarea_NIR_ului_si_da_diferenta_de_stoc()
    {
        var nir = NirGresit();

        var r = nir.Corecteaza([new LinieNirCorectata(0, 7, 7, 30m, null)], Zi, "cantitate citită greșit", Guid.NewGuid(), "Ana");

        Assert.True(r.Succes);
        // 7 × 30 = 210 + var 10 × 12 = 120 → 330.
        Assert.Equal((7m, 330m), (nir.Linii[0].CantitateReceptionata, nir.Valoare));
        var linie = Assert.Single(r.Valoare.Linii);
        Assert.Equal((-6993m, -209790m), (linie.DiferentaCantitate, linie.DiferentaValoare)); // 210 − 210.000
        Assert.Equal(nir.Id, r.Valoare.DocumentCorectatId);
        Assert.Contains("recepționat 7.000 → 7 sac", r.Valoare.Rezumat);
    }

    [Fact]
    public void Fara_motiv_fara_schimbare_sau_cu_diferenta_neexplicata_nu_se_corecteaza()
    {
        var nir = NirGresit();

        Assert.True(nir.Corecteaza([new LinieNirCorectata(0, 7, 7, 30m, null)], Zi, " ", Guid.NewGuid(), "Ana").Esec);
        Assert.Equal("Nu ai modificat nimic.", nir.Corecteaza([new LinieNirCorectata(0, 7000, 7000, 30m, null)], Zi, "x", Guid.NewGuid(), "Ana").Eroare.Mesaj);
        // Recepționat 6 din 7, linie închisă: diferența cere observație.
        Assert.True(nir.Corecteaza([new LinieNirCorectata(0, 7, 6, 30m, null)], Zi, "x", Guid.NewGuid(), "Ana").Esec);
        Assert.Equal(7000m, nir.Linii[0].CantitateReceptionata);
    }

    [Fact]
    public void Stocul_primeste_diferenta_doar_daca_mai_e_in_gestiune()
    {
        // În depozit au rămas 50 de saci (210.000 → restul transferat); corectura scoate 6.993: refuzată.
        Assert.True(CalculStoc.Corectie(new Sold(50, 1500), -6993, -209790, "Ciment").Esec);
        // Rămase 7.000 × 30: după corectură 7 saci, 210 lei.
        Assert.Equal(new Sold(7, 210), CalculStoc.Corectie(new Sold(7000, 210000), -6993, -209790, "Ciment").Valoare);
        // Doar prețul: 7 saci 30 → 32 = +14 lei.
        Assert.Equal(new Sold(7, 224), CalculStoc.Corectie(new Sold(7, 210), 0, 14, "Ciment").Valoare);
        // Prețul nu se mai poate corecta dacă nu a rămas nimic în gestiune.
        Assert.True(CalculStoc.Corectie(Sold.Zero, 0, 14, "Ciment").Esec);
    }

    [Fact]
    public void Provenienta_scade_lotul_NIR_ului_corectat_si_ia_pretul_corectat()
    {
        var nir1 = Guid.NewGuid();
        var nir2 = Guid.NewGuid();
        var dep = Guid.NewGuid();
        MiscareTrasabilitate M(TipMiscare tip, decimal q, decimal v, Guid doc, string nr) => new(dep, Ciment, tip, q, v, doc, nr, Zi);

        // NIR 1: 10 × 30; NIR 2: 20 × 32; corectura NIR 1: −3 saci (30 → 7) și prețul 30 → 31.
        var loturi = TrasabilitateStoc.Calculeaza(
            [
                M(TipMiscare.IntrareNir, 10, 300, nir1, "NIR/2026/0001"),
                M(TipMiscare.IntrareNir, 20, 640, nir2, "NIR/2026/0002"),
                M(TipMiscare.CorectieNir, -3, -83, nir1, "NIR/2026/0001"),
            ],
            new Dictionary<Guid, Guid>(),
            new Dictionary<(Guid, Guid), decimal> { [(nir1, Ciment)] = 31m });

        var l1 = Assert.Single(loturi, l => l.DocumentOrigineId == nir1);
        Assert.Equal((7m, 31m), (l1.Cantitate, l1.PretUnitar));
        Assert.Equal(20m, Assert.Single(loturi, l => l.DocumentOrigineId == nir2).Cantitate); // NIR 2 neatins
    }

    [Fact]
    public void Plusul_din_corectura_intra_tot_pe_NIR_ul_corectat()
    {
        var nir1 = Guid.NewGuid();
        var dep = Guid.NewGuid();
        var loturi = TrasabilitateStoc.Calculeaza(
            [
                new MiscareTrasabilitate(dep, Ciment, TipMiscare.IntrareNir, 7, 210, nir1, "NIR/2026/0001", Zi),
                new MiscareTrasabilitate(dep, Ciment, TipMiscare.CorectieNir, 3, 90, nir1, "NIR/2026/0001", Zi),
            ], new Dictionary<Guid, Guid>());

        var lot = Assert.Single(loturi); // un singur lot: NIR-ul cu plusul lui
        Assert.Equal((nir1, TipMiscare.IntrareNir, 10m), (lot.DocumentOrigineId, lot.TipOrigine, lot.Cantitate));
    }
}
