using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Formulare.Infrastructure.Word;

/// <summary>
/// Citește un formular Word completat (REQ-FRM-08..10): proprietățile ascunse și valorile din
/// controalele de conținut. Nu interpretează textul liber; documentele fără șablon nu se citesc aici.
/// </summary>
public sealed class CititorWord : ICititorFormular
{
    public RezultatCitireFormular Citeste(byte[] docx)
    {
        var probleme = new List<ProblemaImport>();
        var campuri = new Dictionary<string, string>(StringComparer.Ordinal);
        var tabele = new Dictionary<string, List<IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);
        MetadateFormular? metadate = null;

        try
        {
            using var ms = new MemoryStream(docx, writable: false);
            using var doc = WordprocessingDocument.Open(ms, isEditable: false);
            metadate = CitesteMetadate(doc, probleme);

            var main = doc.MainDocumentPart;
            if (main is null)
            {
                probleme.Add(new(Severitate.Eroare, "fișier", "Documentul nu are conținut."));
                return Rezultat(metadate, campuri, tabele, probleme);
            }

            foreach (var radacina in OpenXmlUtil.Radacini(main))
            {
                // Câmpurile simple.
                foreach (var sdt in radacina.Descendants<SdtElement>())
                {
                    var eticheta = OpenXmlUtil.Eticheta(sdt);
                    if (eticheta is null || eticheta.Contains('.', StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (campuri.ContainsKey(eticheta))
                    {
                        probleme.Add(new(Severitate.Avertisment, eticheta, "Câmpul apare de mai multe ori; s-a păstrat prima valoare."));
                        continue;
                    }

                    campuri[eticheta] = OpenXmlUtil.TextDin(sdt);
                }

                // Rândurile tabelelor: un rând = o linie, coloanele din etichetele „tabel.coloana”.
                foreach (var rand in radacina.Descendants<TableRow>())
                {
                    var celule = rand.Descendants<SdtElement>()
                        .Select(s => (Eticheta: OpenXmlUtil.Eticheta(s), Text: OpenXmlUtil.TextDin(s)))
                        .Where(x => x.Eticheta?.Contains('.', StringComparison.Ordinal) == true)
                        .ToList();
                    if (celule.Count == 0)
                    {
                        continue;
                    }

                    var numeTabel = celule[0].Eticheta![..celule[0].Eticheta!.IndexOf('.', StringComparison.Ordinal)];
                    var linie = celule.ToDictionary(c => c.Eticheta![(numeTabel.Length + 1)..], c => c.Text, StringComparer.Ordinal);
                    if (linie.Values.All(string.IsNullOrWhiteSpace))
                    {
                        continue; // rând gol din formularul descărcat
                    }

                    if (!tabele.TryGetValue(numeTabel, out var lista))
                    {
                        tabele[numeTabel] = lista = [];
                    }

                    lista.Add(linie);
                }
            }
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FormatException)
        {
            probleme.Add(new(Severitate.Eroare, "fișier", $"Fișierul nu este un document Word valid: {ex.Message}"));
        }

        return Rezultat(metadate, campuri, tabele, probleme);
    }

    private static MetadateFormular? CitesteMetadate(WordprocessingDocument doc, List<ProblemaImport> probleme)
    {
        var tip = OpenXmlUtil.CitesteProprietate(doc, OpenXmlUtil.PropTip);
        if (string.IsNullOrWhiteSpace(tip))
        {
            probleme.Add(new(Severitate.Eroare, "fișier",
                "Documentul nu provine dintr-un șablon al sistemului (lipsește tipul formularului). Atașează-l ca fișier și introdu datele manual."));
            return null;
        }

        _ = int.TryParse(OpenXmlUtil.CitesteProprietate(doc, OpenXmlUtil.PropVersiune), NumberStyles.Integer, CultureInfo.InvariantCulture, out var versiune);
        Guid? documentId = Guid.TryParse(OpenXmlUtil.CitesteProprietate(doc, OpenXmlUtil.PropDocument), out var id) ? id : null;
        var numar = OpenXmlUtil.CitesteProprietate(doc, OpenXmlUtil.PropNumar);
        return new MetadateFormular(tip, versiune, documentId, numar);
    }

    private static RezultatCitireFormular Rezultat(
        MetadateFormular? metadate,
        Dictionary<string, string> campuri,
        Dictionary<string, List<IReadOnlyDictionary<string, string>>> tabele,
        List<ProblemaImport> probleme) =>
        new(
            metadate,
            new DateFormular(campuri, tabele.ToDictionary(t => t.Key, t => (IReadOnlyList<IReadOnlyDictionary<string, string>>)t.Value)),
            probleme);
}
