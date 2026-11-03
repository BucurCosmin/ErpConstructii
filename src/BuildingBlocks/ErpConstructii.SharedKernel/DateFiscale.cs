using System.Numerics;

namespace ErpConstructii.SharedKernel;

/// <summary>
/// Adresă structurată, cum o cere e-Factura (UBL CIUS-RO: stradă, localitate, cod județ ISO 3166-2, cod poștal, țară).
/// Folosită pentru firmă, parteneri, șantiere.
/// </summary>
/// <param name="Judet">Codul ISO 3166-2 fără prefixul țării (ex. „CT”, „B”); null pentru adrese din străinătate.</param>
/// <param name="Tara">Codul ISO 3166-1 alpha-2 (ex. „RO”).</param>
public sealed record Adresa(string Strada, string Localitate, string? Judet, string? CodPostal, string Tara = "RO")
{
    public static readonly Adresa Goala = new(string.Empty, string.Empty, null, null);

    /// <summary>Adresa pe un rând, pentru documente (ex. „str. Unirii 1, 905200 Cernavodă, jud. Constanța”).</summary>
    public string Formatata()
    {
        var parti = new List<string>();
        if (!string.IsNullOrWhiteSpace(Strada))
        {
            parti.Add(Strada.Trim());
        }

        var localitate = string.Join(' ', new[] { CodPostal, Localitate }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        if (localitate.Length > 0)
        {
            parti.Add(localitate);
        }

        if (Judete.Denumire(Judet) is { } judet)
        {
            // București nu are „jud.”; nu se repetă dacă localitatea e chiar București (altfel: „Sector 3, București”).
            if (Judet != "B")
            {
                parti.Add($"jud. {judet}");
            }
            else if (!Localitate.Trim().StartsWith(judet, StringComparison.OrdinalIgnoreCase))
            {
                parti.Add(judet);
            }
        }

        if (!string.IsNullOrWhiteSpace(Tara) && Tara != "RO")
        {
            parti.Add(Tara);
        }

        return string.Join(", ", parti);
    }
}

/// <summary>Cont bancar al firmei sau al unui partener.</summary>
/// <param name="Principal">Contul trecut implicit pe documente (comenzi, facturi).</param>
public sealed record ContBancar(string Banca, string Iban, string Moneda = "RON", bool Principal = false);

/// <summary>Județele României cu codurile ISO 3166-2 folosite în e-Factura (CountrySubentity „RO-XX”).</summary>
public static class Judete
{
    public static readonly IReadOnlyDictionary<string, string> Toate = new Dictionary<string, string>
    {
        ["AB"] = "Alba", ["AR"] = "Arad", ["AG"] = "Argeș", ["BC"] = "Bacău", ["BH"] = "Bihor",
        ["BN"] = "Bistrița-Năsăud", ["BT"] = "Botoșani", ["BV"] = "Brașov", ["BR"] = "Brăila", ["B"] = "București",
        ["BZ"] = "Buzău", ["CS"] = "Caraș-Severin", ["CL"] = "Călărași", ["CJ"] = "Cluj", ["CT"] = "Constanța",
        ["CV"] = "Covasna", ["DB"] = "Dâmbovița", ["DJ"] = "Dolj", ["GL"] = "Galați", ["GR"] = "Giurgiu",
        ["GJ"] = "Gorj", ["HR"] = "Harghita", ["HD"] = "Hunedoara", ["IL"] = "Ialomița", ["IS"] = "Iași",
        ["IF"] = "Ilfov", ["MM"] = "Maramureș", ["MH"] = "Mehedinți", ["MS"] = "Mureș", ["NT"] = "Neamț",
        ["OT"] = "Olt", ["PH"] = "Prahova", ["SM"] = "Satu Mare", ["SJ"] = "Sălaj", ["SB"] = "Sibiu",
        ["SV"] = "Suceava", ["TR"] = "Teleorman", ["TM"] = "Timiș", ["TL"] = "Tulcea", ["VS"] = "Vaslui",
        ["VL"] = "Vâlcea", ["VN"] = "Vrancea",
    };

    public static string? Denumire(string? cod) => cod is not null && Toate.TryGetValue(cod, out var d) ? d : null;
}

/// <summary>Validări pentru identificatorii fiscali și bancari (cifre de control).</summary>
public static class ValidareFiscala
{
    private static readonly int[] CheieCui = [7, 5, 3, 2, 1, 7, 5, 3, 2];

    /// <summary>
    /// CUI/CIF fără prefixul „RO” și fără spații (ex. „RO 18547290” → „18547290”). Nu validează.
    /// </summary>
    public static string NormalizeazaCui(string cui)
    {
        var s = new string(cui.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return s.StartsWith("RO", StringComparison.Ordinal) ? s[2..] : s;
    }

    /// <summary>
    /// Verifică cifra de control a CUI-ului (algoritmul ANAF, cheia 753217532): cifrele fără cea de control,
    /// completate la stânga cu zero până la 9, se înmulțesc cu cheia; suma × 10 modulo 11 (10 → 0) = cifra de control.
    /// </summary>
    public static bool EsteCuiValid(string cui)
    {
        var s = NormalizeazaCui(cui);
        if (s.Length is < 2 or > 10 || !s.All(char.IsAsciiDigit))
        {
            return false;
        }

        var corp = s[..^1].PadLeft(9, '0');
        var suma = corp.Select((c, i) => (c - '0') * CheieCui[i]).Sum();
        var control = suma * 10 % 11;
        return (control == 10 ? 0 : control) == s[^1] - '0';
    }

    /// <summary>IBAN fără spații, cu majuscule. Nu validează.</summary>
    public static string NormalizeazaIban(string iban) =>
        new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    /// <summary>
    /// Verifică IBAN-ul (ISO 13616): primele 4 caractere se mută la final, literele devin numere (A=10 … Z=35),
    /// iar numărul rezultat modulo 97 trebuie să fie 1. Pentru RO, lungimea este 24.
    /// </summary>
    public static bool EsteIbanValid(string iban)
    {
        var s = NormalizeazaIban(iban);
        if (s.Length is < 15 or > 34 || !s.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(s[0]) || !char.IsAsciiLetter(s[1]))
        {
            return false;
        }

        if (s.StartsWith("RO", StringComparison.Ordinal) && s.Length != 24)
        {
            return false;
        }

        var rearanjat = s[4..] + s[..4];
        var cifre = string.Concat(rearanjat.Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()));
        return BigInteger.Parse(cifre) % 97 == 1;
    }

    /// <summary>
    /// Problemele de identificare ale unei firme sau ale unui partener: CUI cu cifră de control greșită,
    /// IBAN invalid, mai multe conturi principale în aceeași monedă. Listă goală = valid.
    /// </summary>
    public static IReadOnlyList<string> Probleme(string? cui, IEnumerable<ContBancar> conturi)
    {
        var probleme = new List<string>();
        if (!string.IsNullOrWhiteSpace(cui) && !EsteCuiValid(cui))
        {
            probleme.Add($"CUI-ul „{cui}” nu este valid (cifra de control nu corespunde).");
        }

        var lista = conturi.ToList();
        foreach (var c in lista)
        {
            if (string.IsNullOrWhiteSpace(c.Banca))
            {
                probleme.Add($"Contul {c.Iban} nu are banca completată.");
            }

            if (!EsteIbanValid(c.Iban))
            {
                probleme.Add($"IBAN-ul „{c.Iban}” nu este valid.");
            }
        }

        foreach (var g in lista.Where(c => c.Principal).GroupBy(c => c.Moneda).Where(g => g.Count() > 1))
        {
            probleme.Add($"Poate exista un singur cont principal în {g.Key}.");
        }

        return probleme;
    }
}
