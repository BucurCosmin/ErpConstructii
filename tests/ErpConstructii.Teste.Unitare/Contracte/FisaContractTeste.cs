using ErpConstructii.Contracte;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Contracte;

/// <summary>Fișa de urmărire a contractului (D-56), cu valorile din fișa HT Clima (contract 87/30.04.2026).</summary>
public class FisaContractTeste
{
    private static readonly DateOnly Semnat = new(2026, 4, 30);

    private static readonly Guid Proiect = Guid.NewGuid(), Partener = Guid.NewGuid();

    private static DateContract Date() => new(
        "87/30.04.2026", Semnat, Proiect, "CNE-U1", Partener, "HIGH-TECH CLIMA SA", "RO16645925", TipContract.Subantreprenoriat,
        "Instalații HVAC, sanitare, stingere incendiu, electrice", "EUR", 3_501_265.27m, [], null, null, 35, 0.05m, 0, true, null);

    private static ConditiiContract Conditii(params MembruAsociere[] membri) => ConditiiContract.Goale with
    {
        TipPret = "Prețuri unitare agreate",
        Amplasament = "B5, B6 și B10 Parking Area",
        Membri = membri.Length > 0 ? membri : [new("MELSAM CONSTRUCTION SRL", null, 0.5m, false), new("GENERAL CONCRETE CERNAVODĂ SRL", null, 0.5m, true)],
        TranseAvans = [new(1, 0.05m, "Tranșa 1"), new(2, 0.10m, "Tranșa 2"), new(3, 0.05m, "Tranșa 3")],
        ProcentGarantieAvans = 0.20m,
        ProcentGarantieTehnica = 0.05m,
        LuniGarantieTehnica = 60,
        PenalitateExecutieZi = 0.0013m,
        PlafonPenalitateExecutie = 0.10m,
    };

    [Fact]
    public void Transele_de_avans_dau_avansul_din_valoarea_contractului()
    {
        var c = ContractPartener.Creeaza(2026, 1, Date(), Guid.NewGuid(), "Ana").Valoare;

        Assert.True(c.SeteazaConditii(Conditii()).Succes);

        // 5% + 10% + 5% = 20% din 3.501.265,27 = 700.253,054 → 700.253,05 EUR.
        Assert.Equal(700_253.05m, c.Avans);
        Assert.Equal([1, 2, 3], c.TranseAvans.Select(t => t.Numar));
        Assert.Equal((2, 60), (c.Membri.Count, c.LuniGarantieTehnica!.Value));
    }

    [Fact]
    public void Cotele_asocierii_fac_100_la_suta_si_un_singur_membru_e_firma_activa()
    {
        var c = ContractPartener.Creeaza(2026, 1, Date(), Guid.NewGuid(), "Ana").Valoare;

        Assert.Contains("100%", c.SeteazaConditii(Conditii(new("MELSAM", null, 0.5m, false), new("GCC", null, 0.4m, true))).Eroare.Mesaj);
        Assert.True(c.SeteazaConditii(Conditii(new("MELSAM", null, 0.5m, true), new("GCC", null, 0.5m, true))).Esec);
        Assert.True(c.SeteazaConditii(Conditii(new("MELSAM", null, 0.5m, false), new("GCC", null, 0.5m, true))).Succes);
    }

    [Fact]
    public void Actul_aditional_propus_nu_schimba_valoarea_pana_la_semnare()
    {
        var c = ContractPartener.Creeaza(2026, 1, Date(), Guid.NewGuid(), "Ana").Valoare;
        c.Activeaza("Șef proiect", DateTimeOffset.UtcNow);
        var aa1 = ActAditional.Creeaza(c, null, 1, null, new DateOnly(2026, 10, 12), "Înlocuire chillere și CTA", 26_439.26m, null, c.Valoare, "Ana", semnat: false).Valoare;

        var s = SituatieContract.Calculeaza(c, [aa1], [], false, new DateOnly(2026, 10, 15));
        // Valoarea rămâne cea semnată; propusă = 3.501.265,27 + 26.439,26 = 3.527.704,53 (ca în fișă).
        Assert.Equal((3_501_265.27m, 3_527_704.53m), (s.ValoareActualizata, s.ValoarePropusa));

        Assert.True(aa1.Semneaza(new DateOnly(2026, 10, 20), "Director").Succes);
        Assert.Equal(3_527_704.53m, SituatieContract.Calculeaza(c, [aa1], [], false, new DateOnly(2026, 10, 21)).ValoareActualizata);
        Assert.True(aa1.Semneaza(new DateOnly(2026, 10, 21), "Director").Esec); // deja semnat
    }

    [Fact]
    public void Contractul_semnat_se_corecteaza_cu_motiv_si_pastreaza_ce_s_a_schimbat()
    {
        var c = ContractPartener.Creeaza(2026, 1, Date(), Guid.NewGuid(), "Ana").Valoare;
        c.SeteazaConditii(Conditii());
        c.Activeaza("Șef proiect", DateTimeOffset.UtcNow);
        var noi = c.Conditii with { DataPvrtl = new DateOnly(2026, 11, 1), StareText = "Recepționat" };

        Assert.True(c.Corecteaza(Date(), noi, " ", "Ana", DateTimeOffset.UtcNow).Esec); // fără motiv
        var r = c.Corecteaza(Date(), noi, "PVRTL primit", "Ana", DateTimeOffset.UtcNow);

        Assert.True(r.Succes);
        Assert.Equal(c.Id, r.Valoare.DocumentCorectatId);
        Assert.Contains("PVRTL: — → 01.11.2026", r.Valoare.Modificari);
        Assert.Contains("Stadiu: — → Recepționat", r.Valoare.Modificari);
        Assert.Equal(new DateOnly(2026, 11, 1), c.DataPvrtl);
        Assert.True(c.Corecteaza(Date(), noi, "din nou", "Ana", DateTimeOffset.UtcNow).Esec); // nimic schimbat
    }

    [Fact]
    public void Ciorna_nu_se_corecteaza_ci_se_modifica_direct()
    {
        var c = ContractPartener.Creeaza(2026, 1, Date(), Guid.NewGuid(), "Ana").Valoare;

        Assert.True(c.Corecteaza(Date(), Conditii(), "motiv", "Ana", DateTimeOffset.UtcNow).Esec);
        Assert.True(c.Modifica(Date(), Conditii()).Succes);
        Assert.Equal("Prețuri unitare agreate", c.TipPret);
    }

    [Fact]
    public void Registrele_fisei_isi_valideaza_randurile()
    {
        var id = Guid.NewGuid();
        Assert.True(ObligatieContract.Creeaza(id, 1, new DateObligatie(" ", "TCS 5.2", null, null, null, null, null)).Esec);
        var o = ObligatieContract.Creeaza(id, 1, new DateObligatie("Plata facturilor", "TCS 5.11", "35 de zile de la comunicarea facturii", null, "De verificat", null, null)).Valoare;
        Assert.Equal(("TCS 5.11", 1), (o.Temei!, o.Ordine));

        Assert.True(FazaExecutie.Creeaza(id, 1, new DateFaza("Montaj", new DateOnly(2026, 10, 7), new DateOnly(2026, 5, 1), null, null, null)).Esec);
        Assert.True(InstrumentGarantie.Creeaza(id, 1, new DateInstrument(TipInstrument.GarantieAvans, null, "Citibank", "5164602129", "MELSAM", 175_063.26m, "EUR",
            new DateOnly(2027, 4, 30), new DateOnly(2026, 5, 18), null, null)).Esec); // expiră înainte de emitere

        var sgb = InstrumentGarantie.Creeaza(id, 1, new DateInstrument(TipInstrument.GarantieAvans, "Restituire avans 5%", "Citibank Europe plc", "5164602129",
            "MELSAM CONSTRUCTION SRL", 175_063.26m, "eur", new DateOnly(2026, 5, 18), new DateOnly(2027, 4, 30), null, null)).Valoare;
        Assert.Equal(("EUR", 175_063.26m), (sgb.Moneda!, sgb.Valoare!.Value));

        var a = ActiuneContract.Creeaza(id, 1, new DateActiune("Confirmarea semnării AA1", PrioritateActiune.Critica, StareActiune.Deschisa, null, "Jean", null, null)).Valoare;
        Assert.True(a.Modifica(new DateActiune("Confirmarea semnării AA1", PrioritateActiune.Critica, StareActiune.Inchisa, null, "Jean", null, "Semnat")).Succes);
        Assert.Equal(StareActiune.Inchisa, a.Stare);
    }
}
