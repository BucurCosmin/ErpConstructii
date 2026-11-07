using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErpConstructii.Formulare.Infrastructure.Pdf;

/// <summary>
/// PDF din șablonul Word activ al firmei (Open XML + conversie Gotenberg, D-09 / D-10), dacă există; altfel, sau dacă
/// conversia nu e disponibilă, în formatul standard (<see cref="GeneratorPdfStandard"/>).
/// </summary>
internal sealed class ServiciuDocumente(
    IInterogareSabloaneFormulare sabloane,
    IGeneratorFormular generatorWord,
    IConvertorPdf convertor,
    IGeneratorPdfStandard standard,
    ILogger<ServiciuDocumente> logger) : IServiciuDocumente
{
    public async Task<DocumentGenerat> PdfAsync(
        string codTip, AntetFirma firma, AspectDocument aspect, DateFormular date, string numeFisier, CancellationToken ct = default)
    {
        var nume = Path.ChangeExtension(numeFisier, ".pdf");
        if (CatalogFormulare.DupaCod(codTip) is { } tip && await sabloane.SablonActivAsync(codTip, ct) is { } sablon)
        {
            try
            {
                var docx = generatorWord.Genereaza(sablon.Continut, tip, date, new MetadateFormular(codTip, sablon.Versiune, null, null));
                return new DocumentGenerat(await convertor.InPdfAsync(docx, Path.ChangeExtension(numeFisier, ".docx"), ct), "application/pdf", nume, true);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Conversia PDF din șablonul firmei ({Tip}) nu e disponibilă; folosesc formatul standard.", codTip);
            }
        }

        return new DocumentGenerat(standard.Genereaza(firma, aspect, date), "application/pdf", nume, false);
    }

    public DocumentGenerat Combina(IReadOnlyList<DocumentGenerat> documente, string numeFisier)
    {
        using var iesire = new PdfDocument();
        foreach (var d in documente)
        {
            using var intrare = PdfReader.Open(new MemoryStream(d.Continut), PdfDocumentOpenMode.Import);
            foreach (var pagina in intrare.Pages)
                iesire.AddPage(pagina);
        }

        using var flux = new MemoryStream();
        iesire.Save(flux);
        return new DocumentGenerat(flux.ToArray(), "application/pdf", Path.ChangeExtension(numeFisier, ".pdf"), documente.Any(d => d.DinSablonFirma));
    }
}
