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

    private List<ContBancar> _conturiBancare = [];

    public string Denumire { get; private set; } = string.Empty;
    public string? Cui { get; private set; }

    /// <summary>Numărul de ordine în Registrul Comerțului (ex. J13/726/1994 sau formatul nou J1994000726136).</summary>
    public string? NrRegCom { get; private set; }

    public Adresa Adresa { get; private set; } = Adresa.Goala;
    public string? Telefon { get; private set; }
    public string? Fax { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }

    /// <summary>Conturile bancare trecute pe documente (comenzi, facturi); cel principal apare implicit.</summary>
    public IList<ContBancar> ConturiBancare
    {
        get => _conturiBancare.ToList();
        private set => _conturiBancare = value.ToList();
    }

    public bool Activa { get; private set; }

    public void Modifica(string denumire, string? cui)
    {
        if (string.IsNullOrWhiteSpace(denumire))
        {
            throw new ArgumentException("Denumirea firmei este obligatorie.", nameof(denumire));
        }

        Denumire = denumire.Trim();
        Cui = Gol(cui);
    }

    /// <summary>Datele de identificare și contact (antetul documentelor). Validarea: <see cref="ValidareFiscala.Probleme"/>.</summary>
    public Rezultat ModificaDate(DateFirma date)
    {
        if (string.IsNullOrWhiteSpace(date.Denumire))
        {
            return Rezultat.Esuat(Eroare.Validare("Denumirea firmei este obligatorie."));
        }

        if (!string.IsNullOrWhiteSpace(date.Email) && !date.Email.Contains('@'))
        {
            return Rezultat.Esuat(Eroare.Validare("Adresa de email nu este validă."));
        }

        var conturi = date.ConturiBancare
            .Select(c => c with { Banca = c.Banca.Trim(), Iban = ValidareFiscala.NormalizeazaIban(c.Iban) })
            .ToList();
        if (ValidareFiscala.Probleme(date.Cui, conturi) is { Count: > 0 } probleme)
        {
            return Rezultat.Esuat(Eroare.Validare(string.Join(" ", probleme)));
        }

        Denumire = date.Denumire.Trim();
        Cui = Gol(date.Cui)?.ToUpperInvariant();
        NrRegCom = Gol(date.NrRegCom)?.ToUpperInvariant();
        Adresa = date.Adresa;
        Telefon = Gol(date.Telefon);
        Fax = Gol(date.Fax);
        Email = Gol(date.Email)?.ToLowerInvariant();
        Website = Gol(date.Website);
        _conturiBancare = conturi;
        return Rezultat.Ok();
    }

    private static string? Gol(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public void Dezactiveaza() => Activa = false;
}

public sealed record DateFirma(
    string Denumire,
    string? Cui,
    string? NrRegCom,
    Adresa Adresa,
    string? Telefon,
    string? Fax,
    string? Email,
    string? Website,
    IReadOnlyList<ContBancar> ConturiBancare);

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
