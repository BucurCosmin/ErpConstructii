using System.Security.Cryptography;

namespace ErpConstructii.Identitate;

/// <summary>
/// Diagnosticul codurilor din aplicația de autentificare (TOTP, RFC 6238: HMAC-SHA1, pas de 30 s, 6 cifre, ca ASP.NET
/// Identity). Identity acceptă doar ±2 pași (±1 min) față de ceasul serverului; un cod refuzat care ar fi fost corect cu
/// un decalaj mai mare arată că ceasul serverului (sau al telefonului) nu e sincronizat. Doar pentru mesaje: nu acceptă
/// codul. Funcții pure.
/// </summary>
public static class DecalajCeasTotp
{
    public const int PasSecunde = 30;

    /// <summary>Decalajul maxim căutat: 12 ore în fiecare sens (ceas greșit cu ore, inclusiv fusul orar).</summary>
    public const int PasiMaximi = 12 * 60 * 2;

    /// <summary>Codul de 6 cifre pentru cheia Base32 (cum o afișează aplicația) la momentul dat.</summary>
    public static string? Cod(string cheieBase32, DateTimeOffset moment)
    {
        var cheie = DinBase32(cheieBase32);
        return cheie is null ? null : Cod(cheie, moment.ToUnixTimeSeconds() / PasSecunde);
    }

    /// <summary>
    /// Câte secunde ar trebui adăugate ceasului serverului ca <paramref name="cod"/> să fie corect (pozitiv = serverul e în
    /// urmă); null dacă codul nu se potrivește nicăieri în ±12 ore (cod greșit, nu ceas greșit). Se caută întâi cel mai mic decalaj.
    /// </summary>
    public static int? Decalaj(string cheieBase32, string cod, DateTimeOffset acumServer)
    {
        var cheie = DinBase32(cheieBase32);
        cod = cod.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (cheie is null || cod.Length != 6 || !cod.All(char.IsAsciiDigit))
            return null;

        var pas = acumServer.ToUnixTimeSeconds() / PasSecunde;
        for (var i = 0; i <= PasiMaximi; i++)
        {
            if (Cod(cheie, pas + i) == cod)
                return i * PasSecunde;
            if (i > 0 && Cod(cheie, pas - i) == cod)
                return -i * PasSecunde;
        }

        return null;
    }

    private static string Cod(byte[] cheie, long pas)
    {
        Span<byte> mesaj = stackalloc byte[8];
        for (var i = 7; i >= 0; i--, pas >>= 8)
            mesaj[i] = (byte)(pas & 0xFF);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(cheie, mesaj, hash);
        var o = hash[^1] & 0x0F;
        var valoare = ((hash[o] & 0x7F) << 24) | (hash[o + 1] << 16) | (hash[o + 2] << 8) | hash[o + 3];
        return (valoare % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[]? DinBase32(string text)
    {
        const string Alfabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var curat = text.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        if (curat.Length == 0)
            return null;
        var rezultat = new List<byte>(curat.Length * 5 / 8);
        int buffer = 0, biti = 0;
        foreach (var c in curat)
        {
            var v = Alfabet.IndexOf(c, StringComparison.Ordinal);
            if (v < 0)
                return null;
            buffer = (buffer << 5) | v;
            biti += 5;
            if (biti >= 8)
            {
                rezultat.Add((byte)(buffer >> (biti - 8)));
                biti -= 8;
            }
        }

        return [.. rezultat];
    }
}
