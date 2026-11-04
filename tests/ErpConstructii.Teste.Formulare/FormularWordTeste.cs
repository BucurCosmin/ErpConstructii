using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ErpConstructii.Formulare;
using ErpConstructii.Formulare.Infrastructure.Word;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Teste.Formulare;

/// <summary>
/// Drumul complet al unui formular: șablon → generare → citire înapoi (REQ-FRM-03, REQ-FRM-08).
/// Dacă acest test trece, un formular generat de sistem și completat manual se poate importa.
/// </summary>
public class FormularWordTeste
{
    private static readonly TipFormular Tip = CatalogFormulare.NecesarMateriale;
    private static readonly MetadateFormular Metadate = new(Tip.Cod, 3, Guid.Parse("0193f3c0-0000-7000-8000-000000000001"), "NM/2026/0007");

    [Fact]
    public void Generat_apoi_citit_pastreaza_campurile_liniile_si_metadatele()
    {
        var date = new DateFormular(
            new Dictionary<string, string>
            {
                ["numar"] = "NM/2026/0007",
                ["data"] = "03.11.2026",
                ["proiect_cod"] = "P-01",
                ["lucrare"] = "Reabilitare",
                ["compartiment_santier"] = "Șantier central",
            },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["linii"] =
                [
                    new Dictionary<string, string> { ["denumire"] = "Beton C16/20", ["um"] = "m3", ["cantitate"] = "12,5" },
                    new Dictionary<string, string> { ["denumire"] = "Oțel beton PC52", ["um"] = "kg", ["cantitate"] = "1.200" },
                ],
            });

        var docx = new GeneratorWord().Genereaza(Sablon(), Tip, date, Metadate);
        var citit = new CititorWord().Citeste(docx);

        Assert.Empty(citit.Probleme);
        Assert.Equal(Metadate, citit.Metadate);
        Assert.Equal("NM/2026/0007", citit.Date.Campuri["numar"]);
        Assert.Equal("Reabilitare", citit.Date.Campuri["lucrare"]);

        var linii = citit.Date.Tabele["linii"];
        Assert.Equal(2, linii.Count);
        Assert.Equal("Oțel beton PC52", linii[1]["denumire"]);
        Assert.Equal("1.200", linii[1]["cantitate"]);
        Assert.Equal("kg", linii[1]["um"]);
    }

    [Fact]
    public void Formularul_gol_are_randuri_goale_care_nu_devin_linii_la_import()
    {
        var docx = new GeneratorWord().Genereaza(Sablon(), Tip, DateFormular.Gol, Metadate, randuriGoale: 5);

        using (var ms = new MemoryStream(docx))
        using (var doc = WordprocessingDocument.Open(ms, false))
        {
            Assert.Equal(5, doc.MainDocumentPart!.Document!.Descendants<TableRow>().Count());
        }

        var citit = new CititorWord().Citeste(docx);
        Assert.False(citit.Date.Tabele.ContainsKey("linii"));
        // Controalele neatinse arată încă placeholder-ul, deci se citesc ca goale.
        Assert.Equal(string.Empty, citit.Date.Campuri["lucrare"]);
    }

    [Fact]
    public void Documentul_fara_metadate_este_respins_cu_mesaj_clar()
    {
        var citit = new CititorWord().Citeste(Sablon());
        Assert.Null(citit.Metadate);
        Assert.Contains(citit.Probleme, p => p.Severitate == Severitate.Eroare && p.Mesaj.Contains("șablon"));
    }

    [Fact]
    public void Validatorul_de_sablon_gaseste_campurile_obligatorii_lipsa()
    {
        var probleme = new ValidatorSablonWord().Valideaza(SablonPartial(), Tip);

        // Șablonul parțial nu are lucrare și unitatea de măsură.
        Assert.Contains(probleme, p => p.Locatie == "lucrare");
        Assert.Contains(probleme, p => p.Locatie == "compartiment_santier");
        Assert.Contains(probleme, p => p.Locatie == "linii.um");
        Assert.DoesNotContain(probleme, p => p.Locatie == "numar");
    }

    [Fact]
    public void Necesarul_de_materiale_fara_controale_se_normalizeaza_si_se_genereaza_cu_randuri()
    {
        var sablon = NormalizatorNecesarMateriale.Normalizeaza(NecesarMateriale(), Tip);
        var probleme = new ValidatorSablonWord().Valideaza(sablon, Tip);
        Assert.DoesNotContain(probleme, p => p.Severitate == Severitate.Eroare);

        var date = new DateFormular(
            new Dictionary<string, string>
            {
                ["numar"] = "NM/2026/0007",
                ["data"] = "03.11.2026",
                ["proiect_cod"] = "P-01",
                ["lucrare"] = "Reabilitare",
                ["compartiment_santier"] = "Șantier central",
                ["intocmit_de"] = "Ion Popescu",
            },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["linii"] = [new Dictionary<string, string> { ["denumire"] = "Ciment", ["um"] = "sac", ["cantitate"] = "25" }],
            });

        var rezultat = new GeneratorWord().Genereaza(sablon, Tip, date, Metadate, randuriGoale: 15);
        using var ms = new MemoryStream(rezultat);
        using var doc = WordprocessingDocument.Open(ms, false);
        var corp = doc.MainDocumentPart!.Document!.Body!;
        Assert.Contains("NM/2026/0007", corp.InnerText);
        Assert.Contains("03.11.2026", corp.InnerText);
        Assert.Contains("Reabilitare", corp.InnerText);
        Assert.Contains("Șantier central", corp.InnerText);
        Assert.Contains("Intocmit Ion Popescu", corp.InnerText);
        Assert.Contains("Productie Lucrare: Reabilitare", corp.InnerText);
        Assert.DoesNotContain("{{", corp.InnerText);
        Assert.DoesNotContain("}}", corp.InnerText);
        Assert.Contains("Ciment", corp.InnerText);
        var tabelMateriale = corp.Descendants<Table>()
            .Single(t => t.InnerText.Contains("Denumire material", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(16, tabelMateriale.Elements<TableRow>().Count());
    }

    [Fact]
    public void Fisierul_care_nu_e_Word_nu_arunca_exceptie()
    {
        var citit = new CititorWord().Citeste([1, 2, 3, 4]);
        Assert.Contains(citit.Probleme, p => p.Severitate == Severitate.Eroare);
    }

    /// <summary>Șablon minimal: două câmpuri și un tabel cu rândul-șablon „linii.*”.</summary>
    private static byte[] Sablon()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Comanda nr. ")), Control("numar")),
                new Paragraph(new Run(new Text("Data: ")), Control("data")),
                new Paragraph(new Run(new Text("Lucrare: ")), Control("lucrare")),
                new Table(new TableRow(Celula("linii.denumire"), Celula("linii.um"), Celula("linii.cantitate")))));
        }

        return ms.ToArray();
    }

    private static byte[] SablonPartial()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(Control("numar")),
                new Paragraph(Control("data")),
                new Table(new TableRow(Celula("linii.denumire"), Celula("linii.cantitate")))));
        }

        return ms.ToArray();
    }

    private static byte[] NecesarMateriale()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("NECESAR MATERIALE"))),
                new Table(
                    new TableRow(CelulaText("S.C. GENERAL CONCRETE CERNAVODA S.R.L."), CelulaText("Nr. {{NR}}")),
                    new TableRow(CelulaText("{{Compartiment/Santier}}"), CelulaText("Data: {{DATA}}"))),
                new Paragraph(new Run(new Text("Viza Comp. Tehnic-ProductieLucrare: {{PROIECT}}"))),
                new Table(
                    new TableRow(CelulaText("Nr. Crt."), CelulaText("Denumire material"), CelulaText("UM"), CelulaText("Cantitate"), CelulaText("Observatii")),
                    new TableRow(CelulaText("1"), CelulaText("Ciment"), CelulaText("sac"), CelulaText("25"), CelulaText("Livrare"))),
                new Table(new TableRow(CelulaText("Intocmit{{EMITENT}}")))));
        }

        return ms.ToArray();
    }

    private static SdtRun Control(string eticheta) =>
        new(
            new SdtProperties(new Tag { Val = eticheta }, new ShowingPlaceholder()),
            new SdtContentRun(new Run(new Text("Click pentru a completa"))));

    private static TableCell Celula(string eticheta) => new(new Paragraph(Control(eticheta)));

    private static TableCell CelulaText(string text) => new(new Paragraph(new Run(new Text(text))));
}
