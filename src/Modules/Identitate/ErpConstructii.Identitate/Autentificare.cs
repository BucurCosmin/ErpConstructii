using ErpConstructii.SharedKernel;

namespace ErpConstructii.Identitate;

/// <summary>
/// Firma (tenant). Schema <c>identitate</c>, fără RLS: un utilizator poate lucra pentru mai multe firme,
/// iar lista lor trebuie citită la login, înainte să existe o firmă activă.
/// </summary>
public sealed class Firma : Entitate
{
    private Firma() { }

    public Firma(string denumire, string? cui)
    {
        if (string.IsNullOrWhiteSpace(denumire))
        {
            throw new ArgumentException("Denumirea firmei este obligatorie.", nameof(denumire));
        }

        Denumire = denumire.Trim();
        Cui = cui?.Trim();
        Activa = true;
    }

    /// <summary>Doar pentru firme cu Id cunoscut dinainte (date demo, migrări de date).</summary>
    public Firma(Guid id, string denumire, string? cui)
        : this(denumire, cui)
    {
        Id = id;
    }

    public string Denumire { get; private set; } = string.Empty;
    public string? Cui { get; private set; }
    public bool Activa { get; private set; }

    public void Modifica(string denumire, string? cui)
    {
        if (string.IsNullOrWhiteSpace(denumire))
        {
            throw new ArgumentException("Denumirea firmei este obligatorie.", nameof(denumire));
        }

        Denumire = denumire.Trim();
        Cui = cui?.Trim();
    }

    public void Dezactiveaza() => Activa = false;
}

/// <summary>Numele claim-urilor din token-urile emise de aplicație (REQ-UTL-02).</summary>
public static class ClaimuriErp
{
    /// <summary>Id-ul utilizatorului (claim-ul standard OIDC „sub”).</summary>
    public const string Utilizator = "sub";

    /// <summary>Firma activă a sesiunii; citită de interceptorul RLS (D-04).</summary>
    public const string Firma = "firma_id";

    public const string Nume = "name";
    public const string Email = "email";
}

/// <summary>
/// Când e obligatorie autentificarea în doi pași (REQ-UTL-02): administratorii și oricine are acces,
/// pe oricare dintre firmele lui, la modulul financiar, la administrare sau la datele sensibile
/// de personal (salarii, date personale). Editarea pontajului (hr.editare la șeful de șantier) nu
/// e suficientă: utilizatorii de șantier sunt protejați de dispozitivul înregistrat (REQ-UTL-03).
/// </summary>
public static class PoliticaMfa
{
    private static readonly string[] Prefixe =
    [
        $"{Permisiuni.CodModul(ModulErp.Financiar)}.",
        $"{Permisiuni.CodModul(ModulErp.Administrare)}.",
    ];

    public static bool EsteObligatorie(TipUtilizator tip, IEnumerable<string> permisiuni)
    {
        if (tip is TipUtilizator.AdministratorSistem or TipUtilizator.AdministratorFirma)
        {
            return true;
        }

        return permisiuni.Any(p =>
            p is Permisiuni.Salarii or Permisiuni.DatePersonale
            || Prefixe.Any(prefix => p.StartsWith(prefix, StringComparison.Ordinal)));
    }
}

/// <summary>Firmele la care un utilizator are acces și alegerea firmei active.</summary>
public static class AccesFirme
{
    /// <summary>Firmele din atribuirile valabile în ziua dată.</summary>
    public static IReadOnlySet<Guid> Accesibile(IEnumerable<Atribuire> atribuiri, DateOnly zi) =>
        atribuiri.Where(a => a.EsteValabila(zi)).Select(a => a.FirmaId).ToHashSet();

    /// <summary>
    /// Firma activă: cea cerută, dacă e accesibilă; altfel ultima folosită; altfel prima din listă
    /// (ordinea dată de apelant, de regulă alfabetică). Null dacă utilizatorul nu are nicio firmă.
    /// </summary>
    public static Guid? Alege(IReadOnlyList<Guid> accesibile, Guid? ceruta, Guid? ultima)
    {
        if (ceruta is { } c && accesibile.Contains(c))
        {
            return c;
        }

        if (ultima is { } u && accesibile.Contains(u))
        {
            return u;
        }

        return accesibile.Count > 0 ? accesibile[0] : null;
    }

    /// <summary>Contul poate primi token-uri: activ și neexpirat (auditorii au dată de expirare, REQ-UTL-04).</summary>
    public static bool PoateIntra(Utilizator utilizator, DateOnly zi) =>
        utilizator.Activ && (utilizator.ExpiraLa is null || zi <= utilizator.ExpiraLa);
}
