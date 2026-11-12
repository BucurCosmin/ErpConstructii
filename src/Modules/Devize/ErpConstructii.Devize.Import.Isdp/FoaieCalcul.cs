using System.Globalization;
using System.Text;

namespace ErpConstructii.Devize.Import.Isdp;

/// <summary>
/// O foaie de calcul ca grilă de celule (rând, coloană), independentă de biblioteca de citire Excel.
/// Celulele sunt: <c>null</c>, <see cref="string"/>, <see cref="double"/> (ce dă Excel), <see cref="decimal"/>
/// sau alte valori convertibile.
///
/// Parserele NU se bazează pe litere de coloană fixe: găsesc rândul de antet după text și lucrează
/// relativ la el. Astfel nu contează dacă biblioteca de citire sare peste rânduri/coloane goale de la început.
/// </summary>
public sealed class FoaieCalcul(string nume, IReadOnlyList<IReadOnlyList<object?>> randuri)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Nume { get; } = nume;

    public int NrRanduri => randuri.Count;

    public int NrColoane(int rand) => rand < randuri.Count ? randuri[rand].Count : 0;

    public object? Celula(int rand, int coloana) =>
        rand >= 0 && rand < randuri.Count && coloana >= 0 && coloana < randuri[rand].Count ? randuri[rand][coloana] : null;

    /// <summary>Textul celulei, fără spații la capete (numerele în format invariant).</summary>
    public string Text(int rand, int coloana) => (Celula(rand, coloana) switch
    {
        null => string.Empty,
        string s => s,
        double d => d.ToString("R", Inv),
        decimal m => m.ToString(Inv),
        IFormattable f => f.ToString(null, Inv),
        var o => o.ToString() ?? string.Empty,
    }).Trim();

    /// <summary>Textul brut, păstrând spațiile de la început (ISDP indentează denumirile și codurile).</summary>
    public string TextBrut(int rand, int coloana) => Celula(rand, coloana) as string ?? Text(rand, coloana);

    /// <summary>
    /// Valoarea numerică a celulei, ca <see cref="decimal"/>. Conversia double → decimal păstrează
    /// 15 cifre semnificative, ceea ce elimină „zgomotul” binar din Excel (76,58888999999999 → 76,58889).
    /// Textele numerice se acceptă doar în format invariant (exporturile ISDP scriu numere ca numere).
    /// </summary>
    public decimal? Numar(int rand, int coloana) => Celula(rand, coloana) switch
    {
        double d when !double.IsNaN(d) && !double.IsInfinity(d) => (decimal)d,
        decimal m => m,
        int i => i,
        long l => l,
        string s when decimal.TryParse(s.Trim(), NumberStyles.Float, Inv, out var v) => v,
        _ => null,
    };

    public bool EsteGol(int rand, int coloana) => Text(rand, coloana).Length == 0;

    /// <summary>Ultima valoare numerică din rând (folosită la rândurile de recapitulație).</summary>
    public decimal? UltimulNumar(int rand)
    {
        for (var c = NrColoane(rand) - 1; c >= 0; c--)
        {
            if (Numar(rand, c) is { } v)
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>Prima celulă nevidă la dreapta coloanei date (valoarea unei etichete de antet).</summary>
    public string TextDupa(int rand, int coloana)
    {
        for (var c = coloana + 1; c < NrColoane(rand); c++)
        {
            var t = Text(rand, c);
            if (t.Length > 0)
            {
                return t;
            }
        }

        return string.Empty;
    }
}

/// <summary>Normalizarea textelor pentru comparații: litere mici, fără diacritice, spații comasate.</summary>
public static class TextIsdp
{
    public static string Normalizeaza(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        var spatiu = false;
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                spatiu = sb.Length > 0;
                continue;
            }

            if (spatiu)
            {
                sb.Append(' ');
                spatiu = false;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    /// <summary>Unitatea de măsură normalizată (ISDP: „tona”, F3: „tona”, alte surse: „t”).</summary>
    public static string UnitateMasura(string? um) => Normalizeaza(um).TrimEnd('.') switch
    {
        "t" or "to" => "tona",
        "h" or "ore" => "ora",
        "m2" => "mp",
        "m3" => "mc",
        var x => x,
    };
}
