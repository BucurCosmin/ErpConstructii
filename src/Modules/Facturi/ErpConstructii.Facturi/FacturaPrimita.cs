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
    string? Observatii,
    Guid? NotaComandaId = null,
    string? NotaComandaNumar = null);

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

    /// <summary>
    /// Nota de comandă căreia îi aparține factura (D-40): obligatorie la facturile cu materiale de stoc înregistrate de acum
    /// înainte. Prin ea se găsesc avizele cu marfă deja recepționată, care primesc prețurile facturii.
    /// </summary>
    public Guid? NotaComandaId { get; private set; }

    public string? NotaComandaNumar { get; private set; }

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
            NotaComandaId = date.NotaComandaId,
            NotaComandaNumar = date.NotaComandaId is null ? null : date.NotaComandaNumar?.Trim(),
            Sursa = sursa,
            DocumentXmlId = documentXmlId,
            DocumentPdfId = documentPdfId,
            InregistrataDeId = utilizatorId,
            InregistrataDeNume = utilizatorNume.Trim(),
        });
    }

    /// <summary>Datele curente ale facturii (pentru corectură și istoric).</summary>
    public DateFacturaPrimita Date() => new(
        Numar, DataEmitere, DataScadenta, Moneda, FurnizorId, FurnizorDenumire, FurnizorCui, ProiectId, ProiectCod, Linii,
        TotalFaraTva, TotalTva, TotalDePlata, Referinta, Observatii, NotaComandaId, NotaComandaNumar);

    /// <summary>
    /// Corectează datele facturii (citite / introduse greșit) și întoarce corectura, care se salvează împreună cu factura.
    /// Orice câmp se poate corecta. Liniile intrate deja în stoc (<paramref name="liniiReceptionate"/>, din NIR-uri active) nu se
    /// pot șterge și rămân „intră în stoc”; ce rămâne de verificat în NIR spune <see cref="AvertismenteStoc"/>.
    /// </summary>
    public Rezultat<CorectieFacturaPrimita> Corecteaza(
        DateFacturaPrimita date, string motiv, IReadOnlySet<int> liniiReceptionate, Guid utilizatorId, string utilizatorNume, DateTimeOffset moment)
    {
        if (string.IsNullOrWhiteSpace(motiv) || motiv.Length > 1000)
            return Rezultat.Esuat<CorectieFacturaPrimita>(Eroare.Validare("Motivul corecturii este obligatoriu (maxim 1000 de caractere)."));
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            return Rezultat.Esuat<CorectieFacturaPrimita>(Eroare.Validare("Utilizatorul care corectează este obligatoriu."));
        if (ProblemeObligatorii(date) is { Count: > 0 } probleme)
            return Rezultat.Esuat<CorectieFacturaPrimita>(Eroare.Validare(string.Join(" ", probleme)));

        var nou = Normalizeaza(date);
        var vechi = Date();
        // Orice câmp se poate corecta, și pe liniile intrate în stoc: NIR-ul își păstrează cantitatea și prețul lui (copiate la
        // recepție), iar starea recepției se recalculează din factura corectată. NIR-ul se leagă de linie prin poziție, deci
        // liniile existente nu se șterg, iar cele recepționate rămân „intră în stoc”.
        if (liniiReceptionate.Count > 0 && nou.Linii.Count < vechi.Linii.Count)
            return Rezultat.Esuat<CorectieFacturaPrimita>(new Eroare("conflict",
                "Factura are linii intrate în stoc (NIR), legate de poziția lor: liniile existente nu se pot șterge, doar corecta (se pot adăuga linii noi). Stornează întâi NIR-ul."));
        foreach (var i in liniiReceptionate.Order())
        {
            if (!nou.Linii[i].IntraInStoc)
                return Rezultat.Esuat<CorectieFacturaPrimita>(new Eroare("conflict",
                    $"Linia {i + 1} („{vechi.Linii[i].Denumire}”) a intrat în stoc (NIR): rămâne „intră în stoc”. Stornează întâi NIR-ul."));
        }

        // Comparația pe valori (175 = 175,00), nu pe text: rezumatul gol = nimic schimbat.
        var rezumat = RezumatCorectura(vechi, nou);
        if (rezumat.Length == 0)
            return Rezultat.Esuat<CorectieFacturaPrimita>(Eroare.Validare("Nu ai modificat nimic."));
        var jsonVechi = JsonSerializer.Serialize(vechi);
        var jsonNou = JsonSerializer.Serialize(nou);
        (Numar, DataEmitere, DataScadenta, Moneda, FurnizorId, FurnizorDenumire, FurnizorCui, ProiectId, ProiectCod) =
            (nou.Numar, nou.DataEmitere, nou.DataScadenta, nou.Moneda, nou.FurnizorId, nou.FurnizorDenumire, nou.FurnizorCui, nou.ProiectId, nou.ProiectCod);
        LiniiJson = JsonSerializer.Serialize(nou.Linii);
        _linii = null;
        (TotalFaraTva, TotalTva, TotalDePlata, Referinta, Observatii) = (nou.TotalFaraTva, nou.TotalTva, nou.TotalDePlata, nou.Referinta, nou.Observatii);
        (NotaComandaId, NotaComandaNumar) = (nou.NotaComandaId, nou.NotaComandaId is null ? null : nou.NotaComandaNumar);
        return Rezultat.Ok(CorectieFacturaPrimita.Creeaza(Id, motiv, jsonVechi, jsonNou, rezumat, utilizatorId, utilizatorNume, moment));
    }

    private static DateFacturaPrimita Normalizeaza(DateFacturaPrimita d) => d with
    {
        Numar = d.Numar.Trim(),
        Moneda = d.Moneda.Trim().ToUpperInvariant(),
        FurnizorDenumire = d.FurnizorDenumire.Trim(),
        FurnizorCui = string.IsNullOrWhiteSpace(d.FurnizorCui) ? null : ValidareFiscala.NormalizeazaCui(d.FurnizorCui),
        ProiectCod = d.ProiectCod.Trim(),
        Linii = d.Linii.Select(l => l with { Denumire = l.Denumire.Trim(), UnitateMasura = l.UnitateMasura.Trim() }).ToList(),
        Referinta = string.IsNullOrWhiteSpace(d.Referinta) ? null : d.Referinta.Trim(),
        Observatii = string.IsNullOrWhiteSpace(d.Observatii) ? null : d.Observatii.Trim(),
    };

    private static bool AceeasiLinieDeStoc(LinieFactura a, LinieFactura b) =>
        a.Denumire.Trim() == b.Denumire.Trim() && a.UnitateMasura.Trim() == b.UnitateMasura.Trim() && a.Cantitate == b.Cantitate
        && a.PretUnitar == b.PretUnitar && a.ValoareFaraTva == b.ValoareFaraTva && a.IntraInStoc == b.IntraInStoc;

    /// <summary>
    /// Ce rămâne de verificat în stoc după corectură: liniile recepționate al căror material, cantitate sau preț s-a schimbat
    /// (NIR-ul rămâne cu valorile lui) și proiectul schimbat după recepție.
    /// </summary>
    public static IReadOnlyList<string> AvertismenteStoc(DateFacturaPrimita vechi, DateFacturaPrimita nou, IReadOnlySet<int> liniiReceptionate)
    {
        var avertismente = new List<string>();
        foreach (var i in liniiReceptionate.Order().Where(i => i < vechi.Linii.Count && i < nou.Linii.Count))
        {
            if (!AceeasiLinieDeStoc(vechi.Linii[i], nou.Linii[i]))
                avertismente.Add($"Linia {i + 1} („{nou.Linii[i].Denumire.Trim()}”) e recepționată: NIR-ul și stocul rămân cu cantitatea și prețul din NIR. " +
                    "Dacă și recepția e greșită, corectează NIR-ul (Stocuri → Recepții → Registru NIR): stocul primește diferența.");
        }

        if (liniiReceptionate.Count > 0 && nou.ProiectId != vechi.ProiectId)
            avertismente.Add("Proiectul s-a schimbat după recepție: NIR-urile existente rămân pe proiectul vechi.");
        return avertismente;
    }

    /// <summary>Ce s-a schimbat, în cuvinte (pentru istoric).</summary>
    public static string RezumatCorectura(DateFacturaPrimita vechi, DateFacturaPrimita nou)
    {
        var ro = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");
        string Bani(decimal v) => v.ToString("N2", ro);
        string Cant(decimal v) => v.ToString("#,##0.####", ro);
        string Procent(decimal v) => (v * 100).ToString("0.##", ro) + "%";
        var p = new List<string>();
        void Camp(string nume, string? a, string? b)
        {
            if (a != b) p.Add($"{nume}: {a ?? "—"} → {b ?? "—"}");
        }

        Camp("Număr", vechi.Numar, nou.Numar);
        Camp("Data", vechi.DataEmitere.ToString("dd.MM.yyyy", ro), nou.DataEmitere.ToString("dd.MM.yyyy", ro));
        Camp("Scadența", vechi.DataScadenta?.ToString("dd.MM.yyyy", ro), nou.DataScadenta?.ToString("dd.MM.yyyy", ro));
        Camp("Moneda", vechi.Moneda, nou.Moneda);
        Camp("Furnizor", vechi.FurnizorDenumire, nou.FurnizorDenumire);
        Camp("CUI", vechi.FurnizorCui, nou.FurnizorCui);
        if (vechi.ProiectId != nou.ProiectId) p.Add($"Proiect: {vechi.ProiectCod} → {nou.ProiectCod}");
        Camp("Total fără TVA", Bani(vechi.TotalFaraTva), Bani(nou.TotalFaraTva));
        Camp("Total TVA", Bani(vechi.TotalTva), Bani(nou.TotalTva));
        Camp("Total de plată", Bani(vechi.TotalDePlata), Bani(nou.TotalDePlata));
        Camp("Referința", vechi.Referinta, nou.Referinta);
        Camp("Observații", vechi.Observatii, nou.Observatii);
        if (vechi.NotaComandaId != nou.NotaComandaId) p.Add($"Nota de comandă: {vechi.NotaComandaNumar ?? "—"} → {nou.NotaComandaNumar ?? "—"}");
        for (var i = 0; i < Math.Max(vechi.Linii.Count, nou.Linii.Count); i++)
        {
            if (i >= nou.Linii.Count) { p.Add($"linia {i + 1} ștearsă („{vechi.Linii[i].Denumire}”)"); continue; }
            if (i >= vechi.Linii.Count) { p.Add($"linia {i + 1} adăugată („{nou.Linii[i].Denumire}”)"); continue; }
            var (a, b) = (vechi.Linii[i], nou.Linii[i]);
            var d = new List<string>();
            if (a.Denumire != b.Denumire) d.Add($"denumire „{a.Denumire}” → „{b.Denumire}”");
            if (a.UnitateMasura != b.UnitateMasura) d.Add($"UM {a.UnitateMasura} → {b.UnitateMasura}");
            if (a.Cantitate != b.Cantitate) d.Add($"cantitate {Cant(a.Cantitate)} → {Cant(b.Cantitate)}");
            if (a.PretUnitar != b.PretUnitar) d.Add($"preț {Cant(a.PretUnitar)} → {Cant(b.PretUnitar)}");
            if (a.ValoareFaraTva != b.ValoareFaraTva) d.Add($"valoare {Bani(a.ValoareFaraTva)} → {Bani(b.ValoareFaraTva)}");
            if (a.CotaTva != b.CotaTva) d.Add($"cotă TVA {Procent(a.CotaTva)} → {Procent(b.CotaTva)}");
            if (a.ValoareTva != b.ValoareTva) d.Add($"TVA {Bani(a.ValoareTva)} → {Bani(b.ValoareTva)}");
            if (a.IntraInStoc != b.IntraInStoc) d.Add(b.IntraInStoc ? "intră în stoc" : "nu mai intră în stoc");
            if (d.Count > 0) p.Add($"linia {i + 1}: {string.Join(", ", d)}");
        }

        var text = string.Join("; ", p);
        return text.Length <= 4000 ? text : text[..3997] + "...";
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
/// Corectura unei facturi primite (date citite / introduse greșit): istoric append-only cu datele dinainte și de după,
/// motivul și cine a corectat. Factura se modifică doar împreună cu corectura ei (<see cref="ICorectieDocument"/>).
/// </summary>
public sealed class CorectieFacturaPrimita : EntitateFirma, ICorectieDocument
{
    private CorectieFacturaPrimita() { }

    public Guid FacturaId { get; private set; }
    public string Motiv { get; private set; } = string.Empty;
    public string DateVechiJson { get; private set; } = "{}";
    public string DateNoiJson { get; private set; } = "{}";
    /// <summary>Ce s-a schimbat, pe scurt (ex. „Total TVA: 62,86 → 72,34; linia 2: cotă TVA 0% → 21%”).</summary>
    public string Rezumat { get; private set; } = string.Empty;
    public Guid CorectatDeId { get; private set; }
    public string CorectatDeNume { get; private set; } = string.Empty;
    public DateTimeOffset CorectatLa { get; private set; }

    Guid ICorectieDocument.DocumentCorectatId => FacturaId;

    internal static CorectieFacturaPrimita Creeaza(Guid facturaId, string motiv, string vechi, string nou, string rezumat, Guid utilizatorId, string utilizatorNume, DateTimeOffset moment) => new()
    {
        FacturaId = facturaId,
        Motiv = motiv.Trim(),
        DateVechiJson = vechi,
        DateNoiJson = nou,
        Rezumat = rezumat,
        CorectatDeId = utilizatorId,
        CorectatDeNume = utilizatorNume.Trim(),
        CorectatLa = moment,
    };
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
