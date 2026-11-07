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

    /// <summary>
    /// Stornarea unei intrări: iese exact cantitatea și valoarea intrate (nu la CMP). Dacă nu rămâne nimic din articol, ia
    /// toată valoarea rămasă; valoarea nu poate depăși soldul (s-ar ajunge la valoare negativă).
    /// </summary>
    public static Rezultat<Sold> Storno(Sold sold, decimal cantitate, decimal valoare, string articol)
    {
        if (cantitate > sold.Cantitate)
            return Rezultat.Esuat<Sold>(Eroare.Validare(
                $"„{articol}”: în gestiune au rămas {sold.Cantitate:0.####} din {cantitate:0.####} recepționate (restul a fost transferat sau consumat). Fă întâi returul în depozit."));
        if (cantitate == sold.Cantitate)
            return Rezultat.Ok(Sold.Zero);
        if (valoare > sold.Valoare)
            return Rezultat.Esuat<Sold>(Eroare.Validare($"„{articol}”: valoarea de stornat ({valoare:N2}) depășește valoarea din stoc ({sold.Valoare:N2})."));
        return Rezultat.Ok(new Sold(sold.Cantitate - cantitate, sold.Valoare - valoare));
    }

    /// <summary>
    /// Corectura unei recepții (D-35): soldul primește diferența de cantitate și de valoare față de NIR-ul inițial. Diferența
    /// negativă trebuie să mai fie în gestiune; o schimbare de preț cere material rămas în gestiune (altfel valoarea n-ar avea
    /// pe ce să stea). Dacă nu mai rămâne nimic din articol, soldul devine zero.
    /// </summary>
    public static Rezultat<Sold> Corectie(Sold sold, decimal diferentaCantitate, decimal diferentaValoare, string articol)
    {
        if (sold.Cantitate == 0 && diferentaCantitate == 0 && diferentaValoare != 0)
            return Rezultat.Esuat<Sold>(Eroare.Validare($"„{articol}”: nu mai e nimic în gestiune pe care să se corecteze prețul (a ieșit la prețul vechi). Fă întâi returul în depozit."));
        var cantitate = sold.Cantitate + diferentaCantitate;
        if (cantitate < 0)
            return Rezultat.Esuat<Sold>(Eroare.Validare(
                $"„{articol}”: în gestiune au rămas {sold.Cantitate:0.####}, iar corectura scoate {-diferentaCantitate:0.####} (restul a fost transferat sau consumat). Fă întâi returul în depozit."));
        if (cantitate == 0)
            return Rezultat.Ok(Sold.Zero);
        var valoare = sold.Valoare + Rotunjire.Lei.Aplica(diferentaValoare);
        if (valoare < 0)
            return Rezultat.Esuat<Sold>(Eroare.Validare(
                $"„{articol}”: corectura scade valoarea cu {-diferentaValoare:N2}, dar în gestiune a rămas doar {sold.Valoare:N2} (restul a ieșit la prețul vechi)."));
        return Rezultat.Ok(new Sold(cantitate, valoare));
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
