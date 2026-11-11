using System.Security.Cryptography;
using System.Text.Json;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using ErpConstructii.Stocuri.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

public sealed record ProblemaBalantaDto(string Severitate, int? Rand, string Mesaj);

/// <param name="ArticolId">Materialul existent din nomenclator; null = se creează unul nou (MAT-NNNNN).</param>
/// <param name="Sursa">„CodExtern” / „Denumire” (cum s-a găsit materialul) sau null (nou).</param>
/// <param name="UnitateMasura">Goală în „Raport rapid stoc”: materialul existent o are; pentru unul nou se alege la import.</param>
public sealed record LinieBalantaDto(int Rand, string Denumire, string? CodExtern, string UnitateMasura, decimal StocInitial, decimal Intrari, decimal Iesiri,
    decimal StocFinal, string? Cont, Guid? ArticolId, string? ArticolCod, string? ArticolDenumire, string? Sursa,
    decimal? PretUnitar = null, decimal? Valoare = null, string? ArticolUnitateMasura = null);

/// <param name="CodGestiunePropus">Codul gestiunii din ERP („01-G5” → „G5”).</param>
/// <param name="GestiuneId">Gestiunea existentă cu codul propus (în orice depozit); null = se creează în depozitul ales.</param>
/// <param name="DejaImportat">Numărul soldului inițial creat deja din acest fișier pentru această gestiune.</param>
/// <param name="AreMiscari">Gestiunea propusă are deja mișcări de stoc (soldul inițial se adaugă la ce există).</param>
/// <param name="AlteSolduri">Solduri inițiale din alte fișiere, pe aceeași gestiune și categorie (ex. balanța fără valori): de anulat înainte.</param>
public sealed record GestiuneBalantaDto(string Cod, string CodGestiunePropus, Guid? GestiuneId, string? GestiuneDenumire, string? DepozitDenumire,
    string? GestionarNume, bool AreMiscari, string? DejaImportat, int LiniiCuStoc, int MaterialeNoi, IReadOnlyList<LinieBalantaDto> Linii,
    decimal Valoare = 0, IReadOnlyList<string>? AlteSolduri = null);

/// <param name="Data">Data soldului: sfârșitul perioadei din fișier (stocul final).</param>
/// <param name="DepozitIdPropus">Depozitul în care se creează gestiunile care lipsesc (implicit cel central, D-43).</param>
/// <param name="CuValori">„Raport rapid stoc”: stocul intră cu valorile din fișier; fără U.M. (materialele noi o primesc la import).</param>
public sealed record InterpretareBalantaDto(string NumeFisier, string Sha256, string? Firma, string? Cif, bool AltaFirma, string? Categorie,
    DateOnly? PerioadaDe, DateOnly? PerioadaPanaLa, DateOnly? Data, IReadOnlyList<GestiuneBalantaDto> Gestiuni, IReadOnlyList<ProblemaBalantaDto> Probleme,
    Guid? DepozitIdPropus = null, bool CuValori = false);

/// <param name="GestiuneId">Gestiunea aleasă; null = se creează gestiunea cu codul propus în depozitul din cerere.</param>
/// <param name="Importa">False = gestiunea se sare.</param>
public sealed record AsociereGestiuneCerere(string Cod, Guid? GestiuneId, bool Importa = true);

public sealed record SoldInitialDto(Guid Id, string Numar, DateOnly Data, Guid GestiuneId, string Gestiune, string GestiuneExterna, string? Categorie, string NumeFisier,
    int Linii, decimal Cantitate, string ImportatDe, DateTimeOffset CreatLa, IReadOnlyList<LinieSoldInitial>? DetaliiLinii, decimal Valoare = 0);

public sealed record SoldInitialAnulatDto(Guid Id, string Numar, DateOnly Data, string Gestiune, string? Categorie, string NumeFisier,
    int Linii, decimal Valoare, string AnulatDe, DateTimeOffset AnulatLa);

/// <summary>
/// Preluarea stocului din programul de gestiune anterior (D-42): balanța de stoc exportată în Excel (Nexus ERP, câte un
/// fișier pe categorie, cu mai multe gestiuni ale aceluiași depozit, D-43). Previzualizarea arată gestiunile propuse și materialele găsite
/// în nomenclator; confirmarea recitește fișierul (nu se bazează pe liniile din ecran) și creează, pentru fiecare gestiune,
/// un document SI cu stocul final (la valoare 0 din balanță, cu valorile din „Raport rapid stoc”). Soldul preluat greșit se
/// anulează cât timp materialele lui nu au alte mișcări. Dreptul: aprobarea aprovizionării (schimbă stocul fără document de intrare).
/// </summary>
internal static class EndpointuriSoldInitial
{
    private const long MarimeMaxima = 20 * 1024 * 1024;

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var aprobare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Aprobare);

        g.MapGet("/solduri-initiale", Lista).WithName("ListaSolduriInitiale").WithSummary("Stocurile preluate din programul de gestiune anterior")
            .Produces<IReadOnlyList<SoldInitialDto>>().CerePermisiune(vizualizare);
        g.MapPost("/solduri-initiale/interpretare", Interpreteaza).WithName("InterpreteazaBalantaStoc")
            .WithSummary("Citește o balanță de stoc (.xlsx) și propune depozitele și materialele, fără să salveze nimic")
            .Accepts<IFormFile>("multipart/form-data").Produces<InterpretareBalantaDto>().Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery().CerePermisiune(aprobare);
        g.MapPost("/solduri-initiale", Importa).WithName("ImportaSoldInitial")
            .WithSummary("Preia stocul final din balanță în depozite (un document SI pe gestiune), la valoare 0")
            .Accepts<IFormFile>("multipart/form-data").Produces<IReadOnlyList<SoldInitialDto>>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict)
            .DisableAntiforgery().CerePermisiune(aprobare);
        g.MapGet("/solduri-initiale/anulate", ListaAnulate).WithName("ListaSolduriInitialeAnulate").WithSummary("Soldurile inițiale anulate")
            .Produces<IReadOnlyList<SoldInitialAnulatDto>>().CerePermisiune(vizualizare);
        g.MapDelete("/solduri-initiale/{id:guid}", Anuleaza).WithName("AnuleazaSoldInitial")
            .WithSummary("Anulează un sold inițial preluat greșit (stocul lui iese din gestiune), cât timp materialele lui nu au alte mișcări")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(aprobare);
    }

    private static async Task<IResult> ListaAnulate(StocuriDbContext db, CancellationToken ct)
    {
        var lista = await db.SolduriInitialeAnulate.AsNoTracking().OrderByDescending(s => s.AnulatLa).Take(200).ToListAsync(ct);
        return Results.Ok(lista.Select(s => new SoldInitialAnulatDto(s.Id, s.NumarAfisat, s.Data, s.GestiuneDenumire, s.Categorie, s.NumeFisier,
            JsonSerializer.Deserialize<List<LinieSoldInitial>>(s.LiniiJson)?.Count ?? 0, s.Valoare, s.AnulatDeNume, s.AnulatLa)).ToList());
    }

    private static async Task<IResult> Anuleaza(Guid id, StocuriDbContext db, IFirmaCurenta firma, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var si = await db.SolduriInitiale.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (si is null)
            return Results.NotFound(new { mesaj = "Soldul inițial nu există." });
        if (await ModulStocuri.InventarDeschisAsync(db, ct, si.GestiuneId) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });

        var articole = si.Linii.Select(l => l.ArticolId).ToList();
        var cuAlteMiscari = (await db.Miscari.AsNoTracking()
            .Where(m => m.GestiuneId == si.GestiuneId && articole.Contains(m.ArticolId) && m.Tip != TipMiscare.SoldInitial)
            .Select(m => m.ArticolId).Distinct().ToListAsync(ct)).ToHashSet();
        var solduri = await db.Solduri.Where(s => s.GestiuneId == si.GestiuneId && articole.Contains(s.ArticolId)).ToListAsync(ct);

        var r = AnulareSoldInitial.Calculeaza(si, solduri.ToDictionary(s => s.ArticolId, s => s.Sold), cuAlteMiscari);
        if (r.Esec)
            return Results.Conflict(new { mesaj = r.Eroare.Mesaj });
        foreach (var (linie, ramas) in r.Valoare)
            solduri.First(s => s.ArticolId == linie.ArticolId).Seteaza(ramas);

        db.Miscari.RemoveRange(await db.Miscari.Where(m => m.DocumentId == si.Id).ToListAsync(ct));
        db.SolduriInitiale.Remove(si);
        db.SolduriInitialeAnulate.Add(SoldInitialAnulat.Din(si, uid, utilizator.Nume, ceas.Acum));
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Lista(StocuriDbContext db, CancellationToken ct)
    {
        var lista = await db.SolduriInitiale.AsNoTracking().OrderByDescending(s => s.An).ThenByDescending(s => s.Numar).Take(500).ToListAsync(ct);
        return Results.Ok(lista.Select(s => Dto(s, true)).ToList());
    }

    private static async Task<IResult> Interpreteaza(IFormFile fisier, StocuriDbContext db, IInterogareFirma firme, CancellationToken ct)
    {
        var (balanta, sha, eroare) = await CitesteAsync(fisier, ct);
        if (eroare is not null)
            return Results.BadRequest(new { mesaj = eroare });

        await ModulStocuri.AsiguraDepozitCentralAsync(db, ct, faraGestiuneImplicita: true);
        var articole = await ArticoleAsync(db, ct);
        var existente = await db.Gestiuni.AsNoTracking().Where(x => x.Tip == TipGestiune.Depozit).ToListAsync(ct);
        var depozite = await db.Depozite.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var depozitPropus = depozite.Values.Where(x => x.Activ).OrderBy(x => x.Cod != Depozit.CodCentral).ThenBy(x => x.Cod).FirstOrDefault();
        var importate = await db.SolduriInitiale.AsNoTracking().Where(s => s.FisierSha256 == sha)
            .Select(s => new { s.GestiuneExterna, s.An, s.Numar }).ToListAsync(ct);
        var cuMiscari = (await db.Miscari.AsNoTracking().Select(m => m.GestiuneId).Distinct().ToListAsync(ct)).ToHashSet();
        var alteFisiere = await db.SolduriInitiale.AsNoTracking().Where(s => s.FisierSha256 != sha)
            .Select(s => new { s.GestiuneId, s.Categorie, s.An, s.Numar, s.NumeFisier }).ToListAsync(ct);

        var gestiuni = new List<GestiuneBalantaDto>();
        foreach (var gb in balanta!.Gestiuni)
        {
            var cod = PotrivireSoldInitial.CodDepozit(gb.Cod);
            var gestiune = existente.FirstOrDefault(x => x.Cod == cod);
            var linii = gb.Linii.Select(l =>
            {
                var p = PotrivireSoldInitial.Propune(l.Denumire, l.CodExtern, l.UnitateMasura, articole);
                return new LinieBalantaDto(l.Rand, l.Denumire, l.CodExtern, l.UnitateMasura, l.StocInitial, l.Intrari, l.Iesiri, l.StocFinal, l.Cont,
                    p?.Articol.Id, p?.Articol.Cod, p?.Articol.Denumire, p?.Sursa.ToString(), l.PretUnitar, l.Valoare, p?.Articol.UnitateMasura);
            }).ToList();
            var imp = importate.FirstOrDefault(i => i.GestiuneExterna == gb.Cod);
            var alte = gestiune is null ? [] : alteFisiere
                .Where(s => s.GestiuneId == gestiune.Id && string.Equals(s.Categorie, balanta.Categorie, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.An).ThenBy(s => s.Numar).Select(s => $"SI/{s.An}/{s.Numar:D4}").ToList();
            gestiuni.Add(new GestiuneBalantaDto(gb.Cod, cod, gestiune?.Id, gestiune?.Denumire,
                gestiune?.DepozitId is { } did && depozite.TryGetValue(did, out var dep) ? dep.Denumire : null, gestiune?.GestionarNume,
                gestiune is not null && cuMiscari.Contains(gestiune.Id),
                imp is null ? null : $"SI/{imp.An}/{imp.Numar:D4}",
                linii.Count(l => l.StocFinal > 0), linii.Where(l => l.StocFinal > 0 && l.ArticolId is null).Select(l => Cheie(l)).Distinct().Count(), linii,
                linii.Where(l => l.StocFinal > 0).Sum(l => l.Valoare ?? 0), alte));
        }

        var firma = await firme.FirmaActivaAsync(ct);
        var altaFirma = balanta.Cif is not null && firma?.Cui is { Length: > 0 } cui && Cif(cui) != Cif(balanta.Cif);
        return Results.Ok(new InterpretareBalantaDto(fisier.FileName, sha!, balanta.Firma, balanta.Cif, altaFirma, balanta.Categorie,
            balanta.PerioadaDe, balanta.PerioadaPanaLa, balanta.PerioadaPanaLa, gestiuni,
            balanta.Probleme.Select(p => new ProblemaBalantaDto(p.Eroare ? "Eroare" : "Avertisment", p.Rand, p.Mesaj)).ToList(), depozitPropus?.Id,
            balanta.CuValori));
    }

    private static async Task<IResult> Importa(
        IFormFile fisier, HttpRequest cerere, StocuriDbContext db, IFirmaCurenta firma, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();

        var formular = await cerere.ReadFormAsync(ct);
        List<AsociereGestiuneCerere> asocieri;
        Dictionary<int, string> unitati;
        try
        {
            asocieri = JsonSerializer.Deserialize<List<AsociereGestiuneCerere>>(formular["asocieri"].ToString(), JsonSerializerOptions.Web) ?? [];
            // „Raport rapid stoc” nu are U.M.: materialele noi o primesc din ecran (rândul din fișier → U.M.).
            var u = formular["unitati"].ToString();
            unitati = string.IsNullOrWhiteSpace(u) ? [] : JsonSerializer.Deserialize<Dictionary<int, string>>(u, JsonSerializerOptions.Web) ?? [];
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { mesaj = "Asocierea gestiunilor nu este validă." });
        }

        var (balanta, sha, eroare) = await CitesteAsync(fisier, ct);
        if (eroare is not null)
            return Results.BadRequest(new { mesaj = eroare });
        if (balanta!.AreErori)
            return Results.BadRequest(new { mesaj = "Fișierul are erori: " + string.Join(" ", balanta.Probleme.Where(p => p.Eroare).Take(5).Select(p => p.Mesaj)) });

        var data = DateOnly.TryParse(formular["data"].ToString(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : balanta.PerioadaPanaLa;
        if (data is null)
            return Results.BadRequest(new { mesaj = "Data soldului este obligatorie (fișierul nu are perioada)." });
        if (data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data soldului nu poate fi în viitor." });

        var deImportat = balanta.Gestiuni
            .Select(gb => (Balanta: gb, Asociere: asocieri.FirstOrDefault(a => a.Cod == gb.Cod) ?? new AsociereGestiuneCerere(gb.Cod, null)))
            .Where(x => x.Asociere.Importa && x.Balanta.Linii.Any(l => l.StocFinal > 0))
            .ToList();
        if (deImportat.Count == 0)
            return Results.BadRequest(new { mesaj = "Nu e aleasă nicio gestiune cu stoc." });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);

        var dejaImportate = await db.SolduriInitiale.Where(s => s.FisierSha256 == sha).Select(s => s.GestiuneExterna).ToListAsync(ct);
        if (deImportat.Select(x => x.Balanta.Cod).FirstOrDefault(dejaImportate.Contains) is { } dublura)
            return Results.Conflict(new { mesaj = $"Gestiunea {dublura} din acest fișier a fost deja preluată." });

        // Gestiunile: cele alese, cele existente cu codul din fișier sau create acum în depozitul ales (D-43).
        await ModulStocuri.AsiguraDepozitCentralAsync(db, ct, faraGestiuneImplicita: true);
        Depozit? depozitNou = null;
        if (Guid.TryParse(formular["depozitId"].ToString(), out var depozitId))
        {
            depozitNou = await db.Depozite.FirstOrDefaultAsync(x => x.Id == depozitId, ct);
            if (depozitNou is null)
                return Results.BadRequest(new { mesaj = "Depozitul ales nu există." });
        }

        var depozite = new Dictionary<string, Gestiune>();
        foreach (var (gb, asociere) in deImportat)
        {
            Gestiune? depozit;
            if (asociere.GestiuneId is { } gid)
            {
                depozit = await db.Gestiuni.FirstOrDefaultAsync(x => x.Id == gid, ct);
                if (depozit is null || depozit.Tip != TipGestiune.Depozit)
                    return Results.BadRequest(new { mesaj = $"Gestiunea {gb.Cod}: gestiunea aleasă nu există." });
            }
            else
            {
                var cod = PotrivireSoldInitial.CodDepozit(gb.Cod);
                depozit = db.Gestiuni.Local.FirstOrDefault(x => x.Cod == cod) ?? await db.Gestiuni.FirstOrDefaultAsync(x => x.Cod == cod, ct);
                if (depozit is null)
                {
                    depozitNou ??= await db.Depozite.Where(x => x.Activ).OrderBy(x => x.Cod != Depozit.CodCentral).ThenBy(x => x.Cod).FirstAsync(ct);
                    var nou = Gestiune.CreeazaInDepozit(depozitNou, cod, $"Gestiunea {cod}", null, null);
                    if (nou.Esec)
                        return Results.BadRequest(new { mesaj = $"Gestiunea {gb.Cod}: {nou.Eroare.Mesaj}" });
                    depozit = nou.Valoare;
                    db.Gestiuni.Add(depozit);
                }
                else if (depozit.Tip != TipGestiune.Depozit)
                {
                    return Results.Conflict(new { mesaj = $"Codul {cod} aparține unei gestiuni de șantier; alege gestiunea pentru {gb.Cod}." });
                }
            }

            if (await ModulStocuri.InventarDeschisAsync(db, ct, depozit.Id) is { } blocat)
                return Results.Conflict(new { mesaj = blocat });
            depozite[gb.Cod] = depozit;
        }

        // Materialele: existente (cod extern / denumire + UM) sau create; același material nou apare o singură dată.
        var articole = await ArticoleAsync(db, ct);
        var urmatorulCod = await ModulStocuri.UrmatorulNumarArticolAsync(db, ct);
        var noi = new Dictionary<string, ArticolStoc>();
        var documente = new List<SoldInitialStoc>();
        var an = data.Value.Year;
        // Numerele soldurilor anulate nu se refolosesc.
        var numar = Math.Max(
            await db.SolduriInitiale.Where(s => s.An == an).MaxAsync(s => (int?)s.Numar, ct) ?? 0,
            await db.SolduriInitialeAnulate.Where(s => s.An == an).MaxAsync(s => (int?)s.Numar, ct) ?? 0) + 1;

        foreach (var (gb, _) in deImportat)
        {
            var depozit = depozite[gb.Cod];
            var linii = new List<LinieSoldInitialCerere>();
            foreach (var l in gb.Linii.Where(l => l.StocFinal > 0))
            {
                ArticolStoc articol;
                if (PotrivireSoldInitial.Propune(l.Denumire, l.CodExtern, l.UnitateMasura, articole) is { } p)
                {
                    articol = db.Articole.Local.FirstOrDefault(a => a.Id == p.Articol.Id) ?? noi.Values.FirstOrDefault(a => a.Id == p.Articol.Id)
                        ?? await db.Articole.FirstAsync(a => a.Id == p.Articol.Id, ct);
                }
                else
                {
                    var um = string.IsNullOrWhiteSpace(l.UnitateMasura) ? unitati.GetValueOrDefault(l.Rand)?.Trim() : l.UnitateMasura;
                    if (string.IsNullOrWhiteSpace(um))
                        return Results.BadRequest(new { mesaj = $"Rândul {l.Rand}, „{l.Denumire}”: alege unitatea de măsură a materialului nou." });
                    if (!noi.TryGetValue(CheieNou(l.Denumire, l.CodExtern, um), out articol!))
                    {
                        var creat = ArticolStoc.Creeaza(ArticolStoc.CodGenerat(urmatorulCod++), l.Denumire, um);
                        if (creat.Esec)
                            return Results.BadRequest(new { mesaj = $"Rândul {l.Rand}, „{l.Denumire}”: {creat.Eroare.Mesaj}" });
                        articol = creat.Valoare;
                        db.Articole.Add(articol);
                        noi[CheieNou(l.Denumire, l.CodExtern, um)] = articol;
                        articole.Add(new ArticolExistent(articol.Id, articol.Cod, articol.Denumire, articol.UnitateMasura, l.CodExtern));
                    }
                }

                articol.CompleteazaDinImport(l.CodExtern, balanta.Categorie);
                linii.Add(new LinieSoldInitialCerere(articol, l.StocFinal, l.Valoare ?? 0, l.CodExtern));
            }

            var r = SoldInitialStoc.Emite(an, numar++, data.Value, depozit, gb.Cod, balanta.Categorie, fisier.FileName, sha!, linii, uid, utilizator.Nume);
            if (r.Esec)
                return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
            var si = r.Valoare;

            foreach (var l in si.Linii)
            {
                var sold = await ModulStocuri.SoldAsync(db, depozit.Id, l.ArticolId, ct);
                var nou = CalculStoc.Intrare(sold.Sold, l.Cantitate, l.Valoare);
                if (nou.Esec)
                    return Results.BadRequest(new { mesaj = $"„{l.Denumire}”: {nou.Eroare.Mesaj}" });
                sold.Seteaza(nou.Valoare);
                db.Miscari.Add(new MiscareStoc(depozit.Id, l.ArticolId, si.Data, TipMiscare.SoldInitial, l.Cantitate, l.Valoare, si.Id, si.NumarAfisat));
            }

            db.SolduriInitiale.Add(si);
            documente.Add(si);
        }

        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created("/api/stocuri/solduri-initiale", documente.Select(s => Dto(s, false)).ToList());
    }

    private static async Task<(BalantaStoc? Balanta, string? Sha, string? Eroare)> CitesteAsync(IFormFile? fisier, CancellationToken ct)
    {
        if (fisier is null || fisier.Length == 0)
            return (null, null, "Alege fișierul cu balanța de stoc (.xlsx).");
        if (fisier.Length > MarimeMaxima)
            return (null, null, "Fișierul depășește 20 MB.");
        if (!fisier.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return (null, null, "Balanța se importă din fișierul Excel (.xlsx) exportat din programul de gestiune.");

        using var memorie = new MemoryStream();
        await fisier.CopyToAsync(memorie, ct);
        var sha = Convert.ToHexStringLower(SHA256.HashData(memorie.ToArray()));
        memorie.Position = 0;
        var grila = CititorXlsx.Citeste(memorie);
        if (grila is null)
            return (null, sha, "Fișierul nu poate fi citit ca .xlsx.");
        return (InterpretorBalantaStoc.Interpreteaza(grila), sha, null);
    }

    private static async Task<List<ArticolExistent>> ArticoleAsync(StocuriDbContext db, CancellationToken ct) =>
        await db.Articole.AsNoTracking().Where(a => a.Activ)
            .Select(a => new ArticolExistent(a.Id, a.Cod, a.Denumire, a.UnitateMasura, a.CodExtern)).ToListAsync(ct);

    private static string CheieNou(string denumire, string? codExtern, string um) =>
        string.IsNullOrWhiteSpace(codExtern) ? $"{PotrivireArticol.Cheie(denumire)}|{um.Trim().ToLowerInvariant()}" : $"#{codExtern.Trim().ToUpperInvariant()}";

    private static string Cheie(LinieBalantaDto l) => CheieNou(l.Denumire, l.CodExtern, l.UnitateMasura);

    private static string Cif(string cif) => new(cif.Where(char.IsDigit).ToArray());

    private static SoldInitialDto Dto(SoldInitialStoc s, bool cuLinii) => new(s.Id, s.NumarAfisat, s.Data, s.GestiuneId, s.GestiuneDenumire, s.GestiuneExterna,
        s.Categorie, s.NumeFisier, s.Linii.Count, s.Linii.Sum(l => l.Cantitate), s.ImportatDeNume, s.CreatLa, cuLinii ? s.Linii : null, s.Valoare);
}
