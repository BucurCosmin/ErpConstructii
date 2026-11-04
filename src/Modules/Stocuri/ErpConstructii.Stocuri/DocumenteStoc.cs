using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

/// <param name="IndexFactura">Poziția liniei în factură.</param>
/// <param name="CantitateFactura">Cantitatea de pe factură.</param>
/// <param name="CantitateReceptionata">Ce a primit efectiv gestionarul (diferența = lipsă la recepție).</param>
/// <param name="PretUnitar">Prețul fără TVA de pe factură.</param>
/// <param name="Valoare">Cantitatea recepționată × prețul, rotunjită la ban: valoarea intrată în stoc.</param>
public sealed record LinieNir(
    int IndexFactura,
    Guid ArticolId,
    string ArticolCod,
    string Denumire,
    string UnitateMasura,
    decimal CantitateFactura,
    decimal CantitateReceptionata,
    decimal PretUnitar,
    decimal Valoare,
    string? Observatii)
{
    public decimal Diferenta => CantitateReceptionata - CantitateFactura;
}

public sealed record LinieNirCerere(int IndexFactura, Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura,
    decimal CantitateFactura, decimal CantitateReceptionata, decimal PretUnitar, string? Observatii);

/// <summary>
/// Nota de intrare-recepție: gestionarul confirmă ce a primit din factura unui furnizor, în depozitul ales.
/// La emitere, materialele intră în stoc la prețul facturii. Imuabilă (regula 6); o singură recepție pe factură.
/// </summary>
public sealed class Nir : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieNir>? _linii;

    private Nir() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"NIR/{An}/{Numar:D4}";
    public DateOnly Data { get; private set; }
    public Guid GestiuneId { get; private set; }
    public string GestiuneDenumire { get; private set; } = string.Empty;
    public Guid FacturaId { get; private set; }
    public string FacturaNumar { get; private set; } = string.Empty;
    public DateOnly FacturaData { get; private set; }
    public Guid? FurnizorId { get; private set; }
    public string FurnizorDenumire { get; private set; } = string.Empty;

    /// <summary>Proiectul pe care s-a înregistrat factura (informativ: costul ajunge pe proiect la transferul în șantier).</summary>
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieNir> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieNir>>(LiniiJson) ?? [];
    public decimal Valoare { get; private set; }
    public string? Observatii { get; private set; }
    public Guid ReceptionatDeId { get; private set; }
    public string ReceptionatDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;
    public bool AreDiferente => Linii.Any(l => l.Diferenta != 0);

    public static Rezultat<Nir> Emite(
        int an, int numar, DateOnly data, Gestiune gestiune,
        Guid facturaId, string facturaNumar, DateOnly facturaData, Guid? furnizorId, string furnizorDenumire, Guid proiectId, string proiectCod,
        IReadOnlyList<LinieNirCerere> linii, string? observatii, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = new List<string>();
        if (an < 2000 || numar < 1) probleme.Add("Numărul NIR nu este valid.");
        if (gestiune.Tip != TipGestiune.Depozit || !gestiune.Activa) probleme.Add("Recepția se face într-un depozit activ.");
        if (data < facturaData) probleme.Add("Data recepției nu poate fi înaintea datei facturii.");
        if (facturaId == Guid.Empty || string.IsNullOrWhiteSpace(facturaNumar)) probleme.Add("Factura este obligatorie.");
        if (linii.Count == 0) probleme.Add("Recepția trebuie să conțină cel puțin un material.");
        if (linii.Any(l => l.ArticolId == Guid.Empty)) probleme.Add("Fiecare linie trebuie legată de un articol de stoc.");
        if (linii.Any(l => l.CantitateReceptionata < 0 || l.CantitateFactura <= 0 || l.PretUnitar < 0)) probleme.Add("Cantitățile și prețurile nu pot fi negative.");
        if (linii.All(l => l.CantitateReceptionata == 0)) probleme.Add("Nu s-a recepționat nimic; verifică cantitățile.");
        if (linii.Any(l => l.CantitateReceptionata != l.CantitateFactura && string.IsNullOrWhiteSpace(l.Observatii)))
            probleme.Add("La liniile cu diferențe față de factură, completează observația (ex. lipsă la livrare).");
        if (linii.GroupBy(l => l.IndexFactura).Any(g => g.Count() > 1)) probleme.Add("O linie de factură apare de două ori.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume)) probleme.Add("Gestionarul este obligatoriu.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<Nir>(Eroare.Validare(string.Join(" ", probleme)));

        var liniiNir = linii.Select(l => new LinieNir(
            l.IndexFactura, l.ArticolId, l.ArticolCod, l.Denumire.Trim(), l.UnitateMasura.Trim(), l.CantitateFactura, l.CantitateReceptionata,
            l.PretUnitar, Rotunjire.Lei.Aplica(l.CantitateReceptionata * l.PretUnitar), string.IsNullOrWhiteSpace(l.Observatii) ? null : l.Observatii.Trim())).ToList();

        return Rezultat.Ok(new Nir
        {
            An = an,
            Numar = numar,
            Data = data,
            GestiuneId = gestiune.Id,
            GestiuneDenumire = gestiune.Denumire,
            FacturaId = facturaId,
            FacturaNumar = facturaNumar.Trim(),
            FacturaData = facturaData,
            FurnizorId = furnizorId,
            FurnizorDenumire = furnizorDenumire.Trim(),
            ProiectId = proiectId,
            ProiectCod = proiectCod,
            LiniiJson = JsonSerializer.Serialize(liniiNir),
            Valoare = liniiNir.Sum(l => l.Valoare),
            Observatii = string.IsNullOrWhiteSpace(observatii) ? null : observatii.Trim(),
            ReceptionatDeId = utilizatorId,
            ReceptionatDeNume = utilizatorNume.Trim(),
        });
    }
}

/// <param name="PretUnitar">CMP-ul din gestiunea sursă la momentul transferului.</param>
public sealed record LinieTransfer(Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal Valoare);

/// <summary>
/// Bon de transfer: materialele pleacă din depozit în „Materiale în șantier” ale unui proiect, la CMP.
/// Imuabil (regula 6). Valorile liniilor se calculează la emitere, sub blocarea stocului firmei.
/// </summary>
public sealed class TransferStoc : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieTransfer>? _linii;

    private TransferStoc() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"BT/{An}/{Numar:D4}";
    public DateOnly Data { get; private set; }
    public Guid GestiuneSursaId { get; private set; }
    public string GestiuneSursaDenumire { get; private set; } = string.Empty;
    public Guid GestiuneDestinatieId { get; private set; }
    public string GestiuneDestinatieDenumire { get; private set; } = string.Empty;
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieTransfer> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieTransfer>>(LiniiJson) ?? [];
    public decimal Valoare { get; private set; }
    public string? Observatii { get; private set; }
    public Guid EmisDeId { get; private set; }
    public string EmisDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;

    public static Rezultat<TransferStoc> Emite(
        int an, int numar, DateOnly data, Gestiune sursa, Gestiune destinatie, string proiectCod,
        IReadOnlyList<LinieTransfer> linii, string? observatii, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = new List<string>();
        if (an < 2000 || numar < 1) probleme.Add("Numărul bonului de transfer nu este valid.");
        if (sursa.Tip != TipGestiune.Depozit) probleme.Add("Transferul pleacă dintr-un depozit.");
        if (destinatie.Tip != TipGestiune.Santier || destinatie.ProiectId is null) probleme.Add("Transferul ajunge în gestiunea de șantier a unui proiect.");
        if (linii.Count == 0) probleme.Add("Transferul trebuie să conțină cel puțin un material.");
        if (linii.Any(l => l.Cantitate <= 0)) probleme.Add("Cantitățile transferate trebuie să fie pozitive.");
        if (linii.GroupBy(l => l.ArticolId).Any(g => g.Count() > 1)) probleme.Add("Un articol apare de două ori; cumulează cantitatea.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume)) probleme.Add("Utilizatorul este obligatoriu.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<TransferStoc>(Eroare.Validare(string.Join(" ", probleme)));

        return Rezultat.Ok(new TransferStoc
        {
            An = an,
            Numar = numar,
            Data = data,
            GestiuneSursaId = sursa.Id,
            GestiuneSursaDenumire = sursa.Denumire,
            GestiuneDestinatieId = destinatie.Id,
            GestiuneDestinatieDenumire = destinatie.Denumire,
            ProiectId = destinatie.ProiectId!.Value,
            ProiectCod = proiectCod,
            LiniiJson = JsonSerializer.Serialize(linii),
            Valoare = linii.Sum(l => l.Valoare),
            Observatii = string.IsNullOrWhiteSpace(observatii) ? null : observatii.Trim(),
            EmisDeId = utilizatorId,
            EmisDeNume = utilizatorNume.Trim(),
        });
    }
}

public enum TipMiscare
{
    IntrareNir,
    IesireTransfer,
    IntrareTransfer,
}

/// <summary>
/// O mișcare în fișa de magazie (append-only): cantitatea și valoarea cu semn (+ intrare, − ieșire) și documentul.
/// Soldul unei gestiuni = suma mișcărilor; <see cref="SoldStoc"/> îl ține calculat.
/// </summary>
public sealed class MiscareStoc : EntitateFirma
{
    private MiscareStoc() { }

    public MiscareStoc(Guid gestiuneId, Guid articolId, DateOnly data, TipMiscare tip, decimal cantitate, decimal valoare, Guid documentId, string documentNumar)
    {
        (GestiuneId, ArticolId, Data, Tip, Cantitate, Valoare, DocumentId, DocumentNumar) =
            (gestiuneId, articolId, data, tip, cantitate, valoare, documentId, documentNumar);
    }

    public Guid GestiuneId { get; private set; }
    public Guid ArticolId { get; private set; }
    public DateOnly Data { get; private set; }
    public TipMiscare Tip { get; private set; }
    public decimal Cantitate { get; private set; }
    public decimal Valoare { get; private set; }
    public Guid DocumentId { get; private set; }
    public string DocumentNumar { get; private set; } = string.Empty;
}

/// <summary>Soldul curent al unui articol într-o gestiune (actualizat în aceeași tranzacție cu mișcarea).</summary>
public sealed class SoldStoc : EntitateFirma
{
    private SoldStoc() { }

    public SoldStoc(Guid gestiuneId, Guid articolId) => (GestiuneId, ArticolId) = (gestiuneId, articolId);

    public Guid GestiuneId { get; private set; }
    public Guid ArticolId { get; private set; }
    public decimal Cantitate { get; private set; }
    public decimal Valoare { get; private set; }

    public Sold Sold => new(Cantitate, Valoare);

    public void Seteaza(Sold sold) => (Cantitate, Valoare) = (sold.Cantitate, sold.Valoare);
}
