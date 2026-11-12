namespace ErpConstructii.Devize.Import.Isdp;

/// <summary>Parametrii prețului de ofertă (procente ca în ISDP: 7 = 7%). Curs = lei pentru 1 unitate din moneda ofertei.</summary>
public sealed record ParametriOferta(decimal ProcentCam, decimal ProcentIndirecte, decimal ProcentProfit, decimal Curs, string Moneda)
{
    public static ParametriOferta Din(RecapitulatieIsdp r, decimal curs, string moneda) =>
        new(r.ProcentCam ?? 0, r.ProcentIndirecte ?? 0, r.ProcentProfit ?? 0, curs, moneda);
}

/// <summary>Prețul de ofertă al unui articol, pe componente (în moneda ofertei).</summary>
public sealed record PretOfertaArticol(
    string Wbs,
    IReadOnlyDictionary<TipResursaIsdp, (decimal PretUnitar, decimal Total)> Componente)
{
    public decimal PretUnitar => Componente.Values.Sum(c => c.PretUnitar);
    public decimal Total => Componente.Values.Sum(c => c.Total);
}

/// <summary>
/// Prețul de ofertă către beneficiar, ca în formularul F3 generat de ISDP (verificat pe fișierul real: 0 diferențe).
///
///   factor(manoperă)        = (1 + %CAM) × (1 + %indirecte) × (1 + %profit)
///   factor(celelalte)       = (1 + %indirecte) × (1 + %profit)
///   preț unitar componentă  = rotunjit₂( preț unitar lei × factor / curs )
///   total componentă        = rotunjit₂( valoare lei × factor / curs )      ← NU cantitate × preț rotunjit
///   preț / total articol    = suma componentelor rotunjite
/// Rotunjire: jumătate departe de zero, 2 zecimale.
///
/// DE CE CONTEAZĂ: situațiile de lucrări se fac la prețurile de ofertă/contract (acestea), iar controlul
/// costurilor la costurile directe din deviz. ERP-ul trebuie să le păstreze pe amândouă pe articol.
/// ATENȚIE: factorul pentru echipamente nu a putut fi verificat (coloana e zero în exemple) — presupus ca la material.
/// </summary>
public static class PretOferta
{
    public static decimal Factor(TipResursaIsdp tip, ParametriOferta p)
    {
        var f = (1 + p.ProcentIndirecte / 100m) * (1 + p.ProcentProfit / 100m);
        return tip == TipResursaIsdp.Manopera ? f * (1 + p.ProcentCam / 100m) : f;
    }

    public static PretOfertaArticol Calculeaza(NodIsdp articol, ParametriOferta p)
    {
        if (p.Curs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "Cursul trebuie să fie pozitiv (1 pentru oferte în lei).");
        }

        var componente = new Dictionary<TipResursaIsdp, (decimal, decimal)>();
        foreach (var t in ValoriIsdp.Tipuri)
        {
            var f = Factor(t, p);
            componente[t] = (
                Math.Round(articol.PretUnitar[t] * f / p.Curs, 2, MidpointRounding.AwayFromZero),
                Math.Round(articol.Valoare[t] * f / p.Curs, 2, MidpointRounding.AwayFromZero));
        }

        return new PretOfertaArticol(articol.Wbs, componente);
    }

    /// <summary>
    /// Deduce cursul din totaluri: valoarea de ofertă în lei (fără rotunjiri) / totalul general din F3.
    /// Se rotunjește la 4 zecimale (cursurile BNR au 4 zecimale).
    /// </summary>
    public static decimal? DeduceCurs(DevizIsdp deviz, ListaF3 lista, ParametriOferta faraCurs)
    {
        if (lista.TotalGeneral is not { } totalF3 || totalF3 == 0)
        {
            return null;
        }

        var lei = deviz.Articole().Sum(a => ValoriIsdp.Tipuri.Sum(t => a.Valoare[t] * Factor(t, faraCurs)));
        return Math.Round(lei / totalF3, 4, MidpointRounding.AwayFromZero);
    }
}
