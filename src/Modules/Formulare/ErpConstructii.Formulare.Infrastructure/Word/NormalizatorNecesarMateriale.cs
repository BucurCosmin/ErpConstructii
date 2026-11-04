using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ErpConstructii.Formulare.Infrastructure.Word;

public static partial class NormalizatorNecesarMateriale
{
    private static readonly string[] AntetTabel =
    [
        "NR CRT",
        "DENUMIRE MATERIAL",
        "UM",
        "CANTITATE",
        "OBSERVATII",
    ];

    public static byte[] Normalizeaza(byte[] continut, TipFormular tip)
    {
        if (tip.Cod != CatalogFormulare.NecesarMateriale.Cod)
            return continut;

        using var ms = new MemoryStream();
        ms.Write(continut, 0, continut.Length);
        ms.Position = 0;
        try
        {
            using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
            {
                var main = doc.MainDocumentPart;
                var document = main?.Document;
                var body = document?.Body;
                if (main is null || document is null || body is null ||
                    document.Descendants<SdtElement>().Any() ||
                    !NormalizeazaText(body.InnerText).Contains("NECESAR MATERIALE", StringComparison.Ordinal))
                {
                    return continut;
                }

                var paragrafe = body.Descendants<Paragraph>().ToList();
                var paragrafNumar = paragrafe.FirstOrDefault(p => PotrivireNumar().Match(p.InnerText).Success);
                var paragrafData = paragrafe.FirstOrDefault(p => PotrivireData().Match(p.InnerText).Success);
                var paragrafLucrare = paragrafe.FirstOrDefault(p => p.InnerText.Contains("Lucrare:", StringComparison.OrdinalIgnoreCase));
                var paragrafCompartiment = paragrafe.FirstOrDefault(p =>
                    p.InnerText.Contains("Compartiment/Santier", StringComparison.OrdinalIgnoreCase));
                var paragrafEmitent = paragrafe.FirstOrDefault(p => PlaceholderEmitent().IsMatch(p.InnerText));
                var randuriTabel = body.Descendants<Table>()
                    .SelectMany(t => t.Elements<TableRow>())
                    .ToList();
                var randAntet = randuriTabel.FindIndex(r =>
                    r.Elements<TableCell>().Take(AntetTabel.Length).Select(c => NormalizeazaText(c.InnerText))
                        .SequenceEqual(AntetTabel, StringComparer.Ordinal));
                if (paragrafNumar is null || paragrafData is null || paragrafLucrare is null ||
                    paragrafCompartiment is null || randAntet < 0)
                    return continut;

                var tabel = randuriTabel[randAntet].Parent as Table;
                var randuri = tabel?.Elements<TableRow>().ToList();
                var indexAntet = randuri?.IndexOf(randuriTabel[randAntet]) ?? -1;
                if (tabel is null || randuri is null || indexAntet < 0 || indexAntet + 1 >= randuri.Count)
                    return continut;

                var randSablon = randuri[indexAntet + 1];
                var celule = randSablon.Elements<TableCell>().Take(AntetTabel.Length).ToList();
                if (celule.Count != AntetTabel.Length)
                    return continut;

                var potrivireNumar = PotrivireNumar().Match(paragrafNumar.InnerText);
                var potrivireData = PotrivireData().Match(paragrafData.InnerText);
                var prefixLucrare = paragrafLucrare.InnerText;
                var indexLucrare = prefixLucrare.IndexOf("Lucrare:", StringComparison.OrdinalIgnoreCase);
                var prefixInainteDeLucrare = prefixLucrare[..indexLucrare];
                if (prefixInainteDeLucrare.Length > 0 && !char.IsWhiteSpace(prefixInainteDeLucrare[^1]))
                    prefixInainteDeLucrare += " ";
                prefixLucrare = prefixInainteDeLucrare + "Lucrare: ";
                var textCompartiment = PlaceholderCompartiment().Replace(paragrafCompartiment.InnerText, "Compartiment/Santier");
                var prefixCompartiment = textCompartiment;
                var indexCompartiment = prefixCompartiment.IndexOf("Compartiment/Santier", StringComparison.OrdinalIgnoreCase);
                prefixCompartiment = prefixCompartiment[..(indexCompartiment + "Compartiment/Santier".Length)];
                if (!prefixCompartiment.EndsWith(':'))
                    prefixCompartiment += ":";
                prefixCompartiment += " ";

                SeteazaParagraf(paragrafNumar, potrivireNumar.Groups[1].Value, "numar");
                SeteazaParagraf(paragrafData, potrivireData.Groups[1].Value, "data");
                SeteazaParagraf(paragrafLucrare, prefixLucrare, "lucrare");
                SeteazaParagraf(paragrafCompartiment, prefixCompartiment, "compartiment_santier");
                if (paragrafEmitent is not null)
                {
                    var textEmitent = paragrafEmitent.InnerText;
                    var potrivireEmitent = PlaceholderEmitent().Match(textEmitent);
                    var prefixEmitent = textEmitent[..potrivireEmitent.Index];
                    if (prefixEmitent.Length > 0 && !char.IsWhiteSpace(prefixEmitent[^1]))
                        prefixEmitent += " ";
                    SeteazaParagraf(paragrafEmitent, prefixEmitent, "intocmit_de");
                }

                var etichete = new[] { "linii.numar_crt", "linii.denumire", "linii.um", "linii.cantitate", "linii.observatii" };
                for (var index = 0; index < celule.Count; index++)
                    SeteazaCelula(celule[index], etichete[index]);

                foreach (var rand in randuri.Skip(indexAntet + 2))
                    rand.Remove();
            }

            return ms.ToArray();
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FormatException)
        {
            return continut;
        }
    }

    private static Regex PotrivireNumar() => new(@"^\s*(Nr\.\s*)\S+\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static Regex PotrivireData() => new(@"^\s*(Data:\s*).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static Regex PlaceholderEmitent() => new(@"\{\{\s*EMITENT\s*\}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static Regex PlaceholderCompartiment() => new(@"\{\{\s*COMPARTIMENT\s*/\s*SANTIER\s*\}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string NormalizeazaText(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var faraDiacritice = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        return Regex.Replace(faraDiacritice.ToUpperInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    }

    private static void SeteazaCelula(TableCell celula, string eticheta)
    {
        var paragraf = celula.Elements<Paragraph>().FirstOrDefault();
        var proprietatiParagraf = paragraf?.ParagraphProperties?.CloneNode(true) as ParagraphProperties;
        var proprietatiRun = paragraf?.Descendants<RunProperties>().FirstOrDefault()?.CloneNode(true) as RunProperties;
        celula.RemoveAllChildren<Paragraph>();
        var nou = new Paragraph();
        if (proprietatiParagraf is not null)
            nou.AppendChild(proprietatiParagraf);
        nou.AppendChild(Control(eticheta, " ", proprietatiRun));
        celula.AppendChild(nou);
    }

    private static void SeteazaParagraf(Paragraph paragraf, string prefix, string eticheta)
    {
        var proprietatiParagraf = paragraf.ParagraphProperties?.CloneNode(true) as ParagraphProperties;
        var proprietatiRun = paragraf.Descendants<RunProperties>().FirstOrDefault()?.CloneNode(true) as RunProperties;
        paragraf.RemoveAllChildren();
        if (proprietatiParagraf is not null)
            paragraf.AppendChild(proprietatiParagraf);

        if (prefix.Length > 0)
        {
            var run = new Run();
            if (proprietatiRun is not null)
                run.AppendChild((RunProperties)proprietatiRun.CloneNode(true));
            run.AppendChild(new Text(prefix) { Space = SpaceProcessingModeValues.Preserve });
            paragraf.AppendChild(run);
        }

        paragraf.AppendChild(Control(eticheta, " ", proprietatiRun));
    }

    private static SdtRun Control(string eticheta, string text, RunProperties? proprietatiRun)
    {
        var run = new Run();
        if (proprietatiRun is not null)
            run.AppendChild((RunProperties)proprietatiRun.CloneNode(true));
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return new SdtRun(
            new SdtProperties(new Tag { Val = eticheta }),
            new SdtContentRun(run));
    }
}
