using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

/// <param name="Referinta">Versiunea după care se urmăresc bugetul și consumurile obiectivului (null = fără deviz).</param>
public sealed record ObiectivDto(Guid Id, string Denumire, string? Descriere, int Ordine, int Versiuni, ReferintaObiectivDto? Referinta);

public sealed record ReferintaObiectivDto(Guid VersiuneId, string Tip, int Numar, string Denumire, string Stare);

public sealed record ObiectivCerere(string Denumire, string? Descriere);

public sealed record MutareObiectivCerere(Guid ObiectivId, string? Motiv);

/// <summary>
/// Obiectivele proiectului (D-59): fiecare deviz aparține unui obiectiv, cu istoria lui de versiuni; ștergerea unei versiuni
/// (și a celei aprobate, dacă e ultima și articolele ei nu au documente) sau a unui obiectiv întreg (import greșit), mutarea
/// versiunilor între obiective. Ștergerea nu lasă documente fără articol: cheile stabile pierdute nu pot fi folosite de alte
/// module (<see cref="IUtilizariArticoleDeviz"/>).
/// </summary>
internal static class EndpointuriObiective
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Editare);

        g.MapGet("/obiective", Lista).WithName("ObiectiveDeviz").WithSummary("Obiectivele proiectului, cu versiunea de referință a devizului fiecăruia")
            .Produces<IReadOnlyList<ObiectivDto>>().CerePermisiune(vizualizare);
        g.MapPost("/obiective", Creeaza).WithName("CreeazaObiectivDeviz").WithSummary("Obiectiv nou (devizele lui se importă / creează ulterior)")
            .Produces<ObiectivDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).CerePermisiune(editare);
        g.MapPut("/obiective/{obiectivId:guid}", Modifica).WithName("ModificaObiectivDeviz").WithSummary("Redenumește obiectivul")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapDelete("/obiective/{obiectivId:guid}", StergeObiectiv).WithName("StergeObiectivDeviz")
            .WithSummary("Șterge obiectivul cu toate versiunile devizului lui (import greșit); refuzat dacă articolele au documente (consumuri)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).CerePermisiune(editare);
        g.MapPut("/{versiuneId:guid}/obiectiv", Muta).WithName("MutaDevizObiectiv")
            .WithSummary("Mută versiunea (cu toată istoria ei: versiunile din care a pornit și cele pornite din ea) la alt obiectiv")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
    }

    // ---------------------------------------------------------------- referința

    /// <summary>
    /// Versiunea de referință a unui obiectiv: bugetul de execuție aprobat, altfel contractul, actul adițional, oferta aprobată,
    /// altfel cea mai recentă ciornă importată (ciornele de lucru nu, ca șantierul să nu urmărească modificări neaprobate).
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, Guid>> ReferinteAsync(DevizeDbContext db, Guid proiectId, Guid? obiectivId, CancellationToken ct)
    {
        var versiuni = await db.Versiuni.AsNoTracking()
            .Where(v => v.ProiectId == proiectId && (obiectivId == null || v.ObiectivId == obiectivId) && v.Stare != StareVersiuneDeviz.Inlocuita
                        && !(v.Stare == StareVersiuneDeviz.Ciorna && v.VersiuneAnterioaraId != null))
            .Select(v => new { v.Id, v.ObiectivId, v.Tip, v.Stare, v.CreatLa }).ToListAsync(ct);
        return versiuni.GroupBy(v => v.ObiectivId).ToDictionary(g => g.Key, g => g
            .OrderBy(v => v.Stare == StareVersiuneDeviz.Aprobata ? 0 : 1)
            .ThenBy(v => v.Tip switch { TipVersiuneDeviz.BugetExecutie => 0, TipVersiuneDeviz.Contract => 1, TipVersiuneDeviz.ActAditional => 2, _ => 3 })
            .ThenByDescending(v => v.CreatLa)
            .First().Id);
    }

    private static async Task<IResult> Lista(Guid proiectId, DevizeDbContext db, CancellationToken ct) =>
        Results.Ok(await ObiectiveAsync(db, proiectId, ct));

    internal static async Task<IReadOnlyList<ObiectivDto>> ObiectiveAsync(DevizeDbContext db, Guid proiectId, CancellationToken ct)
    {
        var obiective = await db.Obiective.AsNoTracking().Where(o => o.ProiectId == proiectId).OrderBy(o => o.Ordine).ThenBy(o => o.CreatLa).ToListAsync(ct);
        var numar = await db.Versiuni.AsNoTracking().Where(v => v.ProiectId == proiectId).GroupBy(v => v.ObiectivId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var referinte = await ReferinteAsync(db, proiectId, null, ct);
        var ids = referinte.Values.ToList();
        var versiuni = await db.Versiuni.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Tip, v.Numar, v.Denumire, v.Stare }).ToDictionaryAsync(v => v.Id, ct);
        return obiective.Select(o => new ObiectivDto(o.Id, o.Denumire, o.Descriere, o.Ordine, numar.GetValueOrDefault(o.Id),
            referinte.TryGetValue(o.Id, out var r) && versiuni.TryGetValue(r, out var v)
                ? new ReferintaObiectivDto(v.Id, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString())
                : null)).ToList();
    }

    // ---------------------------------------------------------------- obiectivele

    internal static async Task<Rezultat<ObiectivDeviz>> CreeazaAsync(DevizeDbContext db, Guid proiectId, string denumire, string? descriere, CancellationToken ct)
    {
        if (await db.Obiective.AnyAsync(o => o.ProiectId == proiectId && o.Denumire.ToLower() == denumire.Trim().ToLower(), ct))
            return Rezultat.Esuat<ObiectivDeviz>(Eroare.Validare($"Proiectul are deja obiectivul „{denumire.Trim()}”."));
        var ordine = await db.Obiective.Where(o => o.ProiectId == proiectId).MaxAsync(o => (int?)o.Ordine, ct) + 1 ?? 0;
        var r = ObiectivDeviz.Creeaza(proiectId, denumire, descriere, ordine);
        if (r.Succes)
            db.Obiective.Add(r.Valoare);
        return r;
    }

    private static async Task<IResult> Creeaza(Guid proiectId, ObiectivCerere cerere, DevizeDbContext db, CancellationToken ct)
    {
        var r = await CreeazaAsync(db, proiectId, cerere.Denumire ?? string.Empty, cerere.Descriere, ct);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/proiecte/{proiectId}/devize/obiective/{r.Valoare.Id}",
            new ObiectivDto(r.Valoare.Id, r.Valoare.Denumire, r.Valoare.Descriere, r.Valoare.Ordine, 0, null));
    }

    private static async Task<IResult> Modifica(Guid proiectId, Guid obiectivId, ObiectivCerere cerere, DevizeDbContext db, CancellationToken ct)
    {
        var o = await db.Obiective.FirstOrDefaultAsync(x => x.Id == obiectivId && x.ProiectId == proiectId, ct);
        if (o is null)
            return Results.NotFound();
        if (o.Modifica(cerere.Denumire ?? string.Empty, cerere.Descriere) is { Esec: true } r)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> StergeObiectiv(
        Guid proiectId, Guid obiectivId, string? motiv, DevizeDbContext db, IEnumerable<IUtilizariArticoleDeviz> utilizari, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        var o = await db.Obiective.FirstOrDefaultAsync(x => x.Id == obiectivId && x.ProiectId == proiectId, ct);
        if (o is null)
            return Results.NotFound();
        var versiuni = await db.Versiuni.Include(v => v.Capitole).ThenInclude(c => c.Articole).AsSplitQuery()
            .Where(v => v.ObiectivId == obiectivId && v.ProiectId == proiectId).ToListAsync(ct);
        if (versiuni.Any(v => v.Stare != StareVersiuneDeviz.Ciorna))
        {
            if (await RefuzAprobareAsync(proiectId, permisiuni, utilizator, firma, ct) is { } refuz)
                return refuz;
            if (string.IsNullOrWhiteSpace(motiv))
                return Results.BadRequest(new { mesaj = "Obiectivul are versiuni aprobate: scrie motivul ștergerii (rămâne în jurnal)." });
        }

        if (await RefuzUtilizariAsync(db, proiectId, versiuni, utilizari, ct) is { } folosit)
            return folosit;
        var ids = versiuni.Select(v => v.Id).ToList();
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await db.Inlocuiri.Where(i => ids.Contains(i.VersiuneNouaId) || ids.Contains(i.VersiuneInlocuitaId)).ExecuteDeleteAsync(ct);
        await db.Reveniri.Where(i => ids.Contains(i.VersiuneReadusaId)).ExecuteDeleteAsync(ct);
        await db.Mutari.Where(i => ids.Contains(i.VersiuneId)).ExecuteDeleteAsync(ct);
        // Copiile întâi (versiunile pornite din altele), ca nicio versiune să nu rămână fără cea din care a pornit.
        foreach (var v in versiuni.OrderByDescending(v => v.Numar))
            db.Versiuni.Remove(v);
        db.Obiective.Remove(o);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- ștergerea unei versiuni

    /// <summary>
    /// Șterge versiunea (D-59), oricare din istoria devizului: ciorna oricând; versiunea importată, aprobată sau înlocuită cu motiv
    /// și drept de aprobare. Ciornele de lucru pornite din ea se șterg odată cu ea (modificări neaprobate ale ei). Versiunile
    /// blocate pornite din ea se leagă de versiunea dinaintea ei, iar lanțul înlocuirilor se reface: dacă era în vigoare, versiunea
    /// pe care o înlocuise revine (<see cref="RevenireVersiuneDeviz"/>); dacă era în istoric, versiunea mai nouă înlocuiește direct
    /// pe cea mai veche. Refuzată dacă articolele ei, care nu mai rămân în alt deviz, au documente. Istoricul:
    /// <see cref="StergereVersiuneDeviz"/>.
    /// </summary>
    internal static async Task<IResult> StergeVersiune(
        Guid proiectId, Guid versiuneId, string? motiv, DevizeDbContext db, IEnumerable<IUtilizariArticoleDeviz> utilizari, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        var v = await db.Versiuni.Include(x => x.Capitole).ThenInclude(c => c.Articole).AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == versiuneId && x.ProiectId == proiectId, ct);
        if (v is null)
            return Results.NotFound();
        var idsCiorne = await db.Versiuni.Where(x => x.ProiectId == proiectId && x.VersiuneAnterioaraId == v.Id && x.Stare == StareVersiuneDeviz.Ciorna)
            .Select(x => x.Id).ToListAsync(ct);
        var ciorne = await db.Versiuni.Include(x => x.Capitole).ThenInclude(c => c.Articole).AsSplitQuery()
            .Where(x => idsCiorne.Contains(x.Id)).ToListAsync(ct);
        var copii = await db.Versiuni.Where(x => x.ProiectId == proiectId && x.VersiuneAnterioaraId == v.Id && x.Stare != StareVersiuneDeviz.Ciorna)
            .ToListAsync(ct);
        if (v.EsteBlocat || copii.Count > 0)
        {
            if (await RefuzAprobareAsync(proiectId, permisiuni, utilizator, firma, ct) is { } refuz)
                return refuz;
            if (string.IsNullOrWhiteSpace(motiv))
                return Results.BadRequest(new { mesaj = "Versiunea e aprobată sau face parte din istoria devizului: scrie motivul ștergerii (rămâne în istoric)." });
        }

        if (await RefuzUtilizariAsync(db, proiectId, [v, .. ciorne], utilizari, ct) is { } folosit)
            return folosit;

        var motivFinal = string.IsNullOrWhiteSpace(motiv) ? "ștergerea ciornei" : motiv.Trim();
        var eticheta = $"{Eticheta(v.Tip, v.Numar)} — {v.Denumire}";
        var cine = utilizator.Nume ?? string.Empty;
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);

        // Lanțul înlocuirilor: O ← v (v o înlocuise pe O), v ← N (N a înlocuit-o pe v).
        var inlocuireDeV = await db.Inlocuiri.Where(i => i.VersiuneNouaId == v.Id).OrderByDescending(i => i.La).FirstOrDefaultAsync(ct);
        var inlocuireaLuiV = await db.Inlocuiri.Where(i => i.VersiuneInlocuitaId == v.Id).OrderByDescending(i => i.La).FirstOrDefaultAsync(ct);
        var veche = inlocuireDeV is null ? null
            : await db.Versiuni.FirstOrDefaultAsync(x => x.Id == inlocuireDeV.VersiuneInlocuitaId && x.ProiectId == proiectId, ct);
        if (inlocuireaLuiV is not null)
        {
            // v era în istoric: versiunea mai nouă o înlocuiește direct pe cea mai veche (care rămâne în istoric).
            db.Inlocuiri.Remove(inlocuireaLuiV);
            if (inlocuireDeV is not null)
            {
                db.Inlocuiri.Remove(inlocuireDeV);
                if (veche is not null)
                    db.Inlocuiri.Add(new InlocuireVersiuneDeviz(veche.Id, inlocuireaLuiV.VersiuneNouaId, inlocuireaLuiV.La, inlocuireDeV.StareAnterioara));
            }
        }
        else if (inlocuireDeV is not null)
        {
            // v era în vigoare: versiunea pe care o înlocuise revine, cu starea dinaintea înlocuirii.
            if (veche is { Stare: StareVersiuneDeviz.Inlocuita })
            {
                veche.Reactiveaza(inlocuireDeV.StareAnterioara);
                db.Reveniri.Add(new RevenireVersiuneDeviz(veche.Id, v.Id, eticheta, inlocuireDeV.StareAnterioara,
                    string.IsNullOrWhiteSpace(motiv) ? "ștergerea versiunii care o înlocuise" : motiv.Trim(), cine, ceas.Acum));
            }

            db.Inlocuiri.Remove(inlocuireDeV);
        }

        // Versiunile blocate pornite din v se leagă de versiunea dinaintea ei.
        foreach (var c in copii)
        {
            c.RelegaDe(v.VersiuneAnterioaraId);
            db.Stergeri.Add(new StergereVersiuneDeviz(proiectId, v.ObiectivId, v.Id, eticheta, v.Stare, c.Id, motivFinal, cine, ceas.Acum));
        }

        if (copii.Count == 0)
            db.Stergeri.Add(new StergereVersiuneDeviz(proiectId, v.ObiectivId, v.Id, eticheta, v.Stare, null, motivFinal, cine, ceas.Acum));
        foreach (var c in ciorne)
            db.Versiuni.Remove(c);
        db.Versiuni.Remove(v);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- mutarea

    private static async Task<IResult> Muta(
        Guid proiectId, Guid versiuneId, MutareObiectivCerere cerere, DevizeDbContext db, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        var obiectiv = await db.Obiective.AsNoTracking().FirstOrDefaultAsync(o => o.Id == cerere.ObiectivId && o.ProiectId == proiectId, ct);
        if (obiectiv is null)
            return Results.NotFound(new { mesaj = "Obiectivul nu există în proiect." });
        var toate = await db.Versiuni.Where(v => v.ProiectId == proiectId).ToListAsync(ct);
        var v0 = toate.FirstOrDefault(v => v.Id == versiuneId);
        if (v0 is null)
            return Results.NotFound();
        if (v0.ObiectivId == obiectiv.Id)
            return Results.NoContent();

        // Istoria versiunii: tot ce e legat prin „pornită din”, în ambele sensuri.
        var istorie = new HashSet<Guid> { v0.Id };
        for (var schimbat = true; schimbat;)
        {
            schimbat = false;
            foreach (var v in toate)
            {
                if (!istorie.Contains(v.Id) && v.VersiuneAnterioaraId is { } a && istorie.Contains(a))
                    schimbat |= istorie.Add(v.Id);
                if (istorie.Contains(v.Id) && v.VersiuneAnterioaraId is { } b && !istorie.Contains(b))
                    schimbat |= istorie.Add(b);
            }
        }

        var motiv = string.IsNullOrWhiteSpace(cerere.Motiv) ? $"mutat la obiectivul „{obiectiv.Denumire}”" : cerere.Motiv.Trim();
        foreach (var v in toate.Where(v => istorie.Contains(v.Id)))
        {
            db.Mutari.Add(new MutareObiectivDeviz(v.Id, v.ObiectivId, obiectiv.Id, motiv, utilizator.Nume ?? string.Empty, ceas.Acum));
            v.SeteazaObiectiv(obiectiv.Id);
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- ajutătoare

    private static string Eticheta(TipVersiuneDeviz tip, int numar) => tip switch
    {
        TipVersiuneDeviz.Oferta => "oferta",
        TipVersiuneDeviz.Contract => "contractul",
        TipVersiuneDeviz.BugetExecutie => "bugetul de execuție",
        _ => "actul adițional",
    } + $" nr. {numar}";

    private static async Task<IResult?> RefuzAprobareAsync(Guid proiectId, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma,
        CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        return evaluator.Are(Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Aprobare), new Domeniu(fid, proiectId))
            ? null
            : Results.Json(new { mesaj = "Ștergerea unui deviz aprobat cere dreptul de aprobare a devizelor." }, statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Articolele care dispar din proiect odată cu versiunile șterse (cheia nu mai e în nicio altă versiune) nu pot avea documente
    /// (ex. bonuri de consum): altfel acestea ar rămâne fără articol.
    /// </summary>
    private static async Task<IResult?> RefuzUtilizariAsync(
        DevizeDbContext db, Guid proiectId, IReadOnlyList<VersiuneDeviz> sterse, IEnumerable<IUtilizariArticoleDeviz> utilizari, CancellationToken ct)
    {
        var ids = sterse.Select(v => v.Id).ToList();
        var chei = sterse.SelectMany(v => v.Capitole.SelectMany(c => c.Articole)).Select(a => a.CheieStabila).ToHashSet();
        if (chei.Count == 0)
            return null;
        var raman = (await (from a in db.Articole.AsNoTracking()
                            join c in db.Capitole.AsNoTracking() on a.CapitolId equals c.Id
                            join v in db.Versiuni.AsNoTracking() on c.VersiuneId equals v.Id
                            where v.ProiectId == proiectId && !ids.Contains(v.Id)
                            select a.CheieStabila).Distinct().ToListAsync(ct)).ToHashSet();
        var pierdute = chei.Where(k => !raman.Contains(k)).ToList();
        foreach (var u in utilizari)
        {
            var folosite = await u.FolositeAsync(proiectId, pierdute, ct);
            if (folosite.Count > 0)
                return Results.Conflict(new
                {
                    mesaj = $"{folosite.Count} articole ale devizului au documente (ex. {string.Join(", ", folosite.Values.Distinct().Take(3))}): " +
                            "devizul nu se poate șterge. Corectează întâi documentele sau importă o versiune nouă peste el.",
                });
        }

        return null;
    }
}
