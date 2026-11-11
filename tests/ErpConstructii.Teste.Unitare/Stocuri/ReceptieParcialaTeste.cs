using ErpConstructii.Stocuri;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Stocuri;

/// <summary>Recepția parțială: o factură recepționată în mai multe NIR-uri, pe linii și cantități.</summary>
public class ReceptieParcialaTeste
{
    private static LinieNir L(int index, decimal deRecepționat, decimal primit, bool partiala, string? obs = null) =>
        new(index, Guid.NewGuid(), "MAT-1", "Material", "buc", deRecepționat, primit, 10m, primit * 10m, obs, partiala);

    [Fact]
    public void Restul_unei_linii_partiale_ramane_de_receptionat()
    {
        // Factura: linia 0 = 100 buc, linia 1 = 40 buc. NIR 1: 60 din linia 0 („restul vine”); linia 1 neatinsă.
        var stare = StareReceptie.Calculeaza([(0, 100m), (1, 40m)], [L(0, 100, 60, partiala: true)]);

        Assert.Equal((60m, 40m, true), (stare[0].Receptionat, stare[0].Ramas, stare[0].Deschisa)); // 100 − 60 = 40
        Assert.Equal((0m, 40m, true), (stare[1].Receptionat, stare[1].Ramas, stare[1].Deschisa));
    }

    [Fact]
    public void Linia_se_inchide_cand_s_a_primit_tot_sau_cand_restul_nu_mai_vine()
    {
        // Linia 0: 60 + 40 = 100 din 100 (două recepții) → închisă. Linia 1: 30 din 40, închisă („restul nu mai vine”).
        var stare = StareReceptie.Calculeaza([(0, 100m), (1, 40m)],
            [L(0, 100, 60, partiala: true), L(0, 40, 40, partiala: true), L(1, 40, 30, partiala: false, "lipsă definitivă")]);

        Assert.All(stare, s => Assert.False(s.Deschisa));
        Assert.Equal(0m, stare[1].Ramas);
    }

    [Fact]
    public void Nirurile_vechi_cu_toata_factura_inchid_liniile_chiar_cu_lipsa()
    {
        // NIR de dinainte de recepția parțială: linia cu 0 primit și observație („nelivrat”) nu mai e de recepționat.
        var stare = StareReceptie.Calculeaza([(2, 40m)], [L(2, 40, 0, partiala: false, "nelivrat")]);

        Assert.False(Assert.Single(stare).Deschisa);
    }

    [Fact]
    public void Nirul_partial_nu_cere_observatie_dar_linia_inchisa_cu_lipsa_cere()
    {
        Nir Emite(params LinieNirCerere[] linii) => Nir.Emite(2026, 1, new DateOnly(2026, 9, 11), Gestiune.DepozitCentral(Depozit.Central()), Guid.NewGuid(), "F1",
            new DateOnly(2026, 9, 10), null, "Furnizor", Guid.NewGuid(), "P-1", linii, null, Guid.NewGuid(), "Gestionar").Valoare;

        // 60 din 100, restul vine: fără observație, fără diferență pe NIR; valoarea intrată 60 × 10 = 600.
        var partial = Emite(new LinieNirCerere(0, Guid.NewGuid(), "MAT-1", "Ciment", "sac", 100, 60, 10m, null, Partiala: true));
        Assert.Equal((600m, false), (partial.Valoare, partial.AreDiferente));

        // 60 din 100 cu linia închisă: observația e obligatorie.
        Assert.True(Nir.Emite(2026, 1, new DateOnly(2026, 9, 11), Gestiune.DepozitCentral(Depozit.Central()), Guid.NewGuid(), "F1", new DateOnly(2026, 9, 10), null, "Furnizor",
            Guid.NewGuid(), "P-1", [new LinieNirCerere(0, Guid.NewGuid(), "MAT-1", "Ciment", "sac", 100, 60, 10m, null)], null, Guid.NewGuid(), "Gestionar").Esec);

        // Tot ce era de primit, marcat „restul vine”: nu mai rămâne nimic, linia se închide fără diferență.
        var complet = Emite(new LinieNirCerere(0, Guid.NewGuid(), "MAT-1", "Ciment", "sac", 40, 40, 10m, null, Partiala: true));
        Assert.False(Assert.Single(complet.Linii).Partiala);
    }
}
