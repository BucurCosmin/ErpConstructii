using System.Security.Cryptography;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Grafic.Infrastructure;

// ---------------------------------------------------------------- DTO-uri

public sealed record VersiuneGraficDto(
    Guid Id, int Numar, string Denumire, DateOnly? DataActualizare, DateOnly? Start, DateOnly? Sfarsit, string NumeFisier, long Dimensiune,
    string ImportatDe, DateTimeOffset ImportatLa, int Activitati, int Finalizate, int InCurs, int Critice, decimal Procent,
    decimal CostPlanificat, decimal CostRealizat, decimal CostRamas, RezumatComparatie? Comparatie);

public sealed record GraficDto(
    Guid Id, string Denumire, string? CodSursa, string? Moneda, string Format, Guid? VersiuneCurentaId, string CreatDe, DateTimeOffset CreatLa,
    IReadOnlyList<VersiuneGraficDto> Versiuni);

public sealed record NodWbsDto(string Cheie, string Cod, string Denumire, string? CheieParinte, int Ordine, TotalWbs Total);

/// <summary>Activitatea, pentru tabelul / Gantt-ul graficului (fără resurse și pași: vin la detaliu).</summary>
public sealed record ActivitateDto(
    string Cod, string Denumire, string? CheieWbs, string Tip, string Stadiu, decimal Procent,
    DateOnly StartPlanificat, DateOnly SfarsitPlanificat, DateOnly? Start, DateOnly? Sfarsit, DateOnly? StartReal, DateOnly? SfarsitReal,
    decimal DurataZile, decimal DurataRamasaZile, decimal? RezervaTotalaZile, bool Critica,
    decimal CostPlanificat, decimal CostRealizat, decimal CostRamas, IReadOnlyDictionary<string, string> Coduri, int Predecesori);

public sealed record ContinutGraficDto(
    GraficDto Grafic, VersiuneGraficDto Versiune, TotalWbs Total, IReadOnlyList<NodWbsDto> Noduri, IReadOnlyList<ActivitateDto> Activitati,
    IReadOnlyList<CodActivitateCanonic> Coduri);

public sealed record LegaturaActivitateDto(string Cod, string Denumire, string Tip, int DecalajZile, DateOnly? Start, DateOnly? Sfarsit, string Stadiu);

public sealed record IstoricActivitateDto(int Versiune, DateOnly? DataActualizare, decimal Procent, DateOnly? Start, DateOnly? Sfarsit, string Stadiu);

public sealed record DetaliuActivitateDto(
    ActivitateDto Activitate, string? CaleWbs, DateOnly? StartTarziu, DateOnly? SfarsitTarziu, string? Calendar, string? Constrangere,
    IReadOnlyList<LegaturaActivitateDto> Predecesori, IReadOnlyList<LegaturaActivitateDto> Succesori,
    IReadOnlyList<ResursaActivitate> Resurse, IReadOnlyList<PasActivitate> Pasi, IReadOnlyList<IstoricActivitateDto> Istoric);

/// <param name="GraficExistent">Graficul al cărui actualizare ar fi fișierul (același „Project ID” sau cel ales); null = grafic nou.</param>
/// <param name="AcelasiFisier">Fișierul e identic cu o versiune deja importată.</param>
public sealed record PrevizualizareGraficDto(
    string Format, string Denumire, string? CodSursa, string? Moneda, DateOnly? DataActualizare, DateOnly? Start, DateOnly? Sfarsit,
    int NoduriWbs, int Activitati, int Finalizate, int InCurs, int Critice, int Legaturi, int CuResurse, decimal Procent,
    decimal CostPlanificat, decimal CostRealizat, decimal CostRamas, IReadOnlyList<ProblemaImport> Probleme,
    GraficDto? GraficExistent, ComparatieGrafic? Comparatie, int? AcelasiFisier, bool PoateFiConfirmat);

public sealed record RedenumireGraficCerere(string Denumire);

/// <summary>
/// Graficele de execuție ale proiectului (D-57): importul din Primavera (XER), fiecare fișier nou al aceleiași planificări fiind
/// o actualizare (versiune) a graficului; structura WBS cu totalurile ei, activitățile, legăturile, resursele și costurile, comparația
/// cu actualizarea anterioară. Drepturi: <c>proiecte.vizualizare</c> pentru citire, <c>proiecte.editare</c> pentru import / ștergere.
/// </summary>
public sealed class ModulGrafic : IModul
{
    private const long DimensiuneMaxima = 100_000_000;

    public string Nume => GraficDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare) =>
        servicii.AdaugaDbContextModul<GraficDbContext>(configurare, Nume);

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/proiecte/{proiectId:guid}/grafice").WithTags("Grafic");
        var vizualizare = Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Editare);

        g.MapGet("/", Lista).WithName("ListaGrafice").WithSummary("Graficele de execuție ale proiectului, cu actualizările lor")
            .Produces<IReadOnlyList<GraficDto>>().CerePermisiune(vizualizare);
        g.MapPost("/import/previzualizare", Previzualizeaza).WithName("PrevizualizareImportGrafic")
            .WithSummary("Citește graficul (Primavera XER) și arată ce s-ar importa și ce s-a schimbat față de actualizarea anterioară; nu salvează nimic")
            .Produces<PrevizualizareGraficDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status422UnprocessableEntity)
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(DimensiuneMaxima + 100_000)).CerePermisiune(editare);
        g.MapPost("/import", Confirma).WithName("ImportGrafic")
            .WithSummary("Importă graficul: actualizare nouă a graficului existent (același Project ID sau cel ales) sau grafic nou")
            .Produces<VersiuneGraficDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(DimensiuneMaxima + 100_000)).CerePermisiune(editare);
        g.MapPut("/{graficId:guid}", Redenumeste).WithName("RedenumesteGrafic").WithSummary("Redenumește graficul")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapGet("/{graficId:guid}/versiuni/{versiuneId:guid}", Continut).WithName("ContinutGrafic")
            .WithSummary("Actualizarea graficului: structura WBS cu totalurile, activitățile, codurile de activitate")
            .Produces<ContinutGraficDto>().Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapGet("/{graficId:guid}/versiuni/{versiuneId:guid}/activitate", Activitate).WithName("ActivitateGrafic")
            .WithSummary("O activitate (după cod): legăturile, resursele cu costurile, pașii și istoricul pe actualizări")
            .Produces<DetaliuActivitateDto>().Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapGet("/{graficId:guid}/versiuni/{versiuneId:guid}/comparatie", Comparatie).WithName("ComparatieGrafic")
            .WithSummary("Ce s-a schimbat față de actualizarea anterioară: activități noi / șterse, date mutate, procente")
            .Produces<ComparatieGrafic>().Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapGet("/{graficId:guid}/versiuni/{versiuneId:guid}/fisier", Fisier).WithName("FisierGrafic")
            .WithSummary("Descarcă fișierul original al actualizării").Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);
        g.MapDelete("/{graficId:guid}/versiuni/{versiuneId:guid}", StergeVersiune).WithName("StergeVersiuneGrafic")
            .WithSummary("Șterge ultima actualizare (importată din greșeală); graficul cu o singură actualizare se șterge cu totul")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(editare);

        // Costurile ERP alocate pe activități / noduri WBS (D-57, etapa 2).
        EndpointuriCosturi.Mapeaza(g);
    }

    // ---------------------------------------------------------------- lista

    private static async Task<IResult> Lista(Guid proiectId, GraficDbContext db, CancellationToken ct) =>
        Results.Ok(await GraficeAsync(db, proiectId, null, ct));

    private static async Task<IReadOnlyList<GraficDto>> GraficeAsync(GraficDbContext db, Guid proiectId, Guid? graficId, CancellationToken ct)
    {
        var grafice = await db.Grafice.AsNoTracking().Where(x => x.ProiectId == proiectId && (graficId == null || x.Id == graficId))
            .OrderBy(x => x.CreatLa).ToListAsync(ct);
        var ids = grafice.Select(x => x.Id).ToList();
        var versiuni = (await db.Versiuni.AsNoTracking().Where(v => ids.Contains(v.GraficId)).OrderByDescending(v => v.Numar).ToListAsync(ct))
            .ToLookup(v => v.GraficId);
        return grafice.Select(x => Dto(x, versiuni[x.Id])).ToList();
    }

    private static GraficDto Dto(GraficExecutie g, IEnumerable<VersiuneGrafic> versiuni) =>
        new(g.Id, g.Denumire, g.CodSursa, g.Moneda, g.Format, g.VersiuneCurentaId, g.CreatDeNume, g.CreatLa, versiuni.Select(Dto).ToList());

    private static VersiuneGraficDto Dto(VersiuneGrafic v) => new(
        v.Id, v.Numar, v.Denumire, v.DataActualizare, v.Start, v.Sfarsit, v.NumeFisier, v.Dimensiune, v.ImportatDeNume, v.CreatLa,
        v.Activitati, v.Finalizate, v.InCurs, v.Critice, v.Procent, v.CostPlanificat, v.CostRealizat, v.CostRamas, v.Comparatie);

    // ---------------------------------------------------------------- importul

    private sealed record Pregatire(IImportatorGrafic Importator, SursaImport Sursa, GraficCanonic Canonic, PrevizualizareGraficDto Previzualizare);

    private static async Task<IResult> Previzualizeaza(
        Guid proiectId, IFormFile fisier, [FromForm] Guid? graficId, [FromForm] bool? graficNou, IEnumerable<IImportatorGrafic> importatori,
        GraficDbContext db, CancellationToken ct)
    {
        var (p, eroare) = await PregatesteAsync(proiectId, fisier, graficId, graficNou == true, importatori, db, ct);
        return eroare ?? Results.Ok(p!.Previzualizare);
    }

    private static async Task<IResult> Confirma(
        Guid proiectId, IFormFile fisier, [FromForm] Guid? graficId, [FromForm] bool? graficNou, [FromForm] string? denumire,
        IEnumerable<IImportatorGrafic> importatori, IInterogareProiecte proiecte, GraficDbContext db, IUtilizatorCurent utilizator, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        if (proiect.Arhivat)
            return Results.Conflict(new { mesaj = proiect.MesajArhivat });

        var (p, eroare) = await PregatesteAsync(proiectId, fisier, graficId, graficNou == true, importatori, db, ct);
        if (eroare is not null)
            return eroare;
        if (!p!.Previzualizare.PoateFiConfirmat)
            return Results.UnprocessableEntity(p.Previzualizare);

        var c = p.Canonic;
        GraficExecutie grafic;
        ComparatieGrafic? comparatie = null;
        if (p.Previzualizare.GraficExistent is { } existent)
        {
            grafic = await db.Grafice.FirstAsync(x => x.Id == existent.Id, ct);
            comparatie = p.Previzualizare.Comparatie;
        }
        else
        {
            grafic = GraficExecutie.Creeaza(proiectId, string.IsNullOrWhiteSpace(denumire) ? c.Denumire : denumire, c.CodSursa, c.Moneda,
                p.Importator.FormatId, utilizator.Nume);
            db.Grafice.Add(grafic);
        }

        var versiune = VersiuneGrafic.Creeaza(grafic.Id, grafic.UrmatoareaVersiune(), c, p.Sursa.NumeFisier, Sha(p.Sursa.Continut),
            p.Sursa.Continut.LongLength, utilizator.Nume, comparatie);
        grafic.Actualizeaza(versiune, c.Moneda);
        db.Versiuni.Add(versiune);
        db.Fisiere.Add(FisierVersiuneGrafic.Creeaza(versiune.Id, p.Sursa.Continut));
        db.Noduri.AddRange((c.Wbs ?? []).Select(n => NodWbsGrafic.Din(versiune.Id, n)));
        db.Activitati.AddRange(c.Activitati.Select(a => ActivitateGrafic.Din(versiune.Id, a)));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { mesaj = "Graficul a fost actualizat între timp de altcineva. Reîncearcă importul." });
        }

        return Results.Created($"/api/proiecte/{proiectId}/grafice/{grafic.Id}/versiuni/{versiune.Id}", Dto(versiune));
    }

    private static async Task<(Pregatire? Pregatire, IResult? Eroare)> PregatesteAsync(
        Guid proiectId, IFormFile? fisier, Guid? graficId, bool graficNou, IEnumerable<IImportatorGrafic> importatori, GraficDbContext db,
        CancellationToken ct)
    {
        if (fisier is null || fisier.Length == 0)
            return (null, Results.BadRequest(new { mesaj = "Alege fișierul graficului (export Primavera .xer)." }));
        if (fisier.Length > DimensiuneMaxima)
            return (null, Results.BadRequest(new { mesaj = $"Fișierul depășește {DimensiuneMaxima / 1_000_000} MB." }));

        SursaImport sursa;
        using (var ms = new MemoryStream((int)fisier.Length))
        {
            await fisier.CopyToAsync(ms, ct);
            sursa = new SursaImport(Path.GetFileName(fisier.FileName), ms.ToArray());
        }

        var (importator, motive) = AlegereImportator.Alege<IImportatorGrafic, GraficCanonic>(importatori, sursa);
        if (importator is null)
            return (null, Results.BadRequest(new { mesaj = "Formatul fișierului nu e recunoscut: exportă graficul din Primavera ca XER.", motive }));
        var citit = await importator.CitesteAsync(sursa, ct);
        if (citit.Valoare is not { } canonic)
            return (null, Results.UnprocessableEntity(new { mesaj = citit.Probleme.FirstOrDefault()?.Mesaj ?? "Fișierul nu a putut fi citit.", probleme = citit.Probleme }));

        // Graficul existent: cel ales, altfel cel cu același „Project ID” pe proiect (o actualizare nouă a aceleiași planificări).
        GraficExecutie? existent = null;
        if (!graficNou)
        {
            existent = graficId is { } gid
                ? await db.Grafice.AsNoTracking().FirstOrDefaultAsync(x => x.Id == gid && x.ProiectId == proiectId, ct)
                : canonic.CodSursa is { } cod
                    ? await db.Grafice.AsNoTracking().Where(x => x.ProiectId == proiectId && x.CodSursa == cod).OrderByDescending(x => x.CreatLa).FirstOrDefaultAsync(ct)
                    : null;
            if (graficId is not null && existent is null)
                return (null, Results.NotFound(new { mesaj = "Graficul ales nu există pe proiect." }));
        }

        var probleme = citit.Probleme.ToList();
        GraficDto? existentDto = null;
        ComparatieGrafic? comparatie = null;
        int? acelasiFisier = null;
        if (existent is not null)
        {
            existentDto = (await GraficeAsync(db, proiectId, existent.Id, ct))[0];
            var sha = Sha(sursa.Continut);
            acelasiFisier = await db.Versiuni.AsNoTracking().Where(v => v.GraficId == existent.Id && v.Sha256 == sha).Select(v => (int?)v.Numar).FirstOrDefaultAsync(ct);
            if (acelasiFisier is not null)
                probleme.Add(new ProblemaImport(Severitate.Eroare, "fișier", $"Același fișier a fost importat deja ca actualizarea nr. {acelasiFisier}."));
            if (existent.CodSursa is { } c && canonic.CodSursa is { } n && !string.Equals(c, n, StringComparison.Ordinal))
                probleme.Add(new ProblemaImport(Severitate.Avertisment, "fișier",
                    $"Fișierul e al planificării „{n}”, iar graficul ales e „{c}”: verifică dacă e aceeași planificare."));
            var curenta = existent.VersiuneCurentaId;
            if (curenta is not null)
            {
                var anterioara = await db.Versiuni.AsNoTracking().FirstAsync(v => v.Id == curenta, ct);
                if (canonic.DataReferinta is { } dn && anterioara.DataActualizare is { } dv && dn < dv)
                    probleme.Add(new ProblemaImport(Severitate.Avertisment, "fișier",
                        $"Data actualizării din fișier ({dn:dd.MM.yyyy}) e înaintea celei din actualizarea curentă ({dv:dd.MM.yyyy})."));
                var vechi = await db.Activitati.AsNoTracking().Where(a => a.VersiuneId == curenta).ToListAsync(ct);
                comparatie = ComparareGrafice.Compara(vechi.Select(a => a.Canonic()).ToList(), canonic.Activitati);
            }
        }

        var total = AnalizaGrafic.Total(canonic.Activitati);
        var dto = new PrevizualizareGraficDto(
            importator.FormatId, canonic.Denumire, canonic.CodSursa, canonic.Moneda, canonic.DataReferinta, total.Start, total.Sfarsit,
            canonic.Wbs?.Count ?? 0, total.Activitati, total.Finalizate, total.InCurs, total.Critice,
            canonic.Activitati.Sum(a => a.Dependente.Count), canonic.Activitati.Count(a => a.Resurse is { Count: > 0 }), total.Procent,
            Math.Round(total.CostPlanificat, 2), Math.Round(total.CostRealizat, 2), Math.Round(total.CostRamas, 2),
            probleme, existentDto, comparatie, acelasiFisier,
            canonic.Activitati.Count > 0 && probleme.All(x => x.Severitate != Severitate.Eroare));
        return (new Pregatire(importator, sursa, canonic, dto), null);
    }

    private static string Sha(byte[] continut) => Convert.ToHexStringLower(SHA256.HashData(continut));

    private static async Task<IResult> Redenumeste(Guid proiectId, Guid graficId, RedenumireGraficCerere cerere, GraficDbContext db, CancellationToken ct)
    {
        var g = await db.Grafice.FirstOrDefaultAsync(x => x.Id == graficId && x.ProiectId == proiectId, ct);
        if (g is null)
            return Results.NotFound();
        if (g.Redenumeste(cerere.Denumire) is { Esec: true } r)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- conținutul

    private static async Task<(GraficExecutie, VersiuneGrafic)?> VersiuneAsync(GraficDbContext db, Guid proiectId, Guid graficId, Guid versiuneId, CancellationToken ct)
    {
        var g = await db.Grafice.AsNoTracking().FirstOrDefaultAsync(x => x.Id == graficId && x.ProiectId == proiectId, ct);
        var v = g is null ? null : await db.Versiuni.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versiuneId && x.GraficId == graficId, ct);
        return g is null || v is null ? null : (g, v);
    }

    private static ActivitateDto Dto(ActivitateGrafic a) => new(
        a.Cod, a.Denumire, a.CheieWbs, a.Tip.ToString(), a.Stadiu.ToString(), a.Procent, a.StartPlanificat, a.SfarsitPlanificat, a.Start, a.Sfarsit,
        a.StartReal, a.SfarsitReal, a.DurataZile, a.DurataRamasaZile, a.RezervaTotalaZile, a.Critica, a.CostPlanificat, a.CostRealizat, a.CostRamas,
        a.Coduri, a.Dependente.Count);

    private static async Task<IResult> Continut(Guid proiectId, Guid graficId, Guid versiuneId, GraficDbContext db, CancellationToken ct)
    {
        if (await VersiuneAsync(db, proiectId, graficId, versiuneId, ct) is not var (g, v))
            return Results.NotFound();
        var noduri = await db.Noduri.AsNoTracking().Where(n => n.VersiuneId == versiuneId).OrderBy(n => n.Ordine).ToListAsync(ct);
        var activitati = await db.Activitati.AsNoTracking().Where(a => a.VersiuneId == versiuneId).OrderBy(a => a.Start).ThenBy(a => a.Cod).ToListAsync(ct);
        var canonice = activitati.Select(a => a.Canonic()).ToList();
        var totaluri = AnalizaGrafic.Totalizeaza(noduri.Select(n => n.Canonic()).ToList(), canonice);
        var versiuni = await db.Versiuni.AsNoTracking().Where(x => x.GraficId == graficId).OrderByDescending(x => x.Numar).ToListAsync(ct);
        return Results.Ok(new ContinutGraficDto(
            Dto(g, versiuni), Dto(v), AnalizaGrafic.Total(canonice),
            noduri.Select(n => new NodWbsDto(n.Cheie, n.Cod, n.Denumire, n.CheieParinte, n.Ordine, totaluri[n.Cheie])).ToList(),
            activitati.Select(Dto).ToList(), v.Coduri));
    }

    private static async Task<IResult> Activitate(Guid proiectId, Guid graficId, Guid versiuneId, string cod, GraficDbContext db, CancellationToken ct)
    {
        if (await VersiuneAsync(db, proiectId, graficId, versiuneId, ct) is null)
            return Results.NotFound();
        var toate = await db.Activitati.AsNoTracking().Where(a => a.VersiuneId == versiuneId).ToListAsync(ct);
        var a = toate.FirstOrDefault(x => x.Cod == cod);
        if (a is null)
            return Results.NotFound(new { mesaj = $"Activitatea {cod} nu e în această actualizare." });
        var dupaCod = toate.ToDictionary(x => x.Cod, StringComparer.Ordinal);
        LegaturaActivitateDto Leg(ActivitateGrafic x, Dependenta d) => new(x.Cod, x.Denumire, d.Tip.ToString(), d.DecalajZile, x.Start, x.Sfarsit, x.Stadiu.ToString());
        var predecesori = a.Dependente.Where(d => dupaCod.ContainsKey(d.CodPredecesor)).Select(d => Leg(dupaCod[d.CodPredecesor], d)).ToList();
        var succesori = toate.SelectMany(x => x.Dependente.Where(d => d.CodPredecesor == cod).Select(d => Leg(x, d))).OrderBy(x => x.Start).ToList();

        // Calea WBS: denumirile nodurilor de la rădăcină.
        var noduri = await db.Noduri.AsNoTracking().Where(n => n.VersiuneId == versiuneId).ToDictionaryAsync(n => n.Cheie, ct);
        var cale = new List<string>();
        for (var k = a.CheieWbs; k is not null && noduri.TryGetValue(k, out var n); k = n.CheieParinte)
            cale.Insert(0, n.Denumire);

        // Istoricul activității în toate actualizările graficului.
        var istoric = await (from x in db.Activitati.AsNoTracking()
                             join v in db.Versiuni.AsNoTracking() on x.VersiuneId equals v.Id
                             where v.GraficId == graficId && x.Cod == cod
                             orderby v.Numar
                             select new IstoricActivitateDto(v.Numar, v.DataActualizare, x.Procent, x.Start, x.Sfarsit, x.Stadiu.ToString())).ToListAsync(ct);
        return Results.Ok(new DetaliuActivitateDto(
            Dto(a), cale.Count == 0 ? null : string.Join(" › ", cale), a.StartTarziu, a.SfarsitTarziu, a.Calendar, a.Constrangere,
            predecesori, succesori, a.Resurse, a.Pasi, istoric));
    }

    private static async Task<IResult> Comparatie(Guid proiectId, Guid graficId, Guid versiuneId, GraficDbContext db, CancellationToken ct)
    {
        if (await VersiuneAsync(db, proiectId, graficId, versiuneId, ct) is not var (_, v))
            return Results.NotFound();
        var anterioara = await db.Versiuni.AsNoTracking().Where(x => x.GraficId == graficId && x.Numar < v.Numar).OrderByDescending(x => x.Numar)
            .FirstOrDefaultAsync(ct);
        if (anterioara is null)
            return Results.NotFound(new { mesaj = "Este prima actualizare a graficului: nu are cu ce se compara." });
        var vechi = await db.Activitati.AsNoTracking().Where(a => a.VersiuneId == anterioara.Id).ToListAsync(ct);
        var nou = await db.Activitati.AsNoTracking().Where(a => a.VersiuneId == v.Id).ToListAsync(ct);
        return Results.Ok(ComparareGrafice.Compara(vechi.Select(a => a.Canonic()).ToList(), nou.Select(a => a.Canonic()).ToList()));
    }

    private static async Task<IResult> Fisier(Guid proiectId, Guid graficId, Guid versiuneId, GraficDbContext db, CancellationToken ct)
    {
        if (await VersiuneAsync(db, proiectId, graficId, versiuneId, ct) is not var (_, v))
            return Results.NotFound();
        var continut = await db.Fisiere.AsNoTracking().Where(f => f.VersiuneId == versiuneId).Select(f => f.Continut).FirstOrDefaultAsync(ct);
        return continut is null ? Results.NotFound() : Results.File(continut, "application/octet-stream", v.NumeFisier);
    }

    private static async Task<IResult> StergeVersiune(Guid proiectId, Guid graficId, Guid versiuneId, GraficDbContext db, CancellationToken ct)
    {
        var g = await db.Grafice.FirstOrDefaultAsync(x => x.Id == graficId && x.ProiectId == proiectId, ct);
        var v = g is null ? null : await db.Versiuni.FirstOrDefaultAsync(x => x.Id == versiuneId && x.GraficId == graficId, ct);
        if (g is null || v is null)
            return Results.NotFound();
        if (await db.Versiuni.AnyAsync(x => x.GraficId == graficId && x.Numar > v.Numar, ct))
            return Results.Conflict(new { mesaj = "Se poate șterge doar ultima actualizare a graficului." });
        // Graficul întreg (singura actualizare) nu se șterge cât are costuri alocate: alocările ar rămâne fără grafic.
        if (v.Numar == 1 && await db.Alocari.AnyAsync(a => a.GraficId == graficId, ct))
            return Results.Conflict(new { mesaj = "Graficul are costuri alocate: importă o actualizare corectă în locul lui sau șterge întâi alocările." });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await db.Activitati.Where(a => a.VersiuneId == versiuneId).ExecuteDeleteAsync(ct);
        await db.Noduri.Where(n => n.VersiuneId == versiuneId).ExecuteDeleteAsync(ct);
        await db.Fisiere.Where(f => f.VersiuneId == versiuneId).ExecuteDeleteAsync(ct);
        db.Versiuni.Remove(v);
        var anterioara = await db.Versiuni.Where(x => x.GraficId == graficId && x.Numar < v.Numar).OrderByDescending(x => x.Numar).FirstOrDefaultAsync(ct);
        if (anterioara is null)
            db.Grafice.Remove(g);
        else
            g.RevinoLa(anterioara);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.NoContent();
    }
}
