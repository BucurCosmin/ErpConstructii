using ErpConstructii.SharedKernel;

namespace ErpConstructii.Devize.Calcul;

/// <summary>Cele patru tipuri de resurse din devizele românești.</summary>
public enum TipResursa
{
    Material,
    Manopera,
    Utilaj,
    Transport,
}

/// <summary>
/// O valoare descompusă pe cele patru resurse (material, manoperă, utilaj, transport).
/// Folosită atât pentru prețuri unitare, cât și pentru valori totale.
/// </summary>
public readonly record struct ValoriResurse(decimal Material, decimal Manopera, decimal Utilaj, decimal Transport)
{
    public static readonly ValoriResurse Zero = new(0, 0, 0, 0);

    public decimal Total => Material + Manopera + Utilaj + Transport;

    public decimal this[TipResursa tip] => tip switch
    {
        TipResursa.Material => Material,
        TipResursa.Manopera => Manopera,
        TipResursa.Utilaj => Utilaj,
        TipResursa.Transport => Transport,
        _ => throw new ArgumentOutOfRangeException(nameof(tip)),
    };

    public static ValoriResurse operator +(ValoriResurse a, ValoriResurse b) =>
        new(a.Material + b.Material, a.Manopera + b.Manopera, a.Utilaj + b.Utilaj, a.Transport + b.Transport);

    public static ValoriResurse operator *(ValoriResurse a, decimal factor) =>
        new(a.Material * factor, a.Manopera * factor, a.Utilaj * factor, a.Transport * factor);

    public ValoriResurse Rotunjeste(Rotunjire r) =>
        new(r.Aplica(Material), r.Aplica(Manopera), r.Aplica(Utilaj), r.Aplica(Transport));

    public static ValoriResurse Pentru(TipResursa tip, decimal valoare) => tip switch
    {
        TipResursa.Material => new(valoare, 0, 0, 0),
        TipResursa.Manopera => new(0, valoare, 0, 0),
        TipResursa.Utilaj => new(0, 0, valoare, 0),
        TipResursa.Transport => new(0, 0, 0, valoare),
        _ => throw new ArgumentOutOfRangeException(nameof(tip)),
    };
}
