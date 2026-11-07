using System.Globalization;
using System.Text;

namespace ErpConstructii.Aprovizionare;

/// <summary>Un loc care poate cere materiale (D-38): șantier al proiectului sau compartiment al firmei.</summary>
public sealed record LocCandidat(Guid Id, bool EsteSantier, string Denumire);

/// <summary>
/// Potrivește textul scris pe un necesar (importat de pe hârtie / din aplicația Windows) cu un șantier sau compartiment existent:
/// întâi denumirea identică (fără diacritice, majuscule, spații în plus), apoi o singură denumire care o conține sau e conținută.
/// Mai multe potriviri sau niciuna = null (utilizatorul alege).
/// </summary>
public static class PotrivireSolicitant
{
    public static LocCandidat? Cauta(string? text, IReadOnlyList<LocCandidat> locuri)
    {
        var t = Cheie(text);
        if (t.Length == 0 || locuri.Count == 0)
            return null;
        var identice = locuri.Where(l => Cheie(l.Denumire) == t).ToList();
        if (identice.Count == 1)
            return identice[0];
        if (identice.Count > 1)
            return null;
        // pe cuvinte întregi: „Tronson 2” ⊂ „Tronson 2 Podari”, dar „ant” nu e în „Șantier”
        var tc = $" {t} ";
        var partiale = locuri.Where(l => Cheie(l.Denumire) is { Length: >= 3 } c
            && (tc.Contains($" {c} ", StringComparison.Ordinal) || $" {c} ".Contains(tc, StringComparison.Ordinal))).ToList();
        return partiale.Count == 1 ? partiale[0] : null;
    }

    public static string Cheie(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
