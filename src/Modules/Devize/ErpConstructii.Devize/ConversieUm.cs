namespace ErpConstructii.Devize;

/// <summary>
/// Conversia dintre unitatea de măsură a resursei din deviz și a materialului din stoc (D-55): „1 UM resursă = factor ×
/// UM stoc” (ex. resursa în „to”, materialul în „kg” → 1000). Cantitatea în stoc = cantitatea resursei × factor; prețul
/// resursei = prețul din stoc × factor. Recunoaște scrierile uzuale ale aceleiași unități și perechile cu factor fix;
/// pentru restul (ex. mc ↔ to, care depinde de densitate) factorul îl dă utilizatorul. Logică pură.
/// </summary>
public static class ConversieUm
{
    /// <summary>Scrierile uzuale → forma canonică.</summary>
    private static readonly Dictionary<string, string> Sinonime = new(StringComparer.Ordinal)
    {
        ["t"] = "to", ["to"] = "to", ["tona"] = "to", ["tone"] = "to", ["tn"] = "to",
        ["kg"] = "kg", ["kgr"] = "kg", ["g"] = "g", ["gr"] = "g",
        ["mc"] = "mc", ["m3"] = "mc", ["m³"] = "mc", ["mp"] = "mp", ["m2"] = "mp", ["m²"] = "mp",
        ["m"] = "m", ["ml"] = "m", ["metru"] = "m", ["km"] = "km",
        ["l"] = "l", ["litru"] = "l", ["litri"] = "l", ["dmc"] = "l", ["dm3"] = "l",
        ["buc"] = "buc", ["bucata"] = "buc", ["bucati"] = "buc", ["bc"] = "buc", ["set"] = "set",
        ["ora"] = "ora", ["ore"] = "ora", ["h"] = "ora",
    };

    /// <summary>Perechile cu factor fix: (din, în) → factor.</summary>
    private static readonly Dictionary<(string, string), decimal> Factori = new()
    {
        [("to", "kg")] = 1000m, [("kg", "to")] = 0.001m, [("kg", "g")] = 1000m, [("g", "kg")] = 0.001m,
        [("mc", "l")] = 1000m, [("l", "mc")] = 0.001m, [("km", "m")] = 1000m, [("m", "km")] = 0.001m,
    };

    public static string Canonica(string? um)
    {
        var s = new string((um ?? string.Empty).Trim().ToLowerInvariant().Where(ch => !char.IsWhiteSpace(ch) && ch != '.').ToArray());
        return Sinonime.TryGetValue(s, out var c) ? c : s;
    }

    /// <summary>Aceeași unitate, cu scrieri diferite (ex. „MC” și „m3”).</summary>
    public static bool Aceeasi(string? a, string? b) => Canonica(a) == Canonica(b);

    /// <summary>Factorul propus: 1 pentru aceeași unitate, cel fix pentru perechile cunoscute, altfel null (îl dă utilizatorul).</summary>
    public static decimal? Propune(string? umResursa, string? umStoc)
    {
        var (a, b) = (Canonica(umResursa), Canonica(umStoc));
        if (a.Length == 0 || b.Length == 0)
            return null;
        return a == b ? 1m : Factori.TryGetValue((a, b), out var f) ? f : null;
    }
}
