using System.Text.Json;
using System.Text.Json.Serialization;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Aprovizionare;

/// <param name="Termen">Termenul de livrare, așa cum e scris pe comandă (dată sau text, ex. „imediat”).</param>
/// <param name="PretUnitar">Prețul negociat, dacă apare pe comandă (formularul uzual nu îl are).</param>
public sealed record LinieNotaComanda(string Denumire, string UnitateMasura, decimal Cantitate, string? Termen, decimal? PretUnitar);

public enum ReceptieComanda
{
    LaFurnizor,
    LaPrimire,
}

public enum ClasaCalitateComanda
{
    Clasa4,
    BestComercial,
}

public enum TransportComanda
{
    Auto,
    Cfr,
}

public enum PlataComanda
{
    Op,
    FilaCec,
    Alte,
}

/// <summary>Condițiile bifate pe formularul GC 007 (recepție, clasă de calitate, transport, plată); necompletat = nebifat.</summary>
public sealed record ConditiiComanda(ReceptieComanda? Receptie, ClasaCalitateComanda? ClasaCalitate, TransportComanda? Transport, PlataComanda? Plata)
{
    public static readonly ConditiiComanda Niciuna = new(null, null, null, null);
}

/// <summary>Datele unei comenzi către furnizor, verificate de utilizator.</summary>
/// <param name="FurnizorAdresa">Datele de contact ale furnizorului tipărite pe comandă (copiate la emitere: documentul nu se schimbă dacă se schimbă nomenclatorul).</param>
/// <param name="NecesarId">Necesarul de materiale din care provine comanda, dacă e cazul.</param>
public sealed record DateNotaComanda(
    string? NumarExtern,
    DateOnly Data,
    Guid ProiectId,
    string ProiectCod,
    string? Lucrare,
    Guid? FurnizorId,
    string FurnizorDenumire,
    string? FurnizorCui,
    IReadOnlyList<LinieNotaComanda> Linii,
    string? Observatii,
    string? FurnizorAdresa = null,
    string? FurnizorTelefon = null,
    string? FurnizorEmail = null,
    ConditiiComanda? Conditii = null,
    Guid? NecesarId = null);

/// <summary>
/// Notă de comandă către furnizor (formularul GC 007): ce materiale se cer, cui și pentru ce lucrare. Urmează necesarului
/// de materiale aprobat și deschide lanțul notă de comandă → recepție (NIR) → factură, folosit la potrivirea în 3 puncte (REQ-FCT-09).
/// Se emite în sistem (număr NC/AAAA/NNNN, document Word din șablonul firmei) sau se importă cea emisă pe hârtie
/// (scanare / Word), care își păstrează numărul de pe document. Imuabilă după înregistrare (regula 6).
/// </summary>
public sealed class NotaComanda : EntitateFirma, IDocumentCuStare
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private IReadOnlyList<LinieNotaComanda>? _linii;

    private NotaComanda() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"NC/{An}/{Numar:D4}";
    public string? NumarExtern { get; private set; }
    public DateOnly Data { get; private set; }
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;

    /// <summary>„Obiect / Lucrare” de pe comandă (coduri de contract, „STOC”…), păstrat ca pe document.</summary>
    public string? Lucrare { get; private set; }

    public Guid? FurnizorId { get; private set; }
    public string FurnizorDenumire { get; private set; } = string.Empty;
    public string? FurnizorCui { get; private set; }
    public string? FurnizorAdresa { get; private set; }
    public string? FurnizorTelefon { get; private set; }
    public string? FurnizorEmail { get; private set; }
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieNotaComanda> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieNotaComanda>>(LiniiJson) ?? [];
    public string? ConditiiJson { get; private set; }

    public ConditiiComanda Conditii =>
        ConditiiJson is null ? ConditiiComanda.Niciuna : JsonSerializer.Deserialize<ConditiiComanda>(ConditiiJson, Json) ?? ConditiiComanda.Niciuna;

    public string? Observatii { get; private set; }
    public SursaDocument Sursa { get; private set; }
    public Guid? DocumentOriginalId { get; private set; }
    public Guid? NecesarId { get; private set; }

    /// <summary>Șablonul Word al firmei activ la emitere (documentul se regenerează mereu cu aceeași versiune).</summary>
    public Guid? SablonId { get; private set; }

    public int? VersiuneSablon { get; private set; }
    public Guid InregistrataDeId { get; private set; }
    public string InregistrataDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;

    /// <summary>Notă de comandă emisă în sistem; Word-ul se generează din șablonul firmei (dacă există).</summary>
    public static Rezultat<NotaComanda> Emite(
        int an, int numar, DateNotaComanda date, Guid? sablonId, int? versiuneSablon, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = Valideaza(an, numar, date, utilizatorId, utilizatorNume);
        if ((sablonId is null) != (versiuneSablon is null))
            probleme.Add("Șablonul și versiunea lui se dau împreună.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare(string.Join(" ", probleme)));

        var nota = Construieste(an, numar, date, SursaDocument.Sistem, null, utilizatorId, utilizatorNume);
        nota.SablonId = sablonId;
        nota.VersiuneSablon = versiuneSablon;
        return Rezultat.Ok(nota);
    }

    /// <summary>Notă de comandă emisă în afara sistemului, cu originalul arhivat.</summary>
    public static Rezultat<NotaComanda> Importa(
        int an, int numar, DateNotaComanda date, Guid documentOriginalId, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = Valideaza(an, numar, date, utilizatorId, utilizatorNume);
        if (documentOriginalId == Guid.Empty)
            probleme.Add("Documentul original este obligatoriu pentru o notă de comandă importată.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare(string.Join(" ", probleme)));

        return Rezultat.Ok(Construieste(an, numar, date, SursaDocument.Import, documentOriginalId, utilizatorId, utilizatorNume));
    }

    private static List<string> Valideaza(int an, int numar, DateNotaComanda date, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = new List<string>();
        if (an < 2000 || numar < 1)
            probleme.Add("Numărul notei de comandă nu este valid.");
        if (date.NumarExtern?.Length > 50)
            probleme.Add("Numărul de pe comandă nu poate depăși 50 de caractere.");
        if (date.Data.Year < 2000)
            probleme.Add("Data notei de comandă nu este validă.");
        if (date.ProiectId == Guid.Empty || string.IsNullOrWhiteSpace(date.ProiectCod))
            probleme.Add("Proiectul este obligatoriu (pentru stoc general, folosește proiectul de stoc al firmei).");
        if (string.IsNullOrWhiteSpace(date.FurnizorDenumire) || date.FurnizorDenumire.Length > 300)
            probleme.Add("Furnizorul este obligatoriu.");
        if (!string.IsNullOrWhiteSpace(date.FurnizorCui) && !ValidareFiscala.EsteCuiValid(date.FurnizorCui))
            probleme.Add($"CUI-ul furnizorului „{date.FurnizorCui}” nu este valid.");
        if (date.FurnizorAdresa?.Length > 500 || date.FurnizorTelefon?.Length > 100 || date.FurnizorEmail?.Length > 200)
            probleme.Add("Datele de contact ale furnizorului sunt prea lungi.");
        if (date.Linii.Count == 0 || date.Linii.Any(l => string.IsNullOrWhiteSpace(l.Denumire) || string.IsNullOrWhiteSpace(l.UnitateMasura) || l.Cantitate <= 0))
            probleme.Add("Nota de comandă trebuie să conțină materiale cu denumire, UM și cantitate pozitivă.");
        if (date.Linii.Any(l => l.Denumire?.Length > 300 || l.UnitateMasura?.Length > 30 || l.Termen?.Length > 100))
            probleme.Add("Denumirea materialului (300), UM-ul (30) sau termenul (100 de caractere) sunt prea lungi.");
        if (date.Observatii?.Length > 1000)
            probleme.Add("Observațiile nu pot depăși 1000 de caractere.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            probleme.Add("Utilizatorul care înregistrează nota de comandă este obligatoriu.");
        return probleme;
    }

    private static NotaComanda Construieste(
        int an, int numar, DateNotaComanda date, SursaDocument sursa, Guid? documentOriginalId, Guid utilizatorId, string utilizatorNume) => new()
    {
        An = an,
        Numar = numar,
        NumarExtern = Gol(date.NumarExtern),
        Data = date.Data,
        ProiectId = date.ProiectId,
        ProiectCod = date.ProiectCod.Trim(),
        Lucrare = Gol(date.Lucrare) is { } lucrare ? lucrare[..Math.Min(lucrare.Length, 500)] : null,
        FurnizorId = date.FurnizorId,
        FurnizorDenumire = date.FurnizorDenumire.Trim(),
        FurnizorCui = string.IsNullOrWhiteSpace(date.FurnizorCui) ? null : ValidareFiscala.NormalizeazaCui(date.FurnizorCui),
        FurnizorAdresa = Gol(date.FurnizorAdresa),
        FurnizorTelefon = Gol(date.FurnizorTelefon),
        FurnizorEmail = Gol(date.FurnizorEmail),
        LiniiJson = JsonSerializer.Serialize(date.Linii.Select(l => l with
        {
            Denumire = l.Denumire.Trim(),
            UnitateMasura = l.UnitateMasura.Trim(),
            Termen = Gol(l.Termen),
        }).ToList()),
        ConditiiJson = date.Conditii is null || date.Conditii == ConditiiComanda.Niciuna ? null : JsonSerializer.Serialize(date.Conditii, Json),
        Observatii = Gol(date.Observatii),
        Sursa = sursa,
        DocumentOriginalId = documentOriginalId,
        NecesarId = date.NecesarId,
        InregistrataDeId = utilizatorId,
        InregistrataDeNume = utilizatorNume.Trim(),
    };

    private static string? Gol(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
