using System.Runtime.Versioning;
using ErpConstructii.SharedKernel.Import;
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

    /// <summary>Câte pagini se citesc dintr-un document (notele au de regulă o pagină).</summary>
    public int PaginiMaxime { get; set; } = 5;
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
public sealed class ExtractorTextOcr(IOptions<OptiuniOcr> optiuni, ILogger<ExtractorTextOcr> logger) : IExtractorText
{
    private static readonly string[] ExtensiiImagine = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    public bool Accepta(string numeFisier)
    {
        var ext = Path.GetExtension(numeFisier).ToLowerInvariant();
        return ext == ".pdf" || ExtensiiImagine.Contains(ext);
    }

    public Task<RezultatImport<TextDocument>> ExtrageAsync(SursaImport sursa, CancellationToken ct = default) =>
        Task.Run(() => Extrage(sursa, ct), ct);

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
                "Recunoașterea textului (OCR) nu este instalată pe server. Completează nota manual sau cere administratorului să instaleze modelele OCR.");
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
                using var pagina = motor.Process(pix, o.ModSegmentare);
                randuri.AddRange(GrupeazaPeRanduri(Cuvinte(pagina), i + 1));
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
                "Nu am putut citi text din document (scanare neclară sau pagină goală). Completează nota manual."));
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

    private sealed record CuvantPozitionat(string Text, int X0, int X1, int Y0, int Y1, float Incredere);

    private static List<CuvantPozitionat> Cuvinte(Page pagina)
    {
        var cuvinte = new List<CuvantPozitionat>();
        using var it = pagina.GetIterator();
        it.Begin();
        do
        {
            var text = it.GetText(PageIteratorLevel.Word)?.Trim();
            if (!string.IsNullOrEmpty(text) && it.TryGetBoundingBox(PageIteratorLevel.Word, out var b))
            {
                cuvinte.Add(new CuvantPozitionat(text, b.X1, b.X2, b.Y1, b.Y2, it.GetConfidence(PageIteratorLevel.Word)));
            }
        }
        while (it.Next(PageIteratorLevel.Word));

        return cuvinte;
    }

    /// <summary>
    /// Cuvintele de pe aceeași linie vizuală formează un rând (Tesseract poate separa coloanele unui tabel în blocuri
    /// diferite; regrupăm după centrul vertical, cu toleranță de jumătate din înălțimea medie a unui cuvânt).
    /// </summary>
    private static IEnumerable<RandText> GrupeazaPeRanduri(List<CuvantPozitionat> cuvinte, int pagina)
    {
        if (cuvinte.Count == 0)
        {
            yield break;
        }

        var inaltimeMedie = cuvinte.Average(c => c.Y1 - c.Y0);
        var toleranta = Math.Max(4, inaltimeMedie * 0.5);
        var randCurent = new List<CuvantPozitionat>();
        double centruRand = 0;

        foreach (var c in cuvinte.OrderBy(c => (c.Y0 + c.Y1) / 2.0))
        {
            var centru = (c.Y0 + c.Y1) / 2.0;
            if (randCurent.Count > 0 && Math.Abs(centru - centruRand) > toleranta)
            {
                yield return CaRand(randCurent, pagina);
                randCurent = [];
            }

            randCurent.Add(c);
            centruRand = randCurent.Average(x => (x.Y0 + x.Y1) / 2.0);
        }

        if (randCurent.Count > 0)
        {
            yield return CaRand(randCurent, pagina);
        }
    }

    private static RandText CaRand(List<CuvantPozitionat> cuvinte, int pagina)
    {
        var ordonate = cuvinte.OrderBy(c => c.X0).ToList();
        return new RandText(
            string.Join(' ', ordonate.Select(c => c.Text)),
            ordonate.Select(c => new CuvantText(c.Text, c.X0, c.X1, c.Incredere)).ToList(),
            pagina,
            cuvinte.Min(c => c.Y0),
            cuvinte.Max(c => c.Y1));
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
