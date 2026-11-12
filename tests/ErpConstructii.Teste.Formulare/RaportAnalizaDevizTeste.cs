using ErpConstructii.Devize;
using ErpConstructii.Devize.Analize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Infrastructure;
using ErpConstructii.Formulare;
using ErpConstructii.Formulare.Infrastructure.Pdf;
using UglyToad.PdfPig;

namespace ErpConstructii.Teste.Formulare;

public class RaportAnalizaDevizTeste
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 15, 10, 30, 0, TimeSpan.Zero);
    private static RegistruAnalizori Registru() => new([new AnalizorAmprentaNumerelor(), new AnalizorPreturiSpeciale()]);
    private static VersiuneDeviz Deviz() => new(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 7, "Deviz Șantier");
    private static string FaraSpatii(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
    private static void Contine(string asteptat, string text) => Assert.Contains(FaraSpatii(asteptat), text);

    private static byte[] Pdf(PrevizualizareAnalizaDevizDto rezultat)
    {
        var (aspect, date) = RaportAnalizaDeviz.PentruPdf(rezultat, Moment);
        return new GeneratorPdfStandard().Genereaza(new AntetFirma("Firma Șantier", null, null, null), aspect, date);
    }

    [Fact]
    public void Raportul_fara_constatari_precizeaza_regulile_si_limitarile()
    {
        var rezultat = RaportAnalizaDeviz.Previzualizeaza(Deviz(), Registru());
        using var pdf = PdfDocument.Open(Pdf(rezultat));
        var text = FaraSpatii(string.Concat(pdf.GetPages().Select(p => p.Text)));

        Contine("Deviz Șantier", text);
        Contine("15.09.2026 10:30 UTC", text);
        Contine("experimentală", text);
        Contine("A2 v1.0", text);
        Contine("A5 v1.0", text);
        Contine("Nu au fost găsite suspiciuni de regulile rulate.", text);
        foreach (var limitare in rezultat.Analizori.SelectMany(a => a.Limitari))
            Contine(limitare, text);
    }

    [Fact]
    public void Raportul_multipagina_pastreaza_constatarile_dovezile_si_propunerile()
    {
        var deviz = Deviz();
        var c = deviz.AdaugaCapitol("1", "Stadiu", TipCapitol.StadiuFizic);
        c.AdaugaArticol("S", "Săpătură", "mc", 100, ValoriResurse.Zero);
        c.AdaugaArticol("T", "Transport", "tona", 180, ValoriResurse.Zero);
        var gresit = c.AdaugaArticol("U", "Umplutură", "mc", 180, new(0, 100, 0, 0));
        for (var i = 0; i < 50; i++)
            gresit.AdaugaConsum($"R{i:D3}", $"Resursă {i:D3}", TipResursa.Material, "mc", 1, 0);

        var rezultat = RaportAnalizaDeviz.Previzualizeaza(deviz, Registru());
        using var pdf = PdfDocument.Open(Pdf(rezultat));
        Assert.True(pdf.NumberOfPages > 1);
        var text = FaraSpatii(string.Concat(pdf.GetPages().Select(p => p.Text)));
        Contine("A2.UNITATE_COPIATA", text);
        Contine("De verificat", text);
        Contine("180 → 100 mc", text);
        Contine("Cost direct estimat: -8.000,00 lei", text);
        for (var i = 0; i < 50; i++)
            Contine($"Resursă {i:D3}", text);
        Contine("Dovadă: resursa", text);
    }

    [Fact]
    public void Amprenta_este_stabila_si_detecteaza_modificarea_impactului_si_a_articolului_afisat()
    {
        var deviz = Deviz();
        var articol = deviz.AdaugaObiect("1", "Obiect").AdaugaArticol("A", "Articol", "mc", 10, ValoriResurse.Zero);
        var constatare = new ConstatareAnaliza("A2.TEST", "A2", "1.0", SeveritateAnaliza.Info, 1, [articol.CheieStabila],
            "Explicație", [new("dovadă", "valoare")], ImpactCostDirect: 10);
        var analizor = new AnalizorTest("A2", [constatare]);
        var registru = new RegistruAnalizori([analizor, new AnalizorTest("A5", [])]);
        var initial = RaportAnalizaDeviz.Previzualizeaza(deviz, registru);
        Assert.Equal(initial.Amprenta, RaportAnalizaDeviz.Previzualizeaza(deviz, registru).Amprenta);

        analizor.Constatari = [constatare with { ImpactCostDirect = 11 }];
        Assert.NotEqual(initial.Amprenta, RaportAnalizaDeviz.Previzualizeaza(deviz, registru).Amprenta);
        analizor.Constatari = [constatare];
        articol.SeteazaSursa(TipArticol.Normat, null, "WBS-nou");
        Assert.NotEqual(initial.Amprenta, RaportAnalizaDeviz.Previzualizeaza(deviz, registru).Amprenta);
    }

    [Fact]
    public void Amprenta_nu_depinde_de_ordinea_dovezilor_si_constatarilor_si_pastreaza_duplicatele()
    {
        var deviz = Deviz();
        var prima = new ConstatareAnaliza("A2.TEST", "A2", "1.0", SeveritateAnaliza.Info, 1, [],
            "Prima", [new("b", "2"), new("a", "1")]);
        var aDoua = prima with { Explicatie = "A doua" };
        var analizor = new AnalizorTest("A2", [prima, aDoua, prima]);
        var registru = new RegistruAnalizori([analizor, new AnalizorTest("A5", [])]);
        var initial = RaportAnalizaDeviz.Previzualizeaza(deviz, registru);
        analizor.Constatari = [aDoua, prima with { Dovezi = prima.Dovezi.Reverse().ToArray() }, prima];

        var reordonat = RaportAnalizaDeviz.Previzualizeaza(deviz, registru);
        Assert.Equal(initial.Amprenta, reordonat.Amprenta);
        Assert.Equal(3, reordonat.Analizori[0].Constatari.Count);
        analizor.Constatari = [prima, aDoua];
        Assert.NotEqual(initial.Amprenta, RaportAnalizaDeviz.Previzualizeaza(deviz, registru).Amprenta);
    }

    [Fact]
    public void Textul_foarte_lung_fara_spatii_nu_se_pierde_la_randare()
    {
        var deviz = Deviz();
        var explicatie = $"{new string('W', 1000)} SFÂRȘIT";
        var constatare = new ConstatareAnaliza("A2.TEST", "A2", "1.0", SeveritateAnaliza.Info, 1, [],
            explicatie, [new("lung", explicatie)]);
        var registru = new RegistruAnalizori([new AnalizorTest("A2", [constatare]), new AnalizorTest("A5", [])]);
        var rezultat = RaportAnalizaDeviz.Previzualizeaza(deviz, registru);
        using var pdf = PdfDocument.Open(Pdf(rezultat));
        var text = FaraSpatii(string.Concat(pdf.GetPages().Select(p => p.Text)));
        Assert.Equal(2000, text.Count(c => c == 'W'));
        Assert.Equal(2, text.Split("SFÂRȘIT", StringSplitOptions.None).Length - 1);
        foreach (var pagina in pdf.GetPages())
            Assert.All(pagina.GetWords().Where(w => w.Text.Contains('W')),
                cuvant => Assert.True(cuvant.BoundingBox.Right <= pagina.Width));
    }

    private sealed class AnalizorTest(string cod, IReadOnlyList<ConstatareAnaliza> constatari) : IAnalizorDeviz
    {
        public string Cod => cod;
        public string Versiune => "1.0";
        public IReadOnlyList<ConstatareAnaliza> Constatari { get; set; } = constatari;
        public RezultatAnalizor Analizeaza(ContextAnaliza context) => new(Cod, Versiune, Constatari, []);
    }
}
