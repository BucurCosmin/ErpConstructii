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
    public void Linia_intrata_in_stoc_se_corecteaza_si_spune_ca_NIR_ul_ramane_cu_valorile_lui()
    {
        var f = Factura();
        var receptionate = new HashSet<int> { 0 };
        var vechi = f.Date();

        var r = f.Corecteaza(Date(cota: 0.21m, cantitate: 31), "cantitate citită greșit", receptionate, Utilizator, "Ana", Acum);

        Assert.True(r.Succes);
        Assert.Equal(31m, f.Linii[0].Cantitate);
        var avertisment = Assert.Single(FacturaPrimita.AvertismenteStoc(vechi, f.Date(), receptionate));
        Assert.Contains("Linia 1", avertisment);
    }

    [Fact]
    public void Doar_cota_TVA_schimbata_pe_linia_din_stoc_nu_da_avertisment()
    {
        var f = Factura();
        var receptionate = new HashSet<int> { 0 };
        var vechi = f.Date();

        Assert.True(f.Corecteaza(Date(cota: 0.21m), "cota TVA", receptionate, Utilizator, "Ana", Acum).Succes);
        Assert.Empty(FacturaPrimita.AvertismenteStoc(vechi, f.Date(), receptionate)); // stocul e evaluat fără TVA
    }

    [Fact]
    public void Proiectul_se_schimba_si_dupa_NIR_cu_avertisment()
    {
        var f = Factura();
        var receptionate = new HashSet<int> { 0 };
        var vechi = f.Date();

        Assert.True(f.Corecteaza(Date(proiect: Guid.NewGuid()), "alt proiect", receptionate, Utilizator, "Ana", Acum).Succes);
        Assert.Contains(FacturaPrimita.AvertismenteStoc(vechi, f.Date(), receptionate), a => a.Contains("Proiectul"));
    }

    [Fact]
    public void Cu_linii_in_stoc_nu_se_sterg_linii_dar_se_pot_adauga()
    {
        var f = Factura();
        var receptionate = new HashSet<int> { 0 }; // țevile recepționate (NIR-ul le găsește după poziție)
        // ștergerea transportului ar muta pozițiile: refuzată cât factura are recepții
        var stearsa = Date() with { Linii = [Date().Linii[0]] };
        var scoasaDinStoc = Date() with { Linii = [Date().Linii[0] with { IntraInStoc = false }, Date().Linii[1]] };
        var adaugata = Date() with { Linii = [.. Date().Linii, new LinieFactura("Paleți", "buc", 2, 10m, 20m, 0.21m, 4.2m, false)] };

        Assert.Equal("conflict", f.Corecteaza(stearsa, "șterg linia", receptionate, Utilizator, "Ana", Acum).Eroare.Cod);
        Assert.Equal("conflict", f.Corecteaza(scoasaDinStoc, "nu mai e stoc", receptionate, Utilizator, "Ana", Acum).Eroare.Cod);
        Assert.Equal(2, f.Linii.Count);
        Assert.True(f.Corecteaza(adaugata, "linie omisă la import", receptionate, Utilizator, "Ana", Acum).Succes);
    }
}
