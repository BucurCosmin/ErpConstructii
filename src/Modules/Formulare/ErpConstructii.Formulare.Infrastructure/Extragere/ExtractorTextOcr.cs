using System.Runtime.Versioning;
using System.Security.Cryptography;
using ErpConstructii.SharedKernel.Import;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFtoImage;
using Tesseract;

namespace ErpConstructii.Formulare.Infrastructure.Extragere;

/// <summary>Configurarea OCR („Ocr” în appsettings).</summary>
public sealed class OptiuniOcr
{
    /// <summary>Folderul cu modelele de limbă (*.traineddata). Relativ la folderul aplicației sau absolut.</summary>
    public string CaleTessdata { get; set; } = "tessdata";

    /// <summary>Limbile Tesseract; engleza ajută la textele mixte din contracte (ex. denumiri de lucrări).</summary>
    public string Limbi { get; set; } = "ron+eng";

    /// <summary>Rezoluția la care se randează paginile PDF pentru OCR.</summary>
    public int Dpi { get; set; } = 300;

    /// <summary>
    /// Modul de segmentare Tesseract. „SparseText” găsește textul și în celulele tabelelor cu grilă
    /// (segmentarea automată tratează des grila ca zonă fără text).
    /// </summary>
    public PageSegMode ModSegmentare { get; set; } = PageSegMode.SparseText;

    /// <summary>Șterge liniile tabelelor înainte de OCR (grila încurcă recunoașterea textului din celule).</summary>
    public bool EliminaLiniiTabel { get; set; } = true;

    /// <summary>
    /// Câte pagini se citesc dintr-un document. Dosarele de factură scanate au des factura, e-Factura, nota de comandă,
    /// avizul și certificatele în același PDF, iar un fișier poate conține mai multe astfel de dosare.
    /// </summary>
    public int PaginiMaxime { get; set; } = 40;

    /// <summary>Cât timp se păstrează textul recunoscut al paginilor unui fișier (același fișier citit pentru factură și pentru nota de comandă).</summary>
    public TimeSpan DurataCache { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// OCR local (Tesseract) pentru documente scanate: PDF (paginile se randează în imagini cu PDFium) sau imagini
/// (poze, scanări JPG/PNG/TIFF). Întoarce cuvintele cu pozițiile lor, grupate pe rânduri după înălțime, ca
/// interpretorii să poată reconstrui coloanele tabelelor.
///
/// Tesseract nu e thread-safe pe aceeași instanță: fiecare cerere creează motorul ei (modelele se încarcă în ~1 s).
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class ExtractorTextOcr(IOptions<OptiuniOcr> optiuni, ILogger<ExtractorTextOcr> logger, IMemoryCache? cache = null) : IExtractorText
{
    private static readonly string[] ExtensiiImagine = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    /// <summary>
    /// Sub atâtea cuvinte „bune” (încredere ≥ 80, cel puțin 3 litere), pagina e probabil rotită (ex. e-Factura tipărită
    /// pe orizontală și scanată pe verticală) sau goală: se încearcă rotirile de ±90°.
    /// </summary>
    private const int PragCuvinteBune = 12;

    /// <summary>Imaginile direct; PDF-urile trec întâi prin <see cref="ExtractorTextPdf"/>, care cheamă OCR doar pentru scanări.</summary>
    /// <summary>Câte pagini se citesc dintr-un PDF (configurabil: Ocr:PaginiMaxime).</summary>
    public int PaginiMaxime => optiuni.Value.PaginiMaxime;

    public bool Accepta(string numeFisier) => ExtensiiImagine.Contains(Path.GetExtension(numeFisier).ToLowerInvariant());

    public Task<RezultatImport<TextDocument>> ExtrageAsync(SursaImport sursa, CancellationToken ct = default) =>
        ExtragePaginiAsync(sursa, null, ct);

    /// <summary>
    /// Doar paginile date (numerotate de la 1) ale unui PDF: în PDF-urile combinate, paginile cu text digital se citesc
    /// direct, iar OCR-ul trece doar prin paginile scanate. Null = toate paginile.
    /// </summary>
    public Task<RezultatImport<TextDocument>> ExtragePaginiAsync(SursaImport sursa, IReadOnlyCollection<int>? pagini, CancellationToken ct = default) =>
        Task.Run(() => Extrage(sursa, pagini, ct), ct);

    /// <summary>
    /// Pune în cache textul deja recunoscut al paginilor unui fișier (numerotarea paginilor din acel fișier): documentele
    /// tăiate dintr-un fișier citit în centrul de import nu mai trec încă o dată prin OCR.
    /// </summary>
    public void Memoreaza(byte[] continut, string extensie, IEnumerable<RandText> randuri, IEnumerable<LinieOrizontala>? linii = null)
    {
        var amprenta = Amprenta(continut, extensie);
        var liniiPagini = (linii ?? []).GroupBy(l => l.Pagina).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var pagina in randuri.GroupBy(r => r.Pagina))
        {
            cache?.Set(CheiePagina(amprenta, pagina.Key), pagina.ToList(), optiuni.Value.DurataCache);
            cache?.Set(CheieLinii(amprenta, pagina.Key), liniiPagini.GetValueOrDefault(pagina.Key) ?? [], optiuni.Value.DurataCache);
        }
    }

    private static string Amprenta(byte[] continut, string extensie) => Convert.ToHexString(SHA256.HashData(continut)) + extensie;

    /// <summary>Cache pe pagină: același fișier citit pentru factură, apoi pentru nota de comandă, trece o singură dată prin OCR.</summary>
    private static string CheiePagina(string amprenta, int pagina) => $"ocr:{amprenta}:{pagina}";

    private static string CheieLinii(string amprenta, int pagina) => $"ocr-linii:{amprenta}:{pagina}";

    private RezultatImport<TextDocument> Extrage(SursaImport sursa, IReadOnlyCollection<int>? doarPaginile, CancellationToken ct)
    {
        var o = optiuni.Value;
        var amprenta = Amprenta(sursa.Continut, sursa.Extensie);
        var probleme = new List<ProblemaImport>();

        int numar;
        try
        {
            numar = sursa.Extensie == ".pdf" ? Conversion.GetPageCount(sursa.Continut) : 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "PDF-ul {Fisier} nu a putut fi deschis.", sursa.NumeFisier);
            return RezultatImport<TextDocument>.Esuat("PDF-ul nu poate fi deschis (fișier deteriorat sau protejat cu parolă).");
        }

        if (numar > o.PaginiMaxime)
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "fișier",
                $"Documentul are {numar} pagini; am citit primele {o.PaginiMaxime}."));
        }

        var pagini = Enumerable.Range(1, Math.Min(numar, o.PaginiMaxime)).Where(p => doarPaginile is null || doarPaginile.Contains(p)).ToList();
        var citite = new Dictionary<int, List<RandText>>();
        var linii = new Dictionary<int, List<LinieOrizontala>>();
        foreach (var p in pagini)
        {
            if (cache?.TryGetValue(CheiePagina(amprenta, p), out List<RandText>? r) == true && r is not null)
            {
                citite[p] = r;
                linii[p] = cache.TryGetValue(CheieLinii(amprenta, p), out List<LinieOrizontala>? l) && l is not null ? l : [];
            }
        }

        var lipsa = pagini.Where(p => !citite.ContainsKey(p)).ToList();
        if (lipsa.Count > 0)
        {
            var cale = Path.IsPathRooted(o.CaleTessdata) ? o.CaleTessdata : Path.Combine(AppContext.BaseDirectory, o.CaleTessdata);
            if (!Directory.Exists(cale) || !Directory.EnumerateFiles(cale, "*.traineddata").Any())
            {
                cale = CaleAlternativa(o.CaleTessdata) ?? cale;
            }

            if (!Directory.Exists(cale) || !Directory.EnumerateFiles(cale, "*.traineddata").Any())
            {
                logger.LogError("Modelele OCR lipsesc din {Cale}. Rulează scripts/descarca-tessdata.ps1.", cale);
                return RezultatImport<TextDocument>.Esuat(
                    "Recunoașterea textului (OCR) nu este instalată pe server. Completează datele manual sau cere administratorului să instaleze modelele OCR.");
            }

            try
            {
                using var motor = new TesseractEngine(cale, o.Limbi, EngineMode.LstmOnly);
                foreach (var p in lipsa)
                {
                    ct.ThrowIfCancellationRequested();
                    byte[] imagine;
                    try
                    {
                        imagine = sursa.Extensie == ".pdf" ? Randeaza(sursa.Continut, p - 1, o) : sursa.Continut;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Pagina {Pagina} din PDF-ul {Fisier} nu a putut fi randată.", p, sursa.NumeFisier);
                        return RezultatImport<TextDocument>.Esuat("PDF-ul nu poate fi deschis (fișier deteriorat sau protejat cu parolă).");
                    }

                    using var original = Pix.LoadFromMemory(imagine);
                    using var pix = o.EliminaLiniiTabel ? FaraLiniiTabel(original) : original.Clone();
                    var cuvinte = Citeste(motor, pix, o.ModSegmentare);
                    var rotita = false;
                    if (CuvinteBune(cuvinte) < PragCuvinteBune && OrientareMaiBuna(motor, pix, CuvinteBune(cuvinte)) is { } rotire and not 0)
                    {
                        using var pixRotit = pix.Rotate90(rotire);
                        cuvinte = Citeste(motor, pixRotit, o.ModSegmentare);
                        rotita = true;
                        logger.LogDebug("Pagina {Pagina} din {Fisier} a fost rotită ({Rotire}).", p, sursa.NumeFisier, rotire);
                    }

                    var randuriPagina = GrupareRanduri.PeRanduri(cuvinte, p).ToList();
                    citite[p] = randuriPagina;
                    // Liniile tabelelor se caută pe imaginea originală (înainte de ștergerea lor), doar dacă textul s-a citit
                    // fără rotire (altfel coordonatele nu mai corespund).
                    linii[p] = rotita ? [] : DetectareLinii.DinImagine(imagine, p);
                    cache?.Set(CheiePagina(amprenta, p), randuriPagina, o.DurataCache);
                    cache?.Set(CheieLinii(amprenta, p), linii[p], o.DurataCache);
                }
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Imaginea {Fisier} nu a putut fi citită.", sursa.NumeFisier);
                return RezultatImport<TextDocument>.Esuat("Imaginea nu poate fi citită. Încarcă un PDF, JPG sau PNG.");
            }
            catch (TesseractException ex)
            {
                logger.LogError(ex, "Motorul OCR nu a putut porni.");
                return RezultatImport<TextDocument>.Esuat("Recunoașterea textului (OCR) nu a putut porni pe server.");
            }
        }

        var randuri = pagini.SelectMany(p => citite[p]).ToList();
        if (randuri.Count == 0)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "fișier",
                "Nu am putut citi text din document (scanare neclară sau pagină goală). Completează datele manual."));
        }
        else
        {
            var incredereMedie = randuri.SelectMany(r => r.Cuvinte).Average(c => c.Incredere);
            if (incredereMedie < TextDocument.PragIncredere)
            {
                probleme.Add(new ProblemaImport(Severitate.Avertisment, "fișier",
                    $"Scanarea e greu de citit (încredere medie {incredereMedie:0}%). Verifică atent toate câmpurile."));
            }
        }

        return RezultatImport<TextDocument>.Ok(new TextDocument(randuri, [], DinOcr: true, pagini.SelectMany(p => linii.GetValueOrDefault(p) ?? []).ToList()), probleme);

    }

    private static byte[] Randeaza(byte[] pdf, int index, OptiuniOcr o)
    {
        using var ms = new MemoryStream();
        Conversion.SavePng(ms, pdf, index, options: new RenderOptions(Dpi: o.Dpi, WithAnnotations: true, Grayscale: true));
        return ms.ToArray();
    }

    /// <summary>
    /// Imagine în tonuri de gri fără liniile orizontale și verticale ale tabelelor (Leptonica: liniile orizontale se
    /// elimină direct, cele verticale după o rotire cu 90°). Pozițiile textului rămân aceleași.
    /// </summary>
    private static Pix FaraLiniiTabel(Pix original)
    {
        using var gri = original.Depth == 8 ? original.Clone() : original.ConvertRGBToGray();
        using var faraOrizontale = gri.RemoveLines();
        using var rotita = faraOrizontale.Rotate90(1);
        using var faraVerticale = rotita.RemoveLines();
        return faraVerticale.Rotate90(-1);
    }

    private static List<GrupareRanduri.Cuvant> Citeste(TesseractEngine motor, Pix pix, PageSegMode mod)
    {
        using var pagina = motor.Process(pix, mod);
        return Cuvinte(pagina);
    }

    private static int CuvinteBune(IEnumerable<GrupareRanduri.Cuvant> cuvinte) =>
        cuvinte.Count(c => c.Incredere >= 80 && c.Text.Count(char.IsLetter) >= 3);

    /// <summary>
    /// Rotirea (+1 = 90° în sensul acelor de ceasornic, −1 = invers) cu care pagina se citește clar sau null.
    /// Testul se face pe imaginea micșorată la jumătate (de ~4 ori mai rapid); rotirea câștigă doar dacă dă
    /// net mai multe cuvinte bune decât pagina așa cum e.
    /// </summary>
    private static int? OrientareMaiBuna(TesseractEngine motor, Pix pix, int buneInitial)
    {
        using var mica = pix.Scale(0.5f, 0.5f);
        var (rotire, maxim) = (0, buneInitial);
        foreach (var r in new[] { 1, -1 })
        {
            using var rotita = mica.Rotate90(r);
            var bune = CuvinteBune(Citeste(motor, rotita, PageSegMode.SparseText));
            if (bune > maxim)
            {
                (rotire, maxim) = (r, bune);
            }
        }

        return maxim >= PragCuvinteBune && maxim > 2 * buneInitial ? rotire : null;
    }

    private static List<GrupareRanduri.Cuvant> Cuvinte(Page pagina)
    {
        var cuvinte = new List<GrupareRanduri.Cuvant>();
        using var it = pagina.GetIterator();
        it.Begin();
        do
        {
            var text = it.GetText(PageIteratorLevel.Word)?.Trim();
            if (!string.IsNullOrEmpty(text) && it.TryGetBoundingBox(PageIteratorLevel.Word, out var b))
            {
                cuvinte.Add(new GrupareRanduri.Cuvant(text, b.X1, b.X2, b.Y1, b.Y2, it.GetConfidence(PageIteratorLevel.Word)));
            }
        }
        while (it.Next(PageIteratorLevel.Word));

        return cuvinte;
    }

    /// <summary>În dezvoltare, modelele stau în folderul „tessdata” din rădăcina repository-ului.</summary>
    private static string? CaleAlternativa(string relativ)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var cale = Path.Combine(d.FullName, relativ);
            if (Directory.Exists(cale) && Directory.EnumerateFiles(cale, "*.traineddata").Any())
            {
                return cale;
            }
        }

        return null;
    }
}
