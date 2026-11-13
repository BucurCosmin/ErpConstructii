using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ErpConstructii.Devize.Infrastructure;

public sealed record PretResursaCerere(string Cod, decimal Pret);

/// <summary>Prețurile alese din raport (cod din nomenclator → preț nou, în UM-ul resursei).</summary>
public sealed record AplicarePreturiCerere(IReadOnlyList<PretResursaCerere> Preturi);

public sealed record RezultatAplicarePreturi(Guid VersiuneId, int Numar, bool VersiuneNoua, int ArticoleModificate);

/// <summary>
/// „Actualizează prețurile” (D-55, etapa 3): diferențele dintre prețurile resurselor din deviz și prețurile de achiziție
/// (recepțiile = prețul facturilor, prin <see cref="ISursaPreturi"/>), apoi aplicarea prețurilor alese în ciorna de lucru
/// (versiunea importată / aprobată rămâne neschimbată, D-53). Calculul: <see cref="ActualizarePreturi"/> (pur).
/// </summary>
internal static class EndpointuriActualizarePreturi
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        g.MapGet("/{versiuneId:guid}/preturi-achizitie", Raport).WithName("DiferentePreturiAchizitie")
            .WithSummary("Prețurile resurselor din deviz față de prețurile de achiziție: ?politica=MedieInterval|Ultimul|MinimInterval, ?luni=6")
            .Produces<RaportActualizarePreturi>().Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare)).CerePermisiune(Permisiuni.PreturiCost);
        g.MapPost("/{versiuneId:guid}/preturi-achizitie", Aplica).WithName("AplicaPreturiAchizitie")
            .WithSummary("Aplică prețurile alese în ciorna de lucru (creată la nevoie); articolele primesc diferența pe componenta resursei")
            .Produces<RezultatAplicarePreturi>().Produces(StatusCodes.Status400BadRequest)
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Editare)).CerePermisiune(Permisiuni.PreturiCost);
    }

    private static async Task<IResult> Raport(
        Guid proiectId, Guid versiuneId, string? politica, int? luni, DevizeDbContext db, IEnumerable<ISursaPreturi> surse, ICeas ceas, CancellationToken ct)
    {
        var v = await ModulDevize.IncarcaAsync(db, versiuneId, ct);
        if (v is null || v.ProiectId != proiectId)
            return Results.NotFound();
        if (!Enum.TryParse<PoliticaPret>(politica ?? nameof(PoliticaPret.MedieInterval), true, out var p))
            return Results.BadRequest(new { mesaj = "Politica nu e validă (MedieInterval, Ultimul, MinimInterval)." });

        // Resursele legate de materiale din stoc și achizițiile lor, aduse în UM-ul resursei.
        var legate = await EndpointuriCatalogPreturi.ResurseLegateAsync(db, null, ct);
        var coduriDeviz = v.Articole().SelectMany(a => a.Consumuri).Select(c => ResursaCatalog.CheieCod(c.CodResursa)).ToHashSet();
        legate = legate.Where(x => coduriDeviz.Contains(x.Resursa.Cod)).ToList();
        var preturi = (await EndpointuriCatalogPreturi.PreturiAsync(surse, legate.Select(x => x.Resursa.ArticolStocId!.Value).Distinct().ToList(), ct))
            .ToLookup(x => x.ArticolStocId);
        var azi = DateOnly.FromDateTime(ceas.Acum.DateTime);
        var achizitii = new Dictionary<string, StatisticaAchizitie>();
        foreach (var (r, factor) in legate)
        {
            if (CatalogPreturi.Achizitii(r.Cod, r.UnitateMasura, factor, preturi[r.ArticolStocId!.Value], azi, Math.Clamp(luni ?? 6, 1, 60)) is { } s)
                achizitii[r.Cod] = s;
        }

        return Results.Ok(ActualizarePreturi.Propune(v, achizitii, legate.Select(x => x.Resursa.Cod).ToHashSet(), p));
    }

    private static async Task<IResult> Aplica(Guid proiectId, Guid versiuneId, AplicarePreturiCerere cerere, DevizeDbContext db, CancellationToken ct)
    {
        if (cerere.Preturi is not { Count: > 0 })
            return Results.BadRequest(new { mesaj = "Alege cel puțin o resursă." });
        var preturi = cerere.Preturi.GroupBy(x => ResursaCatalog.CheieCod(x.Cod)).ToDictionary(g => g.Key, g => g.Last().Pret);

        // Fără niciun preț efectiv nou nu se creează ciornă de lucru degeaba.
        var v = await ModulDevize.IncarcaAsync(db, versiuneId, ct);
        if (v is null || v.ProiectId != proiectId)
            return Results.NotFound();
        if (!v.Articole().SelectMany(a => a.Consumuri)
                .Any(c => preturi.TryGetValue(ResursaCatalog.CheieCod(c.CodResursa), out var pn) && pn != c.PretUnitar))
            return Results.BadRequest(new { mesaj = "Prețurile alese sunt deja cele din deviz (sau resursele nu sunt în deviz): nimic de aplicat." });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        var lucru = await EndpointuriEditareDeviz.CiornaDeLucruAsync(db, proiectId, versiuneId, ct);
        if (lucru.Esec)
            return EndpointuriEditareDeviz.Refuz(lucru.Eroare);
        var r = lucru.Valoare.Ciorna.ActualizeazaPreturiResurse(preturi);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        var c = lucru.Valoare.Ciorna;
        return Results.Ok(new RezultatAplicarePreturi(c.Id, c.Numar, lucru.Valoare.Noua, r.Valoare));
    }
}
