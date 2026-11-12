using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

public sealed record ProblemaDto(string Severitate, string Locatie, string Mesaj);

/// <summary>Un rând al recapitulației: ce scrie în fișier și ce dă recalculul ERP (cu rotunjirea versiunii).</summary>
public sealed record ComparatieTotalDto(string Rand, decimal? Declarat, decimal Recalculat);

/// <param name="CostDirect">Costul direct al articolului (lei); <paramref name="ValoareOferta"/> = prețul de ofertă total, în moneda versiunii.</param>
public sealed record ArticolImportDto(
    string? CodWbs, string Capitol, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, decimal CostDirect, decimal? ValoareOferta,
    int Resurse, string Tip);

/// <summary>O resursă din extrasul devizului: necesarul total și starea în nomenclatorul firmei.</summary>
/// <param name="Nomenclator">Noua (se creează la confirmare, nemapată) / Nemapata / Mapata.</param>
public sealed record ResursaImportDto(
    string Cod, string Denumire, string Tip, string UnitateMasura, decimal CantitateTotala, decimal Valoare, int Articole, bool TipDedus,
    string Nomenclator, string? Mapare);

/// <summary>Reimport: diferențele față de versiunea existentă a proiectului (doar articolele schimbate, noi sau eliminate).</summary>
public sealed record ReimportDto(Guid VersiuneId, string Versiune, int Chei, int Noi, int Eliminate, int Modificate, IReadOnlyList<DiferentaArticol> Diferente);

public sealed record PrevizualizareImportDto(
    string Format,
    string Denumire,
    int Capitole,
    int Articole,
    RecapitulatieDeviz? Recapitulatie,
    IReadOnlyList<ProblemaDto> Probleme,
    bool PoateFiConfirmat,
    string Moneda = "RON",
    decimal? Curs = null,
    string PoliticaRotunjire = "Standard",
    IReadOnlyList<ComparatieTotalDto>? Totaluri = null,
    IReadOnlyList<ArticolImportDto>? ArticoleLista = null,
    IReadOnlyList<ResursaImportDto>? Resurse = null,
    ReimportDto? Reimport = null,
    string? FisierDejaImportat = null);

public sealed record FisierSursaDto(Guid Id, string NumeFisier, long Marime, bool Principal, string Amprenta);

/// <summary>
/// Importul devizelor în proiect (REQ-IMP-03, docs/import-isdp.md §7): fișierul principal (ISDP: „Deviz ofertă cu resurse”)
/// și, opțional, lista F3 și cursul. Previzualizarea nu salvează nimic; confirmarea creează versiunea (ciornă), leagă
/// resursele de nomenclatorul firmei (codurile noi, nemapate), păstrează <see cref="ArticolDeviz.CheieStabila"/> față de
/// versiunea importată anterior din aceeași sursă și arhivează fișierele originale.
/// </summary>
internal static class EndpointuriImportDeviz
{
    private const long DimensiuneMaxima = 50_000_000;

    public static void Mapeaza(RouteGroupBuilder grup)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Creare);

        grup.MapPost("/import/previzualizare", Previzualizeaza).WithName("PrevizualizareImportDeviz")
            .WithSummary("Citește devizul (ISDP: deviz cu resurse + F3 opțional), îl verifică și arată ce s-ar importa; nu salvează nimic")
            .Produces<PrevizualizareImportDto>().Produces(StatusCodes.Status422UnprocessableEntity)
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(2 * DimensiuneMaxima)).CerePermisiune(creare);
        grup.MapPost("/import", Confirma).WithName("ImportDeviz")
            .WithSummary("Confirmă importul: versiune nouă (ciornă), resursele legate de nomenclator, cheile stabile păstrate la reimport, fișierele arhivate")
            .Produces(StatusCodes.Status201Created).Produces(StatusCodes.Status422UnprocessableEntity)
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(2 * DimensiuneMaxima)).CerePermisiune(creare);
        grup.MapGet("/{versiuneId:guid}/fisiere", Fisiere).WithName("FisiereDeviz")
            .WithSummary("Fișierele originale ale versiunii importate").Produces<IReadOnlyList<FisierSursaDto>>().CerePermisiune(vizualizare);
        grup.MapGet("/{versiuneId:guid}/fisiere/{id:guid}", Descarca).WithName("DescarcaFisierDeviz")
            .WithSummary("Descarcă un fișier original").Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
    }

    /// <summary>Ce a citit importul: modelul canonic, versiunea construită în memorie (nesalvată) și versiunea anterioară din aceeași sursă.</summary>
    private sealed record Pregatire(
        IImportatorDeviz Importator, SursaImport Principal, IReadOnlyList<SursaImport> Anexe, DevizCanonic? Canonic, VersiuneDeviz? Versiune,
        PrevizualizareImportDto Previzualizare, VersiuneDeviz? Existenta);

    private static async Task<IResult> Previzualizeaza(
        Guid proiectId, IFormFile fisier, IFormFile? f3, [FromForm] decimal? curs, [FromForm] string? moneda, RegistruImportatori registru,
        DevizeDbContext db, CancellationToken ct)
    {
        var (p, eroare) = await PregatesteAsync(proiectId, fisier, f3, curs, moneda, registru, db, ct);
        return eroare ?? Results.Ok(p!.Previzualizare);
    }

    private static async Task<IResult> Confirma(
        Guid proiectId, IFormFile fisier, IFormFile? f3, [FromForm] decimal? curs, [FromForm] string? moneda, [FromForm] string? denumire,
        TipVersiuneDeviz tip, RegistruImportatori registru, DevizeDbContext db, CancellationToken ct)
    {
        var (p, eroare) = await PregatesteAsync(proiectId, fisier, f3, curs, moneda, registru, db, ct);
        if (eroare is not null)
            return eroare;
        if (!p!.Previzualizare.PoateFiConfirmat || p.Versiune is null || p.Canonic is null)
            return Results.UnprocessableEntity(p.Previzualizare);

        // Versiunea nouă: număr pe tip, cheile stabile ale articolelor potrivite cu versiunea anterioară din aceeași sursă.
        var numar = await db.Versiuni.Where(v => v.ProiectId == proiectId && v.Tip == tip).MaxAsync(v => (int?)v.Numar, ct) + 1 ?? 1;
        // Denumirea dată de utilizator are prioritate față de titlul din fișier.
        var canonic = string.IsNullOrWhiteSpace(denumire) ? p.Canonic : p.Canonic with { Denumire = denumire.Trim().Length > 300 ? denumire.Trim()[..300] : denumire.Trim() };
        var versiune = ConvertorDevizCanonic.CaVersiune(canonic, proiectId, tip, numar, p.Importator.FormatId, null, p.Versiune.Parametri());
        if (p.Existenta is not null)
            PotrivireReimport.Potriveste([.. versiune.Articole()], [.. p.Existenta.Articole()]);

        var fisiere = p.Anexe.Select(a => (Sursa: a, Principal: false)).Prepend((Sursa: p.Principal, Principal: true))
            .Select(x => new FisierSursaDeviz(versiune.Id, x.Sursa.NumeFisier, TipContinut(x.Sursa.Extensie), x.Sursa.Continut, x.Principal)).ToList();
        versiune.SeteazaSursa($"import:{p.Importator.FormatId}", fisiere[0].Id);
        db.Versiuni.Add(versiune);
        db.Fisiere.AddRange(fisiere);
        await EndpointuriResurse.LeagaDeNomenclatorAsync(db, p.Importator.FormatId, versiune, ct);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/proiecte/{proiectId}/devize/{versiune.Id}", new { versiune.Id, numar });
    }

    private static async Task<(Pregatire? Pregatire, IResult? Eroare)> PregatesteAsync(
        Guid proiectId, IFormFile? fisier, IFormFile? f3, decimal? curs, string? moneda, RegistruImportatori registru, DevizeDbContext db, CancellationToken ct)
    {
        if (fisier is null || fisier.Length == 0)
            return (null, Results.BadRequest(new { mesaj = "Alege fișierul devizului." }));
        if (fisier.Length > DimensiuneMaxima || f3?.Length > DimensiuneMaxima)
            return (null, Results.BadRequest(new { mesaj = $"Fișierul depășește {DimensiuneMaxima / 1_000_000} MB." }));
        if (curs is <= 0)
            return (null, Results.BadRequest(new { mesaj = "Cursul trebuie să fie pozitiv." }));

        var principal = await SursaAsync(fisier, ct);
        var anexe = f3 is { Length: > 0 } ? [await SursaAsync(f3, ct)] : new List<SursaImport>();
        var (importator, motive) = registru.Alege(principal);
        if (importator is null)
            return (null, Results.UnprocessableEntity(new { mesaj = "Format de deviz nerecunoscut.", motive }));
        if (anexe.Count > 0 && importator is not IImportatorDevizCuAnexe)
            return (null, Results.BadRequest(new { mesaj = $"Importatorul {importator.Denumire} nu primește un al doilea fișier." }));

        var rezultat = importator is IImportatorDevizCuAnexe cuAnexe
            ? await cuAnexe.CitesteAsync(principal, new OptiuniImportDeviz(anexe, curs, moneda), ct)
            : await importator.CitesteAsync(principal, ct);

        var probleme = rezultat.Probleme.ToList();
        var deviz = rezultat.Valoare;
        VersiuneDeviz? versiune = null;
        VersiuneDeviz? existenta = null;
        List<ComparatieTotalDto>? totaluri = null;
        List<ArticolImportDto>? articole = null;
        List<ResursaImportDto>? resurse = null;
        ReimportDto? reimport = null;
        if (deviz is not null)
        {
            probleme.AddRange(ValidatorDevizCanonic.Valideaza(deviz));
            if (deviz.Parametri is null)
                probleme.Add(new ProblemaImport(Severitate.Eroare, "recapitulație", "Fișierul nu conține cotele recapitulației și firma nu are cote implicite."));
            else
            {
                versiune = ConvertorDevizCanonic.CaVersiune(deviz, proiectId, TipVersiuneDeviz.Oferta, 0, importator.FormatId, null, deviz.Parametri);
                totaluri = Totaluri(deviz, versiune.Recapitulatie());
                articole = Articole(versiune);
                resurse = await ResurseAsync(db, importator.FormatId, versiune, ct);

                // Reimport: versiunea anterioară a proiectului din aceeași sursă (cea mai recentă).
                var sursa = $"import:{importator.FormatId}";
                var idExistenta = await db.Versiuni.AsNoTracking()
                    .Where(v => v.ProiectId == proiectId && v.Sursa == sursa && v.Stare != StareVersiuneDeviz.Inlocuita)
                    .OrderByDescending(v => v.CreatLa).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
                if (idExistenta is { } ide && await ModulDevize.IncarcaAsync(db, ide, ct) is { } ex)
                {
                    existenta = ex;
                    var comparatie = PotrivireReimport.Potriveste([.. versiune.Articole()], [.. ex.Articole()]);
                    reimport = new ReimportDto(ex.Id, $"{ex.Tip} nr. {ex.Numar} — {ex.Denumire}", comparatie.Chei, comparatie.Noi, comparatie.Eliminate,
                        comparatie.Modificate, comparatie.Diferente.Where(d => d.Stare != StareArticolReimport.Neschimbat || d.ResurseSchimbate).ToList());
                }
            }
        }

        var amprenta = FisierSursaDeviz.Amprenteaza(principal.Continut);
        var dejaImportat = await (from f in db.Fisiere.AsNoTracking()
                                  join v in db.Versiuni.AsNoTracking() on f.VersiuneId equals v.Id
                                  where f.Amprenta == amprenta
                                  select v.Tip + " nr. " + v.Numar + " — " + v.Denumire).FirstOrDefaultAsync(ct);
        if (dejaImportat is not null)
            probleme.Insert(0, new ProblemaImport(Severitate.Avertisment, "fișier", $"Același fișier a mai fost importat: {dejaImportat}."));

        var previzualizare = new PrevizualizareImportDto(
            importator.FormatId,
            deviz?.Denumire ?? string.Empty,
            versiune?.Capitole.Count ?? 0,
            versiune?.Articole().Count() ?? 0,
            versiune?.Recapitulatie(),
            probleme.Select(x => new ProblemaDto(x.Severitate.ToString(), x.Locatie, x.Mesaj)).ToList(),
            versiune is not null && probleme.All(x => x.Severitate != Severitate.Eroare),
            versiune?.Moneda ?? "RON",
            versiune?.Curs,
            versiune?.PoliticaRotunjire.ToString() ?? "Standard",
            totaluri,
            articole,
            resurse,
            reimport,
            dejaImportat);
        return (new Pregatire(importator, principal, anexe, deviz, versiune, previzualizare, existenta), null);
    }

    private static List<ComparatieTotalDto> Totaluri(DevizCanonic d, RecapitulatieDeviz r)
    {
        var t = d.Totaluri;
        var directe = t?.CheltuieliDirecteResurse;
        return
        [
            new("Material", directe?.Material, r.CheltuieliDirecteResurse.Material),
            new("Manoperă", directe?.Manopera, r.CheltuieliDirecteResurse.Manopera),
            new("Utilaj", directe?.Utilaj, r.CheltuieliDirecteResurse.Utilaj),
            new("Transport", directe?.Transport, r.CheltuieliDirecteResurse.Transport),
            new("Echipament", directe?.Echipament, r.CheltuieliDirecteResurse.Echipament),
            new("Total valoare directă", directe?.Total, r.CheltuieliDirecteResurse.Total),
            new("Total fără TVA", t?.TotalFaraTva, r.TotalFaraTva),
            new("Total cu TVA", t?.TotalCuTva, r.TotalCuTva),
        ];
    }

    private static List<ArticolImportDto> Articole(VersiuneDeviz v)
    {
        var capitole = v.Capitole.ToDictionary(c => c.Id);
        var p = v.Parametri();
        return v.Articole().Select(a => new ArticolImportDto(
            a.CodWbsSursa, capitole[a.CapitolId].Denumire, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate,
            CalculatorDeviz.ValoareLinie(a.CaLinie(), p.RotunjireLinie).Total, a.ArePretOferta ? a.ValoareOferta.Total : null,
            a.Consumuri.Count, a.TipArticol.ToString())).ToList();
    }

    /// <summary>Extrasul de resurse al versiunii (necesarul total pe cod), cu starea fiecărei resurse în nomenclatorul firmei.</summary>
    private static async Task<List<ResursaImportDto>> ResurseAsync(DevizeDbContext db, string sursa, VersiuneDeviz v, CancellationToken ct)
    {
        var grupuri = v.Articole().SelectMany(a => a.Consumuri.Select(c => (Articol: a, Consum: c)))
            .GroupBy(x => ResursaCatalog.CheieCod(x.Consum.CodResursa)).ToList();
        var coduri = grupuri.Select(g => g.Key).ToList();
        var s = sursa.Trim().ToLowerInvariant();
        var catalog = await db.Resurse.AsNoTracking().Where(r => r.Sursa == s && coduri.Contains(r.Cod)).ToDictionaryAsync(r => r.Cod, ct);
        return grupuri.Select(g =>
        {
            var c = g.First().Consum;
            var r = catalog.GetValueOrDefault(g.Key);
            var mapare = r is null ? null
                : r.FaraMapare ? "fără corespondent"
                : r.ArticolStocDenumire ?? (r.Meserie is null ? r.Utilaj : $"{r.Meserie}{(r.Categorie is null ? "" : $", cat. {r.Categorie}")}");
            return new ResursaImportDto(g.Key, c.Denumire, (r?.Tip ?? c.Tip).ToString(), c.UnitateMasura, g.Sum(x => x.Consum.CantitateTotala),
                g.Sum(x => x.Consum.CantitateTotala * x.Consum.PretUnitar), g.Select(x => x.Articol.Id).Distinct().Count(), r?.TipDedus ?? c.TipDedus,
                r is null ? "Noua" : r.EsteMapata ? "Mapata" : "Nemapata", mapare);
        }).OrderBy(x => x.Tip).ThenByDescending(x => x.Valoare).ToList();
    }

    private static async Task<IResult> Fisiere(Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct) =>
        Results.Ok(await (from f in db.Fisiere.AsNoTracking()
                          join v in db.Versiuni.AsNoTracking() on f.VersiuneId equals v.Id
                          where v.Id == versiuneId && v.ProiectId == proiectId
                          orderby f.Principal descending, f.NumeFisier
                          select new FisierSursaDto(f.Id, f.NumeFisier, f.Marime, f.Principal, f.Amprenta)).ToListAsync(ct));

    private static async Task<IResult> Descarca(Guid proiectId, Guid versiuneId, Guid id, DevizeDbContext db, CancellationToken ct)
    {
        var f = await (from x in db.Fisiere.AsNoTracking()
                       join v in db.Versiuni.AsNoTracking() on x.VersiuneId equals v.Id
                       where x.Id == id && v.Id == versiuneId && v.ProiectId == proiectId
                       select new { x.NumeFisier, x.TipContinut, x.Continut }).FirstOrDefaultAsync(ct);
        return f is null ? Results.NotFound() : Results.File(f.Continut, f.TipContinut, f.NumeFisier);
    }

    private static async Task<SursaImport> SursaAsync(IFormFile f, CancellationToken ct)
    {
        using var ms = new MemoryStream((int)f.Length);
        await f.CopyToAsync(ms, ct);
        return new SursaImport(Path.GetFileName(f.FileName), ms.ToArray());
    }

    private static string TipContinut(string extensie) => extensie switch
    {
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xml" => "application/xml",
        _ => "application/octet-stream",
    };
}
