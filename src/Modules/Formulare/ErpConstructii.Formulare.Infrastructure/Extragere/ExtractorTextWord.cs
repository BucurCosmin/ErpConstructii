using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Formulare.Infrastructure.Extragere;

/// <summary>
/// Textul unui document Word, în ordinea din document: fiecare paragraf e un rând; fiecare tabel e citit structurat
/// (celule exacte) și, în plus, fiecare rând de tabel apare ca rând de text cu celulele separate prin „ | ”
/// (antetele formularelor stau des în tabele fără borduri).
/// </summary>
public sealed class ExtractorTextWord : IExtractorText
{
    public bool Accepta(string numeFisier) => Path.GetExtension(numeFisier).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<RezultatImport<TextDocument>> ExtrageAsync(SursaImport sursa, CancellationToken ct = default)
    {
        try
        {
            using var ms = new MemoryStream(sursa.Continut, writable: false);
            using var doc = WordprocessingDocument.Open(ms, isEditable: false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return Task.FromResult(RezultatImport<TextDocument>.Esuat("Documentul Word nu are conținut."));
            }

            var randuri = new List<RandText>();
            var tabele = new List<TabelText>();
            foreach (var element in body.ChildElements)
            {
                switch (element)
                {
                    case Paragraph p:
                        AdaugaRand(randuri, TextParagraf(p));
                        break;
                    case Table t:
                        var tabel = t.Elements<TableRow>()
                            .Select(r => (IReadOnlyList<string>)r.Elements<TableCell>()
                                .Select(c => string.Join(' ', c.Elements<Paragraph>().Select(TextParagraf)).Trim())
                                .ToList())
                            .ToList();
                        tabele.Add(new TabelText(tabel));
                        foreach (var rand in tabel)
                        {
                            AdaugaRand(randuri, string.Join(" | ", rand.Where(c => c.Length > 0)));
                        }

                        break;
                }
            }

            return Task.FromResult(RezultatImport<TextDocument>.Ok(new TextDocument(randuri, tabele, DinOcr: false)));
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException)
        {
            return Task.FromResult(RezultatImport<TextDocument>.Esuat("Fișierul nu este un document Word .docx valid."));
        }
    }

    /// <summary>Textul unui paragraf; tab-urile devin spații (eticheta și valoarea sunt des separate prin tab).</summary>
    private static string TextParagraf(Paragraph p) =>
        string.Concat(p.Descendants().Select(e => e switch
        {
            Text t => t.Text,
            TabChar => " ",
            Break => " ",
            _ => string.Empty,
        }));

    private static void AdaugaRand(List<RandText> randuri, string text)
    {
        text = text.Trim();
        if (text.Length > 0)
        {
            randuri.Add(RandText.DinText(text));
        }
    }
}
