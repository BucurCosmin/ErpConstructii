namespace ErpConstructii.Facturi;

public sealed record LinieComanda(string CodArticol, decimal Cantitate, decimal PretUnitar);

public sealed record LinieReceptie(string CodArticol, decimal Cantitate);

public sealed record LinieFacturaPotrivire(string CodArticol, decimal Cantitate, decimal PretUnitar);

/// <summary>Toleranțe configurabile per firmă (ex. 0,02 = 2%).</summary>
public sealed record TolerantePotrivire(decimal ProcentPret, decimal ProcentCantitate);

public enum TipDiferenta
{
    ArticolNecomandat,
    ArticolNereceptionat,
    CantitatePesteReceptie,
    PretPesteComanda,
    PretSubComanda,
}

public sealed record DiferentaPotrivire(string CodArticol, TipDiferenta Tip, string Detalii);

/// <summary>
/// Potrivirea în 3 puncte comandă – recepție (NIR) – factură (REQ-FCT-09).
/// Diferențele peste toleranță merg la achizitor; factura nu se aprobă automat.
/// Liniile se compară pe codul articolului, agregat (o factură poate grupa mai multe recepții).
/// </summary>
public static class PotrivireTreiPuncte
{
    public static IReadOnlyList<DiferentaPotrivire> Compara(
        IReadOnlyList<LinieComanda> comanda,
        IReadOnlyList<LinieReceptie> receptii,
        IReadOnlyList<LinieFacturaPotrivire> factura,
        TolerantePotrivire toleranta)
    {
        var diferente = new List<DiferentaPotrivire>();
        var comandat = comanda.GroupBy(l => l.CodArticol)
            .ToDictionary(g => g.Key, g => (Cantitate: g.Sum(l => l.Cantitate), Pret: g.Max(l => l.PretUnitar)));
        var receptionat = receptii.GroupBy(l => l.CodArticol)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Cantitate));

        foreach (var g in factura.GroupBy(l => l.CodArticol))
        {
            var cod = g.Key;
            var cantitateFacturata = g.Sum(l => l.Cantitate);
            var pretFacturat = g.Max(l => l.PretUnitar);

            if (!comandat.TryGetValue(cod, out var c))
            {
                diferente.Add(new(cod, TipDiferenta.ArticolNecomandat, "Articolul facturat nu apare în comandă."));
                continue;
            }

            if (!receptionat.TryGetValue(cod, out var cantitateReceptionata))
            {
                diferente.Add(new(cod, TipDiferenta.ArticolNereceptionat, "Articolul facturat nu are recepție (NIR)."));
            }
            else if (cantitateFacturata > cantitateReceptionata * (1 + toleranta.ProcentCantitate))
            {
                diferente.Add(new(cod, TipDiferenta.CantitatePesteReceptie,
                    $"Facturat {cantitateFacturata}, recepționat {cantitateReceptionata}."));
            }

            if (pretFacturat > c.Pret * (1 + toleranta.ProcentPret))
            {
                diferente.Add(new(cod, TipDiferenta.PretPesteComanda, $"Preț facturat {pretFacturat}, în comandă {c.Pret}."));
            }
            else if (pretFacturat < c.Pret * (1 - toleranta.ProcentPret))
            {
                // Nu blochează plata, dar merită semnalat (posibilă eroare de cod sau de UM).
                diferente.Add(new(cod, TipDiferenta.PretSubComanda, $"Preț facturat {pretFacturat}, în comandă {c.Pret}."));
            }
        }

        return diferente;
    }
}
