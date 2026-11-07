using ErpConstructii.Facturi;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Facturi;

/// <summary>Totalurile facturilor primite pe furnizor, proiect, lună și cotă TVA.</summary>
public class RaportFacturiTeste
{
    private static readonly Guid ProiectA = Guid.NewGuid();
    private static readonly Guid ProiectB = Guid.NewGuid();

    private static LinieFactura L(decimal baza, decimal cota) => new("Material", "buc", 1, baza, baza, cota, Math.Round(baza * cota, 2));

    private static FacturaPrimita F(string numar, string furnizor, string? cui, Guid proiect, DateOnly data, string moneda, params LinieFactura[] linii)
    {
        var baza = linii.Sum(l => l.ValoareFaraTva);
        var tva = linii.Sum(l => l.ValoareTva);
        var d = new DateFacturaPrimita(numar, data, null, moneda, null, furnizor, cui, proiect, proiect == ProiectA ? "P-A" : "P-B", linii,
            baza, tva, baza + tva, null, null);
        return FacturaPrimita.Inregistreaza(d, SursaFactura.Manual, null, null, Guid.NewGuid(), "Test").Valoare;
    }

    private static readonly FacturaPrimita[] Facturi =
    [
        // Furnizorul 1 (CUI 18547290) apare scris în două feluri: aceeași firmă.
        F("F1", "Materiale Exemplu SRL", "RO18547290", ProiectA, new(2026, 10, 3), "RON", L(1000, 0.21m)),              // 1000 + 210
        F("F2", "MATERIALE EXEMPLU S.R.L.", "18547290", ProiectB, new(2026, 11, 5), "RON", L(500, 0.21m), L(200, 0.09m)), // 700 + 105 + 18
        F("F3", "Transport Fără CUI", null, ProiectA, new(2026, 11, 20), "RON", L(300, 0.21m)),                           // 300 + 63
        F("F4", "Furnizor Extern", null, ProiectA, new(2026, 11, 21), "EUR", L(100, 0m)),                                 // 100 EUR
    ];

    [Fact]
    public void Pe_furnizor_aceeasi_firma_scrisa_diferit_se_aduna_dupa_CUI()
    {
        var t = RaportFacturi.Totaluri(Facturi, GrupareFacturi.Furnizor).Where(x => x.Moneda == "RON").ToList();

        // Furnizorul 1: F1 + F2 = 1000 + 700 = 1700 bază; 210 + 105 + 18 = 333 TVA; 2033 total. Primul (cel mai mare).
        Assert.Equal(("18547290", 2, 1700m, 333m, 2033m), (t[0].Cheie, t[0].Facturi, t[0].BazaImpozabila, t[0].Tva, t[0].Total));
        Assert.Equal((1, 363m), (t[1].Facturi, t[1].Total)); // F3: 300 + 63
    }

    [Fact]
    public void Monedele_diferite_nu_se_aduna()
    {
        var t = RaportFacturi.Totaluri(Facturi, GrupareFacturi.Proiect);

        // Proiectul A în RON: F1 (1210) + F3 (363) = 1573; în EUR separat: F4 = 100.
        Assert.Equal(1573m, t.Single(x => x.Eticheta == "P-A" && x.Moneda == "RON").Total);
        Assert.Equal(100m, t.Single(x => x.Eticheta == "P-A" && x.Moneda == "EUR").Total);
    }

    [Fact]
    public void Pe_luna_in_ordine_cronologica()
    {
        var t = RaportFacturi.Totaluri(Facturi, GrupareFacturi.Luna).Where(x => x.Moneda == "RON").ToList();

        // Octombrie: F1 = 1210; noiembrie: F2 (823) + F3 (363) = 1186.
        Assert.Equal([("2026-10", 1210m), ("2026-11", 1186m)], t.Select(x => (x.Cheie, x.Total)));
    }

    [Fact]
    public void Pe_cota_TVA_din_liniile_facturii()
    {
        var t = RaportFacturi.Totaluri(Facturi, GrupareFacturi.CotaTva).Where(x => x.Moneda == "RON").ToList();

        // 21%: 1000 + 500 + 300 = 1800 bază, 210 + 105 + 63 = 378 TVA, în 3 facturi. 9%: 200 bază, 18 TVA, 1 factură (F2).
        Assert.Equal(("TVA 21%", 3, 1800m, 378m), (t[0].Eticheta, t[0].Facturi, t[0].BazaImpozabila, t[0].Tva));
        Assert.Equal(("TVA 9%", 1, 200m, 18m), (t[1].Eticheta, t[1].Facturi, t[1].BazaImpozabila, t[1].Tva));
    }

    [Fact]
    public void Factura_cu_linii_gresite_intra_pe_cota_din_totalurile_ei_si_e_semnalata()
    {
        // Linia citită de 10 ori prea mare (4440 în loc de 444), dar totalurile facturii: 444 bază + 93,24 TVA (21%).
        var linie = new LinieFactura("Motorină", "l", 70, 6.3428m, 4440m, 0.21m, 932.4m);
        var d = new DateFacturaPrimita("E1", new(2026, 11, 2), null, "RON", null, "Euro Fuel", null, ProiectA, "P-A", [linie], 444m, 93.24m, 537.24m, null, null);
        var f = FacturaPrimita.Inregistreaza(d, SursaFactura.Manual, null, null, Guid.NewGuid(), "Test").Valoare;

        Assert.False(RaportFacturi.LiniiConcordante(f));
        Assert.Equal(0.21m, RaportFacturi.CotaEfectiva(f)); // 93,24 / 444 = 0,21
        var t = Assert.Single(RaportFacturi.Totaluri([f], GrupareFacturi.CotaTva));
        Assert.Equal(("TVA 21%", 444m, 93.24m), (t.Eticheta, t.BazaImpozabila, t.Tva));
    }
}
