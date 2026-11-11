using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Parteneri;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ErpConstructii.Aprovizionare.Infrastructure;

public sealed record RespingeNotaComandaCerere(string Motiv);

/// <param name="Index">Poziția liniei pe nota acceptată (de la 0).</param>
public sealed record LinieComandaFurnizorCerere(int Index, string? Denumire, decimal Cantitate, string? Termen);

/// <summary>Furnizorul, condițiile și liniile (toate sau o parte) cu care achizițiile trimit nota acceptată.</summary>
public sealed record ComandaLaFurnizorCerere(
    DateOnly? Data,
    string? Lucrare,
    string FurnizorDenumire,
    string? FurnizorCui,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    string? FurnizorEmail,
    ConditiiComandaDto? Conditii,
    IReadOnlyList<LinieComandaFurnizorCerere> Linii,
    string? Observatii);

/// <param name="RamasaId">Nota acceptată, dacă au rămas pe ea materiale pentru alt furnizor.</param>
public sealed record ComandaLaFurnizorRaspuns(NotaComandaDto Emisa, Guid? RamasaId, int LiniiRamase);

/// <summary>
/// Fluxul necesar → notă de comandă (D-37): crearea necesarului generează nota „de acceptat”; acceptarea ei aprobă necesarul
/// și o trimite la achiziții („Necesar de achiziție”), unde primește furnizorul și se emite (întreagă sau împărțită pe furnizori).
/// </summary>
internal static class FluxNotaComanda
{
    internal const string SecventaComenzi = ":nc";

    public static void Mapeaza(RouteGroupBuilder grup)
    {
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);
        var aprobare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Aprobare);

        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/acceptare", Accepta)
            .WithName("AcceptaNotaComanda")
            .WithSummary("Acceptă nota de comandă generată din necesar (aprobă și necesarul); ajunge la achiziții")
            .Produces<NotaComandaDto>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(aprobare);

        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/respingere", Respinge)
            .WithName("RespingeNotaComanda")
            .WithSummary("Respinge nota de comandă generată din necesar (respinge și necesarul)")
            .Produces<NotaComandaDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(aprobare);

        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/comanda", ComandaLaFurnizor)
            .WithName("ComandaLaFurnizor")
            .WithSummary("Achiziții: emite nota acceptată către un furnizor (toată sau liniile alese; restul rămâne pentru alt furnizor)")
            .Produces<ComandaLaFurnizorRaspuns>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(creare);
    }

    /// <summary>Generează nota „de acceptat” pentru necesarul nou, în aceeași tranzacție (numărul NC sub blocarea secvenței).</summary>
    internal static async Task<Rezultat<NotaComanda>> GenereazaAsync(
        AprovizionareDbContext db, IDbContextTransaction tranzactie, Guid firmaId, int an, NecesarMateriale necesar,
        Guid utilizatorId, string utilizatorNume, CancellationToken ct)
    {
        await ModulAprovizionare.BlocheazaSecventaAsync(db, tranzactie, firmaId, an, ct, SecventaComenzi);
        var numar = await UrmatorulNumarAsync(db, an, ct);
        var r = NotaComanda.GenereazaDinNecesar(an, numar, necesar, utilizatorId, utilizatorNume);
        if (r.Succes)
            db.NoteComanda.Add(r.Valoare);
        return r;
    }

    /// <summary>
    /// Următorul număr NC al anului. Numerele notelor șterse (D-44) rămân ocupate: se iau în calcul și ele, altfel ștergerea
    /// ultimei note i-ar refolosi numărul. Se apelează după blocarea secvenței.
    /// </summary>
    internal static async Task<int> UrmatorulNumarAsync(AprovizionareDbContext db, int an, CancellationToken ct)
    {
        var maxNote = await db.NoteComanda.Where(c => c.An == an).MaxAsync(c => (int?)c.Numar, ct) ?? 0;
        var prefix = $"NC/{an}/";
        var sterse = await db.StergeriNoteComanda.AsNoTracking().Where(s => s.Numar.StartsWith(prefix)).Select(s => s.Numar).ToListAsync(ct);
        var maxSterse = sterse.Select(n => int.TryParse(n[prefix.Length..], out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return Math.Max(maxNote, maxSterse) + 1;
    }

    /// <summary>
    /// Aprobarea necesarului din lista necesarelor: acceptă nota lui; necesarele de dinainte de D-37 (fără notă) primesc acum
    /// nota, direct acceptată.
    /// </summary>
    internal static async Task<Rezultat> LaAprobareaNecesaruluiAsync(
        AprovizionareDbContext db, IDbContextTransaction tranzactie, Guid firmaId, int an, NecesarMateriale necesar,
        Guid utilizatorId, string utilizatorNume, DateTimeOffset moment, CancellationToken ct)
    {
        var nota = await db.NoteComanda.FirstOrDefaultAsync(c => c.NecesarId == necesar.Id && c.NotaSursaId == null, ct);
        if (nota is null)
        {
            var generata = await GenereazaAsync(db, tranzactie, firmaId, an, necesar, utilizatorId, utilizatorNume, ct);
            if (generata.Esec)
                return Rezultat.Esuat(generata.Eroare);
            nota = generata.Valoare;
        }

        return nota.Stare == StareNotaComanda.DeAcceptat ? nota.Accepta(utilizatorId, utilizatorNume, moment) : Rezultat.Ok();
    }

    internal static async Task<Rezultat> LaRespingereaNecesaruluiAsync(
        AprovizionareDbContext db, NecesarMateriale necesar, Guid utilizatorId, string utilizatorNume, DateTimeOffset moment, string motiv, CancellationToken ct)
    {
        var nota = await db.NoteComanda.FirstOrDefaultAsync(c => c.NecesarId == necesar.Id && c.Stare == StareNotaComanda.DeAcceptat, ct);
        return nota is null ? Rezultat.Ok() : nota.Respinge(utilizatorId, utilizatorNume, moment, motiv);
    }

    private static async Task<IResult> Accepta(
        Guid proiectId, Guid id, AprovizionareDbContext db, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        var nota = await db.NoteComanda.FirstOrDefaultAsync(c => c.Id == id && c.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound();

        // Acceptarea notei e aprobarea necesarului: cine a cerut materialele nu își aprobă singur cererea.
        if (nota.NecesarId is { } necesarId && await db.NecesareMateriale.FirstOrDefaultAsync(n => n.Id == necesarId, ct) is { } necesar
            && necesar.Stare == StareNecesarMateriale.InAsteptareAprobare)
        {
            var aprobat = necesar.Aproba(uid, utilizator.Nume, ceas.Acum);
            if (aprobat.Esec)
                return aprobat.Eroare.Cod == "interzis"
                    ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Cine a emis necesarul nu poate accepta nota de comandă generată din el.")
                    : Results.Conflict(new { mesaj = aprobat.Eroare.Mesaj });
        }

        var r = nota.Accepta(uid, utilizator.Nume, ceas.Acum);
        if (r.Esec)
            return Results.Conflict(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.Ok(EndpointuriNoteComanda.LaDto(nota));
    }

    private static async Task<IResult> Respinge(
        Guid proiectId, Guid id, RespingeNotaComandaCerere cerere, AprovizionareDbContext db, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        var nota = await db.NoteComanda.FirstOrDefaultAsync(c => c.Id == id && c.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound();
        if (nota.Stare != StareNotaComanda.DeAcceptat)
            return Results.Conflict(new { mesaj = "Doar notele de comandă în așteptarea acceptării pot fi respinse." });

        var r = nota.Respinge(uid, utilizator.Nume, ceas.Acum, cerere.Motiv ?? string.Empty);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        if (nota.NecesarId is { } necesarId && await db.NecesareMateriale.FirstOrDefaultAsync(n => n.Id == necesarId, ct) is { Stare: StareNecesarMateriale.InAsteptareAprobare } necesar)
        {
            var respins = necesar.Respinge(uid, utilizator.Nume, ceas.Acum, cerere.Motiv!);
            if (respins.Esec)
                return Results.BadRequest(new { mesaj = respins.Eroare.Mesaj });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(EndpointuriNoteComanda.LaDto(nota));
    }

    private static async Task<IResult> ComandaLaFurnizor(
        Guid proiectId,
        Guid id,
        ComandaLaFurnizorCerere cerere,
        AprovizionareDbContext db,
        IServiciuParteneri parteneri,
        IInterogareSabloaneFormulare sabloane,
        IFirmaCurenta firma,
        IUtilizatorCurent utilizator,
        ICeas ceas,
        CancellationToken ct)
    {
        if (firma.FirmaId is not { } firmaId || utilizator.UtilizatorId is not { } uid)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(utilizator.Nume) || utilizator.Nume.Length > 200)
            return Results.BadRequest(new { mesaj = "Numele utilizatorului nu este disponibil în sesiune." });
        if (string.IsNullOrWhiteSpace(cerere.FurnizorDenumire))
            return Results.BadRequest(new { mesaj = "Alege sau scrie furnizorul." });
        var azi = ceas.Azi;
        var data = cerere.Data ?? azi;
        if (data.Year < 2000 || data > azi.AddDays(30))
            return Results.BadRequest(new { mesaj = "Data notei de comandă nu este validă (cel mult 30 de zile în viitor)." });
        if (!EndpointuriNoteComanda.Conditii(cerere.Conditii, out var conditii))
            return Results.BadRequest(new { mesaj = "Condițiile de livrare nu sunt valide." });
        var cui = string.IsNullOrWhiteSpace(cerere.FurnizorCui) ? null : cerere.FurnizorCui.Trim();
        if (cui is not null && !ValidareFiscala.EsteCuiValid(cui))
            return Results.BadRequest(new { mesaj = $"CUI-ul furnizorului „{cui}” nu este valid." });

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
        var nota = await db.NoteComanda.FirstOrDefaultAsync(c => c.Id == id && c.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound();
        if (nota.Stare != StareNotaComanda.Acceptata)
            return Results.Conflict(new { mesaj = nota.Stare == StareNotaComanda.Emisa ? "Nota a fost deja trimisă la furnizor." : "Nota de comandă nu a fost acceptată." });

        var r = nota.ComandaLaFurnizor(
            new DateComandaFurnizor(data, cerere.Lucrare, furnizor.Id, furnizor.Denumire, furnizor.Cui ?? cui,
                cerere.FurnizorAdresa, cerere.FurnizorTelefon, cerere.FurnizorEmail, conditii, cerere.Observatii),
            (cerere.Linii ?? []).Select(l => new LinieDeComandat(l.Index, l.Denumire?.Trim(), l.Cantitate, l.Termen, null)).ToList(),
            data.Year, await UrmatorulNumarAsync(db, data.Year, ct), sablon?.Id, sablon?.Versiune, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        var emisa = r.Valoare;
        if (!ReferenceEquals(emisa, nota))
            db.NoteComanda.Add(emisa);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);

        var ramase = ReferenceEquals(emisa, nota) ? 0 : nota.Linii.Count;
        return Results.Ok(new ComandaLaFurnizorRaspuns(EndpointuriNoteComanda.LaDto(emisa), ramase > 0 ? nota.Id : null, ramase));
    }
}
