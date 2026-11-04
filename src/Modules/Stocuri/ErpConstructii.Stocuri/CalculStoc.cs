using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

/// <summary>Cantitatea și valoarea unui articol într-o gestiune. Prețul mediu (CMP) = valoare / cantitate.</summary>
public readonly record struct Sold(decimal Cantitate, decimal Valoare)
{
    public static readonly Sold Zero = new(0, 0);

    /// <summary>Costul mediu ponderat, la 4 zecimale (pentru afișare; calculele folosesc valoarea totală).</summary>
    public decimal? Cmp => Cantitate > 0 ? Math.Round(Valoare / Cantitate, 4, MidpointRounding.AwayFromZero) : null;
}

/// <summary>
/// Evaluarea stocului la cost mediu ponderat (CMP, decizia D-23): o intrare adaugă cantitatea și valoarea ei;
/// o ieșire pleacă la CMP-ul momentului, rotunjit la ban. Ieșirea întregii cantități ia toată valoarea rămasă, ca
/// să nu rămână bani „orfani” din rotunjiri. Stocul nu poate deveni negativ. Funcții pure.
/// </summary>
public static class CalculStoc
{
    public static Rezultat<Sold> Intrare(Sold sold, decimal cantitate, decimal valoare)
    {
        if (cantitate <= 0)
            return Rezultat.Esuat<Sold>(Eroare.Validare("Cantitatea intrată trebuie să fie pozitivă."));
        if (valoare < 0)
            return Rezultat.Esuat<Sold>(Eroare.Validare("Valoarea intrată nu poate fi negativă."));
        return Rezultat.Ok(new Sold(sold.Cantitate + cantitate, sold.Valoare + Rotunjire.Lei.Aplica(valoare)));
    }

    /// <returns>Soldul rămas și valoarea ieșirii (cantitate × CMP).</returns>
    public static Rezultat<(Sold Ramas, decimal ValoareIesire)> Iesire(Sold sold, decimal cantitate, string articol)
    {
        if (cantitate <= 0)
            return Rezultat.Esuat<(Sold, decimal)>(Eroare.Validare($"Cantitatea pentru „{articol}” trebuie să fie pozitivă."));
        if (cantitate > sold.Cantitate)
            return Rezultat.Esuat<(Sold, decimal)>(Eroare.Validare(
                $"Stoc insuficient pentru „{articol}”: disponibil {sold.Cantitate:0.####}, cerut {cantitate:0.####}."));

        var valoare = cantitate == sold.Cantitate
            ? sold.Valoare
            : Rotunjire.Lei.Aplica(sold.Valoare * cantitate / sold.Cantitate);
        return Rezultat.Ok((new Sold(sold.Cantitate - cantitate, sold.Valoare - valoare), valoare));
    }
}
