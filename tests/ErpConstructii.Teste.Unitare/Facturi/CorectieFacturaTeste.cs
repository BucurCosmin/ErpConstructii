using ErpConstructii.Facturi;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Facturi;

/// <summary>Corectura facturilor primite înregistrate cu date greșite (istoric append-only, liniile din stoc blocate).</summary>
public class CorectieFacturaTeste
{
    private static readonly Guid Proiect = Guid.NewGuid();
    private static readonly Guid Utilizator = Guid.NewGuid();
    private static readonly DateTimeOffset Acum = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(2));
    private static readonly HashSet<int> Nimic = [];

    // Factura citită greșit: linia de țevi cu cotă 100% (TVA 2144,70 în loc de 450,39).
    private static DateFacturaPrimita Date(decimal cota = 1m, decimal tvaTotal = 450.37m, Guid? proiect = null, decimal cantitate = 30) => new(
        "KZ 467411", new DateOnly(2025, 11, 4), null, "RON", null, "SC MAX SRL", null, proiect ?? Proiect, "RTH-A",
        [new LinieFactura("TEVI ROT NEGRE", "buc", cantitate, 71.49m, 2144.70m, cota, Math.Round(2144.70m * cota, 2)), new LinieFactura("Transport", "buc", 1, 0m, 0m, 0.21m, 0m, false)],
        2144.63m, tvaTotal, 2595m, null, null);

    private static FacturaPrimita Factura() => FacturaPrimita.Inregistreaza(Date(), SursaFactura.Manual, null, null, Utilizator, "Elena").Valoare;

    [Fact]
    public void Corectura_schimba_factura_si_pastreaza_ce_era_inainte()
    {
        var f = Factura();

        // Cota corectă 21%: TVA linie = 2144,70 × 0,21 = 450,39.
        var r = f.Corecteaza(Date(cota: 0.21m), "cota TVA citită greșit", Nimic, Utilizator, "Ana", Acum);

        Assert.True(r.Succes);
        Assert.Equal((0.21m, 450.39m), (f.Linii[0].CotaTva, f.Linii[0].ValoareTva));
        Assert.Equal(f.Id, ((ErpConstructii.SharedKernel.ICorectieDocument)r.Valoare).DocumentCorectatId);
        Assert.Contains("cotă TVA 100% → 21%", r.Valoare.Rezumat);
        Assert.Contains("\"CotaTva\":1", r.Valoare.DateVechiJson);
        Assert.Equal(("cota TVA citită greșit", "Ana"), (r.Valoare.Motiv, r.Valoare.CorectatDeNume));
    }

    [Fact]
    public void Fara_motiv_sau_fara_nicio_schimbare_nu_se_corecteaza()
    {
        var f = Factura();

        Assert.False(f.Corecteaza(Date(cota: 0.21m), " ", Nimic, Utilizator, "Ana", Acum).Succes);
        Assert.Equal("Nu ai modificat nimic.", f.Corecteaza(Date(), "verificare", Nimic, Utilizator, "Ana", Acum).Eroare.Mesaj);
        // Aceleași valori scrise altfel (2595 față de 2595,00, cum vin din bază) nu sunt o schimbare.
        Assert.Equal("Nu ai modificat nimic.", f.Corecteaza(Date() with { TotalDePlata = 2595.00m }, "verificare", Nimic, Utilizator, "Ana", Acum).Eroare.Mesaj);
        Assert.Equal(1m, f.Linii[0].CotaTva); // nimic aplicat
    }

    [Fact]
    public void Linia_intrata_in_stoc_nu_isi_schimba_cantitatea_dar_TVA_ul_da()
    {
        var f = Factura();
        var receptionate = new HashSet<int> { 0 };

        var cantitate = f.Corecteaza(Date(cota: 0.21m, cantitate: 31), "cantitate", receptionate, Utilizator, "Ana", Acum);
        Assert.Equal("conflict", cantitate.Eroare.Cod);
        Assert.Equal(30m, f.Linii[0].Cantitate);

        // Cota TVA nu schimbă stocul (evaluat fără TVA): se poate corecta și după NIR.
        Assert.True(f.Corecteaza(Date(cota: 0.21m), "cota TVA", receptionate, Utilizator, "Ana", Acum).Succes);
    }

    [Fact]
    public void Proiectul_nu_se_schimba_dupa_ce_factura_a_intrat_in_stoc()
    {
        var f = Factura();

        var r = f.Corecteaza(Date(proiect: Guid.NewGuid()), "alt proiect", new HashSet<int> { 0 }, Utilizator, "Ana", Acum);

        Assert.Equal("conflict", r.Eroare.Cod);
        Assert.True(f.Corecteaza(Date(proiect: Guid.NewGuid()), "alt proiect", Nimic, Utilizator, "Ana", Acum).Succes);
    }
}
