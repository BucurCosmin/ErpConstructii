using ErpConstructii.Contracte;
using ErpConstructii.Contracte.Import;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Contracte;

/// <summary>Interpretorul fișei de urmărire (D-56, etapa 3) pe foi sintetice, în formatul fișelor reale (anonimizat).</summary>
public class InterpretorFisaContractTeste
{
    private static FoaieTabel Foaie(string nume, params string?[][] randuri) => new(nume, randuri);

    private static readonly IReadOnlyList<FoaieTabel> Fisa =
    [
        Foaie("1. Date Generale",
            ["FIȘĂ DE URMĂRIRE CONTRACT — EXEMPLU SRL"],
            [],
            ["Indicator", "Informații", "Observații"],
            ["Contract", "12/15.03.2026", "Contract semnat"],
            ["Subantreprenor", "EXEMPLU INSTAL SRL", "CUI RO123456"],
            ["Antreprenori", "ALFA CONSTRUCT SRL și BETA BUILD SRL", "Asociere"],
            ["Proiect", "Hală industrială", "Lot 2"],
            ["Obiect", "Instalații HVAC", null],
            ["Monedă", "EUR fără TVA", null],
            ["Valoare inițială", "1000000", "fără TVA"],
            ["Avans contractual", "20% în tranșe", "TCS: 10% + 10%"],
            ["Perioadă de execuție", "01.04.2026 – 30.09.2026", null],
            ["Plăți efectuate", "De reconciliat", "Fără extrase"]),
        Foaie("2. Valoare contract",
            ["MONITORIZAREA VALORII CONTRACTULUI"],
            [],
            ["Document", "Modificare EUR", "Valoare cumulată EUR", "Statut"],
            ["Contract 12 / 15.03.2026", "1000000", "1000000", "Semnat"],
            ["AA1 / 10.06.2026", "50000", "1050000", "nesemnat — propunere"]),
        Foaie("3. Obligatii Termene",
            ["REGISTRUL OBLIGAȚIILOR"],
            [],
            ["Obligație / control", "Temei contractual", "Termen / condiție", "Status", "Responsabil / observații"],
            ["Plata facturilor", "TCS 5.11", "30 de zile de la comunicarea facturii", "De verificat", null]),
        Foaie("4. Facturi Plati",
            ["URMĂRIREA FACTURILOR ȘI PLĂȚILOR"],
            [],
            ["Nr. factură", "Data", "Beneficiar", "Tip", "EUR fără TVA", "Curs BNR", "RON fără TVA", "TVA RON", "RON cu TVA", "Plată RON", "Status plată"],
            ["F001", "20.04.2026", "BETA BUILD SRL", "Avans tranșa 1", "50000", "5", "250000", "47500", "297500", null, "Achitat 25.04.2026"],
            ["F002", "20.04.2026", "ALFA CONSTRUCT SRL", "Avans tranșa 1", "50000", "5", "250000", "47500", "297500", null, "BO emis"],
            ["TOTAL", null, null, null, "100000"]),
        Foaie("5. Garantii Riscuri",
            ["GARANȚII"],
            [],
            ["Element", "Prevedere contractuală / verificare", "Status / risc"],
            ["Garanție bună execuție", "5% din valoarea contractului", "De constituit"],
            ["Garanție lucrări", "60 de luni de la procesul-verbal de recepție", null],
            ["Penalitate întârziere execuție", "0,1%/zi din valoarea contractului; plafon 10%", null]),
        Foaie("6. Actiuni",
            ["REGISTRU DE ACȚIUNI"],
            [],
            ["Nr.", "Acțiune", "Prioritate", "Status", "Observații"],
            ["1", "Semnarea AA1", "Critică", "Deschis", "urmărim"]),
    ];

    [Fact]
    public void Fisa_se_citeste_cu_asocierea_transele_actul_propus_facturile_si_platile()
    {
        var f = InterpretorFisaContract.Citeste(Fisa, "Beta Build SRL", "RO999");

        Assert.Equal(("12", new DateOnly(2026, 3, 15), "EXEMPLU INSTAL SRL", "RO123456", "EUR", 1_000_000m),
            (f.NumarExtern.Valoare, f.DataSemnarii.Valoare, f.PartenerDenumire.Valoare, f.PartenerCui.Valoare, f.Moneda.Valoare, f.Valoare.Valoare));
        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30), 30), (f.DataInceput!.Value, f.DataSfarsit!.Value, f.TermenPlataZile!.Value));
        // Asocierea: doi membri 50/50, firma activă recunoscută după denumire (fără diacritice / majuscule).
        Assert.Equal([("ALFA CONSTRUCT SRL", 0.5m, false), ("BETA BUILD SRL", 0.5m, true)], f.Conditii.Membri.Select(m => (m.Denumire, m.Cota, m.EsteFirmaActiva)));
        Assert.Equal([0.10m, 0.10m], f.Conditii.TranseAvans.Select(t => t.Procent));
        var aa = Assert.Single(f.Acte);
        Assert.Equal(("1", 50_000m, false), (aa.NumarExtern!, aa.Diferenta, aa.Semnat));
        // Facturile (rândul TOTAL nu e factură), pe membrul de pe factură; tranșa din „Avans tranșa 1”.
        Assert.Equal(["F001", "F002"], f.Facturi.Select(x => x.Numar));
        Assert.All(f.Facturi, x => Assert.Equal((TipFacturaContract.Avans, 1, 50_000m), (x.Tip, x.TransaAvans!.Value, x.Valoare)));
        Assert.Equal("BETA BUILD SRL", f.Facturi[0].Membru);
        // „Achitat 25.04.2026” = plată confirmată; „BO emis” = bilet la ordin neconfirmat.
        Assert.Equal([("F001", true, InstrumentPlata.OrdinDePlata, new DateOnly(2026, 4, 25)), ("F002", false, InstrumentPlata.BiletLaOrdin, new DateOnly(2026, 4, 20))],
            f.Plati.Select(p => (p.NumarFactura, p.Plata.Confirmata, p.Plata.Instrument, p.Plata.Data)));
        Assert.Equal((0.05m, 60, 0.001m, 0.10m), (f.ProcentGarantie, f.Conditii.LuniGarantieTehnica!.Value, f.Conditii.PenalitateExecutieZi, f.Conditii.PlafonPenalitateExecutie));
        Assert.Equal(PrioritateActiune.Critica, Assert.Single(f.Actiuni).Prioritate);
        // Rândurile nerecunoscute din „Date generale” rămân în observații.
        Assert.Contains("Plăți efectuate: De reconciliat (Fără extrase)", f.Observatii);
    }

    [Fact]
    public void Fisa_fara_date_esentiale_le_semnaleaza()
    {
        var f = InterpretorFisaContract.Citeste([Foaie("1. Date Generale", ["Subantreprenor", "De completat"])], null, null);

        Assert.Equal(Certitudine.Lipsa, f.Valoare.Certitudine);
        Assert.Equal(Certitudine.Lipsa, f.PartenerDenumire.Certitudine);
        Assert.Contains(f.Note, n => n.Contains("Valoarea"));
    }
}
