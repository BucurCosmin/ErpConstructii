namespace ErpConstructii.Identitate;

public sealed record PermisiuneCatalog(string Cod, string Denumire);

public sealed record ModulCatalog(string Cod, string Denumire, IReadOnlyList<PermisiuneCatalog> Permisiuni);

/// <summary>
/// Toate permisiunile care pot fi date unui rol, cu etichete pentru ecranul de administrare
/// (matricea rol × modul × acțiune, REQ-DRP-01/02).
/// </summary>
public static class CatalogPermisiuni
{
    private static readonly (ModulErp Modul, string Denumire)[] DenumiriModule =
    [
        (ModulErp.Proiecte, "Proiecte"),
        (ModulErp.Ofertare, "Ofertare și devize"),
        (ModulErp.Contracte, "Contracte"),
        (ModulErp.Buget, "Buget și cost"),
        (ModulErp.Santier, "Execuție șantier"),
        (ModulErp.Situatii, "Situații de lucrări"),
        (ModulErp.Aprovizionare, "Aprovizionare"),
        (ModulErp.Utilaje, "Utilaje"),
        (ModulErp.Financiar, "Financiar"),
        (ModulErp.HrSalarii, "Personal și salarizare"),
        (ModulErp.Administrare, "Administrare"),
    ];

    private static readonly (Actiune Actiune, string Denumire)[] DenumiriActiuni =
    [
        (Actiune.Vizualizare, "Vizualizare"),
        (Actiune.Creare, "Creare"),
        (Actiune.Editare, "Editare"),
        (Actiune.Anulare, "Anulare"),
        (Actiune.Aprobare, "Aprobare"),
        (Actiune.Export, "Export"),
    ];

    public static readonly IReadOnlyList<ModulCatalog> Module =
        DenumiriModule.Select(m => new ModulCatalog(
            Permisiuni.CodModul(m.Modul),
            m.Denumire,
            DenumiriActiuni.Select(a => new PermisiuneCatalog(Permisiuni.Pentru(m.Modul, a.Actiune), a.Denumire)).ToList()))
        .ToList();

    /// <summary>Câmpuri sensibile, cu permisiuni proprii (REQ-DRP-04).</summary>
    public static readonly IReadOnlyList<PermisiuneCatalog> DateSensibile =
    [
        new(Permisiuni.PreturiCost, "Prețuri de cost și marje"),
        new(Permisiuni.Salarii, "Salarii"),
        new(Permisiuni.DatePersonale, "Date personale (CNP, adrese)"),
    ];

    public static readonly IReadOnlySet<string> Toate =
        Module.SelectMany(m => m.Permisiuni).Concat(DateSensibile).Select(p => p.Cod).ToHashSet(StringComparer.Ordinal);

    /// <summary>Permisiunile din listă care nu există în catalog (pentru validarea cererilor).</summary>
    public static IReadOnlyList<string> Necunoscute(IEnumerable<string> permisiuni) =>
        permisiuni.Where(p => !Toate.Contains(p)).Distinct().ToList();
}

/// <summary>
/// Regulile de siguranță ale administrării: firma nu rămâne fără administrator, administratorul unei
/// firme nu poate prelua conturi care au acces și la alte firme, iar tipul „administrator de sistem”
/// îl dă doar un alt administrator de sistem.
/// </summary>
public static class RegulaAdministrare
{
    /// <summary>Permisiunea care face dintr-un utilizator administrator al firmei.</summary>
    public static readonly string PermisiuneAdministrator = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Editare);

    /// <summary>
    /// Firma are cel puțin un utilizator activ cu drept de administrare pe toată firma (nu doar pe un proiect).
    /// Se verifică pe starea de DUPĂ modificare, înainte de salvare.
    /// </summary>
    public static bool AreAdministrator(
        Guid firmaId,
        IEnumerable<Atribuire> atribuiri,
        IReadOnlyDictionary<Guid, Rol> roluri,
        IReadOnlySet<Guid> utilizatoriActivi,
        DateOnly zi) =>
        atribuiri.Any(a =>
            a.FirmaId == firmaId
            && a.ProiectId is null
            && a.GestiuneId is null
            && a.EsteValabila(zi)
            && utilizatoriActivi.Contains(a.UtilizatorId)
            && roluri.TryGetValue(a.RolId, out var rol)
            && rol.Permisiuni.Contains(PermisiuneAdministrator));

    /// <summary>Doar un administrator de sistem poate crea, modifica sau numi administratori de sistem.</summary>
    public static bool PoateGestionaTipul(TipUtilizator actor, TipUtilizator tinta) =>
        tinta != TipUtilizator.AdministratorSistem || actor == TipUtilizator.AdministratorSistem;

    /// <summary>
    /// Operațiile pe contul propriu-zis (dezactivare, resetare parolă, resetare MFA) afectează toate firmele
    /// utilizatorului. Administratorul unei firme le poate face doar dacă utilizatorul lucrează numai pentru
    /// firma lui; altfel, doar administratorul de sistem.
    /// </summary>
    public static bool PoateModificaContul(TipUtilizator actor, Guid firmaActiva, IEnumerable<Guid> firmeleUtilizatorului) =>
        actor == TipUtilizator.AdministratorSistem || firmeleUtilizatorului.All(f => f == firmaActiva);
}
