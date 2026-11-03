using ErpConstructii.Facturi;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Teste.Unitare.Facturi;

public class TermenEFacturaTeste
{
    // Sfântul Andrei și Ziua Națională: 30 noiembrie și 1 decembrie 2026.
    private static readonly CalendarLucru Calendar = new([new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 1)]);

    [Fact]
    public void Cinci_zile_lucratoare_fara_ziua_emiterii()
    {
        // Marți 3.11 → mie 4, joi 5, vin 6, lun 9, mar 10
        Assert.Equal(new DateOnly(2026, 11, 10), TermenEFactura.UltimaZi(new DateOnly(2026, 11, 3), Calendar));
    }

    [Fact]
    public void Sarbatorile_legale_nu_se_numara()
    {
        // Vineri 27.11 → (30.11 și 1.12 libere) mie 2, joi 3, vin 4, lun 7, mar 8
        Assert.Equal(new DateOnly(2026, 12, 8), TermenEFactura.UltimaZi(new DateOnly(2026, 11, 27), Calendar));
    }

    [Fact]
    public void Facturile_din_2025_au_5_zile_calendaristice()
    {
        Assert.Equal(new DateOnly(2026, 1, 5), TermenEFactura.UltimaZi(new DateOnly(2025, 12, 31), Calendar));
    }

    [Fact]
    public void Alerta_dupa_trei_zile_lucratoare()
    {
        var emisa = new DateOnly(2026, 11, 3);
        Assert.False(TermenEFactura.TrebuieAlerta(emisa, new DateOnly(2026, 11, 5), Calendar));
        Assert.True(TermenEFactura.TrebuieAlerta(emisa, new DateOnly(2026, 11, 6), Calendar));
    }
}

public class PotrivireTreiPuncteTeste
{
    private static readonly LinieComanda[] Comanda = [new("A", 100m, 10m), new("B", 50m, 20m)];
    private static readonly LinieReceptie[] Receptii = [new("A", 100m), new("B", 40m)];
    private static readonly TolerantePotrivire Toleranta = new(ProcentPret: 0.02m, ProcentCantitate: 0m);

    [Fact]
    public void Gaseste_cantitate_peste_receptie_si_articol_necomandat()
    {
        LinieFactura[] factura = [new("A", 100m, 10.10m), new("B", 45m, 20m), new("C", 1m, 5m)];

        var diferente = PotrivireTreiPuncte.Compara(Comanda, Receptii, factura, Toleranta);

        Assert.Equal(2, diferente.Count);                       // A: +1% preț e în toleranță
        Assert.Contains(diferente, d => d is { CodArticol: "B", Tip: TipDiferenta.CantitatePesteReceptie });
        Assert.Contains(diferente, d => d is { CodArticol: "C", Tip: TipDiferenta.ArticolNecomandat });
    }

    [Fact]
    public void Pret_peste_toleranta()
    {
        var diferente = PotrivireTreiPuncte.Compara(Comanda, Receptii, [new LinieFactura("A", 100m, 10.50m)], Toleranta);
        var d = Assert.Single(diferente);
        Assert.Equal(TipDiferenta.PretPesteComanda, d.Tip);
    }

    [Fact]
    public void Factura_conforma_nu_are_diferente()
    {
        Assert.Empty(PotrivireTreiPuncte.Compara(Comanda, Receptii, [new LinieFactura("A", 100m, 10m), new LinieFactura("B", 40m, 20m)], Toleranta));
    }
}
