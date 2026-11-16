using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Infrastructure.Import;

/// <summary>
/// Toate foile unui fișier .xlsx, cu numele lor, ca grile de texte (<see cref="FoaieTabel"/>): rândurile și coloanele
/// corespund cu Excel (golurile rămân), numerele în forma invariantă din fișier, textele din tabela de șiruri comune sau din
/// celulă, valoarea salvată a formulelor. Fără formatare. Pentru interpretorii care citesc fișe / exporturi Excel (ex. fișa
/// de urmărire a contractului, D-56).
/// </summary>
public static class CititorFoiXlsx
{
    /// <returns>Null dacă fișierul nu e un .xlsx valid.</returns>
    public static IReadOnlyList<FoaieTabel>? Citeste(Stream continut)
    {
        try
        {
            using var document = SpreadsheetDocument.Open(continut, false);
            var carte = document.WorkbookPart;
            if (carte?.Workbook?.Sheets is null)
                return null;
            var siruri = carte.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
            var foi = new List<FoaieTabel>();
            foreach (var foaie in carte.Workbook.Sheets.Elements<Sheet>())
            {
                if (foaie.Id?.Value is not { } id || carte.GetPartById(id) is not WorksheetPart parte)
                    continue;
                foi.Add(new FoaieTabel(foaie.Name?.Value ?? string.Empty, Grila(parte, siruri)));
            }

            return foi;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or OpenXmlPackageException or InvalidOperationException or ArgumentException
                                      or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static List<IReadOnlyList<string?>> Grila(WorksheetPart parte, IReadOnlyList<string> siruri)
    {
        var randuri = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var rand in parte.Worksheet?.Descendants<Row>() ?? [])
        {
            var nr = (int)(rand.RowIndex?.Value ?? (uint)(randuri.Count + 1));
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
                randuri[nr] = celule;
        }

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

    private static string? Text(Cell c, IReadOnlyList<string> siruri)
    {
        if (c.DataType?.Value == CellValues.InlineString)
            return c.InlineString?.InnerText?.Trim();
        var v = c.CellValue?.InnerText;
        if (string.IsNullOrEmpty(v))
            return null;
        if (c.DataType?.Value == CellValues.SharedString)
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < siruri.Count ? siruri[i].Trim() : null;
        if (c.DataType?.Value == CellValues.Boolean)
            return v == "1" ? "DA" : "NU";
        return v.Trim();
    }

    /// <summary>„AB12” → indexul coloanei (A = 0).</summary>
    private static int Coloana(string referinta)
    {
        var n = 0;
        foreach (var ch in referinta.TakeWhile(char.IsLetter))
            n = (n * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        return n - 1;
    }
}
