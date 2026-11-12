using ErpConstructii.SharedKernel;

namespace ErpConstructii.Devize.Calcul;

/// <summary>
/// Tipurile de resurse din devizele românești: cele patru clasice plus echipamentele, pe care ISDP le are ca a cincea
/// coloană (D-50, docs/import-isdp.md).
/// </summary>
public enum TipResursa
{
    Material,
    Manopera,
    Utilaj,
    Transport,
    Echipament,
}

/// <summary>
/// O valoare descompusă pe resurse (material, manoperă, utilaj, transport, echipament).
/// Folosită atât pentru prețuri unitare, cât și pentru valori totale. Echipamentul lipsește din devizele vechi (0).
/// </summary>
public readonly record struct ValoriResurse(decimal Material, decimal Manopera, decimal Utilaj, decimal Transport, decimal Echipament = 0)
{
    public static readonly ValoriResurse Zero = new(0, 0, 0, 0, 0);

    public decimal Total => Material + Manopera + Utilaj + Transport + Echipament;

    public decimal this[TipResursa tip] => tip switch
    {
        TipResursa.Material => Material,
        TipResursa.Manopera => Manopera,
        TipResursa.Utilaj => Utilaj,
        TipResursa.Transport => Transport,
        TipResursa.Echipament => Echipament,
        _ => throw new ArgumentOutOfRangeException(nameof(tip)),
    };

    public static ValoriResurse operator +(ValoriResurse a, ValoriResurse b) =>
        new(a.Material + b.Material, a.Manopera + b.Manopera, a.Utilaj + b.Utilaj, a.Transport + b.Transport, a.Echipament + b.Echipament);

    public static ValoriResurse operator *(ValoriResurse a, decimal factor) =>
        new(a.Material * factor, a.Manopera * factor, a.Utilaj * factor, a.Transport * factor, a.Echipament * factor);

    public ValoriResurse Rotunjeste(Rotunjire r) =>
        new(r.Aplica(Material), r.Aplica(Manopera), r.Aplica(Utilaj), r.Aplica(Transport), r.Aplica(Echipament));

    public static ValoriResurse Pentru(TipResursa tip, decimal valoare) => tip switch
    {
        TipResursa.Material => new(valoare, 0, 0, 0),
        TipResursa.Manopera => new(0, valoare, 0, 0),
        TipResursa.Utilaj => new(0, 0, valoare, 0),
        TipResursa.Transport => new(0, 0, 0, valoare),
        TipResursa.Echipament => new(0, 0, 0, 0, valoare),
        _ => throw new ArgumentOutOfRangeException(nameof(tip)),
    };
}
