using System.Globalization;

namespace ErpConstructii.SharedKernel.Import;

/// <summary>Cât de sigur e sistemul de valoarea citită dintr-un document extern.</summary>
public enum Certitudine
{
    /// <summary>Citit clar (din Word sau OCR cu încredere mare și valoare validă).</summary>
    Sigur,

    /// <summary>Citit, dar trebuie verificat (OCR neclar, valoare corectată, unitate necunoscută).</summary>
    Nesigur,

    /// <summary>Nu a fost găsit sau e necompletat în document: utilizatorul îl completează.</summary>
    Lipsa,
}

/// <param name="TextSursa">Textul exact din document, arătat utilizatorului lângă valoarea propusă.</param>
public sealed record CampCitit<T>(T? Valoare, Certitudine Certitudine, string? TextSursa = null)
{
    public static CampCitit<T> Lipsa(string? textSursa = null) => new(default, Certitudine.Lipsa, textSursa);
}

/// <summary>Citirea numerelor scrise în documente (format românesc „1.234,56” sau englezesc „1,234.56”).</summary>
public static class TextNumeric
{
    /// <summary>
    /// Separatorul zecimal e ultimul dintre „,” și „.”; celălalt e separator de mii. Un singur separator urmat de exact
    /// trei cifre („1.250”, „1,250”) e ambiguu: se tratează ca separator de mii doar dacă <paramref name="miiImplicit"/>.
    /// Acceptă semnul minus (storno) și spații între grupe.
    /// </summary>
    public static bool ParseazaNumar(string? text, out decimal valoare, bool miiImplicit = false)
    {
        valoare = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\u00a0", string.Empty, StringComparison.Ordinal);
        var negativ = t.StartsWith('-') || (t.StartsWith('(') && t.EndsWith(')'));
        t = t.Trim('-', '(', ')', '+');
        if (t.Length == 0 || !t.All(c => char.IsAsciiDigit(c) || c is ',' or '.'))
        {
            return false;
        }

        var virgula = t.LastIndexOf(',');
        var punct = t.LastIndexOf('.');
        if (virgula >= 0 && punct >= 0)
        {
            t = virgula > punct
                ? t.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : t.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (virgula >= 0 || punct >= 0)
        {
            var sep = virgula >= 0 ? ',' : '.';
            var aparitii = t.Count(c => c == sep);
            var dupa = t.Length - t.LastIndexOf(sep) - 1;
            if (aparitii > 1 || (miiImplicit && dupa == 3))
            {
                t = t.Replace(sep.ToString(), string.Empty, StringComparison.Ordinal);
            }
            else
            {
                t = t.Replace(sep, '.');
            }
        }

        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valoare))
        {
            return false;
        }

        if (negativ)
        {
            valoare = -valoare;
        }

        return true;
    }
}
