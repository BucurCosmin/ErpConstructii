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
    /// avizul și certificatele în același PDF.
    /// </summary>
    public int PaginiMaxime { get; set; } = 20;

    /// <summary>Cât timp se păstrează textul recunoscut al unui fișier (același fișier citit pentru factură și pentru nota de comandă).</summary>
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
    public bool Accepta(string numeFisier) => ExtensiiImagine.Contains(Path.GetExtension(numeFisier).ToLowerInvariant());

    public async Task<RezultatImport<TextDocument>> ExtrageAsync(SursaImport sursa, CancellationToken ct = default)
    {
        var cheie = "ocr:" + Convert.ToHexString(SHA256.HashData(sursa.Continut)) + sursa.Extensie;
        if (cache?.TryGetValue(cheie, out RezultatImport<TextDocument>? citit) == true && citit is not null)
        {
            return citit;
        }

        var rezultat = await Task.Run(() => Extrage(sursa, ct), ct);
        if (rezultat.Valoare is not null)
        {
            cache?.Set(cheie, rezultat, optiuni.Value.DurataCache);
        }

        return rezultat;
    }

    private RezultatImport<TextDocument> Extrage(SursaImport sursa, CancellationToken ct)
    {
        var o = optiuni.Value;
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

        List<byte[]> imagini;
        var probleme = new List<ProblemaImport>();
        try
        {
            imagini = sursa.Extensie == ".pdf" ? PaginiPdf(sursa.Continut, o, probleme) : [sursa.Continut];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "PDF-ul {Fisier} nu a putut fi randat.", sursa.NumeFisier);
            return RezultatImport<TextDocument>.Esuat("PDF-ul nu poate fi deschis (fișier deteriorat sau protejat cu parolă).");
        }

        var randuri = new List<RandText>();
        try
        {
            using var motor = new TesseractEngine(cale, o.Limbi, EngineMode.LstmOnly);
            for (var i = 0; i < imagini.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var original = Pix.LoadFromMemory(imagini[i]);
                using var pix = o.EliminaLiniiTabel ? FaraLiniiTabel(original) : original.Clone();
                var cuvinte = Citeste(motor, pix, o.ModSegmentare);
                if (CuvinteBune(cuvinte) < PragCuvinteBune && OrientareMaiBuna(motor, pix, CuvinteBune(cuvinte)) is { } rotire and not 0)
                {
                    using var rotita = pix.Rotate90(rotire);
                    cuvinte = Citeste(motor, rotita, o.ModSegmentare);
                    logger.LogDebug("Pagina {Pagina} din {Fisier} a fost rotită ({Rotire}).", i + 1, sursa.NumeFisier, rotire);
                }

                randuri.AddRange(GrupareRanduri.PeRanduri(cuvinte, i + 1));
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

        return RezultatImport<TextDocument>.Ok(new TextDocument(randuri, [], DinOcr: true), probleme);
    }

    private static List<byte[]> PaginiPdf(byte[] pdf, OptiuniOcr o, List<ProblemaImport> probleme)
    {
        var numar = Conversion.GetPageCount(pdf);
        if (numar > o.PaginiMaxime)
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "fișier",
                $"Documentul are {numar} pagini; am citit primele {o.PaginiMaxime}."));
        }

        var pagini = new List<byte[]>();
        for (var i = 0; i < Math.Min(numar, o.PaginiMaxime); i++)
        {
            using var ms = new MemoryStream();
            Conversion.SavePng(ms, pdf, i, options: new RenderOptions(Dpi: o.Dpi, WithAnnotations: true, Grayscale: true));
            pagini.Add(ms.ToArray());
        }

        return pagini;
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
