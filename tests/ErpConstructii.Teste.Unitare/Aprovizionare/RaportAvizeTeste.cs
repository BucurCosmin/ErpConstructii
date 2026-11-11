using ErpConstructii.Aprovizionare;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Rapoartele sintetice ale avizelor de însoțire: materiale primite pe lucrare și sinteza pe furnizori.</summary>
public class RaportAvizeTeste
{
    private static readonly Guid Proiect = Guid.NewGuid();
    private static readonly Guid Intertranscom = Guid.NewGuid();

    private static AvizInsotire Aviz(int numar, string numarExtern, int zi, Guid? furnizor, string furnizorDenumire, params LinieAviz[] linii) =>
        AvizInsotire.Importa(2026, numar,
            new DateAviz(numarExtern, new DateOnly(2026, 6, zi), Proiect, "RTH-A", "Retehnologizare U1", furnizor, furnizorDenumire, null, linii, null),
            Guid.NewGuid(), "NC/2026/0001", notaGenerata: false, Guid.NewGuid(), Guid.NewGuid(), "Ana").Valoare;

    [Fact]
    public void Materialele_se_aduna_pe_lucrare_cu_avizele_din_care_vin()
    {
        var a1 = Aviz(1, "INTFSN/18794", 22, Intertranscom, "INTERTRANSCOM IMPEX SRL",
            new LinieAviz("OTEL BETON B500C 10MM", "kg", 767.76m), new LinieAviz("OTEL BETON B500C 12MM", "kg", 806.71m));
        var a2 = Aviz(2, "INTFSN/20488", 25, Intertranscom, "INTERTRANSCOM IMPEX SRL", new LinieAviz("Otel beton  b500c 10mm", "KG", 286.72m));

        var m = RaportAvize.Materiale([a2, a1]);

        // 10 mm: 767,76 + 286,72 = 1.054,48 kg, din ambele avize; 12 mm rămâne separat.
        Assert.Equal(2, m.Count);
        var zece = m.Single(x => x.Denumire == "OTEL BETON B500C 10MM");
        Assert.Equal(1054.48m, zece.Cantitate);
        Assert.Equal(["INTFSN/18794 (AV/2026/0001)", "INTFSN/20488 (AV/2026/0002)"], zece.Avize);
        Assert.Equal((new DateOnly(2026, 6, 22), new DateOnly(2026, 6, 25)), (zece.PrimaData, zece.UltimaData));
    }

    [Fact]
    public void Sinteza_pe_furnizori_numara_avizele_dupa_starea_marfii()
    {
        var a1 = Aviz(1, "INTFSN/18794", 22, Intertranscom, "INTERTRANSCOM IMPEX SRL", new LinieAviz("Otel 10", "kg", 10), new LinieAviz("Otel 12", "kg", 5));
        var a2 = Aviz(2, "INTFSN/20488", 25, Intertranscom, "INTERTRANSCOM IMPEX SRL", new LinieAviz("Otel 10", "kg", 3));
        var a3 = Aviz(3, "INTFSN/20500", 26, Intertranscom, "INTERTRANSCOM IMPEX SRL", new LinieAviz("Otel 16", "kg", 4));
        var a4 = Aviz(4, "OTL-220", 3, null, "SC ONE TRANS LOAD SRL", new LinieAviz("Piatra sparta", "to", 101.7m));
        var stari = new Dictionary<Guid, StareStocAviz>
        {
            [a1.Id] = new(1, true, "F 77"),   // facturat
            [a2.Id] = new(2, false, null),    // recepționat, fără factură
            // a3, a4: fără recepții → de recepționat
        };

        var s = RaportAvize.PeFurnizori([a1, a2, a3, a4], stari);

        Assert.Equal(["INTERTRANSCOM IMPEX SRL", "SC ONE TRANS LOAD SRL"], s.Select(x => x.Furnizor));
        // Intertranscom: 3 avize, 2 + 1 + 1 = 4 poziții; câte unul de recepționat, recepționat fără factură, facturat.
        Assert.Equal((3, 4, 1, 1, 1), (s[0].Avize, s[0].Pozitii, s[0].DeReceptionat, s[0].ReceptionateFaraFactura, s[0].Facturate));
        Assert.Equal((1, 1, 1, 0, 0), (s[1].Avize, s[1].Pozitii, s[1].DeReceptionat, s[1].ReceptionateFaraFactura, s[1].Facturate));
    }
}
