using ErpConstructii.Contracte;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Contracte;

/// <summary>Potrivirea rândurilor din fișă (preluate din Excel) cu facturile din modulul Facturi (D-56): fără dubluri.</summary>
public class PotrivireFacturiFisaTeste
{
    private const string Gcc = "GENERAL CONCRETE CERNAVODĂ SRL", Melsam = "MELSAM CONSTRUCTION SRL";
    private static readonly Guid Contract = Guid.NewGuid();

    private static FacturaContract Rand(string membru, string numar, DateOnly data, decimal net) =>
        FacturaContract.Creeaza(Contract, 1, new DateFacturaContract(membru, null, numar, data, TipFacturaContract.Lucrari, null, null, null, 100m, 0, 5m, net, 0, net, null, null)).Valoare;

    [Fact]
    public void Factura_firmei_cu_acelasi_numar_si_suma_se_potriveste_sigur()
    {
        var r = Rand(Gcc, "HTC 20260146", new DateOnly(2026, 5, 15), 911_239.28m);
        var f = new FacturaPeContract(Guid.NewGuid(), "20260146", new DateOnly(2026, 5, 15), 911_239.70m, "RON"); // 0,42 lei diferență: rotunjire

        var p = Assert.Single(PotrivireFacturiFisa.Gaseste([r], [f], Gcc));

        Assert.Equal((r.Id, f.Id, true, (string?)null), (p.RandId, p.FacturaId, p.Sigura, p.Diferente));
    }

    [Fact]
    public void Suma_diferita_ramane_propunere_iar_data_diferita_e_semnalata()
    {
        var r = Rand(Gcc, "7126100029", new DateOnly(2026, 1, 26), 605_593.32m);
        var f = new FacturaPeContract(Guid.NewGuid(), "7126100029", new DateOnly(2026, 1, 27), 600_000m, "RON");

        var p = Assert.Single(PotrivireFacturiFisa.Gaseste([r], [f], Gcc));

        Assert.False(p.Sigura);
        Assert.Contains("data", p.Diferente);
        Assert.Contains("fără TVA", p.Diferente);
    }

    [Fact]
    public void Facturile_celuilalt_membru_si_randurile_deja_legate_nu_se_potrivesc()
    {
        var melsam = Rand(Melsam, "ARM0012011", new DateOnly(2026, 7, 24), 66_435.01m);
        var legat = Rand(Gcc, "ARM0012012", new DateOnly(2026, 7, 24), 66_435.02m);
        legat.LeagaDe(new FacturaPentruFisa(Guid.NewGuid(), "ARM0012012", new DateOnly(2026, 7, 24), null, "RON", 66_435.02m, 13_951.35m, 80_386.37m, Contract, []));
        var f = new FacturaPeContract(Guid.NewGuid(), "ARM0012011", new DateOnly(2026, 7, 24), 66_435.01m, "RON");

        Assert.Empty(PotrivireFacturiFisa.Gaseste([melsam, legat], [f], Gcc));
    }

    [Fact]
    public void Doua_randuri_cu_acelasi_numar_nu_se_ghicesc()
    {
        var a = Rand(Gcc, "F 100", new DateOnly(2026, 3, 1), 10m);
        var b = Rand(Gcc, "F-100", new DateOnly(2026, 3, 2), 10m);

        Assert.Empty(PotrivireFacturiFisa.Gaseste([a, b], [new FacturaPeContract(Guid.NewGuid(), "F100", new DateOnly(2026, 3, 1), 10m, "RON")], Gcc));
    }

    [Fact]
    public void Legarea_completeaza_sumele_lipsa_din_rand_si_pastreaza_cele_existente()
    {
        var gol = Rand(Gcc, "ARM0011843", new DateOnly(2026, 5, 4), 0);
        var f = new FacturaPentruFisa(Guid.NewGuid(), "ARM0011843", new DateOnly(2026, 5, 4), new DateOnly(2026, 6, 18), "RON", 1_537_391.44m, 322_852.20m, 1_860_243.64m, Contract, []);

        gol.LeagaDe(f);

        Assert.Equal((f.Id, 1_537_391.44m, 322_852.20m, 1_860_243.64m, new DateOnly(2026, 6, 18)), (gol.FacturaId!.Value, gol.NetRon, gol.TvaRon, gol.TotalRon, gol.Scadenta!.Value));
        gol.Dezleaga();
        Assert.Null(gol.FacturaId);
    }

    [Theory]
    [InlineData("HTC 20260145", "20260145", true)]
    [InlineData("ARM0012011", "arm 0012011", true)]
    [InlineData("F12", "F13", false)]
    [InlineData("A1", "B1", false)] // cifre puține: nu se compară doar cifrele
    public void Numarul_facturii_se_compara_fara_spatii_si_semne(string a, string b, bool acelasi) =>
        Assert.Equal(acelasi, PotrivireFacturiFisa.AcelasiNumar(a, b));
}
