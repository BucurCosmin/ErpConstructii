using System.Text.Json;
using System.Text.Json.Serialization;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte;

/// <summary>Felul contractului (pe partea de cost a proiectului: ce cumpără firma de la terți).</summary>
public enum TipContract
{
    /// <summary>Execuție de lucrări de către un subantreprenor (de regulă cu taxare inversă și garanție de bună execuție).</summary>
    Subantreprenoriat,
    PrestariServicii,
    InchiriereUtilaje,
    Transport,
    ProiectareConsultanta,
    Altele,
}

/// <summary>
/// Ciornă (se modifică liber) → Activ (semnat: imuabil, regula 6). Închiderea (finalizat / reziliat) e entitate separată,
/// <see cref="InchidereContract"/>; modificările după semnare sunt acte adiționale (<see cref="ActAditional"/>).
/// </summary>
public enum StareContract
{
    Ciorna,
    Activ,
}

/// <summary>O poziție din contract (preț unitar convenit), pentru contractele pe prețuri unitare.</summary>
public sealed record LinieContract(string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal Valoare);

/// <param name="Valoare">Valoarea fără TVA, pentru contractele la preț global (fără poziții); cu poziții, e suma lor.</param>
/// <param name="ProcentGarantie">Garanția de bună execuție reținută din fiecare factură, ca fracție (0,05 = 5%); din contract, nu din cod (regula 5).</param>
/// <param name="TaxareInversa">Lucrări de construcții între plătitori de TVA: factura vine cu taxare inversă.</param>
public sealed record DateContract(
    string? NumarExtern,
    DateOnly DataSemnarii,
    Guid ProiectId,
    string ProiectCod,
    Guid PartenerId,
    string PartenerDenumire,
    string? PartenerCui,
    TipContract Tip,
    string Obiect,
    string Moneda,
    decimal Valoare,
    IReadOnlyList<LinieContract> Linii,
    DateOnly? DataInceput,
    DateOnly? DataSfarsit,
    int? TermenPlataZile,
    decimal ProcentGarantie,
    decimal Avans,
    bool TaxareInversa,
    string? Observatii);

/// <summary>
/// Contractul firmei cu un subantreprenor sau partener (furnizor de servicii, utilaje, transport, proiectare), pe un proiect
/// (REQ-CTR-04). Ține ce s-a convenit: obiectul, valoarea (globală sau pe poziții cu prețuri unitare), termenele, plata,
/// garanția de bună execuție, avansul. Facturile de servicii ale partenerului se leagă de el; situația contractului
/// (<see cref="SituatieContract"/>) arată cât s-a facturat, cât a rămas și garanția reținută. Număr intern CT/AAAA/NNNN,
/// numărul partenerului / de registratură păstrat separat.
/// </summary>
public sealed class ContractPartener : EntitateFirma, IDocumentCuStare
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private IReadOnlyList<LinieContract>? _linii;

    private ContractPartener() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"CT/{An}/{Numar:D4}";
    public string? NumarExtern { get; private set; }
    public DateOnly DataSemnarii { get; private set; }
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public Guid PartenerId { get; private set; }
    public string PartenerDenumire { get; private set; } = string.Empty;
    public string? PartenerCui { get; private set; }
    public TipContract Tip { get; private set; }
    public string Obiect { get; private set; } = string.Empty;
    public string Moneda { get; private set; } = "RON";
    public decimal Valoare { get; private set; }
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieContract> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieContract>>(LiniiJson, Json) ?? [];
    public DateOnly? DataInceput { get; private set; }
    public DateOnly? DataSfarsit { get; private set; }
    public int? TermenPlataZile { get; private set; }
    public decimal ProcentGarantie { get; private set; }
    public decimal Avans { get; private set; }
    public bool TaxareInversa { get; private set; }
    public string? Observatii { get; private set; }
    public StareContract Stare { get; private set; }
    public Guid CreatDeId { get; private set; }
    public string CreatDeNume { get; private set; } = string.Empty;
    public DateTimeOffset? ActivatLa { get; private set; }
    public string? ActivatDeNume { get; private set; }

    public bool EsteBlocat => Stare != StareContract.Ciorna;

    public static Rezultat<ContractPartener> Creeaza(int an, int numar, DateContract date, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = Valideaza(date);
        if (an < 2000 || numar < 1) probleme.Add("Numărul contractului nu este valid.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume)) probleme.Add("Utilizatorul este obligatoriu.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<ContractPartener>(Eroare.Validare(string.Join(" ", probleme)));

        var c = new ContractPartener { An = an, Numar = numar, CreatDeId = utilizatorId, CreatDeNume = utilizatorNume.Trim(), Stare = StareContract.Ciorna };
        c.Aplica(date);
        return Rezultat.Ok(c);
    }

    /// <summary>Ciorna se modifică liber; contractul semnat doar prin acte adiționale.</summary>
    public Rezultat Modifica(DateContract date)
    {
        if (Stare != StareContract.Ciorna)
            return Rezultat.Esuat(Eroare.Validare($"Contractul {NumarAfisat} e semnat: se modifică doar prin act adițional."));
        var probleme = Valideaza(date);
        if (date.ProiectId != ProiectId) probleme.Add("Contractul rămâne pe proiectul lui.");
        if (probleme.Count > 0)
            return Rezultat.Esuat(Eroare.Validare(string.Join(" ", probleme)));
        Aplica(date);
        return Rezultat.Ok();
    }

    /// <summary>Contractul a fost semnat: intră în vigoare și nu se mai modifică (regula 6); facturile se pot lega de el.</summary>
    public Rezultat Activeaza(string utilizatorNume, DateTimeOffset moment)
    {
        if (Stare != StareContract.Ciorna)
            return Rezultat.Esuat(Eroare.Validare($"Contractul {NumarAfisat} e deja activ."));
        if (Valoare <= 0)
            return Rezultat.Esuat(Eroare.Validare("Contractul activ trebuie să aibă o valoare (globală sau pe poziții)."));
        if (string.IsNullOrWhiteSpace(utilizatorNume))
            return Rezultat.Esuat(Eroare.Validare("Utilizatorul este obligatoriu."));
        Stare = StareContract.Activ;
        ActivatLa = moment;
        ActivatDeNume = utilizatorNume.Trim();
        return Rezultat.Ok();
    }

    public static List<string> Valideaza(DateContract d)
    {
        var p = new List<string>();
        if (d.NumarExtern?.Trim().Length > 50) p.Add("Numărul partenerului / de registratură are cel mult 50 de caractere.");
        if (d.DataSemnarii.Year < 2000) p.Add("Data contractului nu este validă.");
        if (d.ProiectId == Guid.Empty || string.IsNullOrWhiteSpace(d.ProiectCod)) p.Add("Proiectul este obligatoriu.");
        if (d.PartenerId == Guid.Empty || string.IsNullOrWhiteSpace(d.PartenerDenumire)) p.Add("Partenerul este obligatoriu.");
        if (!Enum.IsDefined(d.Tip)) p.Add("Tipul contractului nu este valid.");
        if (string.IsNullOrWhiteSpace(d.Obiect) || d.Obiect.Trim().Length > 1000) p.Add("Obiectul contractului este obligatoriu (cel mult 1000 de caractere).");
        if (string.IsNullOrWhiteSpace(d.Moneda) || d.Moneda.Trim().Length != 3) p.Add("Moneda are 3 litere (RON, EUR).");
        if (d.Linii.Count == 0 && d.Valoare < 0) p.Add("Valoarea nu poate fi negativă.");
        if (d.Linii.Any(l => string.IsNullOrWhiteSpace(l.Denumire) || string.IsNullOrWhiteSpace(l.UnitateMasura) || l.Cantitate <= 0 || l.PretUnitar < 0))
            p.Add("Pozițiile contractului au denumire, UM, cantitate pozitivă și preț.");
        if (d.Linii.Any(l => l.Denumire?.Length > 300 || l.UnitateMasura?.Length > 30)) p.Add("Denumirea poziției (300) sau UM-ul (30) sunt prea lungi.");
        if (d.DataInceput is { } i && d.DataSfarsit is { } s && s < i) p.Add("Termenul de finalizare e înaintea datei de început.");
        if (d.TermenPlataZile is < 0 or > 365) p.Add("Termenul de plată e între 0 și 365 de zile.");
        if (d.ProcentGarantie is < 0 or > 0.5m) p.Add("Garanția de bună execuție e între 0% și 50%.");
        if (d.Avans < 0) p.Add("Avansul nu poate fi negativ.");
        if (d.Observatii?.Length > 2000) p.Add("Observațiile au cel mult 2000 de caractere.");
        return p;
    }

    private void Aplica(DateContract d)
    {
        var linii = d.Linii.Select(l => l with
        {
            Denumire = l.Denumire.Trim(),
            UnitateMasura = l.UnitateMasura.Trim(),
            Valoare = Rotunjire.Lei.Aplica(l.Cantitate * l.PretUnitar),
        }).ToList();
        NumarExtern = string.IsNullOrWhiteSpace(d.NumarExtern) ? null : d.NumarExtern.Trim();
        DataSemnarii = d.DataSemnarii;
        ProiectId = d.ProiectId;
        ProiectCod = d.ProiectCod.Trim();
        PartenerId = d.PartenerId;
        PartenerDenumire = d.PartenerDenumire.Trim();
        PartenerCui = string.IsNullOrWhiteSpace(d.PartenerCui) ? null : ValidareFiscala.NormalizeazaCui(d.PartenerCui);
        Tip = d.Tip;
        Obiect = d.Obiect.Trim();
        Moneda = d.Moneda.Trim().ToUpperInvariant();
        LiniiJson = JsonSerializer.Serialize(linii, Json);
        _linii = null;
        Valoare = linii.Count > 0 ? linii.Sum(l => l.Valoare) : Rotunjire.Lei.Aplica(d.Valoare);
        DataInceput = d.DataInceput;
        DataSfarsit = d.DataSfarsit;
        TermenPlataZile = d.TermenPlataZile;
        ProcentGarantie = d.ProcentGarantie;
        Avans = Rotunjire.Lei.Aplica(d.Avans);
        TaxareInversa = d.TaxareInversa;
        Observatii = string.IsNullOrWhiteSpace(d.Observatii) ? null : d.Observatii.Trim();
    }
}

/// <summary>
/// Act adițional la un contract semnat (append-only): suplimentare / diminuare de valoare și / sau prelungirea termenului.
/// Numerotat în cadrul contractului (1, 2, …).
/// </summary>
public sealed class ActAditional : EntitateFirma
{
    private ActAditional() { }

    public Guid ContractId { get; private set; }
    public int Numar { get; private set; }
    public string? NumarExtern { get; private set; }
    public DateOnly Data { get; private set; }
    public string Obiect { get; private set; } = string.Empty;

    /// <summary>Cu cât se schimbă valoarea contractului (negativ = renunțări).</summary>
    public decimal DiferentaValoare { get; private set; }

    public DateOnly? DataSfarsitNoua { get; private set; }
    public string CreatDeNume { get; private set; } = string.Empty;

    public static Rezultat<ActAditional> Creeaza(
        ContractPartener contract, InchidereContract? inchidere, int numar, string? numarExtern, DateOnly data, string obiect, decimal diferentaValoare,
        DateOnly? dataSfarsitNoua, decimal valoareActuala, string utilizatorNume)
    {
        var p = new List<string>();
        if (contract.Stare != StareContract.Activ) p.Add("Actele adiționale se încheie la contractele semnate (active).");
        if (inchidere is not null) p.Add($"Contractul {contract.NumarAfisat} e închis ({inchidere.Tip}).");
        if (numar < 1) p.Add("Numărul actului adițional nu este valid.");
        if (data < contract.DataSemnarii) p.Add("Actul adițional nu poate fi dinaintea contractului.");
        if (string.IsNullOrWhiteSpace(obiect) || obiect.Trim().Length > 1000) p.Add("Obiectul actului adițional este obligatoriu (cel mult 1000 de caractere).");
        if (diferentaValoare == 0 && dataSfarsitNoua is null) p.Add("Actul adițional schimbă valoarea sau termenul.");
        if (valoareActuala + diferentaValoare < 0) p.Add("Valoarea contractului nu poate deveni negativă.");
        if (dataSfarsitNoua is { } s && contract.DataInceput is { } i && s < i) p.Add("Noul termen e înaintea datei de început.");
        if (numarExtern?.Trim().Length > 50) p.Add("Numărul de pe act are cel mult 50 de caractere.");
        if (string.IsNullOrWhiteSpace(utilizatorNume)) p.Add("Utilizatorul este obligatoriu.");
        if (p.Count > 0)
            return Rezultat.Esuat<ActAditional>(Eroare.Validare(string.Join(" ", p)));
        return Rezultat.Ok(new ActAditional
        {
            ContractId = contract.Id,
            Numar = numar,
            NumarExtern = string.IsNullOrWhiteSpace(numarExtern) ? null : numarExtern.Trim(),
            Data = data,
            Obiect = obiect.Trim(),
            DiferentaValoare = Rotunjire.Lei.Aplica(diferentaValoare),
            DataSfarsitNoua = dataSfarsitNoua,
            CreatDeNume = utilizatorNume.Trim(),
        });
    }
}

public enum TipInchidere
{
    /// <summary>Lucrările / serviciile au fost recepționate: nu se mai leagă facturi noi (decât cu redeschidere, viitor).</summary>
    Finalizat,

    /// <summary>Contractul a fost reziliat (motivul e obligatoriu).</summary>
    Reziliat,
}

/// <summary>Închiderea contractului (append-only, regula 6): după ea nu se mai leagă facturi noi și nu se mai încheie acte adiționale.</summary>
public sealed class InchidereContract : EntitateFirma
{
    private InchidereContract() { }

    public Guid ContractId { get; private set; }
    public TipInchidere Tip { get; private set; }
    public DateOnly Data { get; private set; }
    public string? Motiv { get; private set; }
    public string InchisDeNume { get; private set; } = string.Empty;

    public static Rezultat<InchidereContract> Creeaza(ContractPartener contract, bool dejaInchis, TipInchidere tip, DateOnly data, string? motiv, string utilizatorNume)
    {
        var p = new List<string>();
        if (contract.Stare != StareContract.Activ) p.Add("Se închide doar un contract activ (ciorna se șterge).");
        if (dejaInchis) p.Add($"Contractul {contract.NumarAfisat} e deja închis.");
        if (!Enum.IsDefined(tip)) p.Add("Felul închiderii nu este valid.");
        if (data < contract.DataSemnarii) p.Add("Data închiderii nu poate fi înaintea contractului.");
        if (tip == TipInchidere.Reziliat && string.IsNullOrWhiteSpace(motiv)) p.Add("Motivul rezilierii este obligatoriu.");
        if (motiv?.Length > 1000) p.Add("Motivul are cel mult 1000 de caractere.");
        if (string.IsNullOrWhiteSpace(utilizatorNume)) p.Add("Utilizatorul este obligatoriu.");
        if (p.Count > 0)
            return Rezultat.Esuat<InchidereContract>(Eroare.Validare(string.Join(" ", p)));
        return Rezultat.Ok(new InchidereContract
        {
            ContractId = contract.Id,
            Tip = tip,
            Data = data,
            Motiv = string.IsNullOrWhiteSpace(motiv) ? null : motiv.Trim(),
            InchisDeNume = utilizatorNume.Trim(),
        });
    }
}

/// <summary>Un document atașat contractului (contractul semnat, un act adițional scanat, anexe), arhivat neschimbat.</summary>
public sealed class DocumentContract : EntitateFirma
{
    public const int DimensiuneMaxima = 25 * 1024 * 1024;

    private DocumentContract() { }

    public DocumentContract(Guid contractId, Guid? actAditionalId, string numeFisier, string tipContinut, byte[] continut, string incarcatDeNume)
    {
        ContractId = contractId;
        ActAditionalId = actAditionalId;
        NumeFisier = numeFisier;
        TipContinut = tipContinut;
        Continut = continut;
        Dimensiune = continut.Length;
        HashSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(continut));
        IncarcatDeNume = incarcatDeNume;
    }

    public Guid ContractId { get; private set; }
    public Guid? ActAditionalId { get; private set; }
    public string NumeFisier { get; private set; } = string.Empty;
    public string TipContinut { get; private set; } = string.Empty;
    public long Dimensiune { get; private set; }
    public string HashSha256 { get; private set; } = string.Empty;
    public byte[] Continut { get; private set; } = [];
    public string IncarcatDeNume { get; private set; } = string.Empty;
}
