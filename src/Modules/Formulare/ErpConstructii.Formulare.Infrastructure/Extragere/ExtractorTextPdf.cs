using System.Runtime.Versioning;
using ErpConstructii.SharedKernel.Import;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

namespace ErpConstructii.Formulare.Infrastructure.Extragere;

/// <summary>
/// PDF-uri: textul digital (PDF generat de un program de facturare / Word) se citește exact, cu pozițiile cuvintelor;
/// un PDF fără text (scanare) trece la OCR. În PDF-urile combinate (scanări + documente generate), doar paginile fără
/// text trec prin OCR. Pozițiile sunt aduse la scara unei imagini de 300 DPI, ca regulile
/// interpretorilor (toleranțe în pixeli) să fie aceleași pentru text digital și OCR.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class ExtractorTextPdf(ExtractorTextOcr ocr, ILogger<ExtractorTextPdf> logger) : IExtractorText
{
    private const double Scara = 300.0 / 72.0;

    /// <summary>Sub atâtea litere, pagina (sau tot PDF-ul) e considerată scanată (doar imagini, eventual un antet tipărit).</summary>
    private const int LitereMinime = 40;

    public bool Accepta(string numeFisier) => Path.GetExtension(numeFisier).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<RezultatImport<TextDocument>> ExtrageAsync(SursaImport sursa, CancellationToken ct = default)
    {
        var randuri = new List<RandText>();
        var scanate = new List<int>();
        try
        {
            using var pdf = PdfDocument.Open(sursa.Continut);
            var offset = 0;
            foreach (var pagina in pdf.GetPages().Take(ocr.PaginiMaxime))
            {
                ct.ThrowIfCancellationRequested();
                var cuvinte = pagina.GetWords()
                    .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                    .Select(w => new GrupareRanduri.Cuvant(
                        w.Text,
                        (int)(w.BoundingBox.Left * Scara),
                        (int)(w.BoundingBox.Right * Scara),
                        offset + (int)((pagina.Height - w.BoundingBox.Top) * Scara),
                        offset + (int)((pagina.Height - w.BoundingBox.Bottom) * Scara),
                        100f))
                    .ToList();
                var randuriPagina = GrupareRanduri.PeRanduri(cuvinte, pagina.Number);
                if (randuriPagina.Sum(r => r.Text.Count(char.IsLetter)) < LitereMinime)
                {
                    scanate.Add(pagina.Number);
                }
                else
                {
                    randuri.AddRange(randuriPagina);
                }

                offset += (int)(pagina.Height * Scara) + 100; // paginile una sub alta, fără suprapuneri
            }
        }
        catch (Exception ex) when (ex is PdfDocumentEncryptedException or PdfDocumentFormatException or InvalidOperationException or ArgumentException)
        {
            logger.LogInformation(ex, "Textul PDF-ului {Fisier} nu a putut fi citit direct; încerc OCR.", sursa.NumeFisier);
            return await ocr.ExtrageAsync(sursa, ct);
        }

        if (randuri.Count == 0)
        {
            return await ocr.ExtrageAsync(sursa, ct); // scanare: PDF fără text
        }

        if (scanate.Count == 0)
        {
            return RezultatImport<TextDocument>.Ok(new TextDocument(randuri, [], DinOcr: false));
        }

        // PDF combinat: paginile scanate prin OCR, puse la locul lor.
        var citite = await ocr.ExtragePaginiAsync(sursa, scanate, ct);
        var toate = randuri.Concat(citite.Valoare?.Randuri ?? []).OrderBy(r => r.Pagina).ToList();
        return RezultatImport<TextDocument>.Ok(new TextDocument(toate, [], DinOcr: true), citite.Probleme);
    }
}
