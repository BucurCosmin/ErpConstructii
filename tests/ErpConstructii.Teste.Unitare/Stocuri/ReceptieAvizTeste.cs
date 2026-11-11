using ErpConstructii.Stocuri;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Stocuri;

/// <summary>Marfa venită cu aviz (D-40): NIR pe aviz fără valoare, apoi factura îi dă prețurile.</summary>
public class ReceptieAvizTeste
{
    private static readonly Guid Otel8 = Guid.NewGuid();
    private static readonly Guid Otel10 = Guid.NewGuid();
    private static readonly Guid Factura = Guid.NewGuid();

    // Avizul: oțel 8 mm 100 kg (poziția 0), oțel 10 mm 200 kg (poziția 1), primite complet, fără prețuri.
    private static Nir NirPeAviz() => Nir.EmiteDinAviz(2026, 7, new DateOnly(2026, 8, 15), Gestiune.DepozitCentral(Depozit.Central()), Guid.NewGuid(), "INTFSN/20488",
        new DateOnly(2026, 8, 14), null, "INTERTRANSCOM", Guid.NewGuid(), "P-07",
        [new LinieNirCerere(0, Otel8, "MAT-1", "Otel 8", "kg", 100, 100, 0m, null), new LinieNirCerere(1, Otel10, "MAT-2", "Otel 10", "kg", 200, 200, 0m, null)],
        null, Guid.NewGuid(), "Gestionar").Valoare;

    [Fact]
    public void NIR_ul_pe_aviz_intra_fara_valoare_si_asteapta_factura()
    {
        var nir = NirPeAviz();

        Assert.Equal((0m, true, (Guid?)null), (nir.Valoare, nir.AsteaptaFactura, nir.FacturaId));
        Assert.Equal([(0, -1), (1, -1)], nir.Linii.Select(l => (l.Cheie, l.IndexFactura)));
        Assert.Equal("aviz INTFSN/20488 (fără factură)", nir.Document);
    }

    [Fact]
    public void Factura_da_pretul_liniilor_si_corectura_cu_diferenta_de_valoare()
    {
        var nir = NirPeAviz();

        // Factura are ambele materiale pe o singură linie (poziția 2, 5,10 lei/kg) — ex. „oțel beton fasonat”.
        var r = nir.Factureaza(Factura, "ITC 5512", new DateOnly(2026, 8, 31),
            [new LinieFacturareAviz(0, 2, 5.10m), new LinieFacturareAviz(1, 2, 5.10m)], new DateOnly(2026, 9, 1), Guid.NewGuid(), "Ana");

        Assert.True(r.Succes);
        // 100 × 5,10 = 510; 200 × 5,10 = 1.020 → 1.530.
        Assert.Equal((1530m, false, (Guid?)Factura), (nir.Valoare, nir.AsteaptaFactura, nir.FacturaId));
        Assert.Equal([2, 2], nir.Linii.Select(l => l.IndexFactura));
        Assert.Equal([0, 1], nir.Linii.Select(l => l.Cheie)); // linia NIR-ului rămâne cea de pe aviz
        Assert.Equal([510m, 1020m], r.Valoare!.Linii.Select(l => l.DiferentaValoare));
        Assert.Contains("factura ITC 5512", r.Valoare.Motiv);
        Assert.Equal("aviz INTFSN/20488 + factura ITC 5512", nir.Document);
    }

    [Fact]
    public void Facturarea_cere_toate_liniile_primite_si_nu_se_repeta()
    {
        var nir = NirPeAviz();

        var partial = nir.Factureaza(Factura, "F1", new DateOnly(2026, 8, 31), [new LinieFacturareAviz(0, 0, 5m)], new DateOnly(2026, 9, 1), Guid.NewGuid(), "Ana");
        Assert.True(partial.Esec);
        Assert.Contains("„Otel 10”", partial.Eroare.Mesaj);

        Assert.True(nir.Factureaza(Factura, "F1", new DateOnly(2026, 8, 31), [new LinieFacturareAviz(0, 0, 5m), new LinieFacturareAviz(1, 1, 5m)],
            new DateOnly(2026, 9, 1), Guid.NewGuid(), "Ana").Succes);
        Assert.True(nir.Factureaza(Factura, "F1", new DateOnly(2026, 8, 31), [new LinieFacturareAviz(0, 0, 5m), new LinieFacturareAviz(1, 1, 5m)],
            new DateOnly(2026, 9, 1), Guid.NewGuid(), "Ana").Esec);
    }

    [Fact]
    public void Starea_avizului_si_a_facturii_se_calculeaza_din_NIR_urile_pe_aviz()
    {
        // Pe aviz s-au primit doar 60 din 100 kg de oțel 8 (restul vine), oțel 10 complet.
        var nir = Nir.EmiteDinAviz(2026, 8, new DateOnly(2026, 8, 15), Gestiune.DepozitCentral(Depozit.Central()), Guid.NewGuid(), "A1", new DateOnly(2026, 8, 14), null, "F",
            Guid.NewGuid(), "P", [new LinieNirCerere(0, Otel8, "MAT-1", "Otel 8", "kg", 100, 60, 0m, null, Partiala: true),
                new LinieNirCerere(1, Otel10, "MAT-2", "Otel 10", "kg", 200, 200, 0m, null)], null, Guid.NewGuid(), "G").Valoare;

        var aviz = StareReceptie.CalculeazaAviz([(0, 100m), (1, 200m)], nir.Linii);
        Assert.Equal([40m, 0m], aviz.Select(s => s.Ramas));

        // Factura: linia 0 = 100 kg oțel 8, linia 1 = 200 kg oțel 10. NIR-ul pe aviz acoperă 60 + 200.
        nir.Factureaza(Factura, "F1", new DateOnly(2026, 8, 31), [new LinieFacturareAviz(0, 0, 5m), new LinieFacturareAviz(1, 1, 5m)],
            new DateOnly(2026, 9, 1), Guid.NewGuid(), "Ana");
        var factura = StareReceptie.CalculeazaFactura([(0, 100m), (1, 200m)], [nir]);
        // Restul de 40 kg din factură rămâne de recepționat (vine cu alt aviz sau direct pe factură).
        Assert.Equal([40m, 0m], factura.Select(s => s.Ramas));
    }
}
