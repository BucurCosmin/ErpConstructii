using ErpConstructii.Devize;

namespace ErpConstructii.Teste.Unitare.Devize;

public class CatalogPreturiTeste
{
    private static readonly Guid V1 = Guid.NewGuid(), V2 = Guid.NewGuid(), V3 = Guid.NewGuid();
    private static readonly Guid P1 = Guid.NewGuid(), P2 = Guid.NewGuid();
    private static readonly DateTimeOffset Ian = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero), Mar = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

    private static ObservatiePret O(string cod, string um, decimal pret, decimal cantitate, Guid v, Guid p, DateTimeOffset data, string denumire = "Beton C16/20") =>
        new(cod, denumire, um, "Material", pret, cantitate, v, p, data);

    [Fact]
    public void Aceeasi_resursa_din_devize_diferite_are_minim_maxim_medie_ponderata_si_ultimul_pret()
    {
        var s = CatalogPreturi.Agrega(
        [
            O("2100133", "mc", 400m, 10m, V1, P1, Ian),
            O(" 2100133 ", "MC", 460m, 30m, V2, P2, Mar, "Beton  C16/20 (revizuit)"),
        ]).Single();

        // Media ponderată: (400 × 10 + 460 × 30) / 40 = (4.000 + 13.800) / 40 = 445.
        Assert.Equal((400m, 460m, 445m), (s.Minim, s.Maxim, s.Medie));
        // Ultimul preț și denumirea vin din devizul cel mai nou (martie); spațiile duble dispar.
        Assert.Equal((460m, Mar, "Beton C16/20 (revizuit)"), (s.Ultimul, s.DataUltimului, s.Denumire));
        Assert.Equal((2, 2, 2, 40m), (s.Observatii, s.Devize, s.Proiecte, s.Cantitate));
        Assert.Equal(0.15m, s.Abatere); // (460 − 400) / 400
    }

    [Fact]
    public void Unitatea_de_masura_diferita_inseamna_alta_resursa()
    {
        var s = CatalogPreturi.Agrega([O("OL37", "to", 4200m, 1m, V1, P1, Ian), O("OL37", "kg", 4.2m, 1000m, V2, P1, Mar)]);

        Assert.Equal(["kg", "to"], s.Select(x => x.UnitateMasura));
    }

    [Fact]
    public void Fara_cantitati_media_e_simpla_iar_abaterea_lipseste_la_pret_zero()
    {
        var s = CatalogPreturi.Agrega([O("X", "buc", 0m, 0m, V1, P1, Ian), O("X", "buc", 3m, 0m, V2, P1, Mar)]).Single();

        Assert.Equal(1.5m, s.Medie); // (0 + 3) / 2
        Assert.Null(s.Abatere);
    }

    [Fact]
    public void Comparatia_pune_preturile_versiunilor_una_langa_alta()
    {
        var r = CatalogPreturi.Compara([V1, V2, V3],
        [
            O("2100133", "mc", 400m, 10m, V1, P1, Ian),
            O("2100133", "mc", 420m, 10m, V1, P1, Ian), // același cod în alt capitol al lui V1
            O("2100133", "mc", 500m, 5m, V3, P2, Mar),
            O("6000222", "ora", 111m, 50m, V2, P2, Mar, "Muncitor deservire"),
            O("ALTUL", "mc", 1m, 1m, Guid.NewGuid(), P2, Mar), // altă versiune, necerută
        ]);

        Assert.Equal(["2100133", "6000222"], r.Select(x => x.Cod));
        var beton = r[0];
        // V1: (400 × 10 + 420 × 10) / 20 = 410; lipsește din V2; V3: 500. Abaterea: (500 − 410) / 410 = 0,2195…
        Assert.Equal([410m, null, 500m], beton.Preturi);
        Assert.Equal(0.2195m, beton.Abatere);
        Assert.False(beton.InToate);
        Assert.Null(r[1].Abatere); // apare într-un singur deviz
    }

    [Theory]
    [InlineData("TSA04G1", "TS")]
    [InlineData("acc01a", "AC")]
    [InlineData("1234", "")]
    public void Capitolul_de_indicator_e_prefixul_de_litere(string simbol, string asteptat) =>
        Assert.Equal(asteptat, CatalogPreturi.CapitolIndicator(simbol));
}
