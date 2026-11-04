using ErpConstructii.Formulare.Infrastructure.Word;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Formulare.Infrastructure.Extragere;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Cryptography;

namespace ErpConstructii.Formulare.Infrastructure;

public sealed class ModulFormulare : IModul
{
    public string Nume => FormulareDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AdaugaDbContextModul<FormulareDbContext>(configurare, Nume);
        servicii.AddScoped<IInterogareSabloaneFormulare, InterogareSabloaneFormulare>();
        servicii.AddSingleton<IGeneratorFormular, GeneratorWord>();
        servicii.AddSingleton<ICititorFormular, CititorWord>();
        servicii.AddSingleton<IValidatorSablon, ValidatorSablonWord>();
        // PDF-urile documentelor: șablonul firmei (Word → Gotenberg) sau formatul standard (MigraDoc).
        servicii.AddSingleton<IGeneratorPdfStandard, Pdf.GeneratorPdfStandard>();
        servicii.AddScoped<IServiciuDocumente, Pdf.ServiciuDocumente>();

        // Extragerea textului din documente externe (Word direct; PDF scanat și imagini prin OCR local).
        servicii.Configure<OptiuniOcr>(configurare.GetSection("Ocr"));
        servicii.AddMemoryCache();
        servicii.AddSingleton<IExtractorText, ExtractorTextWord>();
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            servicii.AddSingleton<ExtractorTextOcr>();
            // Lambda-ul e deja în garda de platformă de mai sus; analizorul CA1416 nu o vede în interiorul lambda-ului.
#pragma warning disable CA1416
            servicii.AddSingleton<IExtractorText>(sp => sp.GetRequiredService<ExtractorTextOcr>());
#pragma warning restore CA1416
            servicii.AddSingleton<IExtractorText, ExtractorTextPdf>();
        }

        var urlGotenberg = configurare["Formulare:GotenbergUrl"] ?? "http://localhost:3000";
        servicii.AddHttpClient<IConvertorPdf, ConvertorPdfGotenberg>(c =>
        {
            c.BaseAddress = new Uri(urlGotenberg.TrimEnd('/') + "/");
            c.Timeout = TimeSpan.FromSeconds(60);
        });
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/formulare").WithTags("Formulare");
        var citireAdmin = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Vizualizare);
        var editareAdmin = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Editare);

        grup.MapGet("/tipuri", () => CatalogFormulare.Toate)
            .WithName("ListaTipuriFormular")
            .WithSummary("Listează tipurile de formular și câmpurile disponibile")
            .Produces<IReadOnlyList<TipFormular>>()
            .CerePermisiune(citireAdmin);

        grup.MapGet("/sabloane", ListaSabloane)
            .WithName("ListaSabloaneFormular")
            .WithSummary("Listează versiunile de șabloane ale firmei")
            .Produces<IReadOnlyList<SablonFormularDto>>()
            .CerePermisiune(citireAdmin);

        grup.MapPost("/sabloane", IncarcaSablon)
            .WithName("IncarcaSablonFormular")
            .WithSummary("Adaugă o versiune nouă de șablon Word")
            .Produces<SablonFormularDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(10_000_000))
            .CerePermisiune(editareAdmin);

        grup.MapGet("/sabloane/{id:guid}/fisier", DescarcaSablon)
            .WithName("DescarcaSablonFormular")
            .WithSummary("Descarcă o versiune de șablon Word")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(citireAdmin);

        grup.MapPost("/sabloane/{id:guid}/activare", ActiveazaSablon)
            .WithName("ActiveazaSablonFormular")
            .WithSummary("Activează o versiune de șablon și dezactivează versiunea curentă")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editareAdmin);

        grup.MapGet("/import", ListaImporturi)
            .WithName("ListaImporturiFormular")
            .WithSummary("Listează documentele importate care așteaptă verificare")
            .Produces<IReadOnlyList<ImportFormularDto>>()
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare));

        grup.MapPost("/import", ImportaFormular)
            .WithName("ImportaFormular")
            .WithSummary("Arhivează originalul și datele unui formular extern ca ciornă de verificat")
            .Produces<ImportFormularDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(10_000_000))
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare));

        grup.MapGet("/import/{id:guid}/original", DescarcaOriginalImportat)
            .WithName("DescarcaOriginalImportat")
            .WithSummary("Descarcă documentul original importat")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare));

        // Pașii 2 + 4 din importul formularelor făcute manual: citire + validare, fără salvare.
        // Potrivirea (furnizor după CUI, materiale după cod) și ciorna intră în modulul fiecărui tip.
        grup.MapPost("/import/previzualizare", async (IFormFile fisier, ICititorFormular cititor, CancellationToken ct) =>
        {
            if (fisier is null || fisier.Length == 0 || !Path.GetExtension(fisier.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { mesaj = "Încarcă un document Word .docx valid pentru previzualizarea automată." });
            }
            if (fisier.Length > 10_000_000)
            {
                return Results.BadRequest(new { mesaj = "Fișierul depășește limita de 10 MB." });
            }

            using var ms = new MemoryStream();
            await fisier.CopyToAsync(ms, ct);
            var citit = cititor.Citeste(ms.ToArray());

            var probleme = citit.Probleme.ToList();
            var tip = citit.Metadate is null ? null : CatalogFormulare.DupaCod(citit.Metadate.CodTip);
            if (citit.Metadate is not null && tip is null)
            {
                probleme.Add(new ProblemaImport(Severitate.Eroare, "fișier", $"Tip de formular necunoscut: {citit.Metadate.CodTip}."));
            }

            if (tip is not null)
            {
                probleme.AddRange(ValidatorDateFormular.Valideaza(tip, citit.Date));
            }

            return Results.Ok(new
            {
                tip = tip?.Cod,
                citit.Metadate,
                citit.Date,
                probleme,
                poateFiImportat = tip is not null && probleme.All(p => p.Severitate != Severitate.Eroare),
            });
        })
            .WithName("PrevizualizeazaImportFormular")
            .WithSummary("Citește și validează un formular Word fără a-l salva")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(10_000_000))
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare));
    }

    private static async Task<IResult> ListaSabloane(string? codTip, FormulareDbContext db, CancellationToken ct)
    {
        var interogare = db.Sabloane.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(codTip))
            interogare = interogare.Where(s => s.CodTip == codTip);

        var sabloane = await interogare
            .OrderBy(s => s.CodTip)
            .ThenByDescending(s => s.Versiune)
            .Select(s => new SablonFormularDto(s.Id, s.CodTip, s.Versiune, s.NumeFisier, s.HashSha256, s.Activ))
            .ToListAsync(ct);
        return Results.Ok(sabloane);
    }

    private static async Task<IResult> IncarcaSablon(
        [FromQuery] string codTip,
        IFormFile fisier,
        FormulareDbContext db,
        IValidatorSablon validator,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codTip))
            return Results.BadRequest(new { mesaj = "Tipul de formular este obligatoriu." });
        var tip = CatalogFormulare.DupaCod(codTip);
        if (tip is null)
            return Results.BadRequest(new { mesaj = $"Tip de formular necunoscut: {codTip}." });
        if (fisier is null || fisier.Length == 0 || !Path.GetExtension(fisier.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { mesaj = "Încarcă un fișier Word .docx valid." });
        if (fisier.Length > 10_000_000)
            return Results.BadRequest(new { mesaj = "Fișierul depășește limita de 10 MB." });

        await using var ms = new MemoryStream((int)fisier.Length);
        await fisier.CopyToAsync(ms, ct);
        var continut = NormalizatorNecesarMateriale.Normalizeaza(ms.ToArray(), tip);
        var probleme = validator.Valideaza(continut, tip);
        if (probleme.Any(p => p.Severitate == Severitate.Eroare))
            return Results.BadRequest(new { mesaj = "Șablonul nu respectă schema formularului.", probleme });

        var existente = await db.Sabloane.Where(s => s.CodTip == codTip).ToListAsync(ct);
        foreach (var sablon in existente.Where(s => s.Activ))
            sablon.Dezactiveaza();

        var versiune = existente.Count == 0 ? 1 : existente.Max(s => s.Versiune) + 1;
        var hash = Convert.ToHexString(SHA256.HashData(continut));
        var nou = new SablonFormular(codTip, versiune, fisier.FileName, continut, hash);
        db.Sabloane.Add(nou);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/formulare/sabloane/{nou.Id}/fisier",
            new SablonFormularDto(nou.Id, nou.CodTip, nou.Versiune, nou.NumeFisier, nou.HashSha256, nou.Activ));
    }

    private static async Task<IResult> DescarcaSablon(Guid id, FormulareDbContext db, CancellationToken ct)
    {
        var sablon = await db.Sabloane.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return sablon is null
            ? Results.NotFound()
            : Results.File(sablon.Continut, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", sablon.NumeFisier);
    }

    private static async Task<IResult> ActiveazaSablon(Guid id, FormulareDbContext db, CancellationToken ct)
    {
        var sablon = await db.Sabloane.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sablon is null)
            return Results.NotFound();

        var versiuni = await db.Sabloane.Where(s => s.CodTip == sablon.CodTip).ToListAsync(ct);
        foreach (var versiune in versiuni)
            versiune.Dezactiveaza();
        sablon.Activeaza();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListaImporturi(FormulareDbContext db, CancellationToken ct)
    {
        var importuri = await db.Importuri.AsNoTracking()
            .OrderByDescending(i => i.CreatLa)
            .Select(i => new ImportFormularLista(i.Id, i.CodTip, i.NumeFisier, i.HashSha256, i.DateJson, i.Stare, i.CreatLa))
            .ToListAsync(ct);
        return Results.Ok(importuri.Select(LaDto));
    }

    private static async Task<IResult> ImportaFormular(
        [FromForm] string codTip,
        [FromForm] string dateJson,
        IFormFile fisier,
        FormulareDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codTip))
            return Results.BadRequest(new { mesaj = "Tipul de formular este obligatoriu." });
        var tip = CatalogFormulare.DupaCod(codTip);
        if (tip is null || !tip.SePoateImporta)
            return Results.BadRequest(new { mesaj = $"Tipul de formular „{codTip}” nu permite import." });
        if (fisier is null || fisier.Length == 0 || !new[] { ".docx", ".pdf" }.Contains(Path.GetExtension(fisier.FileName), StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { mesaj = "Sunt acceptate documente Word .docx sau PDF." });
        if (fisier.Length > 10_000_000)
            return Results.BadRequest(new { mesaj = "Fișierul depășește limita de 10 MB." });
        if (string.IsNullOrWhiteSpace(dateJson))
            return Results.BadRequest(new { mesaj = "Datele formularului sunt obligatorii." });

        DateFormular? date;
        try
        {
            date = JsonSerializer.Deserialize<DateFormular>(dateJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { mesaj = "Datele formularului nu sunt JSON valid." });
        }

        if (date is null)
            return Results.BadRequest(new { mesaj = "Datele formularului sunt obligatorii." });

        var probleme = ValidatorDateFormular.Valideaza(tip, date);
        if (probleme.Any(p => p.Severitate == Severitate.Eroare))
            return Results.BadRequest(new { mesaj = "Formularul are câmpuri obligatorii necompletate.", probleme });

        await using var ms = new MemoryStream((int)fisier.Length);
        await fisier.CopyToAsync(ms, ct);
        var original = ms.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(original));
        var json = JsonSerializer.Serialize(date, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var import = new ImportFormular(tip.Cod, fisier.FileName, hash, original, json);
        db.Importuri.Add(import);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/formulare/import/{import.Id}",
            LaDto(import));
    }

    private static async Task<IResult> DescarcaOriginalImportat(Guid id, FormulareDbContext db, CancellationToken ct)
    {
        var import = await db.Importuri.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return import is null
            ? Results.NotFound()
            : Results.File(import.Original, TipFisier(import.NumeFisier), import.NumeFisier);
    }

    private static string TipFisier(string numeFisier) =>
        Path.GetExtension(numeFisier).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private static ImportFormularDto LaDto(ImportFormularLista import) =>
        new(import.Id, import.CodTip, import.NumeFisier, import.HashSha256,
            JsonSerializer.Deserialize<DateFormular>(import.DateJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? DateFormular.Gol,
            import.Stare, import.CreatLa);

    private static ImportFormularDto LaDto(ImportFormular import) =>
        new(import.Id, import.CodTip, import.NumeFisier, import.HashSha256,
            JsonSerializer.Deserialize<DateFormular>(import.DateJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? DateFormular.Gol,
            import.Stare, import.CreatLa);

    private sealed record ImportFormularLista(Guid Id, string CodTip, string NumeFisier, string HashSha256, string DateJson, string Stare, DateTimeOffset CreatLa);
}
