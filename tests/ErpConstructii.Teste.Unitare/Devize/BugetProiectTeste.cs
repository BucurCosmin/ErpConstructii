using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Teste.Unitare.Devize;

public class BugetProiectTeste
{
    private static readonly Guid Sapatura = Guid.NewGuid(), Beton = Guid.NewGuid(), Scos = Guid.NewGuid();
    private static readonly DateOnly Zi = new(2026, 10, 1);

    private static readonly ArticolBugetDeviz[] Deviz =
    [
        new(Sapatura, "TSA04G1", "Săpătură manuală", "mc", 320.82m, "Obiect / Terasamente", new ValoriResurse(1917.89m, 194046.79m, 0, 0)),
        new(Beton, "CB05A", "Beton C16/20 în fundații", "mc", 40m, "Obiect / Fundații", new ValoriResurse(18000m, 2000m, 400m, 600m)),
    ];

    private static CostRealizat C(Guid? articol, NaturaCost natura, decimal valoare, string document, TipResursa? componenta = TipResursa.Material) =>
        new(articol, natura, natura == NaturaCost.Consum ? componenta : null, valoare, Zi, document, null, "");

    [Fact]
    public void Articolele_primesc_consumul_lor_iar_restul_costurilor_intra_in_rezumat()
    {
        var b = CalculBuget.Calculeaza(Deviz,
        [
            C(Beton, NaturaCost.Consum, 9000m, "BC/2026/0001"),
            C(Beton, NaturaCost.Consum, 1500.5m, "BC/2026/0002"),
            C(null, NaturaCost.Consum, 300m, "BC/2026/0003"),             // fără articol: cheltuieli generale
            C(Scos, NaturaCost.Consum, 200m, "BC/2026/0004"),             // articol care nu mai e în deviz
            C(null, NaturaCost.AchizitieMateriale, 12000m, "factura 1"),
            C(null, NaturaCost.Serviciu, 5000m, "factura 2"),
        ]);

        var beton = b.Articole.Single(r => r.Cheie == Beton);
        Assert.Equal((10500.5m, 2), (beton.Realizat.Material, beton.Documente)); // 9.000 + 1.500,50, din două bonuri
        Assert.Equal(0m, b.Articole.Single(r => r.Cheie == Sapatura).Realizat.Total); // neînceput
        Assert.Equal(4, b.Articole.Count); // 2 articole + cel scos din deviz + cheltuielile generale
        Assert.False(b.Articole.Single(r => r.Cheie == Scos).InDeviz);

        var r = b.Rezumat;
        // Buget direct: 1.917,89 + 194.046,79 + 18.000 + 2.000 + 400 + 600 = 216.964,68.
        Assert.Equal(216964.68m, r.BugetDirect);
        Assert.Equal((10700.5m, 300m, 5000m), (r.ConsumPeArticole, r.CheltuieliGenerale, r.ServiciiNerepartizate)); // 10.500,50 + 200 pe articole
        // Consumul total de materiale: 10.500,50 + 300 + 200 = 11.000,50; cumpărate 12.000 → neconsumate 999,50.
        Assert.Equal((12000m, 999.5m), (r.MaterialeCumparate, r.MaterialeNeconsumate));
        // Costuri înregistrate: consum 11.000,50 + servicii 5.000 = 16.000,50; facturat: 12.000 + 5.000.
        Assert.Equal((16000.5m, 17000m), (r.CosturiInregistrate, r.Facturat));
        Assert.Equal(7.4m, r.ProcentCosturi); // 16.000,50 / 216.964,68 = 7,37%
    }

    [Fact]
    public void Consumul_peste_cumparaturi_nu_da_materiale_neconsumate_negative()
    {
        // Materialele consumate pot veni din stocul de pornire, fără factură pe proiect.
        var r = CalculBuget.Calculeaza(Deviz, [C(Beton, NaturaCost.Consum, 500m, "BC/1"), C(null, NaturaCost.AchizitieMateriale, 100m, "f")]).Rezumat;

        Assert.Equal(0m, r.MaterialeNeconsumate);
    }

    [Fact]
    public void Fara_deviz_costurile_apar_doar_in_rezumat()
    {
        var b = CalculBuget.Calculeaza([], [C(null, NaturaCost.Serviciu, 100m, "f")]);

        Assert.Equal((0m, 100m, (decimal?)null), (b.Rezumat.BugetDirect, b.Rezumat.CosturiInregistrate, b.Rezumat.ProcentCosturi));
    }

    [Fact]
    public void Preturile_de_achizitie_se_aduc_in_UM_resursei_cu_factorul()
    {
        var mat = Guid.NewGuid();
        // Resursa în „to”, materialul în „kg” (factor 1000): 4,20 lei/kg = 4.200 lei/to.
        var s = CatalogPreturi.Achizitii("3800123", "to", 1000m,
        [
            new PretAchizitie(mat, 4.2m, 2000m, new DateOnly(2026, 9, 10), SursaPret.Receptie, "NIR/2026/0010", null, "Metal SRL", null),
            new PretAchizitie(mat, 4.5m, 1000m, new DateOnly(2026, 10, 20), SursaPret.Receptie, "NIR/2026/0020", null, "Fier SA", null),
            new PretAchizitie(mat, 3.9m, 5000m, new DateOnly(2025, 12, 1), SursaPret.Receptie, "NIR/2025/0100", null, "Vechi SRL", null), // în afara a 6 luni
            new PretAchizitie(mat, 0m, 10m, new DateOnly(2026, 10, 25), SursaPret.Receptie, "NIR pe aviz", null, null, null),          // fără preț
        ], new DateOnly(2026, 11, 13), 6)!;

        Assert.Equal((3, 3900m, 4500m), (s.Achizitii, s.Minim, s.Maxim));
        Assert.Equal((4500m, new DateOnly(2026, 10, 20), "Fier SA"), (s.Ultimul, s.DataUltimului, s.FurnizorUltim));
        // Media pe 6 luni, ponderată cu cantitățile în to: (4.200 × 2 + 4.500 × 1) / 3 = 4.300.
        Assert.Equal((4300m, 2), (s.MedieInterval, s.AchizitiiInInterval));
    }
}
