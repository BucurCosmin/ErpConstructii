using ErpConstructii.Contracte;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Contracte;

/// <summary>Calculul fișei (D-56, etapa 2), cu facturile GCC din fișa Züblin (contract 247/23.10.2025, 2.864.813,83 EUR).</summary>
public class CalculFisaContractTeste
{
    private static readonly DateOnly Azi = new(2026, 11, 16);
    private const string Gcc = "GENERAL CONCRETE CERNAVODĂ SRL";

    private static ContractPartener Contract()
    {
        var d = new DateContract("247/23.10.2025", new DateOnly(2025, 10, 23), Guid.NewGuid(), "CNE-U1", Guid.NewGuid(), "ZÜBLIN ROMANIA SRL", null,
            TipContract.Subantreprenoriat, "Piloți forați D900", "EUR", 2_864_813.83m, [], null, new DateOnly(2026, 3, 31), 45, 0.05m, 0, true, null);
        var c = ContractPartener.Creeaza(2025, 1, d, Guid.NewGuid(), "Ana").Valoare;
        c.SeteazaConditii(ConditiiContract.Goale with
        {
            Membri = [new("MELSAM CONSTRUCTION SRL", null, 0.5m, false), new(Gcc, null, 0.5m, true)],
            TranseAvans = [new(1, 0.10m, "Avans 10%")],
            PenalitateExecutieZi = 0.001m,
            PlafonPenalitateExecutie = 0.05m,
        });
        c.Activeaza("Șef proiect", DateTimeOffset.UtcNow);
        return c;
    }

    private static FacturaContract F(Guid contract, string numar, DateOnly data, TipFacturaContract tip, decimal lucrari, decimal storno,
        decimal net, decimal tva, decimal total, int? transa = null) =>
        FacturaContract.Creeaza(contract, 1, new DateFacturaContract(Gcc, null, numar, data, tip, transa, null, null, lucrari, storno, 5m, net, tva, total, null, null)).Valoare;

    [Fact]
    public void Situatia_pe_membru_aduna_avansul_lucrarile_platile_si_soldul()
    {
        var c = Contract();
        var avans = F(c.Id, "AV-GCC", new DateOnly(2025, 11, 21), TipFacturaContract.Avans, 144_487.79m, 0, 0, 0, 0, transa: 1);
        var nov = F(c.Id, "7125100522", new DateOnly(2025, 12, 10), TipFacturaContract.Lucrari, 52_697.65m, 0, 268_215.23m, 56_325.20m, 324_540.43m);
        var dec = F(c.Id, "7126100029", new DateOnly(2026, 1, 26), TipFacturaContract.Lucrari, 132_085.17m, 13_208.52m, 605_593.32m, 127_174.60m, 732_767.92m);
        var ian = F(c.Id, "7126100051", new DateOnly(2026, 2, 10), TipFacturaContract.Lucrari, 155_314.43m, 15_531.44m, 711_970.68m, 149_513.85m, 861_484.53m);
        var plataNov = PlataContract.Creeaza(c.Id, 1, new DatePlata(nov.Id, new DateOnly(2026, 1, 20), 324_540.43m, InstrumentPlata.OrdinDePlata, "OP 12", null, true, null)).Valoare;
        var bo = PlataContract.Creeaza(c.Id, 2, new DatePlata(ian.Id, new DateOnly(2026, 3, 1), 100_000m, InstrumentPlata.BiletLaOrdin, "BO 5497", new DateOnly(2026, 12, 1), false, null)).Valoare;

        var s = CalculFisaContract.Calculeaza(c, [], [avans, nov, dec, ian], [plataNov, bo], [], [], false, Azi);
        var gcc = s.Membri.Single(m => m.Membru == Gcc);

        // Valoarea GCC: 2.864.813,83 × 50% = 1.432.406,915 → 1.432.406,92; avansul tranșei: 10% × 50% = 143.240,69 (ca în fișă).
        Assert.Equal((1_432_406.92m, 143_240.69m), (gcc.ValoareActuala, gcc.Transe.Single().Contractual));
        // Lucrări: 52.697,65 + 132.085,17 + 155.314,43 = 340.097,25; rest = 1.432.406,92 − 340.097,25 = 1.092.309,67; 23,7%.
        Assert.Equal((340_097.25m, 1_092_309.67m, 23.7m), (gcc.LucrariFacturate, gcc.RestDeExecutat, gcc.ProcentExecutat));
        // Avans: facturat 144.487,79, recuperat 13.208,52 + 15.531,44 = 28.739,96, rămas 115.747,83.
        Assert.Equal((144_487.79m, 28_739.96m, 115_747.83m), (gcc.AvansFacturat, gcc.AvansRecuperat, gcc.AvansRamas));
        // RON: 324.540,43 + 732.767,92 + 861.484,53 = 1.918.792,88; plătit confirmat 324.540,43; BO emis 100.000 (nu scade soldul).
        Assert.Equal((1_918_792.88m, 324_540.43m, 100_000m, 1_594_252.45m), (gcc.FacturatTotalRon, gcc.PlatitConfirmat, gcc.EmisNeconfirmat, gcc.Sold));
        Assert.Equal(0, s.Membri.Single(m => m.Membru.StartsWith("MELSAM")).Facturi);
        Assert.Equal(732_767.92m, s.SoldFactura[dec.Id]);
    }

    [Fact]
    public void Facturile_scadente_neplatite_si_termenul_depasit_dau_alerte_cu_penalitatea_plafonata()
    {
        var c = Contract();
        var dec = F(c.Id, "7126100029", new DateOnly(2026, 1, 26), TipFacturaContract.Lucrari, 132_085.17m, 13_208.52m, 605_593.32m, 127_174.60m, 732_767.92m);

        var s = CalculFisaContract.Calculeaza(c, [], [dec], [], [], [], false, Azi);

        // Scadența: 26.01.2026 + 45 zile = 12.03.2026, trecută și neplătită.
        Assert.Contains(s.Alerte, a => a.Tip == TipAlerta.FacturaScadenta && a.Data == new DateOnly(2026, 3, 12));
        // Termen 31.03.2026 → 16.11.2026 = 230 de zile; 2.864.813,83 × 0,1% × 230 = 658.907,18, plafonat la 5% = 143.240,69.
        Assert.Equal((230, 143_240.69m), (s.ZileDepasire, s.PenalitatePosibila!.Value));
        Assert.Contains(s.Alerte, a => a.Tip == TipAlerta.TermenDepasit);
    }

    [Fact]
    public void Instrumentele_care_expira_si_actele_propuse_sunt_semnalate()
    {
        var c = Contract();
        var sgb = InstrumentGarantie.Creeaza(c.Id, 1, new DateInstrument(TipInstrument.GarantieAvans, null, "Garanti Bank", "6130305302338", Gcc, 1_860_243.64m, "RON",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 11, 26), null, null)).Valoare;
        var expirat = InstrumentGarantie.Creeaza(c.Id, 2, new DateInstrument(TipInstrument.BiletLaOrdin, null, null, "5497987", Gcc, 1_860_243.64m, "RON",
            new DateOnly(2026, 5, 4), new DateOnly(2026, 7, 4), null, null)).Valoare;
        var aa = ActAditional.Creeaza(c, null, 1, null, new DateOnly(2026, 10, 12), "Suplimentare", 10_000m, null, c.Valoare, "Ana", semnat: false).Valoare;

        var s = CalculFisaContract.Calculeaza(c, [aa], [], [], [sgb, expirat], [], false, Azi);

        Assert.Contains(s.Alerte, a => a.Tip == TipAlerta.InstrumentExpira && a.Mesaj.Contains("10 zile")); // 16.11 → 26.11
        Assert.Contains(s.Alerte, a => a.Tip == TipAlerta.InstrumentExpirat);
        Assert.Contains(s.Alerte, a => a.Tip == TipAlerta.ActPropus);
        // Actul propus nu intră în valoare: GCC 50% din valoarea semnată; propusă (2.874.813,83 × 50%) separat.
        Assert.Equal((1_432_406.92m, 1_437_406.92m), (s.Membri[1].ValoareActuala, s.Membri[1].ValoarePropusa));
    }

    [Fact]
    public void Factura_cu_total_care_nu_e_net_plus_tva_e_refuzata()
    {
        var r = FacturaContract.Creeaza(Guid.NewGuid(), 1, new DateFacturaContract(Gcc, null, "X1", new DateOnly(2026, 1, 1), TipFacturaContract.Lucrari, null,
            null, null, 100m, 0, 5m, 500m, 95m, 700m, null, null));

        Assert.True(r.Esec);
    }

    [Fact]
    public void Propunerea_din_factura_separa_lucrarile_de_stornarea_avansului_si_le_aduce_in_euro()
    {
        // ARM0012011: brut 88.053,60 lei, stornare −21.618,59 lei; curs 5,10 (exemplu).
        var f = new FacturaPentruFisa(Guid.NewGuid(), "ARM0012011", new DateOnly(2026, 7, 24), null, "RON", 66_435.01m, 13_951.36m, 80_386.37m, null,
            [new LinieFacturaPentruFisa("Structură metalică – livrare", 88_053.60m), new LinieFacturaPentruFisa("Storno avans tranșa 1", -21_618.59m)]);

        var p = PropunereFacturaFisa.Din(f, Gcc, "EUR", 5.10m, 45);

        // 88.053,60 / 5,10 = 17.265,41; 21.618,59 / 5,10 = 4.238,94; scadența = 24.07 + 45 zile = 07.09.2026.
        Assert.Equal((TipFacturaContract.Lucrari, 17_265.41m, 4_238.94m), (p.Tip, p.Valoare, p.StornoAvans));
        Assert.Equal((80_386.37m, new DateOnly(2026, 9, 7)), (p.TotalRon, p.Scadenta!.Value));
        Assert.Equal(TipFacturaContract.Avans, PropunereFacturaFisa.Din(f with { Linii = [new("Avans 25% conform contract", 1_537_391.44m)] }, Gcc, "EUR", 5.14m, null).Tip);
    }
}
