using ErpConstructii.Formulare.Infrastructure.Extragere;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Security.Cryptography;

namespace ErpConstructii.Formulare.Infrastructure.ImportDocumente;

public sealed record PaginaImportDto(int Pagina, string Tip, string Descriere, string? Numar);

/// <param name="Zona">Al câtelea document de pe aceste pagini (bonurile de consum, câte două pe pagină).</param>
public sealed record DocumentImportDto(string Tip, IReadOnlyList<int> Pagini, string? Numar, DateOnly? Data, int? Zona = null);

public sealed record ProblemaImportDto(string Severitate, string Locatie, string Mesaj);

/// <param name="Cheie">Identifică fișierul citit (păstrat 30 de minute pe server) la cererea documentelor din el.</param>
/// <param name="Separabil">PDF cu mai multe pagini: documentele se pot tăia în PDF-uri separate.</param>
public sealed record AnalizaImportDto(
    string Cheie,
    string NumeFisier,
    bool Separabil,
    bool DinOcr,
    IReadOnlyList<PaginaImportDto> Pagini,
    IReadOnlyList<DocumentImportDto> Documente,
    IReadOnlyList<ProblemaImportDto> Probleme);

public sealed record CerereDocumentImport(IReadOnlyList<int> Pagini);

/// <summary>
/// Centrul de import (D-27): un fișier scanat cu mai multe documente (facturi cu e-Factura, aviz și certificate, note de
/// comandă, necesare de materiale, bonuri de consum) se citește o dată, se împarte în documente
/// (<see cref="ImpartireDocumente"/>), iar fiecare document se taie într-un PDF separat, importat apoi prin ecranul
/// tipului lui (aceleași verificări și aceeași confirmare ca la importul unui singur document). Textul recunoscut al
/// paginilor trece în PDF-ul tăiat, ca importul să nu mai aștepte încă o dată OCR-ul.
/// </summary>
internal static class EndpointuriImportDocumente
{
    public const long DimensiuneMaxima = 40_000_000;

    private static readonly string[] Extensii = [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".docx", ".xml", ".zip"];

    private sealed record FisierCitit(string NumeFisier, byte[] Continut, TextDocument? Text);

    public static void Mapeaza(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/import-documente").WithTags("Import documente");
        var aprovizionare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);
        var financiar = Permisiuni.Pentru(ModulErp.Financiar, Actiune.Creare);

        grup.MapPost("/analiza", Analizeaza)
            .WithName("AnalizeazaFisierImport")
            .WithSummary("Citește un fișier și propune documentele din el (pe pagini)")
            .Produces<AnalizaImportDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DimensiuneMaxima + 100_000))
            .CereOricarePermisiune(aprovizionare, financiar);

        grup.MapPost("/{cheie}/document", Document)
            .WithName("DocumentDinFisierImport")
            .WithSummary("PDF-ul cu paginile unui document din fișierul citit")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound)
            .CereOricarePermisiune(aprovizionare, financiar);
    }

    private static string CheieCache(IFirmaCurenta firma, string cheie) => $"import-documente:{firma.FirmaId}:{cheie}";

    private static async Task<IResult> Analizeaza(
        IFormFile fisier,
        IEnumerable<IExtractorText> extractori,
        IFirmaCurenta firma,
        IMemoryCache cache,
        IOptions<OptiuniOcr> optiuni,
        CancellationToken ct)
    {
        if (fisier is null || fisier.Length == 0)
        {
            return Results.BadRequest(new { mesaj = "Alege fișierul." });
        }

        if (fisier.Length > DimensiuneMaxima)
        {
            return Results.BadRequest(new { mesaj = $"Fișierul depășește limita de {DimensiuneMaxima / 1_000_000} MB." });
        }

        var extensie = Path.GetExtension(fisier.FileName).ToLowerInvariant();
        if (!Extensii.Contains(extensie))
        {
            return Results.BadRequest(new { mesaj = "Format nesuportat. Încarcă un PDF, o imagine (JPG, PNG, TIFF), un document Word sau XML-ul / arhiva e-Factura." });
        }

        await using var ms = new MemoryStream((int)fisier.Length);
        await fisier.CopyToAsync(ms, ct);
        var sursa = new SursaImport(Path.GetFileName(fisier.FileName), ms.ToArray());
        var cheie = Convert.ToHexString(SHA256.HashData(sursa.Continut))[..32];

        // e-Factura (XML / ZIP din SPV): o singură factură, citită exact de importul facturilor.
        if (extensie is ".xml" or ".zip")
        {
            cache.Set(CheieCache(firma, cheie), new FisierCitit(sursa.NumeFisier, sursa.Continut, null), optiuni.Value.DurataCache);
            return Results.Ok(new AnalizaImportDto(cheie, sursa.NumeFisier, false, false,
                [new PaginaImportDto(1, nameof(TipPagina.EFactura), "e-Factura (XML)", null)],
                [new DocumentImportDto(nameof(TipDocumentImport.Factura), [1], null, null)], []));
        }

        var extractor = extractori.FirstOrDefault(e => e.Accepta(sursa.NumeFisier));
        if (extractor is null)
        {
            return Results.BadRequest(new { mesaj = "Citirea automată nu este disponibilă pentru acest format pe server." });
        }

        var text = await extractor.ExtrageAsync(sursa, ct);
        if (text.Valoare is null)
        {
            return Results.BadRequest(new { mesaj = text.Probleme.FirstOrDefault()?.Mesaj ?? "Documentul nu poate fi citit." });
        }

        var clasificare = ClasificarePagini.Clasifica(text.Valoare);
        var documente = ImpartireDocumente.Imparte(clasificare, text.Valoare);
        var probleme = text.Probleme.ToList();
        if (documente.Count == 0)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "fișier", "Nu am găsit niciun document în fișier (pagini goale sau scanare neclară)."));
        }

        cache.Set(CheieCache(firma, cheie), new FisierCitit(sursa.NumeFisier, sursa.Continut, text.Valoare), optiuni.Value.DurataCache);
        return Results.Ok(new AnalizaImportDto(
            cheie,
            sursa.NumeFisier,
            extensie == ".pdf" && clasificare.Count > 1,
            text.Valoare.DinOcr,
            clasificare.Select(p => new PaginaImportDto(p.Pagina, p.Tip.ToString(), p.Descriere, p.Numar)).ToList(),
            documente.Select(d => new DocumentImportDto(d.Tip.ToString(), d.Pagini, d.Numar, d.Data, d.Zona)).ToList(),
            probleme.Select(p => new ProblemaImportDto(p.Severitate.ToString(), p.Locatie, p.Mesaj)).ToList()));
    }

    private static IResult Document(
        string cheie,
        CerereDocumentImport cerere,
        IFirmaCurenta firma,
        IMemoryCache cache,
        IServiceProvider servicii,
        ILoggerFactory jurnal)
    {
        if (!cache.TryGetValue(CheieCache(firma, cheie), out FisierCitit? citit) || citit is null)
        {
            return Results.NotFound(new { mesaj = "Fișierul nu mai este pe server (au trecut peste 30 de minute de la citire). Încarcă-l din nou." });
        }

        var extensie = Path.GetExtension(citit.NumeFisier).ToLowerInvariant();
        var pagini = cerere.Pagini.Distinct().Order().ToList();
        var toate = citit.Text?.Randuri.Select(r => r.Pagina).Distinct().Order().ToList() ?? [1];
        if (extensie != ".pdf" || pagini.Count == 0 || pagini.SequenceEqual(toate))
        {
            return Results.File(citit.Continut, ContentType(extensie), citit.NumeFisier);
        }

        byte[] continut;
        try
        {
            using var intrare = PdfReader.Open(new MemoryStream(citit.Continut), PdfDocumentOpenMode.Import);
            if (pagini.Any(p => p < 1 || p > intrare.PageCount))
            {
                return Results.BadRequest(new { mesaj = "Pagini inexistente în fișier." });
            }

            using var iesire = new PdfDocument();
            foreach (var p in pagini)
            {
                iesire.AddPage(intrare.Pages[p - 1]);
            }

            using var ms = new MemoryStream();
            iesire.Save(ms);
            continut = ms.ToArray();
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotSupportedException)
        {
            jurnal.CreateLogger(typeof(EndpointuriImportDocumente)).LogWarning(ex, "Paginile din {Fisier} nu au putut fi separate.", citit.NumeFisier);
            return Results.BadRequest(new { mesaj = "Paginile acestui PDF nu pot fi separate (PDF protejat sau deteriorat). Importă fișierul întreg din ecranul documentului." });
        }

        // Textul recunoscut trece în noul PDF (paginile renumerotate 1..n): importul lui nu mai așteaptă OCR-ul.
        if (citit.Text is { DinOcr: true } text && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        {
            var noua = pagini.Select((p, i) => (p, i + 1)).ToDictionary(x => x.p, x => x.Item2);
            servicii.GetService<ExtractorTextOcr>()?.Memoreaza(continut, ".pdf",
                text.Randuri.Where(r => noua.ContainsKey(r.Pagina)).Select(r => r with { Pagina = noua[r.Pagina] }),
                text.LiniiOrizontale.Where(l => noua.ContainsKey(l.Pagina)).Select(l => l with { Pagina = noua[l.Pagina] }));

        }

        var nume = $"{Path.GetFileNameWithoutExtension(citit.NumeFisier)}_p{Interval(pagini)}.pdf";
        return Results.File(continut, "application/pdf", nume);
    }

    private static string Interval(List<int> pagini) =>
        pagini.Count == 1 ? $"{pagini[0]}" : pagini[^1] - pagini[0] == pagini.Count - 1 ? $"{pagini[0]}-{pagini[^1]}" : string.Join('_', pagini);

    private static string ContentType(string extensie) => extensie switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tif" or ".tiff" => "image/tiff",
        ".xml" => "application/xml",
        ".zip" => "application/zip",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream",
    };
}
