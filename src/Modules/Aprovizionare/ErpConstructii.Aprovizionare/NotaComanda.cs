using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Aprovizionare;

/// <param name="Termen">Termenul de livrare, așa cum e scris pe comandă (dată sau text, ex. „imediat”).</param>
/// <param name="PretUnitar">Prețul negociat, dacă apare pe comandă (formularul uzual nu îl are).</param>
public sealed record LinieNotaComanda(string Denumire, string UnitateMasura, decimal Cantitate, string? Termen, decimal? PretUnitar);

/// <summary>Datele unei comenzi către furnizor, verificate de utilizator.</summary>
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
    string? Observatii);

/// <summary>
/// Notă de comandă către furnizor (formularul GC 007): ce materiale se cer, cui și pentru ce lucrare. Urmează necesarului
/// de materiale aprobat și deschide lanțul notă de comandă → recepție (NIR) → factură, folosit la potrivirea în 3 puncte (REQ-FCT-09). Notele emise pe hârtie
/// se importă (scanare / Word): primesc număr de sistem NC/AAAA/NNNN și își păstrează numărul de pe document.
/// Imuabilă după înregistrare (regula 6).
/// </summary>
public sealed class NotaComanda : EntitateFirma, IDocumentCuStare
{
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
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieNotaComanda> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieNotaComanda>>(LiniiJson) ?? [];
    public string? Observatii { get; private set; }
    public SursaDocument Sursa { get; private set; }
    public Guid? DocumentOriginalId { get; private set; }
    public Guid InregistrataDeId { get; private set; }
    public string InregistrataDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;

    /// <summary>Notă de comandă emisă în afara sistemului, cu originalul arhivat.</summary>
    public static Rezultat<NotaComanda> Importa(
        int an, int numar, DateNotaComanda date, Guid documentOriginalId, Guid utilizatorId, string utilizatorNume)
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
        if (date.Linii.Count == 0 || date.Linii.Any(l => string.IsNullOrWhiteSpace(l.Denumire) || string.IsNullOrWhiteSpace(l.UnitateMasura) || l.Cantitate <= 0))
            probleme.Add("Nota de comandă trebuie să conțină materiale cu denumire, UM și cantitate pozitivă.");
        if (documentOriginalId == Guid.Empty)
            probleme.Add("Documentul original este obligatoriu pentru o notă de comandă importată.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            probleme.Add("Utilizatorul care înregistrează nota de comandă este obligatoriu.");
        if (probleme.Count > 0)
        {
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare(string.Join(" ", probleme)));
        }

        return Rezultat.Ok(new NotaComanda
        {
            An = an,
            Numar = numar,
            NumarExtern = string.IsNullOrWhiteSpace(date.NumarExtern) ? null : date.NumarExtern.Trim(),
            Data = date.Data,
            ProiectId = date.ProiectId,
            ProiectCod = date.ProiectCod.Trim(),
            Lucrare = string.IsNullOrWhiteSpace(date.Lucrare) ? null : date.Lucrare.Trim()[..Math.Min(date.Lucrare.Trim().Length, 500)],
            FurnizorId = date.FurnizorId,
            FurnizorDenumire = date.FurnizorDenumire.Trim(),
            FurnizorCui = string.IsNullOrWhiteSpace(date.FurnizorCui) ? null : ValidareFiscala.NormalizeazaCui(date.FurnizorCui),
            LiniiJson = JsonSerializer.Serialize(date.Linii.Select(l => l with
            {
                Denumire = l.Denumire.Trim(),
                UnitateMasura = l.UnitateMasura.Trim(),
                Termen = string.IsNullOrWhiteSpace(l.Termen) ? null : l.Termen.Trim(),
            }).ToList()),
            Observatii = string.IsNullOrWhiteSpace(date.Observatii) ? null : date.Observatii.Trim(),
            Sursa = SursaDocument.Import,
            DocumentOriginalId = documentOriginalId,
            InregistrataDeId = utilizatorId,
            InregistrataDeNume = utilizatorNume.Trim(),
        });
    }
}
