namespace ErpConstructii.Devize.Analize;

public enum DimensiuneAnaliza
{
    Volum,
    Arie,
    Lungime,
    Masa,
    Timp,
    Bucata,
    Procent,
    Forfetar,
}

public sealed record UnitateAnaliza(DimensiuneAnaliza Dimensiune, decimal FactorBaza);

public static class UnitatiAnaliza
{
    private static readonly IReadOnlyDictionary<string, UnitateAnaliza> Unitati =
        new Dictionary<string, UnitateAnaliza>(StringComparer.OrdinalIgnoreCase)
        {
            ["mc"] = new(DimensiuneAnaliza.Volum, 1),
            ["m3"] = new(DimensiuneAnaliza.Volum, 1),
            ["100 mc"] = new(DimensiuneAnaliza.Volum, 100),
            ["mp"] = new(DimensiuneAnaliza.Arie, 1),
            ["m2"] = new(DimensiuneAnaliza.Arie, 1),
            ["m"] = new(DimensiuneAnaliza.Lungime, 1),
            ["km"] = new(DimensiuneAnaliza.Lungime, 1000),
            ["tona"] = new(DimensiuneAnaliza.Masa, 1),
            ["t"] = new(DimensiuneAnaliza.Masa, 1),
            ["kg"] = new(DimensiuneAnaliza.Masa, 0.001m),
            ["ora"] = new(DimensiuneAnaliza.Timp, 1),
            ["h"] = new(DimensiuneAnaliza.Timp, 1),
            ["buc"] = new(DimensiuneAnaliza.Bucata, 1),
            ["%"] = new(DimensiuneAnaliza.Procent, 1),
            ["forfetar"] = new(DimensiuneAnaliza.Forfetar, 1),
        };

    public static bool Incearca(string unitate, out UnitateAnaliza rezultat) =>
        Unitati.TryGetValue(unitate.Trim(), out rezultat!);
}
