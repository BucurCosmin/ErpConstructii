using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <summary>
/// Prima foaie a unui fișier .xlsx ca grilă de texte (rând × coloană, golurile = null). Numerele rămân în forma invariantă
/// din fișier („25.5”), textele din tabela de șiruri comune sau din celulă. Fără formatare și fără formule recalculate
/// (se ia valoarea salvată).
/// </summary>
internal static class CititorXlsx
{
    /// <returns>Null dacă fișierul nu e un .xlsx valid.</returns>
    public static IReadOnlyList<IReadOnlyList<string?>>? Citeste(Stream continut)
    {
        try
        {
            using var document = SpreadsheetDocument.Open(continut, false);
            var parteCarte = document.WorkbookPart;
            var foaie = parteCarte?.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault();
            if (parteCarte is null || foaie?.Id?.Value is not { } id || parteCarte.GetPartById(id) is not WorksheetPart parteFoaie)
                return null;

            var siruri = parteCarte.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
            var randuri = new SortedDictionary<int, SortedDictionary<int, string>>();
            foreach (var rand in parteFoaie.Worksheet?.Descendants<Row>() ?? [])
            {
                var nrRand = (int)(rand.RowIndex?.Value ?? (uint)(randuri.Count + 1));
                var celule = new SortedDictionary<int, string>();
                var urmatoarea = 0;
                foreach (var celula in rand.Elements<Cell>())
                {
                    var coloana = celula.CellReference?.Value is { } r ? Coloana(r) : urmatoarea;
                    urmatoarea = coloana + 1;
                    if (Text(celula, siruri) is { Length: > 0 } t)
                        celule[coloana] = t;
                }

                if (celule.Count > 0)
                    randuri[nrRand] = celule;
            }

            // Grilă densă: rândurile goale rămân (numerotarea corespunde cu Excel).
            var ultimul = randuri.Count == 0 ? 0 : randuri.Keys.Max();
            var grila = new List<IReadOnlyList<string?>>(ultimul);
            for (var i = 1; i <= ultimul; i++)
            {
                if (!randuri.TryGetValue(i, out var celule))
                {
                    grila.Add([]);
                    continue;
                }

                var linie = new string?[celule.Keys.Max() + 1];
                foreach (var (c, v) in celule)
                    linie[c] = v;
                grila.Add(linie);
            }

            return grila;
        }
        catch (Exception ex) when (ex is DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or InvalidDataException or FileFormatException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string? Text(Cell celula, List<string> siruri)
    {
        if (celula.DataType?.Value == CellValues.InlineString)
            return celula.InlineString?.InnerText;
        var valoare = celula.CellValue?.Text;
        if (valoare is null)
            return null;
        if (celula.DataType?.Value == CellValues.SharedString)
            return int.TryParse(valoare, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < siruri.Count ? siruri[i] : null;
        return valoare;
    }

    /// <summary>„AB12” → 27 (coloanele de la 0).</summary>
    private static int Coloana(string referinta)
    {
        var c = 0;
        foreach (var ch in referinta)
        {
            if (!char.IsLetter(ch))
                break;
            c = c * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return Math.Max(c - 1, 0);
    }
}
