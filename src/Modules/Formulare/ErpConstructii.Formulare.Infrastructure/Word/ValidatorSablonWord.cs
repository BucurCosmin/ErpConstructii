using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Formulare.Infrastructure.Word;

/// <summary>
/// Verifică un șablon încărcat de administratorul firmei (REQ-FRM-04): are controalele pentru
/// toate câmpurile obligatorii și rândul-șablon pentru fiecare tabel? Etichetele necunoscute
/// sunt semnalate (de obicei greșeli de scriere).
/// </summary>
public sealed class ValidatorSablonWord : IValidatorSablon
{
    public IReadOnlyList<ProblemaImport> Valideaza(byte[] sablonDocx, TipFormular tip)
    {
        var probleme = new List<ProblemaImport>();
        HashSet<string> etichete;
        try
        {
            using var ms = new MemoryStream(sablonDocx, writable: false);
            using var doc = WordprocessingDocument.Open(ms, isEditable: false);
            etichete = doc.MainDocumentPart is { } main
                ? OpenXmlUtil.Radacini(main)
                    .SelectMany(r => r.Descendants<SdtElement>())
                    .Select(OpenXmlUtil.Eticheta)
                    .OfType<string>()
                    .ToHashSet(StringComparer.Ordinal)
                : [];
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FormatException)
        {
            probleme.Add(new(Severitate.Eroare, "șablon", $"Fișierul nu este un document Word valid: {ex.Message}"));
            return probleme;
        }

        foreach (var camp in tip.Campuri.Where(c => c.Obligatoriu && !etichete.Contains(c.Nume)))
        {
            probleme.Add(new(Severitate.Eroare, camp.Nume, $"Lipsește controlul pentru câmpul obligatoriu „{camp.Eticheta}”."));
        }

        foreach (var tabel in tip.Tabele)
        {
            foreach (var col in tabel.Coloane.Where(c => c.Obligatoriu && !etichete.Contains($"{tabel.Nume}.{c.Nume}")))
            {
                probleme.Add(new(Severitate.Eroare, $"{tabel.Nume}.{col.Nume}", $"Lipsește coloana obligatorie „{col.Eticheta}” din tabel."));
            }
        }

        var cunoscute = tip.Campuri.Select(c => c.Nume)
            .Concat(tip.Tabele.SelectMany(t => t.Coloane.Select(c => $"{t.Nume}.{c.Nume}")))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var necunoscuta in etichete.Where(e => !cunoscute.Contains(e)))
        {
            probleme.Add(new(Severitate.Avertisment, necunoscuta, "Etichetă necunoscută pentru acest tip de formular (greșeală de scriere?)."));
        }

        return probleme;
    }
}
