using ErpConstructii.SharedKernel.Import;
using System.Globalization;
using System.Text;

namespace ErpConstructii.Proiecte;

public sealed record ProiectCandidat(Guid Id, string Cod, string Denumire);

/// <param name="Scor">0..1; 1 = codul proiectului apare în text.</param>
public sealed record PropunereProiect(ProiectCandidat Proiect, double Scor, Certitudine Certitudine);

/// <summary>
/// Propune proiectul la care se referă textul „Lucrare” dintr-un document extern (REQ-FRM-09).
/// Codul proiectului găsit în text → sigur; altfel, potrivire după cuvintele comune din denumire → de verificat.
/// Utilizatorul confirmă sau alege alt proiect.
/// </summary>
public static class PotrivireProiect
{
    public const double ScorMinim = 0.5;

    private static readonly HashSet<string> CuvinteIgnorate =
    [
        "SI", "DE", "LA", "IN", "CU", "PE", "DIN", "PENTRU", "STR", "NR", "BL", "SC", "AP",
        "THE", "AND", "FOR", "OF", "LUCRARE", "LUCRARI", "PROIECT", "PROJECT",
    ];

    public static PropunereProiect? Propune(string? textLucrare, IEnumerable<ProiectCandidat> proiecte)
    {
        if (string.IsNullOrWhiteSpace(textLucrare))
        {
            return null;
        }

        var text = Normalizeaza(textLucrare);
        var cuvinteText = Cuvinte(text);
        PropunereProiect? cea = null;

        foreach (var p in proiecte)
        {
            var cod = Normalizeaza(p.Cod);
            if (cod.Length >= 3 && ContineCuvant(text, cod))
            {
                return new PropunereProiect(p, 1, Certitudine.Sigur);
            }

            var cuvinteProiect = Cuvinte(Normalizeaza(p.Denumire));
            if (cuvinteProiect.Count == 0)
            {
                continue;
            }

            // Ce parte din denumirea proiectului apare în text (denumirile din documente sunt de regulă mai lungi).
            var scor = (double)cuvinteProiect.Count(cuvinteText.Contains) / cuvinteProiect.Count;
            if (scor >= ScorMinim && (cea is null || scor > cea.Scor))
            {
                cea = new PropunereProiect(p, Math.Round(scor, 2), Certitudine.Nesigur);
            }
        }

        return cea;
    }

    private static HashSet<string> Cuvinte(string text) =>
        text.Split([' ', ',', '.', ';', ':', '(', ')', '-', '/', '–', '"', '„', '”'], StringSplitOptions.RemoveEmptyEntries)
            .Where(c => c.Length >= 3 && !CuvinteIgnorate.Contains(c))
            .ToHashSet(StringComparer.Ordinal);

    private static bool ContineCuvant(string text, string cuvant)
    {
        var i = text.IndexOf(cuvant, StringComparison.Ordinal);
        while (i >= 0)
        {
            var stanga = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var dreapta = i + cuvant.Length == text.Length || !char.IsLetterOrDigit(text[i + cuvant.Length]);
            if (stanga && dreapta)
            {
                return true;
            }

            i = text.IndexOf(cuvant, i + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static string Normalizeaza(string text)
    {
        var fd = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(fd.Length);
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString();
    }
}
