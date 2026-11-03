namespace ErpConstructii.SharedKernel;

/// <summary>
/// Regula de rotunjire pentru sume. Toate sumele din sistem sunt <see cref="decimal"/>;
/// niciodată double/float.
///
/// Rotunjirea se aplică EXPLICIT, pe fiecare nivel al calculului (linie de deviz, capitol,
/// recapitulație), pentru că devizele românești se verifică la ban față de programul de
/// devizare din care au fost importate. Regula implicită este „jumătate departe de zero”
/// (2,345 → 2,35), cea folosită uzual în practica contabilă românească.
/// </summary>
public sealed record Rotunjire(int Zecimale, MidpointRounding Mod)
{
    public static readonly Rotunjire Lei = new(2, MidpointRounding.AwayFromZero);
    public static readonly Rotunjire Cantitati = new(3, MidpointRounding.AwayFromZero);
    public static readonly Rotunjire Fara = new(28, MidpointRounding.AwayFromZero);

    public decimal Aplica(decimal valoare) => Math.Round(valoare, Zecimale, Mod);
}
