using ErpConstructii.SharedKernel;

namespace ErpConstructii.Parteneri;

/// <summary>Datele introduse pentru un partener (creare sau modificare).</summary>
public sealed record DatePartener(
    string Denumire,
    string? Cui,
    string? NrRegCom,
    bool PlatitorTva,
    Adresa Adresa,
    string? Telefon,
    string? Email,
    string? PersoanaContact,
    string? TelefonContact,
    string? Observatii,
    IReadOnlyList<ContBancar> ConturiBancare,
    bool EsteFurnizor,
    bool EsteClient,
    bool EsteSubantreprenor);

/// <summary>
/// Partener de afaceri al firmei: furnizor, client (beneficiar) și/sau subantreprenor. O singură fișă per firmă
/// parteneră, cu roluri, ca datele (CUI, adresă, conturi) să nu se dubleze când aceeași firmă e și furnizor și client.
/// Datele apar pe documente (comenzi, NIR, facturi) și se folosesc la potrivirea facturilor primite după CUI (REQ-FRM-09).
/// </summary>
public sealed class Partener : EntitateFirma
{
    private List<ContBancar> _conturiBancare = [];

    private Partener() { }

    public string Denumire { get; private set; } = string.Empty;

    /// <summary>CUI fără prefixul „RO” (prefixul ține de <see cref="PlatitorTva"/>); null pentru persoane fizice sau parteneri străini.</summary>
    public string? Cui { get; private set; }

    public string? NrRegCom { get; private set; }

    /// <summary>Plătitor de TVA: determină prefixul „RO” și, la facturare, taxarea inversă (REQ-FCT-02).</summary>
    public bool PlatitorTva { get; private set; }

    public Adresa Adresa { get; private set; } = Adresa.Goala;
    public string? Telefon { get; private set; }
    public string? Email { get; private set; }
    public string? PersoanaContact { get; private set; }
    public string? TelefonContact { get; private set; }
    public string? Observatii { get; private set; }
    public IList<ContBancar> ConturiBancare
    {
        get => _conturiBancare.ToList();
        private set => _conturiBancare = value.ToList();
    }
    public bool EsteFurnizor { get; private set; }
    public bool EsteClient { get; private set; }
    public bool EsteSubantreprenor { get; private set; }
    public bool Activ { get; private set; }

    /// <summary>Codul fiscal cum apare pe documente: cu „RO” pentru plătitorii de TVA.</summary>
    public string? CodFiscal => Cui is null ? null : PlatitorTva ? $"RO{Cui}" : Cui;

    public static Rezultat<Partener> Creeaza(DatePartener date)
    {
        var partener = new Partener { Activ = true };
        var rezultat = partener.Modifica(date);
        return rezultat.Succes ? Rezultat.Ok(partener) : Rezultat.Esuat<Partener>(rezultat.Eroare);
    }

    public Rezultat Modifica(DatePartener date)
    {
        if (Valideaza(date) is { Count: > 0 } probleme)
        {
            return Rezultat.Esuat(Eroare.Validare(string.Join(" ", probleme)));
        }

        Denumire = date.Denumire.Trim();
        Cui = string.IsNullOrWhiteSpace(date.Cui) ? null : ValidareFiscala.NormalizeazaCui(date.Cui);
        NrRegCom = Gol(date.NrRegCom)?.ToUpperInvariant();
        PlatitorTva = date.PlatitorTva;
        Adresa = date.Adresa;
        Telefon = Gol(date.Telefon);
        Email = Gol(date.Email)?.ToLowerInvariant();
        PersoanaContact = Gol(date.PersoanaContact);
        TelefonContact = Gol(date.TelefonContact);
        Observatii = Gol(date.Observatii);
        _conturiBancare = date.ConturiBancare
            .Select(c => c with { Banca = c.Banca.Trim(), Iban = ValidareFiscala.NormalizeazaIban(c.Iban) })
            .ToList();
        EsteFurnizor = date.EsteFurnizor;
        EsteClient = date.EsteClient;
        EsteSubantreprenor = date.EsteSubantreprenor;
        return Rezultat.Ok();
    }

    public void Dezactiveaza() => Activ = false;

    public void Activeaza() => Activ = true;

    /// <summary>Problemele datelor unui partener; listă goală = valid.</summary>
    public static IReadOnlyList<string> Valideaza(DatePartener date)
    {
        var probleme = new List<string>();
        if (string.IsNullOrWhiteSpace(date.Denumire))
        {
            probleme.Add("Denumirea este obligatorie.");
        }

        if (!date.EsteFurnizor && !date.EsteClient && !date.EsteSubantreprenor)
        {
            probleme.Add("Alege cel puțin un rol: furnizor, client sau subantreprenor.");
        }

        if (date.PlatitorTva && string.IsNullOrWhiteSpace(date.Cui))
        {
            probleme.Add("Un plătitor de TVA trebuie să aibă CUI.");
        }

        if (!string.IsNullOrWhiteSpace(date.Email) && !date.Email.Contains('@'))
        {
            probleme.Add("Adresa de email nu este validă.");
        }

        probleme.AddRange(ValidareFiscala.Probleme(date.Cui, date.ConturiBancare.Select(c => c with { Iban = ValidareFiscala.NormalizeazaIban(c.Iban) })));
        return probleme;
    }

    private static string? Gol(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
