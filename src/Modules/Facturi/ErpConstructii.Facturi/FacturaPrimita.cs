using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Facturi;

/// <summary>De unde vine factura primită.</summary>
public enum SursaFactura
{
    /// <summary>XML-ul oficial din e-Factura (SPV): citit exact.</summary>
    EFactura,

    /// <summary>PDF (text digital sau scanare prin OCR), verificat de utilizator.</summary>
    Pdf,

    Manual,
}


/// <param name="CotaTva">Cota din document, ca fracție (0,21 pentru 21%); niciodată o constantă din cod (regula 5).</param>
/// <param name="IntraInStoc">Materialul intră în stoc (recepție / NIR în modulul Stocuri); serviciile (transport, taxe) nu.</param>
public sealed record LinieFactura(
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    decimal PretUnitar,
    decimal ValoareFaraTva,
    decimal CotaTva,
    decimal ValoareTva,
    bool IntraInStoc = true);

/// <summary>Datele unei facturi primite, verificate de utilizator, la înregistrare.</summary>
public sealed record DateFacturaPrimita(
    string Numar,
    DateOnly DataEmitere,
    DateOnly? DataScadenta,
    string Moneda,
    Guid? FurnizorId,
    string FurnizorDenumire,
    string? FurnizorCui,
    Guid ProiectId,
    string ProiectCod,
    IReadOnlyList<LinieFactura> Linii,
    decimal TotalFaraTva,
    decimal TotalTva,
    decimal TotalDePlata,
    string? Referinta,
    string? Observatii);

/// <summary>
/// Factură primită de la un furnizor (REQ-FCT-07/08): sursa documentelor de cost, înaintea intrării în stoc (NIR)
/// și a potrivirii comandă–NIR–factură (REQ-FCT-09). Are obligatoriu un proiect (REQ-GEN-01).
///
/// După înregistrare e imuabilă (REQ-GEN-06, regula 6): o înregistrare greșită se anulează printr-o entitate separată
/// (<see cref="AnulareFacturaPrimita"/>) și se reînregistrează; furnizorul emite storno pentru facturile greșite, care se
/// înregistrează tot ca facturi primite, cu valori negative.
/// Furnizorul e referit prin Id (modulul Parteneri), cu denumirea și CUI-ul păstrate ca la data facturii.
/// </summary>
public sealed class FacturaPrimita : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieFactura>? _linii;

    private FacturaPrimita() { }

    public string Numar { get; private set; } = string.Empty;
    public DateOnly DataEmitere { get; private set; }
    public DateOnly? DataScadenta { get; private set; }
    public string Moneda { get; private set; } = "RON";
    public Guid? FurnizorId { get; private set; }
    public string FurnizorDenumire { get; private set; } = string.Empty;
    public string? FurnizorCui { get; private set; }
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    /// <summary>Liniile, ca JSON (jsonb): documentul e imuabil și se citește mereu întreg.</summary>
    public string LiniiJson { get; private set; } = "[]";

    public IReadOnlyList<LinieFactura> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieFactura>>(LiniiJson) ?? [];
    public decimal TotalFaraTva { get; private set; }
    public decimal TotalTva { get; private set; }
    public decimal TotalDePlata { get; private set; }

    /// <summary>Comanda sau alt document la care se referă factura (din XML: OrderReference; din PDF: textul găsit).</summary>
    public string? Referinta { get; private set; }

    public string? Observatii { get; private set; }
    public SursaFactura Sursa { get; private set; }

    /// <summary>XML-ul e-Factura și/sau PDF-ul original, arhivate neschimbate.</summary>
    public Guid? DocumentXmlId { get; private set; }

    public Guid? DocumentPdfId { get; private set; }
    public Guid InregistrataDeId { get; private set; }
    public string InregistrataDeNume { get; private set; } = string.Empty;

    /// <summary>Storno: total negativ (factură de corecție emisă de furnizor).</summary>
    public bool EsteStorno => TotalDePlata < 0;

    public bool EsteBlocat => true;

    public static Rezultat<FacturaPrimita> Inregistreaza(
        DateFacturaPrimita date,
        SursaFactura sursa,
        Guid? documentXmlId,
        Guid? documentPdfId,
        Guid utilizatorId,
        string utilizatorNume)
    {
        if (ProblemeObligatorii(date) is { Count: > 0 } probleme)
        {
            return Rezultat.Esuat<FacturaPrimita>(Eroare.Validare(string.Join(" ", probleme)));
        }

        if (documentXmlId is null && documentPdfId is null && sursa != SursaFactura.Manual)
        {
            return Rezultat.Esuat<FacturaPrimita>(Eroare.Validare("Documentul original (XML sau PDF) este obligatoriu."));
        }

        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
        {
            return Rezultat.Esuat<FacturaPrimita>(Eroare.Validare("Utilizatorul care înregistrează factura este obligatoriu."));
        }

        return Rezultat.Ok(new FacturaPrimita
        {
            Numar = date.Numar.Trim(),
            DataEmitere = date.DataEmitere,
            DataScadenta = date.DataScadenta,
            Moneda = date.Moneda.Trim().ToUpperInvariant(),
            FurnizorId = date.FurnizorId,
            FurnizorDenumire = date.FurnizorDenumire.Trim(),
            FurnizorCui = string.IsNullOrWhiteSpace(date.FurnizorCui) ? null : ValidareFiscala.NormalizeazaCui(date.FurnizorCui),
            ProiectId = date.ProiectId,
            ProiectCod = date.ProiectCod.Trim(),
            LiniiJson = JsonSerializer.Serialize(date.Linii.Select(l => l with { Denumire = l.Denumire.Trim(), UnitateMasura = l.UnitateMasura.Trim() }).ToList()),
            TotalFaraTva = date.TotalFaraTva,
            TotalTva = date.TotalTva,
            TotalDePlata = date.TotalDePlata,
            Referinta = string.IsNullOrWhiteSpace(date.Referinta) ? null : date.Referinta.Trim(),
            Observatii = string.IsNullOrWhiteSpace(date.Observatii) ? null : date.Observatii.Trim(),
            Sursa = sursa,
            DocumentXmlId = documentXmlId,
            DocumentPdfId = documentPdfId,
            InregistrataDeId = utilizatorId,
            InregistrataDeNume = utilizatorNume.Trim(),
        });
    }

    private static List<string> ProblemeObligatorii(DateFacturaPrimita d)
    {
        var p = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Numar) || d.Numar.Length > 50)
            p.Add("Numărul facturii este obligatoriu (maxim 50 de caractere).");
        if (d.DataEmitere.Year < 2000)
            p.Add("Data facturii nu este validă.");
        if (d.DataScadenta is { } s && s < d.DataEmitere)
            p.Add("Data scadenței este înaintea datei facturii.");
        if (string.IsNullOrWhiteSpace(d.FurnizorDenumire) || d.FurnizorDenumire.Length > 300)
            p.Add("Furnizorul este obligatoriu.");
        if (!string.IsNullOrWhiteSpace(d.FurnizorCui) && !ValidareFiscala.EsteCuiValid(d.FurnizorCui))
            p.Add($"CUI-ul furnizorului „{d.FurnizorCui}” nu este valid.");
        if (d.ProiectId == Guid.Empty || string.IsNullOrWhiteSpace(d.ProiectCod))
            p.Add("Proiectul este obligatoriu: orice document de cost se leagă de un proiect.");
        if (string.IsNullOrWhiteSpace(d.Moneda) || d.Moneda.Trim().Length != 3)
            p.Add("Moneda trebuie să fie un cod de 3 litere (ex. RON).");
        if (d.Linii.Count == 0)
            p.Add("Factura trebuie să aibă cel puțin o linie.");
        if (d.Linii.Any(l => string.IsNullOrWhiteSpace(l.Denumire) || l.Cantitate == 0 || l.CotaTva is < 0 or > 1))
            p.Add("Fiecare linie trebuie să aibă denumire, cantitate nenulă și cotă TVA între 0 și 100%.");
        return p;
    }
}

/// <summary>
/// Anularea unei facturi primite înregistrate greșit (ex. pe alt proiect). Entitate separată: factura, ca document
/// înregistrat, nu se modifică (regula 6); anularea spune cine, când și de ce a scos-o din evidență.
/// </summary>
public sealed class AnulareFacturaPrimita : EntitateFirma
{
    private AnulareFacturaPrimita() { }

    public Guid FacturaId { get; private set; }
    public string Motiv { get; private set; } = string.Empty;
    public Guid AnulataDeId { get; private set; }
    public string AnulataDeNume { get; private set; } = string.Empty;
    public DateTimeOffset AnulataLa { get; private set; }

    public static Rezultat<AnulareFacturaPrimita> Creeaza(Guid facturaId, string motiv, Guid utilizatorId, string utilizatorNume, DateTimeOffset moment)
    {
        if (facturaId == Guid.Empty)
        {
            return Rezultat.Esuat<AnulareFacturaPrimita>(Eroare.Validare("Factura este obligatorie."));
        }

        if (string.IsNullOrWhiteSpace(motiv) || motiv.Length > 1000)
        {
            return Rezultat.Esuat<AnulareFacturaPrimita>(Eroare.Validare("Motivul anulării este obligatoriu (maxim 1000 de caractere)."));
        }

        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
        {
            return Rezultat.Esuat<AnulareFacturaPrimita>(Eroare.Validare("Utilizatorul care anulează este obligatoriu."));
        }

        return Rezultat.Ok(new AnulareFacturaPrimita
        {
            FacturaId = facturaId,
            Motiv = motiv.Trim(),
            AnulataDeId = utilizatorId,
            AnulataDeNume = utilizatorNume.Trim(),
            AnulataLa = moment,
        });
    }
}

/// <summary>
/// Verificările de consistență ale unei facturi (totaluri față de linii). Nu blochează înregistrarea: factura e ce a
/// emis furnizorul; diferențele se arată utilizatorului (pot fi greșeli de citire sau rotunjiri ale furnizorului).
/// </summary>
public static class VerificareTotaluriFactura
{
    /// <summary>Toleranța de rotunjire: 1 ban pe linie, cel puțin 1 ban.</summary>
    public static decimal Toleranta(int numarLinii) => Math.Max(0.01m, 0.01m * numarLinii);

    public static IReadOnlyList<string> Diferente(IReadOnlyList<LinieFactura> linii, decimal totalFaraTva, decimal totalTva, decimal totalDePlata)
    {
        var diferente = new List<string>();
        var sumaLinii = linii.Sum(l => l.ValoareFaraTva);
        var sumaTva = linii.Sum(l => l.ValoareTva);
        var toleranta = Toleranta(linii.Count);

        if (Math.Abs(sumaLinii - totalFaraTva) > toleranta)
            diferente.Add($"Suma liniilor ({sumaLinii:N2}) diferă de totalul fără TVA ({totalFaraTva:N2}).");
        if (Math.Abs(sumaTva - totalTva) > toleranta)
            diferente.Add($"TVA-ul liniilor ({sumaTva:N2}) diferă de TVA-ul total ({totalTva:N2}).");
        if (Math.Abs(totalFaraTva + totalTva - totalDePlata) > 0.01m)
            diferente.Add($"Total fără TVA + TVA ({totalFaraTva + totalTva:N2}) diferă de totalul de plată ({totalDePlata:N2}); poate există un avans dedus.");
        return diferente;
    }

    /// <summary>TVA-ul unei linii, rotunjit la ban (jumătate departe de zero).</summary>
    public static decimal TvaLinie(decimal valoareFaraTva, decimal cota) => Rotunjire.Lei.Aplica(valoareFaraTva * cota);
}
