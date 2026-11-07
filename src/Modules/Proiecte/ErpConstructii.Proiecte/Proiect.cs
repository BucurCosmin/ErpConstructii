using ErpConstructii.SharedKernel;

namespace ErpConstructii.Proiecte;

/// <summary>Ciclul de viață al unui proiect (șantier).</summary>
public enum StareProiect
{
    Ofertare,
    Contractat,
    InExecutie,
    ReceptieLaTerminare,
    PerioadaGarantie,
    Inchis,
    Pierdut,
}

/// <summary>
/// Proiectul este axa costurilor (REQ-GEN-01): oferta, bugetul, consumurile, pontajul,
/// situațiile și facturile se leagă toate de un proiect și de articolele lui de deviz.
/// Structura obiect → categorie → articol stă în modulul Devize, versionată.
/// </summary>
public sealed class Proiect : EntitateFirma
{
    private Proiect() { }

    public Proiect(string cod, string denumire, string beneficiar, string? cuiBeneficiar, string adresaSantier)
    {
        if (string.IsNullOrWhiteSpace(cod))
        {
            throw new ArgumentException("Codul proiectului este obligatoriu.", nameof(cod));
        }

        Cod = cod.Trim().ToUpperInvariant();
        Denumire = denumire.Trim();
        Beneficiar = beneficiar.Trim();
        CuiBeneficiar = cuiBeneficiar?.Trim();
        AdresaSantier = adresaSantier.Trim();
        Stare = StareProiect.Ofertare;
    }

    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public string Beneficiar { get; private set; } = string.Empty;
    public string? CuiBeneficiar { get; private set; }
    public string AdresaSantier { get; private set; } = string.Empty;
    public StareProiect Stare { get; private set; }
    public Guid? ManagerProiectId { get; private set; }
    public DateOnly? DataInceput { get; private set; }
    public DateOnly? TermenContractual { get; private set; }

    /// <summary>
    /// Arhivat (D-39): proiectul dispare din liste și din alegeri și nu mai primește documente noi (în afară de returul
    /// materialelor rămase în șantier). Documentele și rapoartele lui rămân. Se reactivează oricând.
    /// </summary>
    public bool Arhivat { get; private set; }

    public DateTimeOffset? ArhivatLa { get; private set; }
    public string? ArhivatDeNume { get; private set; }

    public Rezultat Arhiveaza(string utilizatorNume, DateTimeOffset moment)
    {
        if (Arhivat)
            return Rezultat.Esuat(Eroare.Validare("Proiectul este deja arhivat."));
        if (string.IsNullOrWhiteSpace(utilizatorNume) || utilizatorNume.Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Numele utilizatorului este obligatoriu."));
        Arhivat = true;
        ArhivatLa = moment;
        ArhivatDeNume = utilizatorNume.Trim();
        return Rezultat.Ok();
    }

    public Rezultat Reactiveaza()
    {
        if (!Arhivat)
            return Rezultat.Esuat(Eroare.Validare("Proiectul nu este arhivat."));
        Arhivat = false;
        ArhivatLa = null;
        ArhivatDeNume = null;
        return Rezultat.Ok();
    }

    public void AlocaManager(Guid utilizatorId) => ManagerProiectId = utilizatorId;

    public Rezultat Contracteaza(DateOnly dataInceput, DateOnly termen)
    {
        if (Stare != StareProiect.Ofertare)
        {
            return Rezultat.Esuat(Eroare.Validare("Doar un proiect în ofertare poate fi contractat."));
        }

        if (termen < dataInceput)
        {
            return Rezultat.Esuat(Eroare.Validare("Termenul contractual este înaintea datei de început."));
        }

        DataInceput = dataInceput;
        TermenContractual = termen;
        Stare = StareProiect.Contractat;
        return Rezultat.Ok();
    }

    public void IncepeExecutia() => Stare = StareProiect.InExecutie;

    /// <summary>
    /// Editarea completă (administratorul firmei): datele de identificare, starea și datele contractuale. Documentele emise
    /// își păstrează codul / denumirea copiate la emitere.
    /// </summary>
    public Rezultat Modifica(
        string cod, string denumire, string beneficiar, string? cuiBeneficiar, string adresaSantier,
        StareProiect stare, DateOnly? dataInceput, DateOnly? termenContractual)
    {
        var probleme = new List<string>();
        if (string.IsNullOrWhiteSpace(cod) || cod.Trim().Length > 40)
            probleme.Add("Codul este obligatoriu (cel mult 40 de caractere).");
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 300)
            probleme.Add("Denumirea este obligatorie (cel mult 300 de caractere).");
        if (string.IsNullOrWhiteSpace(beneficiar) || beneficiar.Trim().Length > 300)
            probleme.Add("Beneficiarul este obligatoriu (cel mult 300 de caractere).");
        if (cuiBeneficiar?.Trim().Length > 20)
            probleme.Add("CUI-ul beneficiarului are cel mult 20 de caractere.");
        if ((adresaSantier ?? string.Empty).Trim().Length > 500)
            probleme.Add("Adresa are cel mult 500 de caractere.");
        if (!Enum.IsDefined(stare))
            probleme.Add("Starea proiectului nu este validă.");
        if (dataInceput is { } d && termenContractual is { } t && t < d)
            probleme.Add("Termenul contractual este înaintea datei de început.");
        if (probleme.Count > 0)
            return Rezultat.Esuat(Eroare.Validare(string.Join(" ", probleme)));

        Cod = cod.Trim().ToUpperInvariant();
        Denumire = denumire.Trim();
        Beneficiar = beneficiar.Trim();
        CuiBeneficiar = string.IsNullOrWhiteSpace(cuiBeneficiar) ? null : cuiBeneficiar.Trim();
        AdresaSantier = (adresaSantier ?? string.Empty).Trim();
        Stare = stare;
        DataInceput = dataInceput;
        TermenContractual = termenContractual;
        return Rezultat.Ok();
    }
}
