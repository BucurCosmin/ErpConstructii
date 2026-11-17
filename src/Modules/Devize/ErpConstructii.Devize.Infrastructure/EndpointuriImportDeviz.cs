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
    string? FisierDejaImportat = null,
    IReadOnlyList<ComponentaPret>? Componente = null,
    IReadOnlyList<SectiuneImport>? Sectiuni = null,
    IReadOnlyList<ParteIgnorata>? Ignorate = null,
    bool PeObiecte = false,
    TintaImportDto? Tinta = null);

/// <summary>
/// Importul pe obiecte (D-58): devizul în care intră obiectul. <paramref name="VersiuneLucruId"/> = ciorna de lucru care se
/// modifică (null = se creează la confirmare, din versiunea aleasă); <paramref name="Inlocuieste"/> = obiectul cu același cod
/// există deja și se înlocuiește (articolele potrivite își păstrează cheile).
/// </summary>
public sealed record TintaImportDto(Guid VersiuneId, string Versiune, Guid? VersiuneLucruId, IReadOnlyList<string> Obiecte, string Obiect, bool Inlocuieste);

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

    /// <param name="componente">Alegerea componentelor de preț neclare, JSON {"C":"Utilaj"} (D-58).</param>
    /// <param name="versiuneTintaId">Importul pe obiecte: devizul în care se adaugă / înlocuiește obiectul.</param>
    private static async Task<IResult> Previzualizeaza(
        Guid proiectId, IFormFile fisier, IFormFile? f3, [FromForm] decimal? curs, [FromForm] string? moneda, [FromForm] string? componente,
        [FromForm] Guid? versiuneTintaId, RegistruImportatori registru, DevizeDbContext db, CancellationToken ct)
    {
        var (p, eroare) = await PregatesteAsync(proiectId, fisier, f3, curs, moneda, componente, versiuneTintaId, registru, db, ct);
        return eroare ?? Results.Ok(p!.Previzualizare);
    }

    private static async Task<IResult> Confirma(
        Guid proiectId, IFormFile fisier, IFormFile? f3, [FromForm] decimal? curs, [FromForm] string? moneda, [FromForm] string? denumire,
        [FromForm] string? componente, [FromForm] Guid? versiuneTintaId, TipVersiuneDeviz tip, RegistruImportatori registru, DevizeDbContext db,
        CancellationToken ct)
    {
        var (p, eroare) = await PregatesteAsync(proiectId, fisier, f3, curs, moneda, componente, versiuneTintaId, registru, db, ct);
        if (eroare is not null)
            return eroare;
        if (!p!.Previzualizare.PoateFiConfirmat || p.Versiune is null || p.Canonic is null)
            return Results.UnprocessableEntity(p.Previzualizare);
        if (p.Canonic.PeObiecte && versiuneTintaId is { } tinta)
            return await AdaugaObiectAsync(proiectId, tinta, p, db, ct);

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

    /// <summary>
    /// Obiectul importat intră în devizul ales (D-58), în ciorna lui de lucru (creată acum dacă nu există, ca la editare: versiunea
    /// aleasă rămâne neschimbată). Obiectul cu același cod se înlocuiește: articolele potrivite (cod, simbol) își păstrează cheile,
    /// iar noul obiect ia locul celui vechi. Fișierul se arhivează pe ciornă.
    /// </summary>
    private static async Task<IResult> AdaugaObiectAsync(Guid proiectId, Guid tintaId, Pregatire p, DevizeDbContext db, CancellationToken ct)
    {
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        var lucru = await EndpointuriEditareDeviz.CiornaDeLucruAsync(db, proiectId, tintaId, ct);
        if (lucru.Esec)
            return EndpointuriEditareDeviz.Refuz(lucru.Eroare);
        var ciorna = lucru.Valoare.Ciorna;
        if (!string.Equals(ciorna.Moneda, p.Versiune!.Moneda, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { mesaj = $"Devizul ales e în {ciorna.Moneda}, fișierul în {p.Versiune.Moneda}." });

        var obiectNou = p.Versiune.Radacini.First();
        var vechi = ciorna.Radacini.FirstOrDefault(c => c.Tip == TipCapitol.Obiect && string.Equals(c.Cod, obiectNou.Cod, StringComparison.OrdinalIgnoreCase));
        var pozitie = vechi?.Ordine ?? int.MaxValue;
        if (vechi is not null)
        {
            var subVechi = ciorna.Subarbore(vechi);
            PotrivireReimport.Potriveste([.. p.Versiune.Subarbore(obiectNou).SelectMany(c => c.Articole)], [.. subVechi.SelectMany(c => c.Articole)]);
            // Întâi din model (structura se citește după părinți), apoi din bază, de la frunze spre rădăcină: ștergerea unui
            // capitol urmărit golește imediat părintele copiilor lui, care ar părea altfel capitole de primul nivel.
            ciorna.ScoateCapitol(vechi);
            foreach (var c in subVechi.Reverse())
            {
                db.Articole.RemoveRange(c.Articole);
                db.Capitole.Remove(c);
            }
        }

        var create = ciorna.CopiazaCapitolDin(p.Versiune, obiectNou);
        // Id-urile sunt generate în aplicație: EF trebuie anunțat că sunt noi (altfel le-ar căuta în bază).
        db.Capitole.AddRange(create);
        ciorna.MutaCapitol(create[0], pozitie);
        db.Fisiere.Add(new FisierSursaDeviz(ciorna.Id, p.Principal.NumeFisier, TipContinut(p.Principal.Extensie), p.Principal.Continut, false));
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created($"/api/proiecte/{proiectId}/devize/{ciorna.Id}",
            new { ciorna.Id, numar = ciorna.Numar, obiect = obiectNou.Cod, inlocuit = vechi is not null, ciornaNoua = lucru.Valoare.Noua });
    }

    /// <summary>Versiunea în care ar intra obiectul: ciorna de lucru a celei alese (dacă există), altfel cea aleasă. Fără urmărire.</summary>
    private static async Task<(VersiuneDeviz? Aleasa, VersiuneDeviz? Lucru)> TintaAsync(DevizeDbContext db, Guid proiectId, Guid tintaId, CancellationToken ct)
    {
        var aleasa = await ModulDevize.IncarcaAsync(db, tintaId, ct);
        if (aleasa is null || aleasa.ProiectId != proiectId)
            return (null, null);
        if (aleasa.EsteCiornaDeLucru)
            return (aleasa, aleasa);
        var idLucru = await db.Versiuni.AsNoTracking()
            .Where(x => x.ProiectId == proiectId && x.VersiuneAnterioaraId == aleasa.Id && x.Stare == StareVersiuneDeviz.Ciorna)
            .OrderByDescending(x => x.Numar).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return (aleasa, idLucru is { } id ? await ModulDevize.IncarcaAsync(db, id, ct) : null);
    }

    private static IReadOnlyDictionary<string, Calcul.TipResursa>? Componente(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            return d.Where(x => Enum.TryParse<Calcul.TipResursa>(x.Value, true, out _))
                .ToDictionary(x => x.Key, x => Enum.Parse<Calcul.TipResursa>(x.Value, true));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task<(Pregatire? Pregatire, IResult? Eroare)> PregatesteAsync(
        Guid proiectId, IFormFile? fisier, IFormFile? f3, decimal? curs, string? moneda, string? componente, Guid? versiuneTintaId,
        RegistruImportatori registru, DevizeDbContext db, CancellationToken ct)
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
            ? await cuAnexe.CitesteAsync(principal, new OptiuniImportDeviz(anexe, curs, moneda, Componente(componente)), ct)
            : await importator.CitesteAsync(principal, ct);

        var probleme = rezultat.Probleme.ToList();
        var deviz = rezultat.Valoare;
        VersiuneDeviz? versiune = null;
        VersiuneDeviz? existenta = null;
        List<ComparatieTotalDto>? totaluri = null;
        List<ArticolImportDto>? articole = null;
        List<ResursaImportDto>? resurse = null;
        ReimportDto? reimport = null;
        TintaImportDto? tinta = null;
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

                // Importul pe obiecte (D-58): comparația e cu obiectul cu același cod din devizul ales (dacă există).
                if (deviz.PeObiecte)
                {
                    if (versiuneTintaId is { } tid)
                    {
                        var (aleasa, lucru) = await TintaAsync(db, proiectId, tid, ct);
                        if (aleasa is null)
                            return (null, Results.NotFound(new { mesaj = "Devizul ales nu există în proiect." }));
                        if (aleasa.Stare == StareVersiuneDeviz.Inlocuita)
                            probleme.Add(new ProblemaImport(Severitate.Eroare, "deviz", "Devizul ales a fost înlocuit de unul mai nou: alege versiunea în vigoare."));
                        var tintaV = lucru ?? aleasa;
                        if (!string.Equals(tintaV.Moneda, versiune.Moneda, StringComparison.OrdinalIgnoreCase))
                            probleme.Add(new ProblemaImport(Severitate.Eroare, "deviz", $"Devizul ales e în {tintaV.Moneda}, fișierul în {versiune.Moneda}."));
                        var obiectNou = versiune.Radacini.First();
                        var vechi = tintaV.Radacini.FirstOrDefault(c => c.Tip == TipCapitol.Obiect && string.Equals(c.Cod, obiectNou.Cod, StringComparison.OrdinalIgnoreCase));
                        tinta = new TintaImportDto(aleasa.Id, $"{aleasa.Tip} nr. {aleasa.Numar} — {aleasa.Denumire}", lucru?.Id,
                            tintaV.Radacini.Select(c => $"{c.Cod} — {c.Denumire}").ToList(), $"{obiectNou.Cod} — {obiectNou.Denumire}", vechi is not null);
                        if (vechi is not null)
                        {
                            existenta = tintaV;
                            var comparatie = PotrivireReimport.Potriveste([.. versiune.Subarbore(obiectNou).SelectMany(c => c.Articole)],
                                [.. tintaV.Subarbore(vechi).SelectMany(c => c.Articole)]);
                            reimport = new ReimportDto(tintaV.Id, $"obiectul {vechi.Cod} din {tintaV.Tip} nr. {tintaV.Numar}", comparatie.Chei, comparatie.Noi,
                                comparatie.Eliminate, comparatie.Modificate,
                                comparatie.Diferente.Where(d => d.Stare != StareArticolReimport.Neschimbat || d.ResurseSchimbate).ToList());
                        }
                    }
                }
                // Reimport: versiunea anterioară a proiectului din aceeași sursă (cea mai recentă).
                else if (await db.Versiuni.AsNoTracking()
                    .Where(v => v.ProiectId == proiectId && v.Sursa == $"import:{importator.FormatId}" && v.Stare != StareVersiuneDeviz.Inlocuita)
                    .OrderByDescending(v => v.CreatLa).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct) is { } ide
                    && await ModulDevize.IncarcaAsync(db, ide, ct) is { } ex)
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
            dejaImportat,
            deviz?.Componente,
            deviz?.Sectiuni,
            deviz?.Ignorate,
            deviz?.PeObiecte ?? false,
            tinta);
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
