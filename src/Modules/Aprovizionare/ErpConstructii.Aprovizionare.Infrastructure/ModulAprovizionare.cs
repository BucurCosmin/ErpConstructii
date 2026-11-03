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
    private const string CodTipNota = "comanda-furnizor";
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

        grup.MapGet("/proiecte/{proiectId:guid}/note-comanda", ListaNote)
            .WithName("ListaNoteComandaAprovizionare")
            .WithSummary("Listează notele de comandă ale unui proiect și statusurile lor")
            .Produces<IReadOnlyList<NotaComandaDto>>()
            .CerePermisiune(vizualizare);

        grup.MapGet("/note-comanda", ListaNoteFiltrata)
            .WithName("CautaNoteComandaAprovizionare")
            .WithSummary("Listează și filtrează notele de comandă din proiectele accesibile utilizatorului")
            .Produces<IReadOnlyList<NotaComandaDto>>()
            .Produces(StatusCodes.Status400BadRequest)
            .CerePermisiune(vizualizare);

        grup.MapGet("/proiecte/{proiectId:guid}/note-comanda/{id:guid}", DetaliiNota)
            .WithName("DetaliiNotaComandaAprovizionare")
            .WithSummary("Obține o notă de comandă")
            .Produces<NotaComandaDto>()
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);

        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda", EmiteNota)
            .WithName("EmiteNotaComandaAprovizionare")
            .WithSummary("Emite o notă cu număr și dată generate de sistem și o trimite spre aprobare")
            .Produces<NotaComandaDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(creare);

        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/aprobare", AprobaNota)
            .WithName("AprobaNotaComandaAprovizionare")
            .WithSummary("Aprobă o notă de comandă aflată în așteptare")
            .Produces<NotaComandaDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(aprobare);

        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/respingere", RespingeNota)
            .WithName("RespingeNotaComandaAprovizionare")
            .WithSummary("Respinge o notă de comandă și înregistrează motivul")
            .Produces<NotaComandaDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(aprobare);

        grup.MapGet("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/document.docx", DescarcaNota)
            .WithName("DescarcaNotaComandaAprovizionare")
            .WithSummary("Generează documentul Word al unei note, indiferent de status")
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
        var note = await db.NoteComanda.AsNoTracking()
            .Where(n => n.ProiectId == proiectId)
            .OrderByDescending(n => n.An)
            .ThenByDescending(n => n.Numar)
            .ToListAsync(ct);
        return Results.Ok(note.Select(MapareNotaComanda.LaDto).ToList());
    }

    private static async Task<IResult> ListaNoteFiltrata(
        [AsParameters] FiltreNoteComanda filtre,
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

        StareNotaComanda? stare = null;
        if (!string.IsNullOrWhiteSpace(filtre.Stare))
        {
            if (!Enum.TryParse<StareNotaComanda>(filtre.Stare, ignoreCase: true, out var stareCitita) ||
                !Enum.IsDefined(stareCitita))
                return Results.BadRequest(new { mesaj = "Statusul notei de comandă nu este valid." });
            stare = stareCitita;
        }

        var evaluator = await permisiuni.PentruUtilizatorAsync(utilizatorId, ct);
        var proiecteAccesibile = evaluator.ProiecteAccesibile(
            Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare),
            firmaId);
        var interogare = db.NoteComanda.AsNoTracking();
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
                n.Stare == StareNotaComanda.Aprobata &&
                n.DecisaDeNume != null &&
                EF.Functions.ILike(n.DecisaDeNume, $"%{filtre.AprobatDe.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filtre.Numar))
        {
            var numar = filtre.Numar.Trim();
            if (numar.StartsWith("NC/", StringComparison.OrdinalIgnoreCase))
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
                return Results.BadRequest(new { mesaj = "Numărul trebuie să fie de forma NC/AAAA/NNNN sau NNNN." });
        }

        var note = await interogare
            .OrderByDescending(n => n.Data)
            .ThenByDescending(n => n.An)
            .ThenByDescending(n => n.Numar)
            .ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(filtre.Material))
        {
            var material = filtre.Material.Trim();
            note = note.Where(n => n.Linii.Any(l =>
                l.Denumire.Contains(material, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        return Results.Ok(note.Select(MapareNotaComanda.LaDto).ToList());
    }

    private static async Task<IResult> DetaliiNota(
        Guid proiectId,
        Guid id,
        AprovizionareDbContext db,
        CancellationToken ct)
    {
        var nota = await db.NoteComanda.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        return nota is null ? Results.NotFound() : Results.Ok(MapareNotaComanda.LaDto(nota));
    }

    private static async Task<IResult> EmiteNota(
        Guid proiectId,
        EmiteNotaComandaCerere cerere,
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

        var sablon = await sabloane.SablonActivAsync(CodTipNota, ct);
        if (sablon is null)
            return Results.Conflict(new { mesaj = "Nu există un șablon activ pentru nota de comandă. Încarcă-l în Formulare și documente." });

        var azi = ceas.Azi;
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await BlocheazaSecventaAsync(db, tranzactie, firmaId, azi.Year, ct);
        var numar = (await db.NoteComanda
            .Where(n => n.An == azi.Year)
            .MaxAsync(n => (int?)n.Numar, ct) ?? 0) + 1;

        var rezultat = NotaComanda.Emite(
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
            cerere.Linii.Select(l => new LinieNotaComanda(
                l.Denumire.Trim(),
                l.UnitateMasura.Trim(),
                l.Cantitate,
                l.Observatii?.Trim() ?? string.Empty,
                l.ArticolDevizId)).ToList());
        if (rezultat.Esec)
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });

        db.NoteComanda.Add(rezultat.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);

        var dto = MapareNotaComanda.LaDto(rezultat.Valoare);
        return Results.Created($"/api/aprovizionare/proiecte/{proiectId}/note-comanda/{dto.Id}", dto);
    }

    private static async Task BlocheazaSecventaAsync(
        AprovizionareDbContext db,
        IDbContextTransaction tranzactie,
        Guid firmaId,
        int an,
        CancellationToken ct)
    {
        await using var comanda = db.Database.GetDbConnection().CreateCommand();
        comanda.Transaction = tranzactie.GetDbTransaction();
        comanda.CommandText = "SELECT pg_advisory_xact_lock(hashtext(@firma), @an)";

        var parametruFirma = comanda.CreateParameter();
        parametruFirma.ParameterName = "firma";
        parametruFirma.Value = firmaId.ToString("N");
        comanda.Parameters.Add(parametruFirma);

        var parametruAn = comanda.CreateParameter();
        parametruAn.ParameterName = "an";
        parametruAn.Value = an;
        comanda.Parameters.Add(parametruAn);

        await comanda.ExecuteScalarAsync(ct);
    }

    private static async Task<IResult> AprobaNota(
        Guid proiectId,
        Guid id,
        AprovizionareDbContext db,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        var nota = await db.NoteComanda.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound();
        if (utilizator.UtilizatorId is not { } utilizatorId)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.BadRequest(new { mesaj = "Numele aprobatorului nu este disponibil în sesiune." });

        var rezultat = nota.Aproba(utilizatorId, utilizator.Nume, ceas.Acum);
        if (rezultat.Esec)
            return rezultat.Eroare.Cod == "interzis"
                ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: rezultat.Eroare.Mesaj)
                : Results.Conflict(new { mesaj = rezultat.Eroare.Mesaj });

        await db.SaveChangesAsync(ct);
        return Results.Ok(MapareNotaComanda.LaDto(nota));
    }

    private static async Task<IResult> RespingeNota(
        Guid proiectId,
        Guid id,
        RespingeNotaComandaCerere cerere,
        AprovizionareDbContext db,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        var nota = await db.NoteComanda.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound();
        if (utilizator.UtilizatorId is not { } utilizatorId)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.BadRequest(new { mesaj = "Numele decidentului nu este disponibil în sesiune." });
        if (nota.Stare != StareNotaComanda.InAsteptareAprobare)
            return Results.Conflict(new { mesaj = "Doar notele aflate în așteptarea aprobării pot fi respinse." });

        var rezultat = nota.Respinge(utilizatorId, utilizator.Nume, ceas.Acum, cerere.Motiv);
        if (rezultat.Esec)
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });

        await db.SaveChangesAsync(ct);
        return Results.Ok(MapareNotaComanda.LaDto(nota));
    }

    private static async Task<IResult> DescarcaNota(
        Guid proiectId,
        Guid id,
        AprovizionareDbContext db,
        IInterogareSabloaneFormulare sabloane,
        IGeneratorFormular generator,
        CancellationToken ct)
    {
        var nota = await db.NoteComanda.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound();

        var sablon = await sabloane.SablonAsync(nota.SablonId, nota.VersiuneSablon, ct);
        if (sablon is null)
            return Results.Conflict(new { mesaj = "Versiunea șablonului folosită de notă nu mai este disponibilă." });

        var formatRo = CultureInfo.GetCultureInfo("ro-RO");
        var date = new DateFormular(
            new Dictionary<string, string>
            {
                ["numar"] = nota.NumarAfisat,
                ["data"] = nota.Data.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["proiect_cod"] = nota.ProiectCod,
                ["proiect_denumire"] = nota.Lucrare,
                ["lucrare"] = nota.Lucrare,
                ["compartiment_santier"] = nota.CompartimentSantier,
                ["intocmit_de"] = nota.EmitentNume,
            },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
            {
                ["linii"] = nota.Linii.Select(l => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["denumire"] = l.Denumire,
                    ["um"] = l.UnitateMasura,
                    ["cantitate"] = l.Cantitate.ToString("0.####", formatRo),
                    ["observatii"] = l.Observatii,
                    ["articol_deviz"] = l.ArticolDevizId?.ToString() ?? string.Empty,
                }).ToList(),
            });
        var metadate = new MetadateFormular(CodTipNota, sablon.Versiune, nota.Id, nota.NumarAfisat);
        var docx = generator.Genereaza(sablon.Continut, CatalogFormulare.ComandaFurnizor, date, metadate, randuriGoale: 15);
        var numeFisier = Regex.Replace(nota.NumarAfisat, @"[^a-zA-Z0-9_-]", "_", RegexOptions.CultureInvariant);
        return Results.File(docx, TipDocx, $"{numeFisier}.docx");
    }
}
