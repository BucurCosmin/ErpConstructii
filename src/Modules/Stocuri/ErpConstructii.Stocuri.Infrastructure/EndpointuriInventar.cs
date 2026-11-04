using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

public sealed record StornoCerere(DateOnly Data, string Motiv);

public sealed record DeschideInventarCerere(Guid GestiuneId, DateOnly Data, string? Comisie);

public sealed record NumarareCerere(IReadOnlyList<NumarareLinie> Linii);

/// <param name="ValoareScriptica">Null fără dreptul „prețuri de cost”.</param>
public sealed record LinieInventarDto(Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal CantitateScriptica, decimal? ValoareScriptica,
    decimal? CantitateFaptica, decimal? Diferenta, decimal? PretUnitarPlus, decimal? ValoareDiferenta);

public sealed record InventarDto(Guid Id, string Numar, DateOnly Data, Guid GestiuneId, string Gestiune, string Stare, string? Comisie,
    IReadOnlyList<LinieInventarDto> Linii, int Numarate, decimal? ValoarePlusuri, decimal? ValoareMinusuri, string DeschisDe, string? FinalizatDe);

/// <summary>
/// Stornarea NIR-urilor și inventarul gestiunilor. Finalizarea inventarului (care înregistrează plusurile și minusurile în
/// stoc) cere dreptul de aprobare; numărătoarea, dreptul de editare.
/// </summary>
internal static class EndpointuriInventar
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Editare);
        var anulare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Anulare);
        var aprobare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Aprobare);

        g.MapPost("/nir/{id:guid}/storno", Storneaza).WithName("StorneazaNir")
            .WithSummary("Stornează un NIR: materialele recepționate ies din depozit la valoarea de intrare")
            .Produces<NirDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(anulare);

        g.MapGet("/inventare", ListaInventare).WithName("ListaInventare").WithSummary("Inventarele gestiunilor").Produces<IReadOnlyList<InventarDto>>()
            .CerePermisiune(vizualizare);
        g.MapGet("/inventare/{id:guid}", DetaliiInventar).WithName("DetaliiInventar").WithSummary("Un inventar, cu liniile lui").Produces<InventarDto>()
            .Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapPost("/inventare", Deschide).WithName("DeschideInventar")
            .WithSummary("Deschide inventarul unei gestiuni: preia stocul scriptic și blochează mișcările până la finalizare")
            .Produces<InventarDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict).CerePermisiune(editare);
        g.MapPut("/inventare/{id:guid}/numarare", Numara).WithName("NumaraInventar").WithSummary("Cantitățile numărate de comisie")
            .Produces<InventarDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapPost("/inventare/{id:guid}/finalizare", Finalizeaza).WithName("FinalizeazaInventar")
            .WithSummary("Finalizează inventarul: plusurile intră și minusurile ies din stoc, la CMP")
            .Produces<InventarDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(aprobare);
        g.MapDelete("/inventare/{id:guid}", Renunta).WithName("RenuntaInventar").WithSummary("Renunță la un inventar deschis (deblochează gestiunea)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).CerePermisiune(anulare);
    }

    // ---------------------------------------------------------------- storno NIR

    private static async Task<IResult> Storneaza(
        Guid id, StornoCerere cerere, StocuriDbContext db, IServiciuPermisiuni permisiuni, IFirmaCurenta firma, IUtilizatorCurent utilizator, ICeas ceas,
        CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        if (cerere.Data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data stornării nu poate fi în viitor." });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var nir = await db.Nir.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nir is null)
            return Results.NotFound();
        if (await db.StornariNir.AnyAsync(s => s.NirId == id, ct))
            return Results.Conflict(new { mesaj = $"{nir.NumarAfisat} a fost deja stornat." });
        if (await ModulStocuri.InventarDeschisAsync(db, ct, nir.GestiuneId) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });

        var r = StornoNir.Emite(nir, cerere.Data, cerere.Motiv ?? string.Empty, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        var storno = r.Valoare;

        // Liniile aceluiași material se stornează pe rând din același sold.
        var solduri = new Dictionary<Guid, (SoldStoc Sold, Sold Curent)>();
        var miscari = new List<MiscareStoc>();
        foreach (var l in storno.Linii)
        {
            if (!solduri.TryGetValue(l.ArticolId, out var s))
            {
                var sold = await ModulStocuri.SoldAsync(db, nir.GestiuneId, l.ArticolId, ct);
                s = (sold, sold.Sold);
            }

            var nou = CalculStoc.Storno(s.Curent, l.Cantitate, l.Valoare, l.Denumire);
            if (nou.Esec)
                return Results.Conflict(new { mesaj = nou.Eroare.Mesaj });
            miscari.Add(new MiscareStoc(nir.GestiuneId, l.ArticolId, storno.Data, TipMiscare.IesireStornoNir, -l.Cantitate,
                -(s.Curent.Valoare - nou.Valoare.Valoare), storno.Id, $"STORNO {nir.NumarAfisat}"));
            solduri[l.ArticolId] = (s.Sold, nou.Valoare);
        }

        foreach (var (sold, curent) in solduri.Values)
            sold.Seteaza(curent);
        db.StornariNir.Add(storno);
        db.Miscari.AddRange(miscari);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);

        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        return Results.Ok(ModulStocuri.Dto(nir, preturi) with { Storno = new StornoDto(storno.Data, storno.Motiv, storno.StornatDeNume) });
    }

    // ---------------------------------------------------------------- inventar

    private static async Task<IResult> ListaInventare(
        StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var lista = await db.Inventare.AsNoTracking().OrderByDescending(i => i.An).ThenByDescending(i => i.Numar).Take(200).ToListAsync(ct);
        return Results.Ok(lista.Select(i => Dto(i, preturi)).ToList());
    }

    private static async Task<IResult> DetaliiInventar(
        Guid id, StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        var inv = await db.Inventare.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return inv is null ? Results.NotFound() : Results.Ok(Dto(inv, await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct)));
    }

    private static async Task<IResult> Deschide(
        DeschideInventarCerere cerere, StocuriDbContext db, IServiciuPermisiuni permisiuni, IFirmaCurenta firma, IUtilizatorCurent utilizator, ICeas ceas,
        CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        if (cerere.Data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data inventarului nu poate fi în viitor." });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var gestiune = await db.Gestiuni.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cerere.GestiuneId, ct);
        if (gestiune is null)
            return Results.NotFound(new { mesaj = "Gestiunea nu există." });
        if (await ModulStocuri.InventarDeschisAsync(db, ct, gestiune.Id) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });

        var stoc = await (from s in db.Solduri.AsNoTracking()
                          join a in db.Articole.AsNoTracking() on s.ArticolId equals a.Id
                          where s.GestiuneId == gestiune.Id && s.Cantitate != 0
                          select new { a.Id, a.Cod, a.Denumire, a.UnitateMasura, s.Cantitate, s.Valoare }).ToListAsync(ct);
        var an = cerere.Data.Year;
        var numar = (await db.Inventare.Where(i => i.An == an).MaxAsync(i => (int?)i.Numar, ct) ?? 0) + 1;
        var r = Inventar.Deschide(an, numar, cerere.Data, gestiune,
            stoc.Select(x => (new ArticolCandidat(x.Id, x.Cod, x.Denumire, x.UnitateMasura), new Sold(x.Cantitate, x.Valoare))).ToList(),
            cerere.Comisie, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.Inventare.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created($"/api/stocuri/inventare/{r.Valoare.Id}", Dto(r.Valoare, await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct)));
    }

    private static async Task<IResult> Numara(
        Guid id, NumarareCerere cerere, StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        var inv = await db.Inventare.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null)
            return Results.NotFound();
        var linii = cerere.Linii ?? [];
        var existente = inv.Linii.Select(l => l.ArticolId).ToHashSet();
        var noi = linii.Select(l => l.ArticolId).Where(a => !existente.Contains(a)).ToList();
        var articoleNoi = await db.Articole.AsNoTracking().Where(a => noi.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new ArticolCandidat(a.Id, a.Cod, a.Denumire, a.UnitateMasura), ct);
        var r = inv.Numara(linii, articoleNoi);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.Ok(Dto(inv, await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct)));
    }

    private static async Task<IResult> Finalizeaza(
        Guid id, StocuriDbContext db, IServiciuPermisiuni permisiuni, IFirmaCurenta firma, IUtilizatorCurent utilizator, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var inv = await db.Inventare.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null)
            return Results.NotFound();

        var articole = inv.Linii.Select(l => l.ArticolId).ToList();
        var solduri = await db.Solduri.Where(s => s.GestiuneId == inv.GestiuneId && articole.Contains(s.ArticolId)).ToListAsync(ct);
        var r = inv.Finalizeaza(solduri.ToDictionary(s => s.ArticolId, s => s.Sold), uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        foreach (var (linie, soldNou) in r.Valoare)
        {
            var sold = await ModulStocuri.SoldAsync(db, inv.GestiuneId, linie.ArticolId, ct);
            sold.Seteaza(soldNou);
            var plus = linie.ValoareDiferenta >= 0;
            var cantitate = linie.CantitateFaptica!.Value - linie.CantitateScriptica;
            db.Miscari.Add(new MiscareStoc(inv.GestiuneId, linie.ArticolId, inv.Data, plus ? TipMiscare.PlusInventar : TipMiscare.MinusInventar,
                cantitate, linie.ValoareDiferenta ?? 0, inv.Id, inv.NumarAfisat));
        }

        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(Dto(inv, await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct)));
    }

    private static async Task<IResult> Renunta(Guid id, StocuriDbContext db, CancellationToken ct)
    {
        var inv = await db.Inventare.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null)
            return Results.NotFound();
        if (inv.Stare != StareInventar.Deschis)
            return Results.Conflict(new { mesaj = "Inventarul finalizat nu se mai poate șterge." });
        db.Inventare.Remove(inv);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static InventarDto Dto(Inventar i, bool preturi) => new(i.Id, i.NumarAfisat, i.Data, i.GestiuneId, i.GestiuneDenumire, i.Stare.ToString(), i.Comisie,
        i.Linii.Select(l => new LinieInventarDto(l.ArticolId, l.ArticolCod, l.Denumire, l.UnitateMasura, l.CantitateScriptica, preturi ? l.ValoareScriptica : null,
            l.CantitateFaptica, l.Diferenta, l.PretUnitarPlus, preturi ? l.ValoareDiferenta : null)).ToList(),
        i.Linii.Count(l => l.CantitateFaptica is not null),
        preturi ? i.Linii.Where(l => l.ValoareDiferenta > 0).Sum(l => l.ValoareDiferenta) : null,
        preturi ? i.Linii.Where(l => l.ValoareDiferenta < 0).Sum(l => l.ValoareDiferenta) : null,
        i.DeschisDeNume, i.FinalizatDeNume);
}
