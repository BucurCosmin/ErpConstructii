using System.Globalization;
using System.Text.Json;
using ErpConstructii.Aprovizionare.Import;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Aprovizionare.Infrastructure;

public sealed record CampTextDto(string? Valoare, string Certitudine, string? TextSursa);

public sealed record CampDataDto(DateOnly? Valoare, string Certitudine, string? TextSursa);

public sealed record CampNumarDto(decimal? Valoare, string Certitudine, string? TextSursa);

public sealed record LinieInterpretataDto(int Rand, CampTextDto Denumire, CampTextDto UnitateMasura, CampNumarDto Cantitate, CampTextDto Observatii);

public sealed record ProiectPropusDto(Guid Id, string Cod, string Denumire, double Scor, string Certitudine);

public sealed record ProblemaImportDto(string Severitate, string Locatie, string Mesaj);

/// <summary>Ce a înțeles sistemul dintr-un document extern; nu se salvează nimic până la confirmare.</summary>
/// <param name="NecesarExistent">Numărul necesarului, dacă același fișier a mai fost importat.</param>
public sealed record InterpretareNecesarDto(
    string NumeFisier,
    bool DinOcr,
    CampTextDto NumarExtern,
    CampDataDto Data,
    CampTextDto CompartimentSantier,
    CampTextDto Lucrare,
    ProiectPropusDto? ProiectPropus,
    IReadOnlyList<LinieInterpretataDto> Linii,
    IReadOnlyList<ProblemaImportDto> Probleme,
    string? NecesarExistent);

/// <summary>Datele verificate și completate de utilizator, trimise împreună cu fișierul original.</summary>
public sealed record ConfirmaImportNecesarCerere(
    string? NumarExtern,
    DateOnly DataDocument,
    string? CompartimentSantier,
    IReadOnlyList<LinieNecesarMaterialeCerere> Linii,
    Guid? SantierId = null,
    Guid? CompartimentId = null,
    bool DinUrma = false,
    IReadOnlyList<Guid>? NoteComandaIds = null);

/// <summary>
/// Importul necesarelor de materiale emise în afara sistemului (scanări PDF, poze, Word):
/// 1) interpretare (OCR sau citire Word + reguli) → propunere cu câmpurile sigure / de verificat / lipsă;
/// 2) confirmare → necesar cu număr de sistem, numărul original păstrat, originalul arhivat, în așteptarea aprobării.
/// </summary>
internal static class ImportNecesarMateriale
{
    private const string CodTipNecesar = "necesar-materiale";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Mapeaza(RouteGroupBuilder grup)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);

        grup.MapPost("/necesare-materiale/import/interpretare", Interpreteaza)
            .WithName("InterpreteazaNecesarMaterialeExtern")
            .WithSummary("Citește un necesar de materiale emis în afara sistemului (PDF scanat, imagine, Word) și propune datele")
            .Produces<InterpretareNecesarDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentOriginal.DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);

        grup.MapPost("/proiecte/{proiectId:guid}/necesare-materiale/import", Confirma)
            .WithName("ImportaNecesarMaterialeExtern")
            .WithSummary("Creează necesarul de materiale din documentul extern verificat și arhivează originalul")
            .Produces<NecesarMaterialeDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentOriginal.DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);

        grup.MapGet("/proiecte/{proiectId:guid}/necesare-materiale/{id:guid}/original", DescarcaOriginal)
            .WithName("DescarcaOriginalNecesarMateriale")
            .WithSummary("Descarcă documentul original al unui necesar importat")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);
    }

    private static async Task<IResult> Interpreteaza(
        IFormFile fisier,
        IEnumerable<IExtractorText> extractori,
        IInterogareProiecte proiecte,
        AprovizionareDbContext db,
        ICeas ceas,
        CancellationToken ct)
    {
        var (sursa, extractor, eroare) = await CitesteFisierAsync(fisier, extractori, ct);
        if (eroare is not null)
        {
            return eroare;
        }

        var extras = await extractor!.ExtrageAsync(sursa!, ct);
        if (extras.Valoare is null)
        {
            return Results.BadRequest(new { mesaj = extras.Probleme.FirstOrDefault()?.Mesaj ?? "Documentul nu poate fi citit." });
        }

        // Dintr-un dosar scanat cu mai multe documente se citesc doar paginile necesarului.
        var (document, pagini) = ClasificarePagini.PentruTip(extras.Valoare, TipPagina.NecesarMateriale);
        var necesar = InterpretorNecesarMateriale.Interpreteaza(document);
        var candidati = (await proiecte.ListaActiveAsync(ct)).Select(p => new ProiectCandidat(p.Id, p.Cod, p.Denumire)).ToList();
        var propunere = PotrivireProiect.Propune(necesar.Lucrare.Valoare, candidati);
        var existenta = await NecesarCuAcelasiOriginalAsync(db, DocumentOriginal.Amprenta(sursa!.Continut), ct)
            ?? await NecesarDublatAsync(db, necesar.NumarExtern.Valoare, necesar.Data.Certitudine == Certitudine.Lipsa ? null : necesar.Data.Valoare, ct);

        var probleme = extras.Probleme.Concat(necesar.Probleme).Concat(ProblemaPagini(pagini, document, "necesarul")).ToList();

        // D-48: seria de hârtie e pe proiect și an. Fără număr pe document, se propune primul număr liber din seria proiectului
        // propus; un număr folosit deja în serie e semnalat. Fără proiect propus, nu se poate propune nimic.
        var numarExtern = Text(necesar.NumarExtern);
        var anDocument = necesar.Data.Certitudine == Certitudine.Lipsa ? ceas.Azi.Year : necesar.Data.Valoare.Year;
        if (propunere is not null)
        {
            var serie = await EndpointuriNumereNecesare.DocumenteAsync(db, propunere.Proiect.Id, anDocument, ct);
            if (NumerotareExterna.Valoare(necesar.NumarExtern.Valoare) is not > 0)
            {
                var liber = NumerotareExterna.NumarLiber(serie.Select(d => d.NumarExtern)).ToString(CultureInfo.InvariantCulture);
                numarExtern = new CampTextDto(liber, Certitudine.Nesigur.ToString(), $"propus: primul număr liber pe {propunere.Proiect.Cod} în {anDocument}");
                probleme.Insert(0, new ProblemaImport(Severitate.Informare, "numar",
                    $"Documentul nu are număr; propun nr. {liber}, primul liber pe proiectul {propunere.Proiect.Cod} în {anDocument} (îl poți schimba)."));
            }
            else if (existenta is null &&
                     NumerotareExterna.Folosit(necesar.NumarExtern.Valoare, propunere.Proiect.Id, anDocument, serie) is { Count: > 0 } folosit)
            {
                probleme.Insert(0, new ProblemaImport(Severitate.Avertisment, "numar",
                    $"Nr. {necesar.NumarExtern.Valoare} e folosit deja pe proiectul {propunere.Proiect.Cod} în {anDocument} de " +
                    $"{string.Join(", ", folosit.Select(d => $"{d.NumarSistem} din {d.Data:dd.MM.yyyy}"))}. Poți importa și renumerota apoi din „Verifică numerele”."));
            }
        }
        if (propunere is null && necesar.Lucrare.Valoare is not null)
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "lucrare",
                "Nu am găsit proiectul după textul lucrării; alege-l din listă."));
        }

        return Results.Ok(new InterpretareNecesarDto(
            sursa.NumeFisier,
            necesar.DinOcr,
            numarExtern,
            new CampDataDto(necesar.Data.Certitudine == Certitudine.Lipsa ? null : necesar.Data.Valoare, necesar.Data.Certitudine.ToString(), necesar.Data.TextSursa),
            Text(necesar.CompartimentSantier),
            Text(necesar.Lucrare),
            propunere is null
                ? null
                : new ProiectPropusDto(propunere.Proiect.Id, propunere.Proiect.Cod, propunere.Proiect.Denumire, propunere.Scor, propunere.Certitudine.ToString()),
            necesar.Linii.Select(l => new LinieInterpretataDto(
                l.Rand,
                Text(l.Denumire),
                Text(l.UnitateMasura),
                new CampNumarDto(l.Cantitate.Certitudine == Certitudine.Lipsa ? null : l.Cantitate.Valoare, l.Cantitate.Certitudine.ToString(), l.Cantitate.TextSursa),
                Text(l.Observatii))).ToList(),
            probleme.Select(p => new ProblemaImportDto(p.Severitate.ToString(), p.Locatie, p.Mesaj)).ToList(),
            existenta));
    }

    private static async Task<IResult> Confirma(
        Guid proiectId,
        IFormFile fisier,
        [FromForm] string date,
        IEnumerable<IExtractorText> extractori,
        AprovizionareDbContext db,
        IInterogareProiecte proiecte,
        IInterogareSabloaneFormulare sabloane,
        IFirmaCurenta firma,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        ConfirmaImportNecesarCerere? cerere;
        try
        {
            cerere = JsonSerializer.Deserialize<ConfirmaImportNecesarCerere>(date, Json);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { mesaj = "Datele necesarului nu sunt valide." });
        }

        if (cerere is null)
        {
            return Results.BadRequest(new { mesaj = "Datele necesarului sunt obligatorii." });
        }

        var (sursa, _, eroare) = await CitesteFisierAsync(fisier, extractori, ct);
        if (eroare is not null)
        {
            return eroare;
        }

        if (firma.FirmaId is not { } firmaId || utilizator.UtilizatorId is not { } utilizatorId)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(utilizator.Nume) || utilizator.Nume.Length > 200)
        {
            return Results.BadRequest(new { mesaj = "Numele emitentului nu este disponibil în sesiune." });
        }

        if (cerere.DataDocument.Year < 2000 || cerere.DataDocument > ceas.Azi.AddDays(1))
        {
            return Results.BadRequest(new { mesaj = "Data documentului nu este validă (nu poate fi în viitor)." });
        }

        if (cerere.Linii is null || cerere.Linii.Count == 0)
        {
            return Results.BadRequest(new { mesaj = "Adaugă cel puțin un material." });
        }

        // D-45: notele de comandă importate deja se leagă doar de un necesar din urmă (altfel necesarul își generează nota lui).
        var noteDeLegat = (cerere.NoteComandaIds ?? []).Distinct().ToList();
        if (noteDeLegat.Count > 0 && !cerere.DinUrma)
        {
            return Results.BadRequest(new { mesaj = "Notele de comandă existente se leagă doar de un necesar marcat „document din urmă”." });
        }

        var hash = DocumentOriginal.Amprenta(sursa!.Continut);
        if (await NecesarCuAcelasiOriginalAsync(db, hash, ct) is { } existenta)
        {
            return Results.Conflict(new { mesaj = $"Acest document a fost deja importat ca necesarul {existenta}." });
        }

        // Același necesar dintr-un alt fișier (altă scanare): același număr și aceeași dată.
        if (await NecesarDublatAsync(db, cerere.NumarExtern, cerere.DataDocument, ct) is { } dublat)
        {
            return Results.Conflict(new { mesaj = $"Necesarul nr. {cerere.NumarExtern!.Trim()} din {cerere.DataDocument:dd.MM.yyyy} a fost deja importat: {dublat}." });
        }

        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
        {
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        }

        if (proiect.Arhivat)
        {
            return Results.Conflict(new { mesaj = proiect.MesajArhivat });
        }

        // Opțional: cu șablon activ, necesarul importat poate fi și regenerat în formatul firmei.
        var sablon = await sabloane.SablonActivAsync(CodTipNecesar, ct);

        // D-38: șantierul / compartimentul ales; din aplicația Windows vine doar textul, potrivit cu cele existente.
        var (loc, eroareLoc) = await ModulAprovizionare.RezolvaSolicitantAsync(proiecte, proiect.Id, cerere.SantierId, cerere.CompartimentId, cerere.CompartimentSantier, ct);
        if (eroareLoc is not null)
        {
            return eroareLoc;
        }

        var azi = ceas.Azi;
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulAprovizionare.BlocheazaSecventaAsync(db, tranzactie, firmaId, azi.Year, ct);
        var numar = (await db.NecesareMateriale.Where(n => n.An == azi.Year).MaxAsync(n => (int?)n.Numar, ct) ?? 0) + 1;

        var original = new DocumentOriginal(sursa.NumeFisier, TipContinut(sursa.Extensie), sursa.Continut);
        var rezultat = NecesarMateriale.Importa(
            azi.Year, numar, cerere.NumarExtern, proiect.Id, proiect.Cod, proiect.Denumire,
            loc!.Denumire, cerere.DataDocument, utilizatorId, utilizator.Nume,
            sablon?.Id ?? Guid.Empty, sablon?.Versiune ?? 0,
            cerere.Linii.Select(l => new LinieNecesarMateriale(
                (l.Denumire ?? string.Empty).Trim(), (l.UnitateMasura ?? string.Empty).Trim(), l.Cantitate,
                l.Observatii?.Trim() ?? string.Empty, l.ArticolDevizId)).ToList(),
            original.Id);
        if (rezultat.Esec)
        {
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });
        }

        var solicitant = rezultat.Valoare.StabilesteSolicitantul(loc);
        if (solicitant.Esec)
        {
            return Results.BadRequest(new { mesaj = solicitant.Eroare.Mesaj });
        }

        db.DocumenteOriginale.Add(original);
        db.NecesareMateriale.Add(rezultat.Valoare);
        if (cerere.DinUrma)
        {
            // D-45: necesarul vechi intră aprobat, fără nota „de acceptat” (comanda s-a făcut deja); notele lui importate se leagă de el.
            var dinUrma = rezultat.Valoare.MarcheazaDinUrma(ceas.Acum);
            if (dinUrma.Esec)
            {
                return Results.BadRequest(new { mesaj = dinUrma.Eroare.Mesaj });
            }

            var note = noteDeLegat.Count == 0 ? [] : await db.NoteComanda.Where(n => noteDeLegat.Contains(n.Id)).ToListAsync(ct);
            if (note.Count != noteDeLegat.Count)
            {
                return Results.NotFound(new { mesaj = "O notă de comandă aleasă nu mai există." });
            }

            foreach (var n in note)
            {
                var legare = n.LeagaDeNecesar(rezultat.Valoare, utilizatorId, utilizator.Nume, ceas.Acum);
                if (legare.Esec)
                {
                    return Results.BadRequest(new { mesaj = $"{n.NumarAfisat}: {legare.Eroare.Mesaj}" });
                }

                db.CorectiiNoteComanda.Add(legare.Valoare);
            }
        }
        else
        {
            // D-37: și necesarul importat generează nota de comandă „de acceptat”.
            var nota = await FluxNotaComanda.GenereazaAsync(db, tranzactie, firmaId, azi.Year, rezultat.Valoare, utilizatorId, utilizator.Nume, ct);
            if (nota.Esec)
            {
                return Results.BadRequest(new { mesaj = nota.Eroare.Mesaj });
            }
        }

        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);

        var dto = MapareNecesarMateriale.LaDto(rezultat.Valoare);
        return Results.Created($"/api/aprovizionare/proiecte/{proiectId}/necesare-materiale/{dto.Id}", dto);
    }

    private static async Task<IResult> DescarcaOriginal(Guid proiectId, Guid id, AprovizionareDbContext db, CancellationToken ct)
    {
        var documentId = await db.NecesareMateriale.AsNoTracking()
            .Where(n => n.Id == id && n.ProiectId == proiectId)
            .Select(n => n.DocumentOriginalId)
            .FirstOrDefaultAsync(ct);
        var original = documentId is null
            ? null
            : await db.DocumenteOriginale.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        return original is null
            ? Results.NotFound()
            : Results.File(original.Continut, original.TipContinut, original.NumeFisier, enableRangeProcessing: true);
    }

    internal static async Task<(SursaImport? Sursa, IExtractorText? Extractor, IResult? Eroare)> CitesteFisierAsync(
        IFormFile? fisier, IEnumerable<IExtractorText> extractori, CancellationToken ct)
    {
        if (fisier is null || fisier.Length == 0)
        {
            return (null, null, Results.BadRequest(new { mesaj = "Alege documentul (PDF scanat, imagine sau Word)." }));
        }

        if (fisier.Length > DocumentOriginal.DimensiuneMaxima)
        {
            return (null, null, Results.BadRequest(new { mesaj = "Fișierul depășește limita de 15 MB." }));
        }

        var extractor = extractori.FirstOrDefault(e => e.Accepta(fisier.FileName));
        if (extractor is null)
        {
            return (null, null, Results.BadRequest(new { mesaj = "Format nesuportat. Încarcă un PDF, o imagine (JPG, PNG, TIFF) sau un document Word .docx." }));
        }

        using var ms = new MemoryStream((int)fisier.Length);
        await fisier.CopyToAsync(ms, ct);
        return (new SursaImport(fisier.FileName, ms.ToArray()), extractor, null);
    }

    /// <summary>Necesarul importat deja cu același număr extern și aceeași dată (din orice fișier).</summary>
    private static async Task<string?> NecesarDublatAsync(AprovizionareDbContext db, string? numarExtern, DateOnly? data, CancellationToken ct)
    {
        if (ClasificarePagini.CheieNumar(numarExtern) is null || data is null)
        {
            return null;
        }

        var existente = await db.NecesareMateriale.AsNoTracking()
            .Where(n => n.NumarExtern != null && n.Data == data)
            .Select(n => new { n.An, n.Numar, n.NumarExtern, n.Data })
            .ToListAsync(ct);
        return DocumentDublat.DupaNumarSiData(numarExtern, data,
            existente.Select(n => new DocumentImportat($"NM/{n.An}/{n.Numar:D4}", n.NumarExtern, n.Data)))?.Descriere;
    }

    private static async Task<string?> NecesarCuAcelasiOriginalAsync(AprovizionareDbContext db, string hash, CancellationToken ct)
    {
        var documentId = await db.DocumenteOriginale.AsNoTracking()
            .Where(d => d.HashSha256 == hash)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (documentId is null)
        {
            return null;
        }

        var necesar = await db.NecesareMateriale.AsNoTracking()
            .Where(n => n.DocumentOriginalId == documentId)
            .Select(n => new { n.An, n.Numar })
            .FirstOrDefaultAsync(ct);
        return necesar is null ? "(fără număr)" : $"NM/{necesar.An}/{necesar.Numar:D4}";
    }

    /// <summary>Informare când documentul are mai multe pagini: din care s-a citit și ce mai conține.</summary>
    internal static IEnumerable<ProblemaImport> ProblemaPagini(IReadOnlyList<PaginaClasificata> pagini, TextDocument document, string ce)
    {
        if (pagini.Count <= 1)
        {
            yield break;
        }

        var folosite = document.Randuri.Select(r => r.Pagina).Distinct().Order().ToList();
        if (folosite.Count == pagini.Count)
        {
            yield return new ProblemaImport(Severitate.Avertisment, "pagini",
                $"Nu am recunoscut {ce} între cele {pagini.Count} pagini; am citit tot documentul. Verifică atent datele.");
            yield break;
        }

        var altele = pagini.Where(p => !folosite.Contains(p.Pagina)).ToList();
        yield return new ProblemaImport(Severitate.Informare, "pagini",
            $"PDF-ul are {pagini.Count} pagini; am citit {ce} din {(folosite.Count == 1 ? "pagina" : "paginile")} {string.Join(", ", folosite)}. " +
            $"Mai conține: {string.Join("; ", altele.Select(p => $"p. {p.Pagina} – {p.Descriere}"))}.");
    }

    private static CampTextDto Text(CampCitit<string> c) => new(c.Valoare, c.Certitudine.ToString(), c.TextSursa);

    internal static string TipContinut(string extensie) => extensie switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tif" or ".tiff" => "image/tiff",
        ".bmp" => "image/bmp",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream",
    };
}
