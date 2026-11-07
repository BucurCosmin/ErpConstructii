using System.Text.Json;
using ErpConstructii.Aprovizionare.Import;
using ErpConstructii.Formulare;
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

public sealed record LinieNotaComandaDto(string Denumire, string UnitateMasura, decimal Cantitate, string? Termen);

/// <summary>Condițiile bifate pe formular: Receptie = LaFurnizor | LaPrimire; ClasaCalitate = Clasa4 | BestComercial; Transport = Auto | Cfr; Plata = Op | FilaCec | Alte.</summary>
public sealed record ConditiiComandaDto(string? Receptie, string? ClasaCalitate, string? Transport, string? Plata);

public sealed record NotaComandaDto(
    Guid Id,
    string Numar,
    string? NumarExtern,
    DateOnly Data,
    Guid ProiectId,
    string ProiectCod,
    string? Lucrare,
    Guid? FurnizorId,
    string FurnizorDenumire,
    string? FurnizorCui,
    IReadOnlyList<LinieNotaComandaDto> Linii,
    string? Observatii,
    string Sursa,
    bool AreOriginal,
    string InregistrataDe,
    DateTimeOffset InregistrataLa,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    string? FurnizorEmail,
    ConditiiComandaDto Conditii,
    Guid? NecesarId,
    bool AreSablon);

public sealed record LinieNotaComandaCititaDto(int Rand, CampTextDto Denumire, CampTextDto UnitateMasura, CampNumarDto Cantitate, string? Termen);

public sealed record FurnizorGasitComandaDto(Guid Id, string Denumire, string? Cui);

/// <summary>Ce a înțeles sistemul dintr-o notă de comandă pe hârtie; nu se salvează nimic până la confirmare.</summary>
public sealed record InterpretareNotaComandaDto(
    string NumeFisier,
    bool DinOcr,
    CampTextDto NumarExtern,
    CampDataDto Data,
    CampTextDto FurnizorDenumire,
    CampTextDto FurnizorCui,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    FurnizorGasitComandaDto? FurnizorGasit,
    CampTextDto Lucrare,
    ProiectPropusDto? ProiectPropus,
    IReadOnlyList<LinieNotaComandaCititaDto> Linii,
    IReadOnlyList<ProblemaImportDto> Probleme,
    string? ComandaExistenta);

/// <summary>Datele verificate de utilizator, trimise împreună cu fișierul original.</summary>
public sealed record ConfirmaNotaComandaCerere(
    string? NumarExtern,
    DateOnly Data,
    string? Lucrare,
    string FurnizorDenumire,
    string? FurnizorCui,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    IReadOnlyList<LinieNotaComandaDto> Linii,
    string? Observatii);

/// <summary>Datele unei note de comandă emise în sistem; numărul se generează la emitere.</summary>
/// <param name="NecesarId">Necesarul aprobat din care s-au preluat materialele (opțional).</param>
public sealed record EmiteNotaComandaCerere(
    DateOnly? Data,
    string? Lucrare,
    string FurnizorDenumire,
    string? FurnizorCui,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    string? FurnizorEmail,
    ConditiiComandaDto? Conditii,
    IReadOnlyList<LinieNotaComandaDto> Linii,
    string? Observatii,
    Guid? NecesarId);

/// <param name="Cauta">Numărul de sistem (NC/…) sau cel de pe hârtie.</param>
/// <param name="Sursa">Sistem (emise în aplicație) sau Import (de pe hârtie).</param>
public sealed record FiltreNoteComanda(
    Guid? ProiectId, string? Furnizor, string? Material, DateOnly? DataDe, DateOnly? DataPanaLa,
    string? Cauta = null, string? Lucrare = null, string? Sursa = null);

/// <summary>
/// Notele de comandă către furnizori (formularul GC 007). Cele emise pe hârtie se importă (PDF / scanare / Word):
/// interpretare → verificare → notă de comandă cu număr de sistem NC/AAAA/NNNN, numărul de pe hârtie păstrat, originalul arhivat.
/// Furnizorul se caută în nomenclator după CUI sau denumire și se adaugă dacă lipsește.
/// </summary>
internal static class EndpointuriNoteComanda
{
    private const string SecventaComenzi = ":nc";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Mapeaza(RouteGroupBuilder grup)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);

        grup.MapGet("/note-comanda", Lista)
            .WithName("ListaNoteComanda")
            .WithSummary("Listează notele de comandă către furnizori din proiectele accesibile utilizatorului")
            .Produces<IReadOnlyList<NotaComandaDto>>()
            .Produces(StatusCodes.Status400BadRequest)
            .CerePermisiune(vizualizare);

        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda", Emite)
            .WithName("EmiteNotaComanda")
            .WithSummary("Emite o notă de comandă către furnizor (număr NC/AAAA/NNNN; Word din șablonul firmei)")
            .Produces<NotaComandaDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(creare);

        grup.MapGet("/proiecte/{proiectId:guid}/note-comanda/{id:guid}", Detalii)
            .WithName("DetaliiNotaComanda")
            .WithSummary("Obține o notă de comandă")
            .Produces<NotaComandaDto>()
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);

        grup.MapPost("/note-comanda/import/interpretare", Interpreteaza)
            .WithName("InterpreteazaNotaComanda")
            .WithSummary("Citește o notă de comandă emisă pe hârtie (PDF, scanare, Word) și propune datele")
            .Produces<InterpretareNotaComandaDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentOriginal.DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);

        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda/import", Confirma)
            .WithName("ImportaNotaComanda")
            .WithSummary("Înregistrează nota de comandă verificată și arhivează originalul")
            .Produces<NotaComandaDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentOriginal.DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);

        grup.MapGet("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/original", DescarcaOriginal)
            .WithName("DescarcaOriginalNotaComanda")
            .WithSummary("Descarcă documentul original al unei note de comandă importate")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);
    }

    private static async Task<IResult> Lista(
        [AsParameters] FiltreNoteComanda filtre,
        AprovizionareDbContext db,
        IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator,
        IFirmaCurenta firma,
        CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } utilizatorId || firma.FirmaId is not { } firmaId)
        {
            return Results.Unauthorized();
        }

        if (filtre.Furnizor?.Length > 300 || filtre.Material?.Length > 300 || filtre.Cauta?.Length > 50 || filtre.Lucrare?.Length > 300)
        {
            return Results.BadRequest(new { mesaj = "Unul dintre filtre depășește lungimea maximă permisă." });
        }

        var accesibile = (await permisiuni.PentruUtilizatorAsync(utilizatorId, ct))
            .ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), firmaId);
        var q = db.NoteComanda.AsNoTracking();
        if (accesibile is not null)
        {
            var id = accesibile.ToList();
            q = q.Where(c => id.Contains(c.ProiectId));
        }

        if (filtre.ProiectId is { } proiectId)
            q = q.Where(c => c.ProiectId == proiectId);
        if (!string.IsNullOrWhiteSpace(filtre.Furnizor))
            q = q.Where(c => EF.Functions.ILike(c.FurnizorDenumire, $"%{filtre.Furnizor.Trim()}%"));
        if (filtre.DataDe is { } de)
            q = q.Where(c => c.Data >= de);
        if (filtre.DataPanaLa is { } panaLa)
            q = q.Where(c => c.Data <= panaLa);
        if (!string.IsNullOrWhiteSpace(filtre.Lucrare))
            q = q.Where(c => c.Lucrare != null && EF.Functions.ILike(c.Lucrare, $"%{filtre.Lucrare.Trim()}%"));
        if (Enum.TryParse<SursaDocument>(filtre.Sursa, true, out var sursa))
            q = q.Where(c => c.Sursa == sursa);
        if (!string.IsNullOrWhiteSpace(filtre.Cauta))
        {
            // „NC/2026/0005”, „2026/5”, „5” sau numărul de pe hârtie
            var text = filtre.Cauta.Trim();
            var cifre = System.Text.RegularExpressions.Regex.Matches(text, @"\d+").Select(m => int.TryParse(m.Value, out var v) ? v : -1).ToList();
            var numar = cifre.Count > 0 && cifre[^1] < 1_000_000 ? cifre[^1] : -1;
            var an = cifre.Count > 1 && cifre[^2] is >= 2000 and < 3000 ? cifre[^2] : (int?)null;
            q = q.Where(c => (c.NumarExtern != null && EF.Functions.ILike(c.NumarExtern, $"%{text}%")) || (c.Numar == numar && (an == null || c.An == an)));
        }

        var comenzi = await q.OrderByDescending(c => c.Data).ThenByDescending(c => c.An).ThenByDescending(c => c.Numar).Take(2000).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(filtre.Material))
        {
            // fără diacritice și majuscule: „manusi” găsește „MĂNUȘI”
            var m = RaportNoteComanda.CheieMaterial(filtre.Material);
            comenzi = comenzi.Where(c => c.Linii.Any(l => RaportNoteComanda.CheieMaterial(l.Denumire).Contains(m, StringComparison.Ordinal))).ToList();
        }

        return Results.Ok(comenzi.Select(CaDto).ToList());
    }

    private static async Task<IResult> Emite(
        Guid proiectId,
        EmiteNotaComandaCerere cerere,
        AprovizionareDbContext db,
        IInterogareProiecte proiecte,
        IServiciuParteneri parteneri,
        IInterogareSabloaneFormulare sabloane,
        IFirmaCurenta firma,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        if (firma.FirmaId is not { } firmaId || utilizator.UtilizatorId is not { } utilizatorId)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(utilizator.Nume) || utilizator.Nume.Length > 200)
            return Results.BadRequest(new { mesaj = "Numele utilizatorului nu este disponibil în sesiune." });

        var azi = ceas.Azi;
        var data = cerere.Data ?? azi;
        if (data.Year < 2000 || data > azi.AddDays(30))
            return Results.BadRequest(new { mesaj = "Data notei de comandă nu este validă (cel mult 30 de zile în viitor)." });
        if (string.IsNullOrWhiteSpace(cerere.FurnizorDenumire))
            return Results.BadRequest(new { mesaj = "Alege sau scrie furnizorul." });
        if (cerere.Linii is null || cerere.Linii.Count == 0)
            return Results.BadRequest(new { mesaj = "Adaugă cel puțin un material." });
        if (!Conditii(cerere.Conditii, out var conditii))
            return Results.BadRequest(new { mesaj = "Condițiile de livrare nu sunt valide." });

        var cui = string.IsNullOrWhiteSpace(cerere.FurnizorCui) ? null : cerere.FurnizorCui.Trim();
        if (cui is not null && !ValidareFiscala.EsteCuiValid(cui))
            return Results.BadRequest(new { mesaj = $"CUI-ul furnizorului „{cui}” nu este valid." });

        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });

        if (cerere.NecesarId is { } necesarId
            && !await db.NecesareMateriale.AnyAsync(n => n.Id == necesarId && n.ProiectId == proiectId && n.Stare == StareNecesarMateriale.Aprobata, ct))
            return Results.BadRequest(new { mesaj = "Necesarul ales nu e aprobat sau nu e pe acest proiect." });

        // Furnizorul din nomenclator (după CUI sau denumire); dacă lipsește, se adaugă cu datele de pe comandă.
        var furnizor = await parteneri.CautaFurnizorAsync(cui, cerere.FurnizorDenumire, ct);
        if (furnizor is null)
        {
            var adaugat = await parteneri.AsiguraFurnizorAsync(cerere.FurnizorDenumire.Trim(), cui,
                platitorTva: cui?.StartsWith("RO", StringComparison.OrdinalIgnoreCase) == true, cerere.FurnizorAdresa, cerere.FurnizorTelefon, ct);
            if (adaugat.Esec)
                return Results.BadRequest(new { mesaj = adaugat.Eroare.Mesaj });
            furnizor = adaugat.Valoare;
        }

        var sablon = await sabloane.SablonActivAsync(CatalogFormulare.NotaComanda.Cod, ct);

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulAprovizionare.BlocheazaSecventaAsync(db, tranzactie, firmaId, data.Year, ct, SecventaComenzi);
        var numar = (await db.NoteComanda.Where(c => c.An == data.Year).MaxAsync(c => (int?)c.Numar, ct) ?? 0) + 1;

        var rezultat = NotaComanda.Emite(
            data.Year, numar,
            new DateNotaComanda(
                null, data, proiect.Id, proiect.Cod, string.IsNullOrWhiteSpace(cerere.Lucrare) ? proiect.Denumire : cerere.Lucrare,
                furnizor.Id, furnizor.Denumire, furnizor.Cui ?? cui,
                cerere.Linii.Select(l => new LinieNotaComanda(l.Denumire ?? string.Empty, l.UnitateMasura ?? string.Empty, l.Cantitate, l.Termen, null)).ToList(),
                cerere.Observatii, cerere.FurnizorAdresa, cerere.FurnizorTelefon, cerere.FurnizorEmail, conditii, cerere.NecesarId),
            sablon?.Id, sablon?.Versiune, utilizatorId, utilizator.Nume);
        if (rezultat.Esec)
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });

        db.NoteComanda.Add(rezultat.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);

        var dto = CaDto(rezultat.Valoare);
        return Results.Created($"/api/aprovizionare/proiecte/{proiectId}/note-comanda/{dto.Id}", dto);
    }

    private static bool Conditii(ConditiiComandaDto? dto, out ConditiiComanda conditii)
    {
        conditii = ConditiiComanda.Niciuna;
        if (dto is null)
            return true;
        static bool Citeste<T>(string? text, out T? valoare) where T : struct, Enum
        {
            valoare = null;
            if (string.IsNullOrWhiteSpace(text))
                return true;
            if (!Enum.TryParse<T>(text, true, out var v) || !Enum.IsDefined(v))
                return false;
            valoare = v;
            return true;
        }

        if (!Citeste<ReceptieComanda>(dto.Receptie, out var receptie) || !Citeste<ClasaCalitateComanda>(dto.ClasaCalitate, out var clasa)
            || !Citeste<TransportComanda>(dto.Transport, out var transport) || !Citeste<PlataComanda>(dto.Plata, out var plata))
            return false;
        conditii = new ConditiiComanda(receptie, clasa, transport, plata);
        return true;
    }

    private static async Task<IResult> Detalii(Guid proiectId, Guid id, AprovizionareDbContext db, CancellationToken ct)
    {
        var c = await db.NoteComanda.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        return c is null ? Results.NotFound() : Results.Ok(CaDto(c));
    }

    private static async Task<IResult> Interpreteaza(
        IFormFile fisier,
        IEnumerable<IExtractorText> extractori,
        IInterogareFirma firme,
        IServiciuParteneri parteneri,
        IInterogareProiecte proiecte,
        AprovizionareDbContext db,
        CancellationToken ct)
    {
        var (sursa, extractor, eroare) = await ImportNecesarMateriale.CitesteFisierAsync(fisier, extractori, ct);
        if (eroare is not null)
        {
            return eroare;
        }

        var extras = await extractor!.ExtrageAsync(sursa!, ct);
        if (extras.Valoare is null)
        {
            return Results.BadRequest(new { mesaj = extras.Probleme.FirstOrDefault()?.Mesaj ?? "Documentul nu poate fi citit." });
        }

        var firma = await firme.FirmaActivaAsync(ct);
        // Nota de comandă stă des scanată împreună cu factura, avizul și certificatele: citesc doar paginile ei.
        var (document, pagini) = ClasificarePagini.PentruTip(extras.Valoare, TipPagina.NotaComanda);
        var citita = InterpretorNotaComanda.Interpreteaza(document, firma?.Cui);
        var probleme = extras.Probleme.Concat(citita.Probleme).Concat(ImportNecesarMateriale.ProblemaPagini(pagini, document, "nota de comandă")).ToList();

        var furnizor = await parteneri.CautaFurnizorAsync(citita.FurnizorCui.Valoare, citita.FurnizorDenumire.Valoare, ct);
        if (furnizor is null && citita.FurnizorDenumire.Valoare is { } denumire)
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "furnizor",
                $"Furnizorul „{denumire}” nu există în nomenclator; va fi adăugat la înregistrare (completează-i ulterior CUI-ul)."));
        }

        var candidati = (await proiecte.ListaActiveAsync(ct)).Select(p => new ProiectCandidat(p.Id, p.Cod, p.Denumire)).ToList();
        var propunere = PotrivireProiect.Propune(citita.Lucrare.Valoare, candidati);
        if (propunere is null)
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "proiect",
                "Nu am găsit proiectul după textul lucrării; alege-l din listă (pentru stoc, proiectul de stoc al firmei)."));
        }

        var existenta = await ComandaCuAcelasiOriginalAsync(db, DocumentOriginal.Amprenta(sursa!.Continut), ct);

        return Results.Ok(new InterpretareNotaComandaDto(
            sursa.NumeFisier,
            citita.DinOcr,
            Text(citita.NumarExtern),
            new CampDataDto(citita.Data.Certitudine == Certitudine.Lipsa ? null : citita.Data.Valoare, citita.Data.Certitudine.ToString(), citita.Data.TextSursa),
            Text(citita.FurnizorDenumire),
            Text(citita.FurnizorCui),
            citita.FurnizorAdresa,
            citita.FurnizorTelefon,
            furnizor is null ? null : new FurnizorGasitComandaDto(furnizor.Id, furnizor.Denumire, furnizor.Cui),
            Text(citita.Lucrare),
            propunere is null ? null : new ProiectPropusDto(propunere.Proiect.Id, propunere.Proiect.Cod, propunere.Proiect.Denumire, propunere.Scor, propunere.Certitudine.ToString()),
            citita.Linii.Select(l => new LinieNotaComandaCititaDto(
                l.Rand,
                Text(l.Denumire),
                Text(l.UnitateMasura),
                new CampNumarDto(l.Cantitate.Certitudine == Certitudine.Lipsa ? null : l.Cantitate.Valoare, l.Cantitate.Certitudine.ToString(), l.Cantitate.TextSursa),
                l.Termen)).ToList(),
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
        IServiciuParteneri parteneri,
        IFirmaCurenta firma,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        ConfirmaNotaComandaCerere? cerere;
        try
        {
            cerere = JsonSerializer.Deserialize<ConfirmaNotaComandaCerere>(date, Json);
        }
        catch (JsonException)
        {
            cerere = null;
        }

        if (cerere is null)
        {
            return Results.BadRequest(new { mesaj = "Datele notei de comandă nu sunt valide." });
        }

        if (firma.FirmaId is not { } firmaId || utilizator.UtilizatorId is not { } utilizatorId)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(utilizator.Nume) || utilizator.Nume.Length > 200)
        {
            return Results.BadRequest(new { mesaj = "Numele utilizatorului nu este disponibil în sesiune." });
        }

        if (cerere.Data.Year < 2000 || cerere.Data > ceas.Azi.AddDays(1))
        {
            return Results.BadRequest(new { mesaj = "Data notei de comandă nu este validă (nu poate fi în viitor)." });
        }

        if (string.IsNullOrWhiteSpace(cerere.FurnizorDenumire))
        {
            return Results.BadRequest(new { mesaj = "Furnizorul este obligatoriu." });
        }

        if (cerere.Linii is null || cerere.Linii.Count == 0)
        {
            return Results.BadRequest(new { mesaj = "Adaugă cel puțin un material." });
        }

        var (sursa, _, eroare) = await ImportNecesarMateriale.CitesteFisierAsync(fisier, extractori, ct);
        if (eroare is not null)
        {
            return eroare;
        }

        var hash = DocumentOriginal.Amprenta(sursa!.Continut);
        if (await ComandaCuAcelasiOriginalAsync(db, hash, ct) is { } existenta)
        {
            return Results.Conflict(new { mesaj = $"Acest document a fost deja importat ca {existenta}." });
        }

        // Același fișier importat înainte ca necesar de materiale: indexul unic pe amprentă nu permite o a doua copie.
        if (await db.DocumenteOriginale.AsNoTracking().AnyAsync(d => d.HashSha256 == hash, ct))
        {
            return Results.Conflict(new { mesaj = "Acest fișier a fost deja importat ca necesar de materiale." });
        }

        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
        {
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        }

        var cui = string.IsNullOrWhiteSpace(cerere.FurnizorCui) ? null : cerere.FurnizorCui.Trim();
        if (cui is not null && !ValidareFiscala.EsteCuiValid(cui))
        {
            return Results.BadRequest(new { mesaj = $"CUI-ul furnizorului „{cui}” nu este valid." });
        }

        var furnizor = await parteneri.CautaFurnizorAsync(cui, cerere.FurnizorDenumire, ct);
        if (furnizor is null)
        {
            var adaugat = await parteneri.AsiguraFurnizorAsync(cerere.FurnizorDenumire.Trim(), cui, platitorTva: cui?.StartsWith("RO", StringComparison.OrdinalIgnoreCase) == true,
                cerere.FurnizorAdresa, cerere.FurnizorTelefon, ct);
            if (adaugat.Esec)
            {
                return Results.BadRequest(new { mesaj = adaugat.Eroare.Mesaj });
            }

            furnizor = adaugat.Valoare;
        }

        var azi = ceas.Azi;
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulAprovizionare.BlocheazaSecventaAsync(db, tranzactie, firmaId, azi.Year, ct, SecventaComenzi);
        var numar = (await db.NoteComanda.Where(c => c.An == azi.Year).MaxAsync(c => (int?)c.Numar, ct) ?? 0) + 1;

        var original = new DocumentOriginal(sursa.NumeFisier, ImportNecesarMateriale.TipContinut(sursa.Extensie), sursa.Continut);
        var rezultat = NotaComanda.Importa(
            azi.Year, numar,
            new DateNotaComanda(
                cerere.NumarExtern, cerere.Data, proiect.Id, proiect.Cod, cerere.Lucrare,
                furnizor.Id, furnizor.Denumire, furnizor.Cui ?? cui,
                cerere.Linii.Select(l => new LinieNotaComanda(l.Denumire ?? string.Empty, l.UnitateMasura ?? string.Empty, l.Cantitate, l.Termen, null)).ToList(),
                cerere.Observatii, cerere.FurnizorAdresa, cerere.FurnizorTelefon),
            original.Id, utilizatorId, utilizator.Nume);
        if (rezultat.Esec)
        {
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });
        }

        db.DocumenteOriginale.Add(original);
        db.NoteComanda.Add(rezultat.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);

        var dto = CaDto(rezultat.Valoare);
        return Results.Created($"/api/aprovizionare/proiecte/{proiectId}/note-comanda/{dto.Id}", dto);
    }

    private static async Task<IResult> DescarcaOriginal(Guid proiectId, Guid id, AprovizionareDbContext db, CancellationToken ct)
    {
        var documentId = await db.NoteComanda.AsNoTracking()
            .Where(c => c.Id == id && c.ProiectId == proiectId)
            .Select(c => c.DocumentOriginalId)
            .FirstOrDefaultAsync(ct);
        var original = documentId is null
            ? null
            : await db.DocumenteOriginale.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        return original is null
            ? Results.NotFound()
            : Results.File(original.Continut, original.TipContinut, original.NumeFisier, enableRangeProcessing: true);
    }

    private static async Task<string?> ComandaCuAcelasiOriginalAsync(AprovizionareDbContext db, string hash, CancellationToken ct)
    {
        var documentId = await db.DocumenteOriginale.AsNoTracking()
            .Where(d => d.HashSha256 == hash)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (documentId is null)
        {
            return null;
        }

        var c = await db.NoteComanda.AsNoTracking()
            .Where(x => x.DocumentOriginalId == documentId)
            .Select(x => new { x.An, x.Numar })
            .FirstOrDefaultAsync(ct);
        return c is null ? null : $"nota de comandă NC/{c.An}/{c.Numar:D4}";
    }

    private static NotaComandaDto CaDto(NotaComanda c) => new(
        c.Id, c.NumarAfisat, c.NumarExtern, c.Data, c.ProiectId, c.ProiectCod, c.Lucrare,
        c.FurnizorId, c.FurnizorDenumire, c.FurnizorCui,
        c.Linii.Select(l => new LinieNotaComandaDto(l.Denumire, l.UnitateMasura, l.Cantitate, l.Termen)).ToList(),
        c.Observatii, c.Sursa.ToString(), c.DocumentOriginalId is not null, c.InregistrataDeNume, c.CreatLa,
        c.FurnizorAdresa, c.FurnizorTelefon, c.FurnizorEmail,
        new ConditiiComandaDto(c.Conditii.Receptie?.ToString(), c.Conditii.ClasaCalitate?.ToString(), c.Conditii.Transport?.ToString(), c.Conditii.Plata?.ToString()),
        c.NecesarId, c.SablonId is not null);

    internal static NotaComandaDto LaDto(NotaComanda c) => CaDto(c);

    private static CampTextDto Text(CampCitit<string> c) => new(c.Valoare, c.Certitudine.ToString(), c.TextSursa);
}
