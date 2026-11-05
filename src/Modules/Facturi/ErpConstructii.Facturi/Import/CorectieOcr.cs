using System.Globalization;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Facturi.Import;

/// <summary>Corecturi pentru greșelile tipice ale OCR-ului în sumele facturilor, verificate prin calcul.</summary>
public static class CorectieOcr
{
    /// <summary>
    /// Valoarea liniei corectată, dacă cea citită diferă de cantitate × preț printr-o singură cifră, la aceeași lungime
    /// („217.80” citit, 60 × 3,6267 = 217,60: OCR-ul a confundat 6 cu 8), iar diferența nu se poate explica prin
    /// rotunjirea prețului afișat. Un preț cu cel mult două zecimale („8.3300”) poate fi rotunjit (carburanți:
    /// 70,77 × 8,33 = 589,51, dar valoarea e 589,56); unul cu patru zecimale („3.6267”) nu. Null = fără corectură.
    /// </summary>
    public static decimal? ValoareCorectata(decimal citit, decimal cantitate, decimal pret, string? textPret)
    {
        var calculat = Rotunjire.Lei.Aplica(cantitate * pret);
        var a = Cifre(citit);
        var b = Cifre(calculat);
        if (a.Length != b.Length || a == b || a.Zip(b).Count(p => p.First != p.Second) != 1)
        {
            return null;
        }

        var pasRotunjire = 0.5m * (decimal)Math.Pow(10, -Math.Max(Zecimale(textPret, pret), 2));
        return Math.Abs(citit - cantitate * pret) > Math.Abs(cantitate) * pasRotunjire + 0.005m ? calculat : null;
    }

    /// <summary>Zecimalele semnificative ale prețului afișat („8.3300” → 2, „3.6267” → 4).</summary>
    private static int Zecimale(string? text, decimal pret)
    {
        var t = (text ?? pret.ToString(CultureInfo.InvariantCulture)).Trim();
        var sep = Math.Max(t.LastIndexOf('.'), t.LastIndexOf(','));
        return sep < 0 ? 0 : t[(sep + 1)..].TrimEnd('0').Count(char.IsAsciiDigit);
    }

    private static string Cifre(decimal v) =>
        Math.Abs(Math.Round(v, 2)).ToString("0.00", CultureInfo.InvariantCulture).Replace(".", string.Empty, StringComparison.Ordinal);
}
