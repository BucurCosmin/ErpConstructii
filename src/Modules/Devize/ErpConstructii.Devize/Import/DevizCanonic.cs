using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Devize.Import;

/// <summary>
/// Modelul intern comun în care orice importator traduce fișierul sursă (REQ-IMP-02).
/// Același model servește și exportului (formulare de ofertă, Excel).
/// </summary>
public sealed record DevizCanonic(
    string Denumire,
    IReadOnlyList<ObiectCanonic> Obiecte,
    ParametriRecapitulatie? Parametri,
    TotaluriDeclarate? Totaluri);

public sealed record ObiectCanonic(string Cod, string Denumire, IReadOnlyList<CategorieCanonica> Categorii);

public sealed record CategorieCanonica(string Cod, string Denumire, IReadOnlyList<ArticolCanonic> Articole);

/// <param name="Simbol">Simbolul normei din indicator.</param>
/// <param name="PretUnitar">Prețul unitar pe resurse, așa cum apare în fișier.</param>
/// <param name="Consumuri">Descompunerea pe resurse (poate lipsi, dacă sursa nu o exportă).</param>
/// <param name="Rand">Rândul sau poziția din fișierul sursă, pentru mesaje de eroare.</param>
public sealed record ArticolCanonic(
    string Simbol,
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    ValoriResurse PretUnitar,
    IReadOnlyList<ConsumCanonic> Consumuri,
    string? Rand = null);

public sealed record ConsumCanonic(
    string CodResursa,
    string Denumire,
    TipResursa Tip,
    string UnitateMasura,
    decimal ConsumUnitar,
    decimal PretUnitar);

/// <summary>
/// Totalurile scrise în fișierul sursă. Validatorul recalculează devizul și le compară:
/// diferențele arată fie un fișier corupt, fie o regulă de calcul/rotunjire neacoperită.
/// </summary>
public sealed record TotaluriDeclarate(
    ValoriResurse? CheltuieliDirecteResurse,
    decimal? TotalFaraTva,
    decimal? TotalCuTva);
