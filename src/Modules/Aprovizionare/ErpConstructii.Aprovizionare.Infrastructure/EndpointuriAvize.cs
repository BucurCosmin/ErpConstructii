using System.Text.Json;
using ErpConstructii.Aprovizionare.Import;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Parteneri;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Aprovizionare.Infrastructure;

public sealed record LinieAvizDto(string Denumire, string UnitateMasura, decimal Cantitate);

/// <param name="Receptii">Câte NIR-uri active are; <paramref name="Facturat"/> = recepțiile au primit factura (prețurile ei).</param>
public sealed record AvizDto(
    Guid Id, string Numar, string NumarExtern, DateOnly Data, Guid ProiectId, string ProiectCod, string? Lucrare, Guid? FurnizorId, string FurnizorDenumire,
    string? FurnizorCui, IReadOnlyList<LinieAvizDto> Linii, string? Observatii, Guid NotaComandaId, string NotaComandaNumar, bool NotaComandaGenerata,
    string InregistratDe, DateTimeOffset InregistratLa, int Receptii = 0, bool Facturat = false, string? FacturaNumar = null);

public sealed record LinieAvizCititaDto(int Rand, CampTextDto Denumire, CampTextDto UnitateMasura, CampNumarDto Cantitate);

/// <summary>Nota de comandă emisă care pare a fi a avizului (numărul pomenit pe aviz, același furnizor).</summary>
public sealed record NotaComandaPropusaDto(Guid Id, string Numar, string? NumarExtern, DateOnly Data, Guid ProiectId, string ProiectCod, string FurnizorDenumire);

/// <summary>Ce a înțeles sistemul din aviz; nimic nu se salvează până la confirmare.</summary>
public sealed record InterpretareAvizDto(
    string NumeFisier,
    bool DinOcr,
    CampTextDto NumarExtern,
    CampDataDto Data,
    CampTextDto FurnizorDenumire,
    CampTextDto FurnizorCui,
    FurnizorGasitComandaDto? FurnizorGasit,
    CampTextDto Lucrare,
    ProiectPropusDto? ProiectPropus,
    string? ReferintaComanda,
    NotaComandaPropusaDto? NotaComandaPropusa,
    IReadOnlyList<LinieAvizCititaDto> Linii,
    IReadOnlyList<ProblemaImportDto> Probleme,
    string? AvizExistent,
    IReadOnlyList<int> Pagini);

/// <param name="NotaComandaId">Nota de comandă (emisă) căreia îi aparține marfa; lipsă = se generează una din aviz.</param>
public sealed record ConfirmaAvizCerere(
    string NumarExtern,
    DateOnly Data,
    string? Lucrare,
    string FurnizorDenumire,
    string? FurnizorCui,
    IReadOnlyList<LinieAvizDto> Linii,
    string? Observatii,
    Guid? NotaComandaId);

/// <summary>
/// Avizele de însoțire a mărfii (D-40): import din scanare / PDF / Word (interpretare → verificare → aviz AV/AAAA/NNNN cu originalul
/// arhivat), legat de nota de comandă pomenită pe el sau de una generată din el. Recepția (NIR pe aviz) și legarea facturii
/// se fac în Stocuri. Drepturi: aprovizionare.*.
/// </summary>
internal static class EndpointuriAvize
{
    private const string SecventaAvize = ":av";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Mapeaza(RouteGroupBuilder grup)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);

        grup.MapGet("/avize", Lista).WithName("ListaAvize").WithSummary("Avizele de însoțire din proiectele accesibile, cu starea recepției")
            .Produces<IReadOnlyList<AvizDto>>().CerePermisiune(vizualizare);

        grup.MapPost("/avize/import/interpretare", Interpreteaza).WithName("InterpreteazaAviz")
            .WithSummary("Citește un aviz de însoțire (PDF, scanare, Word) și propune datele, proiectul și nota de comandă")
            .Produces<InterpretareAvizDto>().Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(DocumentOriginal.DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);

        grup.MapPost("/proiecte/{proiectId:guid}/avize/import", Confirma).WithName("ImportaAviz")
            .WithSummary("Înregistrează avizul verificat (originalul arhivat) și îl leagă de nota de comandă aleasă sau de una generată din el")
            .Produces<AvizDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(DocumentOriginal.DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);

        grup.MapGet("/proiecte/{proiectId:guid}/avize/{id:guid}/original", Original).WithName("DescarcaOriginalAviz")
            .WithSummary("Descarcă documentul original al avizului").Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);

        grup.MapDelete("/proiecte/{proiectId:guid}/avize/{id:guid}", Sterge).WithName("StergeAviz")
            .WithSummary("Șterge un aviz importat greșit, cât nu are recepții (cu nota de comandă generată din el, dacă nu e folosită)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(creare);
    }

    private static async Task<IResult> Lista(
        Guid? proiectId, string? cauta, AprovizionareDbContext db, IEnumerable<IInterogareReceptiiAviz> receptii, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        var accesibile = (await permisiuni.PentruUtilizatorAsync(uid, ct)).ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);
        var q = db.Avize.AsNoTracking();
        if (accesibile is not null)
        {
            var ids = accesibile.ToList();
            q = q.Where(a => ids.Contains(a.ProiectId));
        }

        if (proiectId is { } pid)
            q = q.Where(a => a.ProiectId == pid);
        if (!string.IsNullOrWhiteSpace(cauta))
        {
            var t = $"%{cauta.Trim()}%";
            q = q.Where(a => EF.Functions.ILike(a.NumarExtern, t) || EF.Functions.ILike(a.FurnizorDenumire, t) || EF.Functions.ILike(a.NotaComandaNumar, t));
        }

        var avize = await q.OrderByDescending(a => a.Data).ThenByDescending(a => a.Numar).Take(2000).ToListAsync(ct);
        var stari = receptii.FirstOrDefault() is { } r ? await r.StariAsync(ct) : new Dictionary<Guid, StareStocAviz>();
        return Results.Ok(avize.Select(a => Dto(a, stari.GetValueOrDefault(a.Id))).ToList());
    }

    private static async Task<IResult> Interpreteaza(
        IFormFile fisier, IEnumerable<IExtractorText> extractori, IInterogareFirma firme, IServiciuParteneri parteneri, IInterogareProiecte proiecte,
        AprovizionareDbContext db, CancellationToken ct)
    {
        var (sursa, extractor, eroare) = await ImportNecesarMateriale.CitesteFisierAsync(fisier, extractori, ct);
        if (eroare is not null)
            return eroare;
        var extras = await extractor!.ExtrageAsync(sursa!, ct);
        if (extras.Valoare is null)
            return Results.BadRequest(new { mesaj = extras.Probleme.FirstOrDefault()?.Mesaj ?? "Documentul nu poate fi citit." });

        var firma = await firme.FirmaActivaAsync(ct);
        var (document, _) = ClasificarePagini.PentruTip(extras.Valoare, TipPagina.Aviz);
        var aviz = InterpretorAviz.Interpreteaza(document, firma?.Cui);
        var probleme = extras.Probleme.Concat(aviz.Probleme).ToList();

        var furnizor = await parteneri.CautaFurnizorAsync(aviz.FurnizorCui.Valoare, aviz.FurnizorDenumire.Valoare, ct);
        if (furnizor is null && aviz.FurnizorDenumire.Valoare is { } denumire)
            probleme.Add(new ProblemaImport(Severitate.Informare, "furnizor", $"Furnizorul „{denumire}” nu există în nomenclator; va fi adăugat la înregistrare."));

        var candidati = (await proiecte.ListaActiveAsync(ct)).Select(p => new ProiectCandidat(p.Id, p.Cod, p.Denumire)).ToList();
        var propunere = PotrivireProiect.Propune(aviz.Lucrare.Valoare, candidati);
        var nota = await NotaPomenitaAsync(db, aviz.ReferintaComanda, furnizor?.Id, aviz.FurnizorDenumire.Valoare, ct);
        if (nota is not null)
            propunere = candidati.FirstOrDefault(c => c.Id == nota.ProiectId) is { } pn ? new PropunereProiect(pn, 1, Certitudine.Sigur) : propunere;
        if (aviz.ReferintaComanda is { } refc && nota is null)
            probleme.Add(new ProblemaImport(Severitate.Informare, "comandă",
                $"Avizul pomenește comanda „{refc}”, dar nu găsesc o notă de comandă emisă cu acest număr la acest furnizor; alege-o sau se generează una din aviz."));

        var existent = await AvizCuAcelasiOriginalAsync(db, DocumentOriginal.Amprenta(sursa!.Continut), ct);
        return Results.Ok(new InterpretareAvizDto(
            sursa.NumeFisier, aviz.DinOcr, Text(aviz.NumarExtern),
            new CampDataDto(aviz.Data.Certitudine == Certitudine.Lipsa ? null : aviz.Data.Valoare, aviz.Data.Certitudine.ToString(), aviz.Data.TextSursa),
            Text(aviz.FurnizorDenumire), Text(aviz.FurnizorCui),
            furnizor is null ? null : new FurnizorGasitComandaDto(furnizor.Id, furnizor.Denumire, furnizor.Cui),
            Text(aviz.Lucrare),
            propunere is null ? null : new ProiectPropusDto(propunere.Proiect.Id, propunere.Proiect.Cod, propunere.Proiect.Denumire, propunere.Scor, propunere.Certitudine.ToString()),
            aviz.ReferintaComanda,
            nota is null ? null : new NotaComandaPropusaDto(nota.Id, nota.NumarAfisat, nota.NumarExtern, nota.Data, nota.ProiectId, nota.ProiectCod, nota.FurnizorDenumire),
            aviz.Linii.Select(l => new LinieAvizCititaDto(l.Rand, Text(l.Denumire), Text(l.UnitateMasura),
                new CampNumarDto(l.Cantitate.Certitudine == Certitudine.Lipsa ? null : l.Cantitate.Valoare, l.Cantitate.Certitudine.ToString(), l.Cantitate.TextSursa))).ToList(),
            probleme.Select(p => new ProblemaImportDto(p.Severitate.ToString(), p.Locatie, p.Mesaj)).ToList(),
            existent,
            aviz.Pagini));
    }

    private static async Task<IResult> Confirma(
        Guid proiectId, IFormFile fisier, [FromForm] string date, IEnumerable<IExtractorText> extractori, AprovizionareDbContext db,
        IInterogareProiecte proiecte, IServiciuParteneri parteneri, IEnumerable<IInterogareReceptiiAviz> receptii, IFirmaCurenta firma,
        IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        ConfirmaAvizCerere? cerere;
        try
        {
            cerere = JsonSerializer.Deserialize<ConfirmaAvizCerere>(date, Json);
        }
        catch (JsonException)
        {
            cerere = null;
        }

        if (cerere is null)
            return Results.BadRequest(new { mesaj = "Datele avizului nu sunt valide." });
        if (firma.FirmaId is not { } firmaId || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        if (cerere.Data.Year < 2000 || cerere.Data > ceas.Azi.AddDays(1))
            return Results.BadRequest(new { mesaj = "Data avizului nu este validă (nu poate fi în viitor)." });

        var (sursa, _, eroare) = await ImportNecesarMateriale.CitesteFisierAsync(fisier, extractori, ct);
        if (eroare is not null)
            return eroare;
        var hash = DocumentOriginal.Amprenta(sursa!.Continut);
        if (await AvizCuAcelasiOriginalAsync(db, hash, ct) is { } existent)
            return Results.Conflict(new { mesaj = $"Acest document a fost deja importat ca {existent}." });
        if (await db.DocumenteOriginale.AsNoTracking().AnyAsync(d => d.HashSha256 == hash, ct))
            return Results.Conflict(new { mesaj = "Acest fișier a fost deja importat (ca necesar de materiale sau notă de comandă)." });

        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        if (proiect.Arhivat)
            return Results.Conflict(new { mesaj = proiect.MesajArhivat });

        var cui = string.IsNullOrWhiteSpace(cerere.FurnizorCui) ? null : cerere.FurnizorCui.Trim();
        if (cui is not null && !ValidareFiscala.EsteCuiValid(cui))
            return Results.BadRequest(new { mesaj = $"CUI-ul furnizorului „{cui}” nu este valid." });
        if (string.IsNullOrWhiteSpace(cerere.FurnizorDenumire))
            return Results.BadRequest(new { mesaj = "Furnizorul este obligatoriu." });

        var dateAviz = new DateAviz(cerere.NumarExtern ?? string.Empty, cerere.Data, proiect.Id, proiect.Cod, cerere.Lucrare, null, cerere.FurnizorDenumire, cui,
            (cerere.Linii ?? []).Select(l => new LinieAviz(l.Denumire ?? string.Empty, l.UnitateMasura ?? string.Empty, l.Cantitate)).ToList(), cerere.Observatii);
        if (AvizInsotire.Valideaza(dateAviz) is { Count: > 0 } probleme)
            return Results.BadRequest(new { mesaj = string.Join(" ", probleme) });

        var furnizor = await parteneri.CautaFurnizorAsync(cui, cerere.FurnizorDenumire, ct);
        if (furnizor is null)
        {
            var adaugat = await parteneri.AsiguraFurnizorAsync(cerere.FurnizorDenumire.Trim(), cui, platitorTva: cui?.StartsWith("RO", StringComparison.OrdinalIgnoreCase) == true, ct);
            if (adaugat.Esec)
                return Results.BadRequest(new { mesaj = adaugat.Eroare.Mesaj });
            furnizor = adaugat.Valoare;
        }

        dateAviz = dateAviz with { FurnizorId = furnizor.Id, FurnizorDenumire = furnizor.Denumire, FurnizorCui = furnizor.Cui ?? cui };
        var numarExtern = dateAviz.NumarExtern.Trim();
        if (await db.Avize.AsNoTracking().FirstOrDefaultAsync(a => a.FurnizorId == furnizor.Id && a.NumarExtern == numarExtern, ct) is { } dublura)
            return Results.Conflict(new { mesaj = $"Avizul {numarExtern} de la {furnizor.Denumire} e deja înregistrat ({dublura.NumarAfisat})." });

        var azi = ceas.Azi;
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        NotaComanda nota;
        var generata = false;
        if (cerere.NotaComandaId is { } notaId)
        {
            var existenta = await db.NoteComanda.FirstOrDefaultAsync(n => n.Id == notaId, ct);
            if (existenta is null)
                return Results.NotFound(new { mesaj = "Nota de comandă aleasă nu există." });
            if (existenta.Stare != StareNotaComanda.Emisa)
                return Results.Conflict(new { mesaj = $"Nota de comandă {existenta.NumarAfisat} nu a fost încă trimisă la furnizor (e „{existenta.Stare}”)." });
            if (existenta.ProiectId != proiect.Id)
                return Results.Conflict(new { mesaj = $"Nota de comandă {existenta.NumarAfisat} e pe proiectul {existenta.ProiectCod}; avizul se înregistrează pe același proiect." });
            nota = existenta;
        }
        else
        {
            // Marfa a venit fără comandă în sistem: nota de comandă se generează din aviz, deja emisă (furnizorul a livrat).
            await ModulAprovizionare.BlocheazaSecventaAsync(db, tranzactie, firmaId, azi.Year, ct, FluxNotaComanda.SecventaComenzi);
            var numarNota = (await db.NoteComanda.Where(c => c.An == azi.Year).MaxAsync(c => (int?)c.Numar, ct) ?? 0) + 1;
            var rn = NotaComanda.Emite(azi.Year, numarNota,
                new DateNotaComanda(null, dateAviz.Data, proiect.Id, proiect.Cod, dateAviz.Lucrare, furnizor.Id, furnizor.Denumire, dateAviz.FurnizorCui,
                    AvizInsotire.LiniiNota(dateAviz), $"Generată din avizul {numarExtern} (marfă livrată fără comandă în sistem)"),
                null, null, uid, utilizator.Nume);
            if (rn.Esec)
                return Results.BadRequest(new { mesaj = rn.Eroare.Mesaj });
            nota = rn.Valoare;
            generata = true;
            db.NoteComanda.Add(nota);
        }

        await ModulAprovizionare.BlocheazaSecventaAsync(db, tranzactie, firmaId, azi.Year, ct, SecventaAvize);
        var numar = (await db.Avize.Where(a => a.An == azi.Year).MaxAsync(a => (int?)a.Numar, ct) ?? 0) + 1;
        var original = new DocumentOriginal(sursa.NumeFisier, ImportNecesarMateriale.TipContinut(sursa.Extensie), sursa.Continut);
        var r = AvizInsotire.Importa(azi.Year, numar, dateAviz, nota.Id, nota.NumarAfisat, generata, original.Id, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        db.DocumenteOriginale.Add(original);
        db.Avize.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created($"/api/aprovizionare/avize/{r.Valoare.Id}", Dto(r.Valoare, null));
    }

    private static async Task<IResult> Original(Guid proiectId, Guid id, AprovizionareDbContext db, CancellationToken ct)
    {
        var documentId = await db.Avize.AsNoTracking().Where(a => a.Id == id && a.ProiectId == proiectId).Select(a => (Guid?)a.DocumentOriginalId).FirstOrDefaultAsync(ct);
        var original = documentId is null ? null : await db.DocumenteOriginale.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        return original is null ? Results.NotFound() : Results.File(original.Continut, original.TipContinut, original.NumeFisier, enableRangeProcessing: true);
    }

    private static async Task<IResult> Sterge(
        Guid proiectId, Guid id, AprovizionareDbContext db, IEnumerable<IInterogareReceptiiAviz> receptii, IEnumerable<IInterogareFacturiNotaComanda> facturi,
        CancellationToken ct)
    {
        var aviz = await db.Avize.FirstOrDefaultAsync(a => a.Id == id && a.ProiectId == proiectId, ct);
        if (aviz is null)
            return Results.NotFound();
        if (receptii.FirstOrDefault() is { } r && (await r.StariAsync(ct)).TryGetValue(id, out var stare) && stare.Receptii > 0)
            return Results.Conflict(new { mesaj = $"Avizul {aviz.NumarExtern} are recepții (NIR): stornează-le întâi." });

        db.Avize.Remove(aviz);
        if (await db.DocumenteOriginale.FirstOrDefaultAsync(d => d.Id == aviz.DocumentOriginalId, ct) is { } original)
            db.DocumenteOriginale.Remove(original);
        // Nota generată din aviz pleacă odată cu el, dacă nu o mai folosește nimic (alt aviz, o factură).
        if (aviz.NotaComandaGenerata && !await db.Avize.AnyAsync(a => a.NotaComandaId == aviz.NotaComandaId && a.Id != aviz.Id, ct)
            && (facturi.FirstOrDefault() is not { } f || await f.FacturiAsync(aviz.NotaComandaId, ct) == 0)
            && await db.NoteComanda.FirstOrDefaultAsync(n => n.Id == aviz.NotaComandaId, ct) is { } nota)
        {
            db.NoteComanda.Remove(nota);
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Nota de comandă emisă cu numărul pomenit pe aviz (de sistem sau de pe hârtie), de la același furnizor.</summary>
    private static async Task<NotaComanda?> NotaPomenitaAsync(AprovizionareDbContext db, string? referinta, Guid? furnizorId, string? furnizor, CancellationToken ct)
    {
        var cheie = ClasificarePagini.CheieNumar(referinta);
        if (cheie is null || !int.TryParse(cheie, out var numar))
            return null;
        var note = await db.NoteComanda.AsNoTracking()
            .Where(n => n.Stare == StareNotaComanda.Emisa && (n.Numar == numar || (n.NumarExtern != null && n.NumarExtern.Contains(cheie))))
            .OrderByDescending(n => n.Data).Take(50).ToListAsync(ct);
        var cheieFurnizor = furnizor is null ? null : DenumireFirma.Cheie(furnizor);
        return note.FirstOrDefault(n =>
            (ClasificarePagini.CheieNumar(n.NumarExtern) == cheie || n.Numar == numar) &&
            ((furnizorId is not null && n.FurnizorId == furnizorId) || (cheieFurnizor is not null && DenumireFirma.Cheie(n.FurnizorDenumire) == cheieFurnizor)));
    }

    private static async Task<string?> AvizCuAcelasiOriginalAsync(AprovizionareDbContext db, string hash, CancellationToken ct)
    {
        var documentId = await db.DocumenteOriginale.AsNoTracking().Where(d => d.HashSha256 == hash).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        if (documentId is null)
            return null;
        var a = await db.Avize.AsNoTracking().Where(x => x.DocumentOriginalId == documentId).Select(x => new { x.An, x.Numar, x.NumarExtern }).FirstOrDefaultAsync(ct);
        return a is null ? null : $"avizul {a.NumarExtern} (AV/{a.An}/{a.Numar:D4})";
    }

    internal static AvizDto Dto(AvizInsotire a, StareStocAviz? stare) => new(
        a.Id, a.NumarAfisat, a.NumarExtern, a.Data, a.ProiectId, a.ProiectCod, a.Lucrare, a.FurnizorId, a.FurnizorDenumire, a.FurnizorCui,
        a.Linii.Select(l => new LinieAvizDto(l.Denumire, l.UnitateMasura, l.Cantitate)).ToList(), a.Observatii,
        a.NotaComandaId, a.NotaComandaNumar, a.NotaComandaGenerata, a.InregistratDeNume, a.CreatLa,
        stare?.Receptii ?? 0, stare?.Facturat ?? false, stare?.FacturaNumar);

    private static CampTextDto Text(CampCitit<string> c) => new(c.Valoare, c.Certitudine.ToString(), c.TextSursa);
}
