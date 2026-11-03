using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Teste.Unitare.Devize;

public class ImportDevizTeste
{
    private static readonly ParametriRecapitulatie Parametri = new(0.0225m, 0.10m, 0.05m, 0.21m);

    private static DevizCanonic Deviz(TotaluriDeclarate? totaluri, params ArticolCanonic[] articole) =>
        new("Test", [new ObiectCanonic("OB1", "Bloc A", [new CategorieCanonica("C1", "Fundații", articole)])], Parametri, totaluri);

    private static ArticolCanonic Articol(string simbol, decimal cantitate, ValoriResurse pret, string um = "mc", params ConsumCanonic[] consumuri) =>
        new(simbol, "Articol " + simbol, um, cantitate, pret, consumuri);

    private static readonly ArticolCanonic[] ArticoleExemplu =
    [
        Articol("CA01A", 10m, new ValoriResurse(100m, 50m, 20m, 5m)),
        Articol("CA02B", 2.5m, new ValoriResurse(40.123m, 10.555m, 0m, 1.111m)),
    ];

    [Fact]
    public void Totaluri_declarate_egale_cu_recalculul_nu_dau_erori()
    {
        var deviz = Deviz(new TotaluriDeclarate(null, 2184.47m, 2643.21m), ArticoleExemplu);
        var probleme = ValidatorDevizCanonic.Valideaza(deviz);
        Assert.DoesNotContain(probleme, p => p.Severitate == Severitate.Eroare);
    }

    [Fact]
    public void Totaluri_declarate_diferite_dau_eroare_cu_diferenta()
    {
        var deviz = Deviz(new TotaluriDeclarate(null, null, 2650.00m), ArticoleExemplu);
        var probleme = ValidatorDevizCanonic.Valideaza(deviz);
        var eroare = Assert.Single(probleme, p => p.Severitate == Severitate.Eroare);
        Assert.Contains("Total cu TVA", eroare.Mesaj);
    }

    [Fact]
    public void Articol_fara_UM_si_cu_cantitate_zero_da_doua_erori()
    {
        var deviz = Deviz(null, Articol("X1", 0m, new ValoriResurse(1, 0, 0, 0), um: ""));
        var erori = ValidatorDevizCanonic.Valideaza(deviz).Where(p => p.Severitate == Severitate.Eroare).ToList();
        Assert.Equal(2, erori.Count);
    }

    [Fact]
    public void Pret_unitar_diferit_de_suma_consumurilor_da_avertisment()
    {
        var articol = Articol("X1", 1m, new ValoriResurse(100m, 0, 0, 0), "mc",
            new ConsumCanonic("6000123", "Beton C16/20", TipResursa.Material, "mc", 1.02m, 90m)); // 91,80 ≠ 100
        var probleme = ValidatorDevizCanonic.Valideaza(Deviz(null, articol));
        Assert.Contains(probleme, p => p.Severitate == Severitate.Avertisment && p.Mesaj.Contains("Material"));
    }

    [Fact]
    public void Registrul_alege_importatorul_cu_increderea_cea_mai_mare_peste_prag()
    {
        var registru = new RegistruImportatori([new ImportatorFals("a", 0.6), new ImportatorFals("b", 0.9), new ImportatorFals("c", 0.2)]);
        var (ales, motive) = registru.Alege(new SursaImport("deviz.xml", [1, 2, 3]));
        Assert.Equal("b", ales?.FormatId);
        Assert.Equal(3, motive.Count);
    }

    [Fact]
    public void Registrul_nu_alege_nimic_sub_prag_si_respecta_formatele_active()
    {
        var registru = new RegistruImportatori([new ImportatorFals("a", 0.4), new ImportatorFals("b", 0.9)]);
        var sursa = new SursaImport("x", []);

        // Doar „a” e activ pentru firmă și e sub prag → niciun importator.
        Assert.Null(registru.Alege(sursa, new HashSet<string> { "a" }).Importator);
        Assert.Equal("b", registru.Alege(sursa).Importator?.FormatId);
    }

    [Fact]
    public void Convertorul_produce_o_versiune_cu_aceeasi_recapitulatie()
    {
        var deviz = Deviz(null, ArticoleExemplu);
        var versiune = ConvertorDevizCanonic.CaVersiune(deviz, Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "test", null, Parametri);

        Assert.Equal("import:test", versiune.Sursa);
        Assert.Equal(2643.21m, versiune.Recapitulatie().TotalCuTva);
    }

    [Fact]
    public void Versiunea_aprobata_nu_mai_poate_fi_modificata()
    {
        var versiune = ConvertorDevizCanonic.CaVersiune(Deviz(null, ArticoleExemplu), Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "test", null, Parametri);

        Assert.True(versiune.Aproba().Succes);
        Assert.True(versiune.EsteBlocat);
        Assert.Throws<InvalidOperationException>(() => versiune.AdaugaObiect("OB2", "Bloc B"));
        Assert.True(versiune.Aproba().Esec);
    }

    private sealed class ImportatorFals(string format, double incredere) : IImportatorDeviz
    {
        public string FormatId => format;
        public string Denumire => format;
        public RezultatDetectare PoateCiti(SursaImport sursa) => new(incredere, "test");
        public Task<RezultatImport<DevizCanonic>> CitesteAsync(SursaImport sursa, CancellationToken ct = default) =>
            Task.FromResult(RezultatImport<DevizCanonic>.Esuat("fals"));
    }
}
