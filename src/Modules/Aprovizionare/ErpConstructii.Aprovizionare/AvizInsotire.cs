using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Aprovizionare;

/// <summary>Un material de pe aviz (avizul nu are prețuri: valoarea vine din factură, D-40).</summary>
public sealed record LinieAviz(string Denumire, string UnitateMasura, decimal Cantitate);

/// <summary>Datele avizului, verificate de utilizator la import.</summary>
/// <param name="NumarExtern">Seria și numărul de pe aviz („INTFSN/20488”, „OTL-220”).</param>
public sealed record DateAviz(
    string NumarExtern,
    DateOnly Data,
    Guid ProiectId,
    string ProiectCod,
    string? Lucrare,
    Guid? FurnizorId,
    string FurnizorDenumire,
    string? FurnizorCui,
    IReadOnlyList<LinieAviz> Linii,
    string? Observatii);

/// <summary>
/// Aviz de însoțire a mărfii primit de la furnizor (D-40): marfa a sosit înaintea facturii. Se importă (scanare / PDF / Word)
/// cu originalul arhivat și primește număr de sistem AV/AAAA/NNNN. Se leagă de nota de comandă căreia îi aparține (cea pomenită
/// pe aviz, dacă există, altfel una generată din aviz), apoi se recepționează în stoc prin NIR (fără valoare, sau la prețul
/// comenzii). Factura care vine ulterior se leagă de aceeași notă de comandă, iar NIR-urile avizului primesc prețurile ei.
/// Imuabil (regula 6); se poate șterge doar cât nu are recepții.
/// Avizul „din urmă” (D-45, import din arhivă) poate rămâne fără notă de comandă: nu i se mai generează una; factura lui
/// îl găsește după furnizor și proiect.
/// </summary>
public sealed class AvizInsotire : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieAviz>? _linii;

    private AvizInsotire() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"AV/{An}/{Numar:D4}";
    public string NumarExtern { get; private set; } = string.Empty;
    public DateOnly Data { get; private set; }
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public string? Lucrare { get; private set; }
    public Guid? FurnizorId { get; private set; }
    public string FurnizorDenumire { get; private set; } = string.Empty;
    public string? FurnizorCui { get; private set; }
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieAviz> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieAviz>>(LiniiJson) ?? [];
    public string? Observatii { get; private set; }

    /// <summary>Nota de comandă căreia îi aparține marfa (aceeași notă leagă apoi factura). Null doar la avizele din urmă (D-45).</summary>
    public Guid? NotaComandaId { get; private set; }

    public string? NotaComandaNumar { get; private set; }

    /// <summary>Document din urmă (D-45): importat din arhivă, poate lipsi nota de comandă.</summary>
    public bool DinUrma { get; private set; }

    /// <summary>Nota a fost generată din aviz (nu exista o comandă pentru această marfă).</summary>
    public bool NotaComandaGenerata { get; private set; }

    public Guid DocumentOriginalId { get; private set; }
    public Guid InregistratDeId { get; private set; }
    public string InregistratDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;

    public static Rezultat<AvizInsotire> Importa(
        int an, int numar, DateAviz date, Guid? notaComandaId, string? notaComandaNumar, bool notaGenerata, Guid documentOriginalId,
        Guid utilizatorId, string utilizatorNume, bool dinUrma = false)
    {
        var probleme = Valideaza(date);
        if (an < 2000 || numar < 1)
            probleme.Add("Numărul avizului nu este valid.");
        if (notaComandaId is null ? !dinUrma : notaComandaId == Guid.Empty || string.IsNullOrWhiteSpace(notaComandaNumar))
            probleme.Add("Nota de comandă a avizului este obligatorie (lipsește doar la documentele din urmă).");
        if (notaComandaId is null && notaGenerata)
            probleme.Add("Nota generată din aviz lipsește.");
        if (documentOriginalId == Guid.Empty)
            probleme.Add("Documentul original este obligatoriu.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            probleme.Add("Utilizatorul care înregistrează avizul este obligatoriu.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<AvizInsotire>(Eroare.Validare(string.Join(" ", probleme)));

        return Rezultat.Ok(new AvizInsotire
        {
            An = an,
            Numar = numar,
            NumarExtern = date.NumarExtern.Trim(),
            Data = date.Data,
            ProiectId = date.ProiectId,
            ProiectCod = date.ProiectCod.Trim(),
            Lucrare = Gol(date.Lucrare) is { } l ? l[..Math.Min(l.Length, 500)] : null,
            FurnizorId = date.FurnizorId,
            FurnizorDenumire = date.FurnizorDenumire.Trim(),
            FurnizorCui = string.IsNullOrWhiteSpace(date.FurnizorCui) ? null : ValidareFiscala.NormalizeazaCui(date.FurnizorCui),
            LiniiJson = JsonSerializer.Serialize(date.Linii.Select(x => x with { Denumire = x.Denumire.Trim(), UnitateMasura = x.UnitateMasura.Trim() }).ToList()),
            Observatii = Gol(date.Observatii),
            NotaComandaId = notaComandaId,
            NotaComandaNumar = notaComandaId is null ? null : notaComandaNumar!.Trim(),
            NotaComandaGenerata = notaGenerata,
            DinUrma = dinUrma,
            DocumentOriginalId = documentOriginalId,
            InregistratDeId = utilizatorId,
            InregistratDeNume = utilizatorNume.Trim(),
        });
    }

    /// <summary>Verificările datelor (folosite și înainte de generarea notei de comandă).</summary>
    public static List<string> Valideaza(DateAviz date)
    {
        var probleme = new List<string>();
        if (string.IsNullOrWhiteSpace(date.NumarExtern) || date.NumarExtern.Trim().Length > 50)
            probleme.Add("Numărul de pe aviz este obligatoriu (cel mult 50 de caractere).");
        if (date.Data.Year < 2000)
            probleme.Add("Data avizului nu este validă.");
        if (date.ProiectId == Guid.Empty || string.IsNullOrWhiteSpace(date.ProiectCod))
            probleme.Add("Proiectul este obligatoriu.");
        if (string.IsNullOrWhiteSpace(date.FurnizorDenumire) || date.FurnizorDenumire.Length > 300)
            probleme.Add("Furnizorul este obligatoriu.");
        if (!string.IsNullOrWhiteSpace(date.FurnizorCui) && !ValidareFiscala.EsteCuiValid(date.FurnizorCui))
            probleme.Add($"CUI-ul furnizorului „{date.FurnizorCui}” nu este valid.");
        if (date.Linii.Count == 0 || date.Linii.Any(l => string.IsNullOrWhiteSpace(l.Denumire) || string.IsNullOrWhiteSpace(l.UnitateMasura) || l.Cantitate <= 0))
            probleme.Add("Avizul trebuie să conțină materiale cu denumire, UM și cantitate pozitivă.");
        if (date.Linii.Any(l => l.Denumire?.Length > 300 || l.UnitateMasura?.Length > 30))
            probleme.Add("Denumirea materialului (300) sau UM-ul (30 de caractere) sunt prea lungi.");
        if (date.Observatii?.Length > 1000)
            probleme.Add("Observațiile nu pot depăși 1000 de caractere.");
        return probleme;
    }

    /// <summary>Liniile avizului ca linii ale notei de comandă generate din el.</summary>
    public static IReadOnlyList<LinieNotaComanda> LiniiNota(DateAviz date) =>
        date.Linii.Select(l => new LinieNotaComanda(l.Denumire.Trim(), l.UnitateMasura.Trim(), l.Cantitate, null, null)).ToList();

    private static string? Gol(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ---------------------------------------------------------------- contractele publice (regula 1)

/// <param name="Index">Poziția liniei pe aviz (0, 1, …): cheia liniei la recepție.</param>
/// <param name="PretUnitar">Prețul din nota de comandă pentru același material, dacă îl are; altfel null (NIR fără valoare până la factură).</param>
public sealed record LinieAvizPentruReceptie(int Index, string Denumire, string UnitateMasura, decimal Cantitate, decimal? PretUnitar);

public sealed record AvizPentruReceptie(
    Guid Id,
    string Numar,
    string NumarExtern,
    DateOnly Data,
    Guid? FurnizorId,
    string FurnizorDenumire,
    Guid ProiectId,
    string ProiectCod,
    Guid? NotaComandaId,
    string? NotaComandaNumar,
    IReadOnlyList<LinieAvizPentruReceptie> Linii)
{
    /// <summary>„INTFSN/20488 (AV/2026/0001)”.</summary>
    public string Descriere => $"{NumarExtern} ({Numar})";
}

/// <summary>Contractul modulului Aprovizionare pentru Stocuri: avizele de recepționat și ale unei note de comandă.</summary>
public interface IInterogareAvize
{
    Task<IReadOnlyList<AvizPentruReceptie>> ToateAsync(CancellationToken ct = default);

    Task<AvizPentruReceptie?> PentruReceptieAsync(Guid avizId, CancellationToken ct = default);

    Task<IReadOnlyList<AvizPentruReceptie>> AleNotelorAsync(IReadOnlyCollection<Guid> noteComandaIds, CancellationToken ct = default);
}

/// <summary>O notă de comandă emisă, de legat de factura furnizorului.</summary>
/// <param name="Avize">Câte avize are (marfă venită înaintea facturii).</param>
public sealed record NotaComandaPentruFactura(
    Guid Id, string Numar, string? NumarExtern, DateOnly Data, Guid ProiectId, string ProiectCod, Guid? FurnizorId, string FurnizorDenumire,
    string? FurnizorCui, int Avize, string Materiale);

/// <summary>Contractul modulului Aprovizionare pentru Facturi: notele de comandă de care se leagă facturile (D-40).</summary>
public interface IInterogareNoteComanda
{
    Task<NotaComandaPentruFactura?> CautaAsync(Guid id, CancellationToken ct = default);

    /// <summary>Notele emise ale proiectului (toate proiectele dacă e null), cele mai noi primele.</summary>
    Task<IReadOnlyList<NotaComandaPentruFactura>> EmiseAsync(Guid? proiectId, CancellationToken ct = default);
}

/// <summary>Ce s-a întâmplat în stoc cu un aviz: câte NIR-uri active are și dacă au primit factura.</summary>
public sealed record StareStocAviz(int Receptii, bool Facturat, string? FacturaNumar);

/// <summary>Implementat de Stocuri: starea în stoc a avizelor care au recepții (NIR nestornate). Fără modulul Stocuri: niciuna.</summary>
public interface IInterogareReceptiiAviz
{
    Task<IReadOnlyDictionary<Guid, StareStocAviz>> StariAsync(CancellationToken ct = default);
}

/// <summary>Implementat de Facturi: câte facturi neanulate sunt legate de nota de comandă. Fără modulul Facturi: niciuna.</summary>
public interface IInterogareFacturiNotaComanda
{
    Task<int> FacturiAsync(Guid notaComandaId, CancellationToken ct = default);
}
