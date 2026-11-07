using System.Globalization;
using System.Text.RegularExpressions;
using ErpConstructii.Aprovizionare;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Aprovizionare.Infrastructure;

public sealed partial class ModulAprovizionare : IModul
{
    private const string CodTipNecesar = "necesar-materiale";
    private const string TipDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public string Nume => AprovizionareDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare) =>
        servicii.AdaugaDbContextModul<AprovizionareDbContext>(configurare, Nume);

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/aprovizionare").WithTags("Aprovizionare");
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);
        var aprobare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Aprobare);

        grup.MapGet("/proiecte/{proiectId:guid}/necesare-materiale", ListaNote)
            .WithName("ListaNecesareMateriale")
            .WithSummary("Listează necesarele de materiale ale unui proiect și statusurile lor")
            .Produces<IReadOnlyList<NecesarMaterialeDto>>()
            .CerePermisiune(vizualizare);

        grup.MapGet("/necesare-materiale", ListaNoteFiltrata)
            .WithName("CautaNecesareMateriale")
            .WithSummary("Listează și filtrează necesarele de materiale din proiectele accesibile utilizatorului")
            .Produces<IReadOnlyList<NecesarMaterialeDto>>()
            .Produces(StatusCodes.Status400BadRequest)
            .CerePermisiune(vizualizare);

        grup.MapGet("/proiecte/{proiectId:guid}/necesare-materiale/{id:guid}", DetaliiNecesar)
            .WithName("DetaliiNecesarMateriale")
            .WithSummary("Obține un necesar de materiale")
            .Produces<NecesarMaterialeDto>()
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);

        grup.MapPost("/proiecte/{proiectId:guid}/necesare-materiale", EmiteNecesar)
            .WithName("EmiteNecesarMateriale")
            .WithSummary("Emite un necesar cu număr și dată generate de sistem și o trimite spre aprobare")
            .Produces<NecesarMaterialeDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(creare);

        // Cât nu e aprobat: emitentul (sau cine are drept de editare) îl corectează ori îl șterge, împreună cu nota lui de acceptat.
        grup.MapPut("/proiecte/{proiectId:guid}/necesare-materiale/{id:guid}", ModificaNecesar)
            .WithName("ModificaNecesarMateriale")
            .WithSummary("Modifică un necesar încă neaprobat (compartimentul și materialele); nota de comandă de acceptat se actualizează")
            .Produces<NecesarMaterialeDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(creare);

        grup.MapDelete("/proiecte/{proiectId:guid}/necesare-materiale/{id:guid}", StergeNecesar)
            .WithName("StergeNecesarMateriale")
            .WithSummary("Șterge un necesar încă neaprobat, cu nota lui de comandă de acceptat (și originalul, dacă a fost importat)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(creare);

        grup.MapPost("/proiecte/{proiectId:guid}/necesare-materiale/{id:guid}/aprobare", AprobaNecesar)
            .WithName("AprobaNecesarMateriale")
            .WithSummary("Aprobă un necesar de materiale aflat în așteptare")
            .Produces<NecesarMaterialeDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(aprobare);

        grup.MapPost("/proiecte/{proiectId:guid}/necesare-materiale/{id:guid}/respingere", RespingeNecesar)
            .WithName("RespingeNecesarMateriale")
            .WithSummary("Respinge un necesar de materiale și înregistrează motivul")
            .Produces<NecesarMaterialeDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(aprobare);

        // Necesare emise în afara sistemului: interpretare (OCR / Word) → verificare → necesar cu originalul arhivat.
        ImportNecesarMateriale.Mapeaza(grup);

        // Note de comandă către furnizori (GC 007) emise pe hârtie: import cu originalul arhivat.
        EndpointuriNoteComanda.Mapeaza(grup);
        DocumenteNoteComanda.Mapeaza(grup);

        // Necesar → notă de comandă de acceptat → achiziții (furnizor, emitere), D-37.
        FluxNotaComanda.Mapeaza(grup);

        grup.MapGet("/proiecte/{proiectId:guid}/necesare-materiale/{id:guid}/document.docx", DescarcaNecesar)
            .WithName("DescarcaNecesarMateriale")
            .WithSummary("Generează documentul Word al unui necesar, indiferent de status")
            .Produces(StatusCodes.Status200OK, contentType: TipDocx)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(vizualizare);
    }

    private static async Task<IResult> ListaNote(
        Guid proiectId,
        AprovizionareDbContext db,
        CancellationToken ct)
    {
        var necesare = await db.NecesareMateriale.AsNoTracking()
            .Where(n => n.ProiectId == proiectId)
            .OrderByDescending(n => n.An)
            .ThenByDescending(n => n.Numar)
            .ToListAsync(ct);
        return Results.Ok(necesare.Select(MapareNecesarMateriale.LaDto).ToList());
    }

    private static async Task<IResult> ListaNoteFiltrata(
        [AsParameters] FiltreNecesareMateriale filtre,
        AprovizionareDbContext db,
        IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator,
        IFirmaCurenta firma,
        CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } utilizatorId || firma.FirmaId is not { } firmaId)
            return Results.Unauthorized();
        if (filtre.DataDe is { } dataDe && filtre.DataPanaLa is { } dataPanaLa && dataDe > dataPanaLa)
            return Results.BadRequest(new { mesaj = "Data de început nu poate fi ulterioară datei de sfârșit." });
        if (filtre.Lucrare?.Length > 300 || filtre.Material?.Length > 300 || filtre.Intocmit?.Length > 200 ||
            filtre.AprobatDe?.Length > 200 || filtre.Numar?.Length > 30)
            return Results.BadRequest(new { mesaj = "Unul dintre filtre depășește lungimea maximă permisă." });

        StareNecesarMateriale? stare = null;
        if (!string.IsNullOrWhiteSpace(filtre.Stare))
        {
            if (!Enum.TryParse<StareNecesarMateriale>(filtre.Stare, ignoreCase: true, out var stareCitita) ||
                !Enum.IsDefined(stareCitita))
                return Results.BadRequest(new { mesaj = "Statusul necesarului de materiale nu este valid." });
            stare = stareCitita;
        }

        var evaluator = await permisiuni.PentruUtilizatorAsync(utilizatorId, ct);
        var proiecteAccesibile = evaluator.ProiecteAccesibile(
            Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare),
            firmaId);
        var interogare = db.NecesareMateriale.AsNoTracking();
        if (proiecteAccesibile is not null)
        {
            var idProiecte = proiecteAccesibile.ToList();
            interogare = interogare.Where(n => idProiecte.Contains(n.ProiectId));
        }
        if (filtre.ProiectId is { } proiectId)
            interogare = interogare.Where(n => n.ProiectId == proiectId);
        if (!string.IsNullOrWhiteSpace(filtre.Lucrare))
            interogare = interogare.Where(n => EF.Functions.ILike(n.Lucrare, $"%{filtre.Lucrare.Trim()}%"));
        if (filtre.DataDe is { } inceput)
            interogare = interogare.Where(n => n.Data >= inceput);
        if (filtre.DataPanaLa is { } sfarsit)
            interogare = interogare.Where(n => n.Data <= sfarsit);
        if (stare is { } stareFiltrata)
            interogare = interogare.Where(n => n.Stare == stareFiltrata);
        if (!string.IsNullOrWhiteSpace(filtre.Intocmit))
            interogare = interogare.Where(n => EF.Functions.ILike(n.EmitentNume, $"%{filtre.Intocmit.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filtre.AprobatDe))
            interogare = interogare.Where(n =>
                n.Stare == StareNecesarMateriale.Aprobata &&
                n.DecisaDeNume != null &&
                EF.Functions.ILike(n.DecisaDeNume, $"%{filtre.AprobatDe.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filtre.Numar))
        {
            var numar = filtre.Numar.Trim();
            if (numar.StartsWith("NM/", StringComparison.OrdinalIgnoreCase))
                numar = numar[3..];
            var segmente = numar.Split('/');
            if (segmente.Length == 2 &&
                int.TryParse(segmente[0], NumberStyles.None, CultureInfo.InvariantCulture, out var an) &&
                int.TryParse(segmente[1], NumberStyles.None, CultureInfo.InvariantCulture, out var numarAnual))
                interogare = interogare.Where(n => n.An == an && n.Numar == numarAnual);
            else if (segmente.Length == 1 &&
                     int.TryParse(segmente[0], NumberStyles.None, CultureInfo.InvariantCulture, out numarAnual))
                interogare = interogare.Where(n => n.Numar == numarAnual);
            else
                return Results.BadRequest(new { mesaj = "Numărul trebuie să fie de forma NM/AAAA/NNNN sau NNNN." });
        }

        var necesare = await interogare
            .OrderByDescending(n => n.Data)
            .ThenByDescending(n => n.An)
            .ThenByDescending(n => n.Numar)
            .ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(filtre.Material))
        {
            var material = filtre.Material.Trim();
            necesare = necesare.Where(n => n.Linii.Any(l =>
                l.Denumire.Contains(material, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        return Results.Ok(necesare.Select(MapareNecesarMateriale.LaDto).ToList());
    }

    private static async Task<IResult> DetaliiNecesar(
        Guid proiectId,
        Guid id,
        AprovizionareDbContext db,
        CancellationToken ct)
    {
        var necesar = await db.NecesareMateriale.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        return necesar is null ? Results.NotFound() : Results.Ok(MapareNecesarMateriale.LaDto(necesar));
    }

    private static async Task<IResult> EmiteNecesar(
        Guid proiectId,
        EmiteNecesarMaterialeCerere cerere,
        AprovizionareDbContext db,
        IInterogareProiecte proiecte,
        IInterogareSabloaneFormulare sabloane,
        IFirmaCurenta firma,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        if (cerere.Linii is null || cerere.Linii.Count == 0)
            return Results.BadRequest(new { mesaj = "Adaugă cel puțin un material." });
        if (string.IsNullOrWhiteSpace(cerere.CompartimentSantier) || cerere.CompartimentSantier.Length > 300)
            return Results.BadRequest(new { mesaj = "Completează compartimentul / șantierul (maximum 300 de caractere)." });
        if (cerere.Linii.Any(l => string.IsNullOrWhiteSpace(l.Denumire) ||
                                  string.IsNullOrWhiteSpace(l.UnitateMasura) ||
                                  l.Cantitate <= 0))
            return Results.BadRequest(new { mesaj = "Fiecare material trebuie să aibă denumire, UM și cantitate pozitivă." });
        if (firma.FirmaId is not { } firmaId || utilizator.UtilizatorId is not { } utilizatorId)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(utilizator.Nume) || utilizator.Nume.Length > 200)
            return Results.BadRequest(new { mesaj = "Numele emitentului nu este disponibil în sesiune." });

        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });

        var sablon = await sabloane.SablonActivAsync(CodTipNecesar, ct);
        if (sablon is null)
            return Results.Conflict(new { mesaj = "Nu există un șablon activ pentru necesarul de materiale. Încarcă-l în Formulare și documente." });

        var azi = ceas.Azi;
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await BlocheazaSecventaAsync(db, tranzactie, firmaId, azi.Year, ct);
        var numar = (await db.NecesareMateriale
            .Where(n => n.An == azi.Year)
            .MaxAsync(n => (int?)n.Numar, ct) ?? 0) + 1;

        var rezultat = NecesarMateriale.Emite(
            azi.Year,
            numar,
            proiect.Id,
            proiect.Cod,
            proiect.Denumire,
            cerere.CompartimentSantier.Trim(),
            azi,
            utilizatorId,
            utilizator.Nume,
            sablon.Id,
            sablon.Versiune,
            cerere.Linii.Select(l => new LinieNecesarMateriale(
                l.Denumire.Trim(),
                l.UnitateMasura.Trim(),
                l.Cantitate,
                l.Observatii?.Trim() ?? string.Empty,
                l.ArticolDevizId)).ToList());
        if (rezultat.Esec)
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });

        db.NecesareMateriale.Add(rezultat.Valoare);
        // D-37: necesarul nou generează nota de comandă „de acceptat”; acceptarea ei aprobă necesarul.
        var nota = await FluxNotaComanda.GenereazaAsync(db, tranzactie, firmaId, azi.Year, rezultat.Valoare, utilizatorId, utilizator.Nume, ct);
        if (nota.Esec)
            return Results.BadRequest(new { mesaj = nota.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);

        var dto = MapareNecesarMateriale.LaDto(rezultat.Valoare);
        return Results.Created($"/api/aprovizionare/proiecte/{proiectId}/necesare-materiale/{dto.Id}", dto);
    }

    internal static async Task BlocheazaSecventaAsync(
        AprovizionareDbContext db,
        IDbContextTransaction tranzactie,
        Guid firmaId,
        int an,
        CancellationToken ct,
        string secventa = "")
    {
        await using var comanda = db.Database.GetDbConnection().CreateCommand();
        comanda.Transaction = tranzactie.GetDbTransaction();
        comanda.CommandText = "SELECT pg_advisory_xact_lock(hashtext(@firma), @an)";

        var parametruFirma = comanda.CreateParameter();
        parametruFirma.ParameterName = "firma";
        parametruFirma.Value = firmaId.ToString("N") + secventa; // secvențe separate: necesare („”), necesare de comandă („:nc”)
        comanda.Parameters.Add(parametruFirma);

        var parametruAn = comanda.CreateParameter();
        parametruAn.ParameterName = "an";
        parametruAn.Value = an;
        comanda.Parameters.Add(parametruAn);

        await comanda.ExecuteScalarAsync(ct);
    }

    /// <summary>Necesarul neaprobat îl schimbă cine l-a emis; altcineva doar cu drept de editare pe aprovizionare (pe proiect).</summary>
    private static async Task<IResult?> PoateSchimbaAsync(
        NecesarMateriale necesar, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        if (necesar.EmitentId == uid)
            return null;
        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        return evaluator.Are(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Editare), new Domeniu(fid, necesar.ProiectId))
            ? null
            : Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: $"Necesarul l-a emis {necesar.EmitentNume}; îl poate schimba doar el sau cine are drept de editare pe aprovizionare.");
    }

    private static async Task<IResult> ModificaNecesar(
        Guid proiectId,
        Guid id,
        EmiteNecesarMaterialeCerere cerere,
        AprovizionareDbContext db,
        IServiciuPermisiuni permisiuni,
        IFirmaCurenta firma,
        IUtilizatorCurent utilizator,
        CancellationToken ct)
    {
        var necesar = await db.NecesareMateriale.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (necesar is null)
            return Results.NotFound();
        if (await PoateSchimbaAsync(necesar, permisiuni, utilizator, firma, ct) is { } refuz)
            return refuz;
        if (necesar.Stare != StareNecesarMateriale.InAsteptareAprobare)
            return Results.Conflict(new { mesaj = "Necesarul a fost deja aprobat sau respins: nu se mai modifică." });

        var r = necesar.Modifica(cerere.CompartimentSantier ?? string.Empty,
            (cerere.Linii ?? []).Select(l => new LinieNecesarMateriale(
                l.Denumire ?? string.Empty, l.UnitateMasura ?? string.Empty, l.Cantitate, l.Observatii ?? string.Empty, l.ArticolDevizId)).ToList());
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        var nota = await db.NoteComanda.FirstOrDefaultAsync(c => c.NecesarId == necesar.Id && c.Stare == StareNotaComanda.DeAcceptat, ct);
        if (nota is not null)
        {
            var actualizata = nota.ActualizeazaDinNecesar(necesar);
            if (actualizata.Esec)
                return Results.Conflict(new { mesaj = actualizata.Eroare.Mesaj });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(MapareNecesarMateriale.LaDto(necesar));
    }

    private static async Task<IResult> StergeNecesar(
        Guid proiectId,
        Guid id,
        AprovizionareDbContext db,
        IServiciuPermisiuni permisiuni,
        IFirmaCurenta firma,
        IUtilizatorCurent utilizator,
        CancellationToken ct)
    {
        var necesar = await db.NecesareMateriale.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (necesar is null)
            return Results.NotFound();
        if (await PoateSchimbaAsync(necesar, permisiuni, utilizator, firma, ct) is { } refuz)
            return refuz;
        var r = necesar.VerificaStergerea();
        if (r.Esec)
            return Results.Conflict(new { mesaj = r.Eroare.Mesaj });

        // Nota generată din el (încă neacceptată) dispare odată cu necesarul; originalul importat la fel, ca fișierul să se poată reimporta.
        var note = await db.NoteComanda.Where(c => c.NecesarId == necesar.Id).ToListAsync(ct);
        if (note.Any(c => c.Stare != StareNotaComanda.DeAcceptat))
            return Results.Conflict(new { mesaj = "Nota de comandă a necesarului a fost deja acceptată: necesarul nu se mai șterge." });
        db.NoteComanda.RemoveRange(note);
        if (necesar.DocumentOriginalId is { } originalId && await db.DocumenteOriginale.FirstOrDefaultAsync(d => d.Id == originalId, ct) is { } original)
            db.DocumenteOriginale.Remove(original);
        db.NecesareMateriale.Remove(necesar);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AprobaNecesar(
        Guid proiectId,
        Guid id,
        AprovizionareDbContext db,
        IFirmaCurenta firma,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        if (firma.FirmaId is not { } firmaId)
            return Results.Unauthorized();
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        var necesar = await db.NecesareMateriale.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (necesar is null)
            return Results.NotFound();
        if (utilizator.UtilizatorId is not { } utilizatorId)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.BadRequest(new { mesaj = "Numele aprobatorului nu este disponibil în sesiune." });

        var rezultat = necesar.Aproba(utilizatorId, utilizator.Nume, ceas.Acum);
        if (rezultat.Esec)
            return rezultat.Eroare.Cod == "interzis"
                ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: rezultat.Eroare.Mesaj)
                : Results.Conflict(new { mesaj = rezultat.Eroare.Mesaj });

        // Aprobarea necesarului acceptă și nota lui de comandă (necesarele vechi o primesc acum, direct acceptată).
        var nota = await FluxNotaComanda.LaAprobareaNecesaruluiAsync(db, tranzactie, firmaId, ceas.Azi.Year, necesar, utilizatorId, utilizator.Nume, ceas.Acum, ct);
        if (nota.Esec)
            return Results.Conflict(new { mesaj = nota.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(MapareNecesarMateriale.LaDto(necesar));
    }

    private static async Task<IResult> RespingeNecesar(
        Guid proiectId,
        Guid id,
        RespingeNecesarMaterialeCerere cerere,
        AprovizionareDbContext db,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        var necesar = await db.NecesareMateriale.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (necesar is null)
            return Results.NotFound();
        if (utilizator.UtilizatorId is not { } utilizatorId)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.BadRequest(new { mesaj = "Numele decidentului nu este disponibil în sesiune." });
        if (necesar.Stare != StareNecesarMateriale.InAsteptareAprobare)
            return Results.Conflict(new { mesaj = "Doar necesarele aflate în așteptarea aprobării pot fi respinse." });

        var rezultat = necesar.Respinge(utilizatorId, utilizator.Nume, ceas.Acum, cerere.Motiv);
        if (rezultat.Esec)
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });
        var nota = await FluxNotaComanda.LaRespingereaNecesaruluiAsync(db, necesar, utilizatorId, utilizator.Nume, ceas.Acum, cerere.Motiv, ct);
        if (nota.Esec)
            return Results.BadRequest(new { mesaj = nota.Eroare.Mesaj });

        await db.SaveChangesAsync(ct);
        return Results.Ok(MapareNecesarMateriale.LaDto(necesar));
    }

    private static async Task<IResult> DescarcaNecesar(
        Guid proiectId,
        Guid id,
        AprovizionareDbContext db,
        IInterogareSabloaneFormulare sabloane,
        IGeneratorFormular generator,
        CancellationToken ct)
    {
        var necesar = await db.NecesareMateriale.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (necesar is null)
            return Results.NotFound();

        if (necesar.SablonId == Guid.Empty)
            return Results.Conflict(new { mesaj = "Necesarul a fost importat fără șablon de firmă; descarcă documentul original." });

        var sablon = await sabloane.SablonAsync(necesar.SablonId, necesar.VersiuneSablon, ct);
        if (sablon is null)
            return Results.Conflict(new { mesaj = "Versiunea șablonului folosită de necesar nu mai este disponibilă." });

        var formatRo = CultureInfo.GetCultureInfo("ro-RO");
        var date = new DateFormular(
            new Dictionary<string, string>
            {
                ["numar"] = necesar.NumarAfisat,
                ["data"] = necesar.Data.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["proiect_cod"] = necesar.ProiectCod,
                ["proiect_denumire"] = necesar.Lucrare,
                ["lucrare"] = necesar.Lucrare,
                ["compartiment_santier"] = necesar.CompartimentSantier,
                ["intocmit_de"] = necesar.EmitentNume,
            },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["linii"] = necesar.Linii.Select(l => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["denumire"] = l.Denumire,
                    ["um"] = l.UnitateMasura,
                    ["cantitate"] = l.Cantitate.ToString("0.####", formatRo),
                    ["observatii"] = l.Observatii,
                    ["articol_deviz"] = l.ArticolDevizId?.ToString() ?? string.Empty,
                }).ToList(),
            });
        var metadate = new MetadateFormular(CodTipNecesar, sablon.Versiune, necesar.Id, necesar.NumarAfisat);
        var docx = generator.Genereaza(sablon.Continut, CatalogFormulare.NecesarMateriale, date, metadate, randuriGoale: 15);
        var numeFisier = Regex.Replace(necesar.NumarAfisat, @"[^a-zA-Z0-9_-]", "_", RegexOptions.CultureInvariant);
        return Results.File(docx, TipDocx, $"{numeFisier}.docx");
    }
}
