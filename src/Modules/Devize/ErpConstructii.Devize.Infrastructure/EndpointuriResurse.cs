using ErpConstructii.Devize.Calcul;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

/// <param name="Utilizari">În câte consumuri (resurse ale articolelor) apare; <paramref name="Versiuni"/> = în câte versiuni de deviz.</param>
public sealed record ResursaCatalogDto(
    Guid Id, string Sursa, string Cod, string Denumire, string UnitateMasura, string Tip, bool TipDedus, bool Mapata, bool FaraMapare,
    Guid? ArticolStocId, string? ArticolStocDenumire, string? Meserie, string? Categorie, string? Utilaj, string? MapataDe,
    DateTimeOffset? MapataLa, int Utilizari, int Versiuni);

/// <param name="Tip">Tipul resursei (confirmă / corectează tipul dedus): Material, Manopera, Utilaj, Transport, Echipament.</param>
public sealed record MapareResursaCerere(
    string? Tip, Guid? ArticolStocId, string? Meserie, string? Categorie, string? Utilaj, bool FaraMapare = false);

/// <summary>
/// Nomenclatorul de resurse al firmei (docs/import-isdp.md §6.3, REQ-IMP-04): lista (nemapate / mapate), maparea spre ERP
/// și propunerile de articole de stoc pentru materiale. Resursele noi le creează importul (<see cref="CatalogResurse"/>).
/// </summary>
internal static class EndpointuriResurse
{
    public static void Mapeaza(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/devize/resurse").WithTags("Devize");
        var vizualizare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Editare);

        g.MapGet("/", Lista).WithName("ResurseCatalog")
            .WithSummary("Nomenclatorul de resurse al firmei: stare = nemapate | mapate | toate; tip; căutare după cod / denumire")
            .Produces<IReadOnlyList<ResursaCatalogDto>>().CerePermisiune(vizualizare);
        g.MapPut("/{id:guid}/mapare", Mapare).WithName("MapeazaResursa")
            .WithSummary("Leagă resursa de ERP: articol de stoc (material / echipament), meserie (manoperă), utilaj / transport, sau „fără corespondent”")
            .Produces<ResursaCatalogDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare);
        g.MapGet("/{id:guid}/propuneri", Propuneri).WithName("PropuneriArticolStoc")
            .WithSummary("Articolele de stoc cu denumirea cea mai apropiată de a resursei (pentru maparea materialelor)")
            .Produces<IReadOnlyList<ArticolStocMapare>>().Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
    }

    /// <summary>
    /// Leagă consumurile versiunii de nomenclator și adaugă resursele noi (nemapate). Apelat la confirmarea importului,
    /// înainte de salvare; întoarce ce e de verificat (UM / tip diferite de nomenclator).
    /// </summary>
    internal static async Task<IReadOnlyList<ProblemaImport>> LeagaDeNomenclatorAsync(DevizeDbContext db, string sursa, VersiuneDeviz versiune, CancellationToken ct)
    {
        var consumuri = versiune.Articole().SelectMany(a => a.Consumuri).ToList();
        var coduri = consumuri.Select(c => ResursaCatalog.CheieCod(c.CodResursa)).Distinct().ToList();
        var s = sursa.Trim().ToLowerInvariant();
        var existente = await db.Resurse.Where(r => r.Sursa == s && coduri.Contains(r.Cod)).ToListAsync(ct);
        var potrivire = CatalogResurse.Potriveste(s, consumuri, existente);
        db.Resurse.AddRange(potrivire.Noi);
        return potrivire.Probleme;
    }

    private static async Task<IResult> Lista(string? stare, string? tip, string? cauta, DevizeDbContext db, CancellationToken ct)
    {
        var q = db.Resurse.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(tip))
        {
            if (!Enum.TryParse<TipResursa>(tip, ignoreCase: true, out var t))
                return Results.BadRequest(new { mesaj = "Tipul resursei nu e valid (Material, Manopera, Utilaj, Transport, Echipament)." });
            q = q.Where(r => r.Tip == t);
        }

        if (!string.IsNullOrWhiteSpace(cauta))
        {
            var text = $"%{cauta.Trim()}%";
            q = q.Where(r => EF.Functions.ILike(r.Cod, text) || EF.Functions.ILike(r.Denumire, text));
        }

        q = stare?.Trim().ToLowerInvariant() switch
        {
            "nemapate" => q.Where(r => !r.FaraMapare && r.ArticolStocId == null && r.Meserie == null && r.Utilaj == null),
            "mapate" => q.Where(r => r.FaraMapare || r.ArticolStocId != null || r.Meserie != null || r.Utilaj != null),
            _ => q,
        };

        var resurse = await q.OrderBy(r => r.Tip).ThenBy(r => r.Denumire).Take(2000).ToListAsync(ct);
        var ids = resurse.Select(r => r.Id).ToList();
        var utilizari = await (from c in db.Consumuri.AsNoTracking()
                               join a in db.Articole.AsNoTracking() on c.ArticolId equals a.Id
                               join cap in db.Capitole.AsNoTracking() on a.CapitolId equals cap.Id
                               where c.ResursaCatalogId != null && ids.Contains(c.ResursaCatalogId.Value)
                               select new { Resursa = c.ResursaCatalogId!.Value, cap.VersiuneId })
            .ToListAsync(ct);
        var peResursa = utilizari.GroupBy(u => u.Resursa).ToDictionary(g => g.Key, g => (Consumuri: g.Count(), Versiuni: g.Select(x => x.VersiuneId).Distinct().Count()));
        return Results.Ok(resurse.Select(r => Dto(r, peResursa.GetValueOrDefault(r.Id))).ToList());
    }

    private static async Task<IResult> Mapare(
        Guid id, MapareResursaCerere cerere, DevizeDbContext db, IEnumerable<IArticoleStocPentruMapare> stoc, IUtilizatorCurent utilizator, ICeas ceas,
        CancellationToken ct)
    {
        var r = await db.Resurse.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return Results.NotFound();
        var tip = r.Tip;
        if (!string.IsNullOrWhiteSpace(cerere.Tip) && !Enum.TryParse(cerere.Tip, ignoreCase: true, out tip))
            return Results.BadRequest(new { mesaj = "Tipul resursei nu e valid (Material, Manopera, Utilaj, Transport, Echipament)." });

        string? articolDenumire = null;
        if (cerere.ArticolStocId is { } articolId && !cerere.FaraMapare)
        {
            if (stoc.FirstOrDefault() is not { } interogare)
                return Results.BadRequest(new { mesaj = "Modulul Stocuri nu e disponibil: marchează resursa „fără corespondent”." });
            if (await interogare.ArticolAsync(articolId, ct) is not { } articol)
                return Results.BadRequest(new { mesaj = "Articolul de stoc ales nu există." });
            articolDenumire = $"{articol.Cod} — {articol.Denumire}";
        }

        var rezultat = r.Mapeaza(new MapareResursa(tip, cerere.ArticolStocId, articolDenumire, cerere.Meserie, cerere.Categorie, cerere.Utilaj, cerere.FaraMapare),
            utilizator.Nume ?? string.Empty, ceas.Acum);
        if (rezultat.Esec)
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        var u = await db.Consumuri.CountAsync(c => c.ResursaCatalogId == id, ct);
        return Results.Ok(Dto(r, (u, 0)));
    }

    private static async Task<IResult> Propuneri(Guid id, DevizeDbContext db, IEnumerable<IArticoleStocPentruMapare> stoc, CancellationToken ct)
    {
        var r = await db.Resurse.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return Results.NotFound();
        return Results.Ok(stoc.FirstOrDefault() is { } interogare
            ? await interogare.PropuneAsync(r.Denumire, r.UnitateMasura, 10, ct)
            : []);
    }

    private static ResursaCatalogDto Dto(ResursaCatalog r, (int Consumuri, int Versiuni) u) => new(
        r.Id, r.Sursa, r.Cod, r.Denumire, r.UnitateMasura, r.Tip.ToString(), r.TipDedus, r.EsteMapata, r.FaraMapare,
        r.ArticolStocId, r.ArticolStocDenumire, r.Meserie, r.Categorie, r.Utilaj, r.MapataDeNume, r.MapataLa, u.Consumuri, u.Versiuni);
}
