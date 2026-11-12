namespace ErpConstructii.Devize.Import.Isdp;

public enum TipResursaIsdp
{
    Material,
    Manopera,
    Utilaj,
    Transport,
    Echipament,
}

/// <summary>
/// Valori pe cele cinci coloane de resurse din ISDP (material, manoperă, utilaj, transport, echipamente).
/// Atenție: ERP-ul avea inițial doar 4 resurse; ISDP are și „Echipamente” (vezi docs/import-isdp.md).
/// </summary>
public readonly record struct ValoriIsdp(decimal Material, decimal Manopera, decimal Utilaj, decimal Transport, decimal Echipament)
{
    public static readonly ValoriIsdp Zero = default;

    public decimal Total => Material + Manopera + Utilaj + Transport + Echipament;

    public decimal this[TipResursaIsdp tip] => tip switch
    {
        TipResursaIsdp.Material => Material,
        TipResursaIsdp.Manopera => Manopera,
        TipResursaIsdp.Utilaj => Utilaj,
        TipResursaIsdp.Transport => Transport,
        TipResursaIsdp.Echipament => Echipament,
        _ => throw new ArgumentOutOfRangeException(nameof(tip)),
    };

    public static ValoriIsdp operator +(ValoriIsdp a, ValoriIsdp b) =>
        new(a.Material + b.Material, a.Manopera + b.Manopera, a.Utilaj + b.Utilaj, a.Transport + b.Transport, a.Echipament + b.Echipament);

    public static ValoriIsdp operator *(ValoriIsdp a, decimal f) =>
        new(a.Material * f, a.Manopera * f, a.Utilaj * f, a.Transport * f, a.Echipament * f);

    public static IReadOnlyList<TipResursaIsdp> Tipuri { get; } = Enum.GetValues<TipResursaIsdp>();
}

public enum TipNodIsdp
{
    /// <summary>Grupare fără simbol: stadiu fizic (rădăcina) sau capitol de lucrări.</summary>
    Grup,

    /// <summary>Articol de deviz (normă din indicator, asimilat sau resursă introdusă direct).</summary>
    Articol,

    /// <summary>Resursă a unui articol (material, meserie, utilaj, transport, echipament).</summary>
    Resursa,
}

public enum TipArticolIsdp
{
    /// <summary>Normă din indicator (ex. TSA04G1).</summary>
    Normat,

    /// <summary>Normă asimilată: simbolul are sufixul „asim” (ex. „TSF01A1 asim”).</summary>
    Asimilat,

    /// <summary>
    /// Resursă introdusă direct ca articol, fără descompunere (ex. 20011961 „Cherestea rășinoase”).
    /// Se tratează ca articol cu o singură resursă, consum unitar 1.
    /// </summary>
    ResursaDirecta,
}

/// <summary>
/// Un rând din „Deviz ofertă cu resurse”, cu poziția lui în arborele WBS.
/// Cantitatea unei RESURSE este cantitatea TOTALĂ pe articol (nu consumul unitar):
/// consumul unitar = cantitate resursă / cantitate articol.
/// </summary>
public sealed class NodIsdp
{
    private readonly List<NodIsdp> _copii = [];

    public required string Wbs { get; init; }
    public required string Simbol { get; init; }
    public required string Denumire { get; init; }
    public required string UnitateMasura { get; init; }
    public decimal Cantitate { get; init; }
    public ValoriIsdp PretUnitar { get; init; }
    public ValoriIsdp Valoare { get; init; }

    /// <summary>Coloana „Total” din fișier (pentru verificare).</summary>
    public decimal? TotalDeclarat { get; init; }

    /// <summary>Rândul din fișier (1 = primul rând), pentru mesaje.</summary>
    public int Rand { get; init; }

    public TipNodIsdp Tip { get; internal set; }
    public NodIsdp? Parinte { get; internal set; }
    public IReadOnlyList<NodIsdp> Copii => _copii;

    // --- doar pentru articole ---
    public TipArticolIsdp TipArticol { get; internal set; }

    /// <summary>Simbolul fără sufixul „asim”.</summary>
    public string SimbolBaza { get; internal set; } = string.Empty;

    // --- doar pentru resurse ---
    public TipResursaIsdp TipResursa { get; internal set; }

    /// <summary>True dacă tipul resursei nu reiese din prețuri (toate zero) și a fost dedus.</summary>
    public bool TipResursaDedus { get; internal set; }

    /// <summary>Consum pe unitatea de articol = Cantitate / Cantitate articol.</summary>
    public decimal ConsumUnitar { get; internal set; }

    internal void AdaugaCopil(NodIsdp copil)
    {
        copil.Parinte = this;
        _copii.Add(copil);
    }

    public IEnumerable<NodIsdp> Descendenti()
    {
        foreach (var c in _copii)
        {
            yield return c;
            foreach (var d in c.Descendenti())
            {
                yield return d;
            }
        }
    }

    public override string ToString() => $"{Wbs} {Simbol} {Denumire}";
}

/// <summary>Datele din antetul raportului.</summary>
public sealed record AntetIsdp(string Obiectiv, string Obiect, string StadiuFizic, string Beneficiar, string Titlu);

/// <summary>Un rând de recapitulație (T0, T1, T2, T3) pe componente.</summary>
public sealed record TotalRecapitulatie(decimal Material, decimal Manopera, decimal Utilaj, decimal Transport, decimal Total);

/// <summary>
/// Recapitulația declarată în fișier. Procentele sunt în PROCENTE (2,25 = 2,25%), ca în ISDP.
/// </summary>
public sealed record RecapitulatieIsdp(
    TotalRecapitulatie? T0ValoareDirecta,
    decimal? ProcentCam,
    TotalRecapitulatie? T1CuCam,
    decimal? ProcentIndirecte,
    TotalRecapitulatie? T2CuIndirecte,
    decimal? ProcentProfit,
    TotalRecapitulatie? T3CuProfit,
    decimal? CotaTva,
    decimal? ValoareTva,
    decimal? ValoareCuTva);

/// <summary>Un deviz (o foaie = un stadiu fizic) citit din „Deviz ofertă cu resurse”.</summary>
public sealed record DevizIsdp(
    string NumeFoaie,
    AntetIsdp Antet,
    string Moneda,
    IReadOnlyList<NodIsdp> Radacini,
    RecapitulatieIsdp? Recapitulatie)
{
    public IEnumerable<NodIsdp> Toate() => Radacini.SelectMany(r => r.Descendenti().Prepend(r));

    public IEnumerable<NodIsdp> Articole() => Toate().Where(n => n.Tip == TipNodIsdp.Articol);
}

// ---------------------------------------------------------------------------------------------
// F3 — Lista cu cantități de lucrări
// ---------------------------------------------------------------------------------------------

/// <param name="Componente">Prețul unitar și totalul pe resurse (material, manoperă, utilaj, transport, echipamente).</param>
public sealed record ArticolF3(
    string Numar,
    string Denumire,
    string UnitateMasura,
    decimal? Cantitate,
    decimal? PretUnitar,
    decimal? Total,
    IReadOnlyDictionary<TipResursaIsdp, (decimal PretUnitar, decimal Total)> Componente,
    int Rand);

public sealed record CapitolF3(string Numar, string Denumire, IReadOnlyList<ArticolF3> Articole);

/// <summary>
/// Lista cu cantități de lucrări (formularul F3). Poate fi: (a) generată de ISDP cu prețuri de ofertă
/// (cum e fișierul primit), sau (b) primită de la beneficiar într-o licitație, FĂRĂ prețuri.
/// </summary>
public sealed record ListaF3(AntetIsdp Antet, string Moneda, IReadOnlyList<CapitolF3> Capitole, decimal? TotalGeneral)
{
    public IEnumerable<ArticolF3> Articole() => Capitole.SelectMany(c => c.Articole);

    public bool ArePreturi => Articole().Any(a => a.PretUnitar is not null);
}

// ---------------------------------------------------------------------------------------------
// Rezultate și probleme
// ---------------------------------------------------------------------------------------------

public enum SeveritateIsdp
{
    Informare,
    Avertisment,
    Eroare,
}

public sealed record ProblemaIsdp(SeveritateIsdp Severitate, string Locatie, string Mesaj);

public sealed record RezultatIsdp<T>(T? Valoare, IReadOnlyList<ProblemaIsdp> Probleme)
{
    public bool AreErori => Valoare is null || Probleme.Any(p => p.Severitate == SeveritateIsdp.Eroare);
}
