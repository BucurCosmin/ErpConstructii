using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Devize.Import;

/// <summary>
/// Modelul intern comun în care orice importator traduce fișierul sursă (REQ-IMP-02).
/// Același model servește și exportului (formulare de ofertă, Excel).
/// </summary>
/// <param name="Capitole">Capitolele de pe primul nivel (arbore: obiect → stadiu fizic → capitole, pe oricâte niveluri).</param>
public sealed record DevizCanonic(
    string Denumire,
    IReadOnlyList<CapitolCanonic> Capitole,
    ParametriRecapitulatie? Parametri,
    TotaluriDeclarate? Totaluri)
{
    /// <summary>Antetul devizului sursă (ISDP: obiectiv, obiect, stadiu fizic).</summary>
    public AntetCanonic? Antet { get; init; }

    /// <summary>Moneda prețurilor de ofertă (costurile directe sunt în lei) și cursul (lei / unitate; null la RON).</summary>
    public string Moneda { get; init; } = "RON";

    public decimal? Curs { get; init; }

    /// <summary>Cum rotunjește calculul: <see cref="PoliticaRotunjire.Fara"/> pentru sursele care nu rotunjesc pe parcurs (ISDP).</summary>
    public PoliticaRotunjire PoliticaRotunjire { get; init; } = PoliticaRotunjire.Standard;

    /// <summary>Totalurile declarate de fișier, așa cum sunt (JSON), pentru audit.</summary>
    public string? TotaluriDeclarateJson { get; init; }

    /// <summary>Toate articolele, în ordinea arborelui.</summary>
    public IEnumerable<ArticolCanonic> Articole() => Capitole.SelectMany(c => c.ArticoleToate());

    /// <summary>Fiecare articol cu calea capitolelor lui (pentru mesaje).</summary>
    public IEnumerable<(IReadOnlyList<CapitolCanonic> Cale, ArticolCanonic Articol)> ArticoleCuCale() =>
        Capitole.SelectMany(c => c.ArticoleCuCale([]));
}

/// <param name="Tip">Nivelul (obiect, stadiu fizic, capitol).</param>
/// <param name="CodWbs">Codul ierarhic din sursă (ISDP), dacă există.</param>
public sealed record CapitolCanonic(
    string Cod,
    string Denumire,
    TipCapitol Tip,
    IReadOnlyList<CapitolCanonic> Subcapitole,
    IReadOnlyList<ArticolCanonic> Articole,
    string? CodWbs = null,
    bool Subcontractat = false)
{
    public IEnumerable<ArticolCanonic> ArticoleToate() => Articole.Concat(Subcapitole.SelectMany(s => s.ArticoleToate()));

    internal IEnumerable<(IReadOnlyList<CapitolCanonic> Cale, ArticolCanonic Articol)> ArticoleCuCale(IReadOnlyList<CapitolCanonic> parinti)
    {
        IReadOnlyList<CapitolCanonic> cale = [.. parinti, this];
        return Articole.Select(a => (cale, a)).Concat(Subcapitole.SelectMany(s => s.ArticoleCuCale(cale)));
    }
}

public sealed record AntetCanonic(string? Obiectiv, string? Obiect, string? StadiuFizic);

/// <param name="Simbol">Simbolul normei din indicator.</param>
/// <param name="PretUnitar">Costul direct unitar pe resurse (lei), așa cum apare în fișier.</param>
/// <param name="Consumuri">Descompunerea pe resurse (poate lipsi, dacă sursa nu o exportă).</param>
/// <param name="Rand">Rândul sau poziția din fișierul sursă, pentru mesaje de eroare.</param>
/// <param name="CodWbs">Codul WBS din sursă (potrivirea la reimport și cu lista F3).</param>
/// <param name="PretOfertaUnitar">Prețul de ofertă unitar pe componente, în moneda devizului (null = fără preț de ofertă).</param>
public sealed record ArticolCanonic(
    string Simbol,
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    ValoriResurse PretUnitar,
    IReadOnlyList<ConsumCanonic> Consumuri,
    string? Rand = null,
    TipArticol Tip = TipArticol.Normat,
    string? SimbolBaza = null,
    string? CodWbs = null,
    ValoriResurse? PretOfertaUnitar = null,
    ValoriResurse? ValoareOferta = null);

/// <param name="CantitateTotala">Cantitatea pe tot articolul, din sursă (ISDP); null = consum unitar × cantitatea articolului.</param>
/// <param name="TipDedus">Tipul nu reiese din prețuri și a fost dedus (de confirmat la maparea în nomenclator).</param>
public sealed record ConsumCanonic(
    string CodResursa,
    string Denumire,
    TipResursa Tip,
    string UnitateMasura,
    decimal ConsumUnitar,
    decimal PretUnitar,
    decimal? CantitateTotala = null,
    bool TipDedus = false);

/// <summary>
/// Totalurile scrise în fișierul sursă. Validatorul recalculează devizul și le compară:
/// diferențele arată fie un fișier corupt, fie o regulă de calcul/rotunjire neacoperită.
/// </summary>
public sealed record TotaluriDeclarate(
    ValoriResurse? CheltuieliDirecteResurse,
    decimal? TotalFaraTva,
    decimal? TotalCuTva);
