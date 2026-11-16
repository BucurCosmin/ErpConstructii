using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ErpConstructii.SharedKernel;

/// <summary>
/// O foaie de calcul ca grilă de texte (rând × coloană, golurile = null), neutră față de formatul fișierului: interpretorii
/// (logică pură) o citesc fără să știe de Excel. Numerele vin în forma invariantă din fișier („25.5”); textele cum au fost
/// scrise („175.063,26”). Valorile formulelor sunt cele salvate în fișier.
/// </summary>
public sealed record FoaieTabel(string Nume, IReadOnlyList<IReadOnlyList<string?>> Randuri)
{
    public string? Celula(int rand, int coloana) =>
        rand >= 0 && rand < Randuri.Count && coloana >= 0 && coloana < Randuri[rand].Count && !string.IsNullOrWhiteSpace(Randuri[rand][coloana])
            ? Randuri[rand][coloana]!.Trim()
            : null;
}

/// <summary>Citirea valorilor din foi: numere (invariante sau scrise românește), date, procente, chei de comparare.</summary>
public static partial class ValoriFoaie
{
    /// <summary>Textul fără diacritice, cu litere mici și spații simple (pentru recunoașterea etichetelor).</summary>
    public static string Cheie(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var fd = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(fd.Length);
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(ch) || ch is '%' or '.' or '/' ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Numărul din celulă: forma invariantă a fișierului („2864813.83”) sau scrierea românească („175.063,26 €”,
    /// „1.860.243,64”). Null dacă nu e un număr.
    /// </summary>
    public static decimal? Numar(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var t = text.Trim();
        // Forma din fișier e un double cu 17 cifre („132085.17000000001”): se ia cu 15 cifre semnificative, cum o arată Excel.
        if (Invariant().IsMatch(t))
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && !double.IsInfinity(x) && Math.Abs(x) < 7.9e27
                ? (decimal)x
                : null;
        var m = NumarScris().Match(t.Replace(" ", string.Empty).Replace(" ", string.Empty));
        if (!m.Success)
            return null;
        var s = m.Value.TrimEnd('.', ',');
        // Cu virgulă: românesc (punctul = mii). Fără virgulă: punctele sunt mii dacă sunt mai multe sau urmate de 3 cifre.
        s = s.Contains(',') ? s.Replace(".", string.Empty).Replace(',', '.')
            : s.Count(c => c == '.') > 1 || Mii().IsMatch(s) ? s.Replace(".", string.Empty) : s;
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Prima dată din text (zz.ll.aaaa) sau data Excel ca număr de serie (valori plauzibile 2000–2100).</summary>
    public static DateOnly? Data(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        foreach (Match m in DataScrisa().Matches(text))
        {
            var zi = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var luna = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var an = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (an is >= 2000 and <= 2100 && luna is >= 1 and <= 12 && zi >= 1 && zi <= DateTime.DaysInMonth(an, luna))
                return new DateOnly(an, luna, zi);
        }

        var t = text.Trim();
        if (Invariant().IsMatch(t) && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var serie) && serie is > 36526 and < 73051)
            return DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serie)));
        return null;
    }

    /// <summary>Toate datele din text (ex. „01.05.2026 – 07.10.2026”).</summary>
    public static IReadOnlyList<DateOnly> Date(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : DataScrisa().Matches(text).Select(m => Data(m.Value)).Where(d => d is not null).Select(d => d!.Value).ToList();

    /// <summary>Procentele din text, ca fracții („5% + 10% + 5%” → 0,05; 0,10; 0,05; „0,13%/zi” → 0,0013).</summary>
    public static IReadOnlyList<decimal> Procente(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : Procent().Matches(text)
            .Select(m => decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p / 100 : -1)
            .Where(p => p >= 0).ToList();

    [GeneratedRegex(@"^-?\d+(\.\d+)?([eE]-?\d+)?$")]
    private static partial Regex Invariant();

    [GeneratedRegex(@"-?\d[\d.,]*")]
    private static partial Regex NumarScris();

    [GeneratedRegex(@"\.\d{3}$")]
    private static partial Regex Mii();

    [GeneratedRegex(@"\b(\d{1,2})\.(\d{1,2})\.(\d{4})\b")]
    private static partial Regex DataScrisa();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*%")]
    private static partial Regex Procent();
}
