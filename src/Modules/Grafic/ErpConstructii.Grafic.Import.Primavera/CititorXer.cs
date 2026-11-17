using System.Globalization;
using System.Text;

namespace ErpConstructii.Grafic.Import.Primavera;

/// <summary>Un tabel din fișierul XER: rândurile ca dicționare câmp → text.</summary>
public sealed class TabelXer(string nume, IReadOnlyList<string> campuri)
{
    private readonly List<IReadOnlyDictionary<string, string>> _randuri = [];

    public string Nume { get; } = nume;
    public IReadOnlyList<string> Campuri { get; } = campuri;
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Randuri => _randuri;

    internal void Adauga(IReadOnlyList<string> valori)
    {
        var rand = new Dictionary<string, string>(Campuri.Count, StringComparer.Ordinal);
        for (var i = 0; i < Campuri.Count; i++)
            rand[Campuri[i]] = i < valori.Count ? valori[i] : string.Empty;
        _randuri.Add(rand);
    }
}

/// <summary>
/// Conținutul unui export Primavera P6 XER: antetul (<c>ERMHDR</c>) și tabelele. Formatul e text separat prin TAB:
/// <c>%T</c> = tabel, <c>%F</c> = câmpuri, <c>%R</c> = rând, <c>%E</c> = sfârșit. Codificarea e cea Windows a calculatorului
/// care a exportat: se încearcă UTF-8, altfel Windows-1250 (literele românești; literele latine comune sunt aceleași ca în 1252).
/// </summary>
public sealed class FisierXer
{
    private readonly Dictionary<string, TabelXer> _tabele = new(StringComparer.Ordinal);

    private FisierXer(IReadOnlyList<string> antet) => Antet = antet;

    /// <summary>Câmpurile liniei <c>ERMHDR</c>: versiunea, data exportului, …, moneda (ultimul).</summary>
    public IReadOnlyList<string> Antet { get; }

    public string? Versiune => Antet.Count > 1 ? Antet[1] : null;

    /// <summary>Moneda de bază a bazei de date P6 (ultimul câmp al antetului), ex. „EUR”.</summary>
    public string? Moneda => Antet.Count > 0 && Antet[^1].Trim() is { Length: 3 } m && m.All(char.IsAsciiLetterUpper) ? m : null;

    public IReadOnlyCollection<TabelXer> Tabele => _tabele.Values;

    /// <summary>Rândurile tabelului; tabelul lipsă = fără rânduri.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Randuri(string tabel) =>
        _tabele.TryGetValue(tabel, out var t) ? t.Randuri : [];

    public bool Are(string tabel) => _tabele.ContainsKey(tabel);

    public static bool EsteXer(byte[] continut)
    {
        var start = continut.AsSpan();
        if (start.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            start = start[3..];
        return start.StartsWith("ERMHDR"u8);
    }

    /// <summary>Citește fișierul; null dacă nu începe cu antetul XER.</summary>
    public static FisierXer? Citeste(byte[] continut)
    {
        if (!EsteXer(continut))
            return null;
        var text = Decodeaza(continut);
        FisierXer? fisier = null;
        TabelXer? curent = null;
        string? numeTabel = null;
        foreach (var linie in text.Split('\n'))
        {
            var l = linie.TrimEnd('\r');
            if (l.Length == 0)
                continue;
            var parti = l.Split('\t');
            switch (parti[0])
            {
                case "ERMHDR":
                    fisier ??= new FisierXer(parti);
                    break;
                case "%T" when fisier is not null && parti.Length > 1:
                    curent = null;
                    numeTabel = parti[1].Trim();
                    break;
                case "%F" when fisier is not null && numeTabel is not null:
                    // Câmpurile vin imediat după %T. Un tabel repetat (rar) își continuă rândurile.
                    if (!fisier._tabele.TryGetValue(numeTabel, out curent))
                    {
                        curent = new TabelXer(numeTabel, parti[1..].Select(c => c.Trim()).ToArray());
                        fisier._tabele[numeTabel] = curent;
                    }
                    break;
                case "%R":
                    curent?.Adauga(parti[1..]);
                    break;
                case "%E":
                    return fisier;
            }
        }

        return fisier;
    }

    private static string Decodeaza(byte[] continut)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(continut);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250).GetString(continut);
        }
    }

    // ---------------------------------------------------------------- valori

    public static decimal Numar(IReadOnlyDictionary<string, string> rand, string camp) =>
        rand.TryGetValue(camp, out var s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    public static decimal? NumarOptional(IReadOnlyDictionary<string, string> rand, string camp) =>
        rand.TryGetValue(camp, out var s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Datele XER: „2026-11-13 08:30” (ora se ignoră).</summary>
    public static DateOnly? Data(IReadOnlyDictionary<string, string> rand, string camp) =>
        rand.TryGetValue(camp, out var s) && s.Length >= 10
            && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string Text(IReadOnlyDictionary<string, string> rand, string camp) =>
        rand.TryGetValue(camp, out var s) ? s.Trim() : string.Empty;
}
