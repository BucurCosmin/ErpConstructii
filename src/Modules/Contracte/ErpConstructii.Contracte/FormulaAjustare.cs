using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte;

/// <summary>Un termen al formulei: o pondere aplicată raportului dintre indicele curent și cel de referință.</summary>
/// <param name="CodIndice">Codul indicelui INS (ex. indicele costului în construcții – materiale).</param>
public sealed record TermenAjustare(string CodIndice, decimal Pondere, decimal IndiceReferinta);

/// <summary>
/// Formula de ajustare a prețului, configurată per contract (REQ-CTR-03):
///
///     Valoare ajustată = Valoare × [ a + Σ bᵢ × (Iᵢ curent / Iᵢ referință) ]
///
/// unde a = partea fixă (neajustabilă), bᵢ = ponderile, a + Σ bᵢ = 1.
///
/// Pentru contractele publice formula trebuie să fie complet determinată la semnare; de aceea,
/// după aprobarea contractului formula se îngheață (nu se mai editează, doar se aplică).
/// </summary>
public sealed record FormulaAjustare(decimal ParteFixa, IReadOnlyList<TermenAjustare> Termeni)
{
    public Rezultat Valideaza()
    {
        var suma = ParteFixa + Termeni.Sum(t => t.Pondere);
        if (Math.Abs(suma - 1m) > 0.0001m)
        {
            return Rezultat.Esuat(Eroare.Validare($"Partea fixă plus ponderile trebuie să dea 1 (acum {suma})."));
        }

        if (Termeni.Any(t => t.IndiceReferinta <= 0))
        {
            return Rezultat.Esuat(Eroare.Validare("Indicii de referință trebuie să fie pozitivi."));
        }

        if (Termeni.Select(t => t.CodIndice).Distinct().Count() != Termeni.Count)
        {
            return Rezultat.Esuat(Eroare.Validare("Un indice apare de două ori în formulă."));
        }

        return Rezultat.Ok();
    }

    /// <summary>
    /// Calculează coeficientul de ajustare pentru indicii curenți. Lipsa unui indice curent
    /// (INS nu l-a publicat încă) este eroare: se folosesc ultimii indici disponibili și se
    /// recalculează ulterior, dar decizia o ia utilizatorul, nu codul.
    /// </summary>
    public Rezultat<decimal> Coeficient(IReadOnlyDictionary<string, decimal> indiciCurenti)
    {
        var validare = Valideaza();
        if (validare.Esec)
        {
            return Rezultat.Esuat<decimal>(validare.Eroare);
        }

        var coeficient = ParteFixa;
        foreach (var t in Termeni)
        {
            if (!indiciCurenti.TryGetValue(t.CodIndice, out var curent))
            {
                return Rezultat.Esuat<decimal>(Eroare.Validare($"Lipsește indicele curent {t.CodIndice}."));
            }

            coeficient += t.Pondere * curent / t.IndiceReferinta;
        }

        return Rezultat.Ok(coeficient);
    }

    /// <summary>Diferența de ajustat pentru o valoare (intră în <see cref="IntrariSituatie.Ajustare"/>).</summary>
    public Rezultat<decimal> Diferenta(decimal valoare, IReadOnlyDictionary<string, decimal> indiciCurenti, Rotunjire? rotunjire = null)
    {
        var c = Coeficient(indiciCurenti);
        return c.Esec
            ? c
            : Rezultat.Ok((rotunjire ?? Rotunjire.Lei).Aplica(valoare * (c.Valoare - 1m)));
    }
}
