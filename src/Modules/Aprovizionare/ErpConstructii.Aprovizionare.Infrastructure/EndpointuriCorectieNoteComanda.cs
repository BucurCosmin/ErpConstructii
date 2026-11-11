using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Parteneri;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Aprovizionare.Infrastructure;

/// <summary>Datele noi ale notei emise; numărul și proiectul nu se schimbă.</summary>
public sealed record ModificaNotaComandaCerere(
    string? NumarExtern,
    DateOnly Data,
    string? Lucrare,
    string FurnizorDenumire,
    string? FurnizorCui,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    string? FurnizorEmail,
    ConditiiComandaDto? Conditii,
    IReadOnlyList<LinieNotaComandaDto> Linii,
    string? Observatii,
    string Motiv);

/// <summary>Necesarul de care se leagă o notă importată fără el (D-45).</summary>
public sealed record LeagaNecesarCerere(Guid NecesarId);

/// <summary>Marchează nota importată ca document din urmă sau scoate marcajul (D-45).</summary>
public sealed record DinUrmaCerere(bool DinUrma);

/// <param name="Rezultat">„Stearsa” sau „RevenitaLaAchizitii” (comanda dintr-o notă acceptată, care revine în „Necesar de achiziție”).</param>
public sealed record StergereNotaComandaRaspuns(string Rezultat, string Mesaj);

public sealed record CorectieNotaComandaDto(Guid Id, string Tip, string Motiv, string Rezumat, string CorectatDe, DateTimeOffset CorectatLa);

public sealed record StergereNotaComandaDto(Guid Id, Guid NotaComandaId, string Numar, string? NumarExtern, DateOnly Data, Guid ProiectId, string ProiectCod,
    string FurnizorDenumire, string Motiv, string? Efect, string StearsaDe, DateTimeOffset StearsaLa);

/// <summary>
/// Modificarea și ștergerea notelor de comandă emise (D-44). Nota emisă rămâne blocată (regula 6): se modifică doar împreună cu o
/// corectură (istoric: datele dinainte / după, motivul, autorul), iar la ștergere rămâne înregistrarea ștergerii cu instantaneul
/// notei. Ambele apar și în jurnalul de modificări. Nota legată de facturi sau avize nu se șterge. Comanda emisă dintr-o notă
/// acceptată din necesar nu dispare: revine la achiziții; cea desprinsă dintr-o notă încă la achiziții își întoarce materialele pe ea.
/// </summary>
internal static class EndpointuriCorectieNoteComanda
{
    public static void Mapeaza(RouteGroupBuilder grup)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Editare);
        var anulare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Anulare);

        grup.MapPut("/proiecte/{proiectId:guid}/note-comanda/{id:guid}", Modifica)
            .WithName("ModificaNotaComanda")
            .WithSummary("Modifică o notă de comandă emisă (cu motiv; istoricul păstrează datele vechi)")
            .Produces<NotaComandaDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare);
        grup.MapDelete("/proiecte/{proiectId:guid}/note-comanda/{id:guid}", Sterge)
            .WithName("StergeNotaComanda")
            .WithSummary("Șterge o notă de comandă emisă (?motiv=…) sau, dacă vine dintr-o notă acceptată, o întoarce la achiziții")
            .Produces<StergereNotaComandaRaspuns>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(anulare);
        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/necesar", LeagaDeNecesar)
            .WithName("LeagaNotaComandaDeNecesar")
            .WithSummary("Leagă o notă de comandă importată fără necesar de necesarul ei (D-45; apare în istoricul notei)")
            .Produces<NotaComandaDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare);
        grup.MapPost("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/din-urma", SchimbaDinUrma)
            .WithName("NotaComandaDinUrma")
            .WithSummary("Marchează nota importată ca document din urmă (fără necesar) sau scoate marcajul (D-45)")
            .Produces<NotaComandaDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare);
        grup.MapGet("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/istoric", Istoric)
            .WithName("IstoricNotaComanda")
            .WithSummary("Modificările unei note de comandă")
            .Produces<IReadOnlyList<CorectieNotaComandaDto>>()
            .CerePermisiune(vizualizare);
        grup.MapGet("/note-comanda/sterse", Sterse)
            .WithName("NoteComandaSterse")
            .WithSummary("Notele de comandă șterse (cine, când, de ce) din proiectele accesibile")
            .Produces<IReadOnlyList<StergereNotaComandaDto>>()
            .CerePermisiune(vizualizare);
    }

    private static async Task<IResult> LeagaDeNecesar(
        Guid proiectId, Guid id, LeagaNecesarCerere cerere, AprovizionareDbContext db, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        var nota = await db.NoteComanda.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        var necesar = await db.NecesareMateriale.AsNoTracking().FirstOrDefaultAsync(n => n.Id == cerere.NecesarId, ct);
        if (nota is null || necesar is null)
            return Results.NotFound(new { mesaj = nota is null ? "Nota de comandă nu există." : "Necesarul nu există." });

        var r = nota.LeagaDeNecesar(necesar, uid, utilizator.Nume, ceas.Acum);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.CorectiiNoteComanda.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Ok(EndpointuriNoteComanda.LaDto(nota) with
        {
            NecesarNumar = necesar.NumarAfisat,
            Corectii = await db.CorectiiNoteComanda.CountAsync(c => c.NotaComandaId == id, ct),
        });
    }

    private static async Task<IResult> SchimbaDinUrma(
        Guid proiectId, Guid id, DinUrmaCerere cerere, AprovizionareDbContext db, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        var nota = await db.NoteComanda.FirstOrDefaultAsync(n => n.Id == id && n.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound(new { mesaj = "Nota de comandă nu există." });

        var r = nota.SchimbaDinUrma(cerere.DinUrma, uid, utilizator.Nume, ceas.Acum);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.CorectiiNoteComanda.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Ok(EndpointuriNoteComanda.LaDto(nota) with { Corectii = await db.CorectiiNoteComanda.CountAsync(c => c.NotaComandaId == id, ct) });
    }

    private static async Task<IResult> Modifica(
        Guid proiectId, Guid id, ModificaNotaComandaCerere cerere, AprovizionareDbContext db, IServiciuParteneri parteneri,
        IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        if (cerere.Data.Year < 2000 || cerere.Data > ceas.Azi.AddDays(30))
            return Results.BadRequest(new { mesaj = "Data notei de comandă nu este validă (cel mult 30 de zile în viitor)." });
        if (string.IsNullOrWhiteSpace(cerere.FurnizorDenumire))
            return Results.BadRequest(new { mesaj = "Alege sau scrie furnizorul." });
        if (cerere.Linii is null || cerere.Linii.Count == 0)
            return Results.BadRequest(new { mesaj = "Nota trebuie să aibă cel puțin un material." });
        if (!EndpointuriNoteComanda.Conditii(cerere.Conditii, out var conditii))
            return Results.BadRequest(new { mesaj = "Condițiile de livrare nu sunt valide." });
        var cui = string.IsNullOrWhiteSpace(cerere.FurnizorCui) ? null : cerere.FurnizorCui.Trim();
        if (cui is not null && !ValidareFiscala.EsteCuiValid(cui))
            return Results.BadRequest(new { mesaj = $"CUI-ul furnizorului „{cui}” nu este valid." });

        var nota = await db.NoteComanda.FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound();

        // Furnizorul din nomenclator (după CUI sau denumire); dacă lipsește, se adaugă, ca la emitere.
        var furnizor = await parteneri.CautaFurnizorAsync(cui, cerere.FurnizorDenumire, ct);
        if (furnizor is null)
        {
            var adaugat = await parteneri.AsiguraFurnizorAsync(cerere.FurnizorDenumire.Trim(), cui,
                platitorTva: cui?.StartsWith("RO", StringComparison.OrdinalIgnoreCase) == true, cerere.FurnizorAdresa, cerere.FurnizorTelefon, ct);
            if (adaugat.Esec)
                return Results.BadRequest(new { mesaj = adaugat.Eroare.Mesaj });
            furnizor = adaugat.Valoare;
        }

        // Prețul negociat și observațiile din necesar rămân pe liniile cu aceeași poziție și denumire.
        var vechi = nota.Linii;
        var linii = cerere.Linii.Select((l, i) =>
        {
            var v = i < vechi.Count && string.Equals(vechi[i].Denumire, l.Denumire?.Trim(), StringComparison.Ordinal) ? vechi[i] : null;
            return new LinieNotaComanda(l.Denumire ?? string.Empty, l.UnitateMasura ?? string.Empty, l.Cantitate, l.Termen, v?.PretUnitar, v?.Observatii);
        }).ToList();

        var r = nota.Corecteaza(
            new DateNotaComanda(cerere.NumarExtern, cerere.Data, nota.ProiectId, nota.ProiectCod, cerere.Lucrare, furnizor.Id, furnizor.Denumire, furnizor.Cui ?? cui,
                linii, cerere.Observatii, cerere.FurnizorAdresa, cerere.FurnizorTelefon, cerere.FurnizorEmail, conditii, nota.NecesarId),
            cerere.Motiv ?? string.Empty, uid, utilizator.Nume, ceas.Acum);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        db.CorectiiNoteComanda.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Ok(EndpointuriNoteComanda.LaDto(nota) with { Corectii = await db.CorectiiNoteComanda.CountAsync(c => c.NotaComandaId == id, ct) });
    }

    private static async Task<IResult> Sterge(
        Guid proiectId, Guid id, string? motiv, AprovizionareDbContext db, IEnumerable<IInterogareFacturiNotaComanda> facturi,
        IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        motiv = motiv?.Trim() ?? string.Empty;
        if (motiv.Length == 0)
            return Results.BadRequest(new { mesaj = "Motivul ștergerii este obligatoriu." });

        var nota = await db.NoteComanda.FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (nota is null)
            return Results.NotFound();
        if (nota.Stare != StareNotaComanda.Emisa)
            return Results.BadRequest(new { mesaj = "Se șterg doar notele de comandă emise; cele în așteptare urmează necesarul (se modifică / șterg de acolo)." });

        // Documentele care se sprijină pe notă: facturile cu materiale și avizele.
        foreach (var f in facturi)
        {
            var n = await f.FacturiAsync(id, ct);
            if (n > 0)
                return Results.Conflict(new { mesaj = $"{nota.NumarAfisat} e legată de {n} {(n == 1 ? "factură" : "facturi")}; corectează întâi factura (altă notă de comandă) sau anuleaz-o." });
        }

        var avize = await db.Avize.CountAsync(a => a.NotaComandaId == id, ct);
        if (avize > 0)
            return Results.Conflict(new { mesaj = $"{nota.NumarAfisat} e legată de {avize} {(avize == 1 ? "aviz" : "avize")} de însoțire; nu se poate șterge." });

        if (nota.RevineLaAchizitiiLaStergere)
        {
            var anulare = nota.AnuleazaComanda(motiv, uid, utilizator.Nume, ceas.Acum);
            if (anulare.Esec)
                return Results.BadRequest(new { mesaj = anulare.Eroare.Mesaj });
            db.CorectiiNoteComanda.Add(anulare.Valoare);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new StergereNotaComandaRaspuns("RevenitaLaAchizitii",
                $"Comanda {nota.NumarAfisat} a fost anulată; nota a revenit în „Necesar de achiziție”, fără furnizor."));
        }

        // Comanda desprinsă dintr-o notă încă la achiziții: materialele se întorc pe ea.
        string? efect = null;
        if (nota.NotaSursaId is { } sursaId && await db.NoteComanda.FirstOrDefaultAsync(x => x.Id == sursaId, ct) is { Stare: StareNotaComanda.Acceptata } sursa)
        {
            var inapoi = sursa.PrimesteInapoi(nota.Linii);
            if (inapoi.Esec)
                return Results.BadRequest(new { mesaj = inapoi.Eroare.Mesaj });
            efect = $"Materialele au revenit pe {sursa.NumarAfisat}, în „Necesar de achiziție”.";
        }

        var r = nota.Sterge(motiv, efect, uid, utilizator.Nume, ceas.Acum);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        db.StergeriNoteComanda.Add(r.Valoare);
        // Originalul scanat pleacă odată cu nota (altfel același fișier n-ar mai putea fi importat).
        if (nota.DocumentOriginalId is { } docId && await db.DocumenteOriginale.FirstOrDefaultAsync(d => d.Id == docId, ct) is { } original)
            db.DocumenteOriginale.Remove(original);
        db.NoteComanda.Remove(nota);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new StergereNotaComandaRaspuns("Stearsa", $"{nota.NumarAfisat} a fost ștearsă.{(efect is null ? string.Empty : " " + efect)}"));
    }

    private static async Task<IResult> Istoric(Guid proiectId, Guid id, AprovizionareDbContext db, CancellationToken ct)
    {
        if (!await db.NoteComanda.AnyAsync(x => x.Id == id && x.ProiectId == proiectId, ct))
            return Results.NotFound();
        var lista = await db.CorectiiNoteComanda.AsNoTracking().Where(c => c.NotaComandaId == id).OrderByDescending(c => c.CorectatLa).ToListAsync(ct);
        return Results.Ok(lista.Select(c => new CorectieNotaComandaDto(c.Id, c.Tip.ToString(), c.Motiv, c.Rezumat, c.CorectatDeNume, c.CorectatLa)).ToList());
    }

    private static async Task<IResult> Sterse(
        AprovizionareDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        var accesibile = (await permisiuni.PentruUtilizatorAsync(uid, ct))
            .ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);
        var q = db.StergeriNoteComanda.AsNoTracking();
        if (accesibile is not null)
        {
            var ids = accesibile.ToList();
            q = q.Where(s => ids.Contains(s.ProiectId));
        }

        var lista = await q.OrderByDescending(s => s.StearsaLa).Take(500).ToListAsync(ct);
        return Results.Ok(lista.Select(s => new StergereNotaComandaDto(s.Id, s.NotaComandaId, s.Numar, s.NumarExtern, s.Data, s.ProiectId, s.ProiectCod,
            s.FurnizorDenumire, s.Motiv, s.Efect, s.StearsaDeNume, s.StearsaLa)).ToList());
    }
}
