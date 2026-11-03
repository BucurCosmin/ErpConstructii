namespace ErpConstructii.Identitate;

/// <summary>
/// O atribuire „rezolvată”: domeniul plus permisiunile rolului, la momentul evaluării.
/// Se calculează la login și se ține în cache (REQ-DRP-09).
/// </summary>
public sealed record AtribuireEfectiva(Domeniu Domeniu, IReadOnlySet<string> Permisiuni);

/// <summary>
/// Decide dacă un utilizator are o permisiune pe un domeniu. Logică pură, fără bază de date,
/// ca să fie testată exhaustiv.
/// </summary>
public sealed class EvaluatorPermisiuni(IReadOnlyList<AtribuireEfectiva> atribuiri)
{
    public static EvaluatorPermisiuni Construieste(
        IEnumerable<Atribuire> atribuiri,
        IReadOnlyDictionary<Guid, Rol> roluri,
        DateOnly zi)
    {
        var efective = atribuiri
            .Where(a => a.EsteValabila(zi) && roluri.ContainsKey(a.RolId))
            .Select(a => new AtribuireEfectiva(
                new Domeniu(a.FirmaId, a.ProiectId, a.GestiuneId),
                roluri[a.RolId].Permisiuni.ToHashSet(StringComparer.Ordinal)))
            .ToList();
        return new EvaluatorPermisiuni(efective);
    }

    /// <summary>
    /// Are permisiunea pe domeniul cerut? O atribuire pe toată firma acoperă orice proiect al firmei;
    /// o atribuire pe un proiect acoperă doar acel proiect.
    /// </summary>
    public bool Are(string permisiune, Domeniu cerut) =>
        atribuiri.Any(a => a.Permisiuni.Contains(permisiune) && Acopera(a.Domeniu, cerut));

    /// <summary>
    /// Pentru liste (ex. „proiectele mele”): null = toate proiectele firmei;
    /// altfel, mulțimea proiectelor pe care utilizatorul are permisiunea.
    /// </summary>
    public IReadOnlySet<Guid>? ProiecteAccesibile(string permisiune, Guid firmaId)
    {
        var relevante = atribuiri
            .Where(a => a.Domeniu.FirmaId == firmaId && a.Permisiuni.Contains(permisiune))
            .ToList();

        if (relevante.Any(a => a.Domeniu.ProiectId is null && a.Domeniu.GestiuneId is null))
        {
            return null;
        }

        return relevante
            .Where(a => a.Domeniu.ProiectId is not null)
            .Select(a => a.Domeniu.ProiectId!.Value)
            .ToHashSet();
    }

    /// <summary>
    /// Separarea atribuțiilor (REQ-DRP-06): cine a creat documentul nu îl poate aproba.
    /// </summary>
    public static bool PoateAprobaDocumentCreatDe(Guid? creatDe, Guid aprobator) =>
        creatDe is null || creatDe.Value != aprobator;

    private static bool Acopera(Domeniu atribuit, Domeniu cerut)
    {
        if (atribuit.FirmaId != cerut.FirmaId)
        {
            return false;
        }

        if (atribuit.ProiectId is not null && atribuit.ProiectId != cerut.ProiectId)
        {
            return false;
        }

        if (atribuit.GestiuneId is not null && atribuit.GestiuneId != cerut.GestiuneId)
        {
            return false;
        }

        return true;
    }
}
