using ErpConstructii.Contracte;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Contracte;

/// <summary>Contractele cu subantreprenorii și partenerii (D-41): ciornă → semnat, acte adiționale, închidere, situație.</summary>
public class ContractPartenerTeste
{
    private static readonly DateOnly Semnat = new(2026, 3, 1);

    private static DateContract Date(decimal valoare = 100_000m, params LinieContract[] linii) => new(
        "45/2026", Semnat, Guid.NewGuid(), "RTH-A", Guid.NewGuid(), "Instal Construct SRL", "RO123456", TipContract.Subantreprenoriat,
        "Instalații sanitare corp A", "RON", valoare, linii, new DateOnly(2026, 3, 15), new DateOnly(2026, 9, 30), 30, 0.05m, 10_000m, true, null);

    private static ContractPartener Activ(DateContract? d = null)
    {
        var c = ContractPartener.Creeaza(2026, 3, d ?? Date(), Guid.NewGuid(), "Ana").Valoare;
        c.Activeaza("Șef proiect", DateTimeOffset.UtcNow);
        return c;
    }

    [Fact]
    public void Contractul_pe_pozitii_are_valoarea_sumei_lor_rotunjite()
    {
        // 120,5 mp × 45,33 = 5.462,265 → 5.462,27; 10 buc × 1.200 = 12.000 → 17.462,27 (valoarea globală trimisă se ignoră).
        var c = ContractPartener.Creeaza(2026, 1, Date(999m, new LinieContract("Placare", "mp", 120.5m, 45.33m, 0), new LinieContract("Obiecte", "buc", 10, 1200m, 0)),
            Guid.NewGuid(), "Ana").Valoare;

        Assert.Equal(("CT/2026/0001", 17_462.27m, StareContract.Ciorna), (c.NumarAfisat, c.Valoare, c.Stare));
        Assert.Equal(5_462.27m, c.Linii[0].Valoare);
        Assert.False(c.EsteBlocat);
    }

    [Fact]
    public void Contractul_semnat_nu_se_mai_modifica_doar_prin_act_aditional()
    {
        var c = Activ();

        Assert.True(c.EsteBlocat);
        Assert.Contains("act adițional", c.Modifica(Date(5m)).Eroare.Mesaj);
        Assert.True(c.Activeaza("X", DateTimeOffset.UtcNow).Esec);
    }

    [Fact]
    public void Datele_gresite_sunt_refuzate_impreuna()
    {
        var r = ContractPartener.Creeaza(2026, 1, Date() with { Obiect = " ", ProcentGarantie = 0.6m, DataSfarsit = new DateOnly(2026, 1, 1), Moneda = "EURO" },
            Guid.NewGuid(), "Ana");

        Assert.True(r.Esec);
        Assert.Contains("Obiectul contractului este obligatoriu", r.Eroare.Mesaj);
        Assert.Contains("Garanția de bună execuție e între 0% și 50%", r.Eroare.Mesaj);
        Assert.Contains("Termenul de finalizare e înaintea datei de început", r.Eroare.Mesaj);
        Assert.Contains("Moneda are 3 litere", r.Eroare.Mesaj);
    }

    [Fact]
    public void Ciorna_fara_valoare_nu_se_activeaza()
    {
        var c = ContractPartener.Creeaza(2026, 1, Date(0m), Guid.NewGuid(), "Ana").Valoare;

        Assert.True(c.Activeaza("Ana", DateTimeOffset.UtcNow).Esec);
    }

    [Fact]
    public void Situatia_aduna_actele_aditionale_facturile_si_garantia_retinuta()
    {
        var c = Activ();
        var act1 = ActAditional.Creeaza(c, null, 1, null, new DateOnly(2026, 6, 1), "Lucrări suplimentare", 20_000m, new DateOnly(2026, 11, 30), c.Valoare, "Ana").Valoare;
        var act2 = ActAditional.Creeaza(c, null, 2, null, new DateOnly(2026, 7, 1), "Renunțare la corp B", -5_000m, null, c.Valoare + 20_000m, "Ana").Valoare;
        FacturaPeContract[] facturi =
        [
            new(Guid.NewGuid(), "F1", new DateOnly(2026, 4, 30), 40_000m, "RON"),
            new(Guid.NewGuid(), "F2", new DateOnly(2026, 5, 31), 35_000m, "RON"),
            new(Guid.NewGuid(), "S1", new DateOnly(2026, 6, 10), -1_000m, "RON"), // storno
        ];

        var s = SituatieContract.Calculeaza(c, [act2, act1], facturi, inchis: false, azi: new DateOnly(2026, 12, 1));

        // 100.000 + 20.000 − 5.000 = 115.000; facturat 40.000 + 35.000 − 1.000 = 74.000; rest 41.000.
        Assert.Equal((115_000m, 74_000m, 41_000m), (s.ValoareActualizata, s.Facturat, s.Rest));
        // 74.000 / 115.000 = 64,35 % → 64,3; garanția 5 % × 74.000 = 3.700.
        Assert.Equal((64.3m, 3_700m, false), (s.ProcentFacturat, s.GarantieRetinuta, s.Depasit));
        // Termenul prelungit de actul 1 (30.11.2026) a trecut pe 01.12.2026.
        Assert.Equal((new DateOnly(2026, 11, 30), true), (s.Termen, s.TermenDepasit));
    }

    [Fact]
    public void Facturat_peste_valoare_e_semnalat()
    {
        var s = SituatieContract.Calculeaza(Activ(), [], [new(Guid.NewGuid(), "F", Semnat, 100_500m, "RON")], false, Semnat);

        Assert.Equal((-500m, true), (s.Rest, s.Depasit));
    }

    [Fact]
    public void Contractul_inchis_nu_mai_primeste_acte_aditionale_si_rezilierea_cere_motiv()
    {
        var c = Activ();
        Assert.True(InchidereContract.Creeaza(c, false, TipInchidere.Reziliat, Semnat, " ", "Ana").Esec);

        var inchis = InchidereContract.Creeaza(c, false, TipInchidere.Finalizat, new DateOnly(2026, 10, 1), null, "Ana").Valoare;

        Assert.True(ActAditional.Creeaza(c, inchis, 1, null, new DateOnly(2026, 10, 2), "x", 100m, null, c.Valoare, "Ana").Esec);
        Assert.True(InchidereContract.Creeaza(c, true, TipInchidere.Finalizat, Semnat, null, "Ana").Esec);
    }

    [Fact]
    public void Actul_aditional_nu_poate_face_valoarea_negativa_si_trebuie_sa_schimbe_ceva()
    {
        var c = Activ();

        Assert.True(ActAditional.Creeaza(c, null, 1, null, Semnat, "Renunțare", -100_001m, null, c.Valoare, "Ana").Esec);
        Assert.True(ActAditional.Creeaza(c, null, 1, null, Semnat, "Nimic", 0m, null, c.Valoare, "Ana").Esec);
    }
}
