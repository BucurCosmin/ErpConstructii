using ErpConstructii.Devize.Calcul;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

/// <summary>O componentă (resursă) a articolului, ca în extrasul de resurse: consumul pe unitate și pe tot articolul.</summary>
/// <param name="ValoareUnitara">Consum unitar × preț: contribuția resursei la prețul unitar al articolului.</param>
/// <param name="Valoare">Cantitatea totală × preț.</param>
public sealed record ConsumArticolDto(
    Guid Id, Guid ArticolId, string Cod, string Denumire, string Tip, string UnitateMasura, decimal ConsumUnitar, decimal CantitateTotala,
    decimal PretUnitar, decimal ValoareUnitara, decimal Valoare, Guid? ResursaCatalogId);

/// <summary>Versiune de deviz în catalog / comparație (din orice proiect al firmei).</summary>
/// <param name="InVigoare">Nu e înlocuită și nu e o ciornă de lucru (care repetă versiunea din care a pornit).</param>
public sealed record VersiuneCatalogDto(
    Guid Id, Guid ProiectId, string Tip, int Numar, string Denumire, string Stare, DateTimeOffset Data, bool InVigoare);

/// <summary>Un preț văzut într-un deviz (pentru istoricul unei resurse / al unui articol).</summary>
public sealed record ObservatiePretDto(
    Guid VersiuneId, Guid ProiectId, DateTimeOffset Data, string Denumire, decimal Pret, decimal Cantitate, ValoriResurse? Componente);

/// <summary>O achiziție a materialului legat de resursă, adusă în UM-ul resursei (D-55).</summary>
public sealed record AchizitieResursaDto(
    DateOnly Data, string Sursa, string Document, Guid? DocumentId, string? Furnizor, Guid? ProiectId, decimal Pret, decimal Cantitate,
    string MaterialStoc, decimal Factor);

public sealed record ComparatiePreturiDto(IReadOnlyList<VersiuneCatalogDto> Versiuni, IReadOnlyList<RandComparatiePreturi> Randuri);

/// <summary>
/// Catalogul de prețuri din devizele firmei (<c>editor-deviz.md</c> §5, sursa „DevizIsdp”; D-54): resursele (materiale,
/// manoperă, utilaje, transport) și articolele (activitățile) cu prețurile lor din fiecare deviz, plus compararea prețurilor
/// între devize. Se citește direct din devize, fără tabele proprii: prețurile din comenzi / facturi se vor adăuga separat.
/// Implicit intră doar versiunile în vigoare (ca un deviz să nu fie numărat de două ori). Prețurile zero (resurse fără preț
/// în deviz) nu intră în statistici.
/// </summary>
internal static class EndpointuriCatalogPreturi
{
    public static void Mapeaza(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/devize/catalog").WithTags("Devize");
        // Prețurile tuturor proiectelor: dreptul se cere pe toată firma, plus accesul la prețurile de cost.
        var vizualizare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare);

        RouteHandlerBuilder Cere(RouteHandlerBuilder b) => b.CerePermisiunePeFirma(vizualizare).CerePermisiunePeFirma(Permisiuni.PreturiCost);

        Cere(g.MapGet("/versiuni", Versiuni).WithName("VersiuniCatalog").WithSummary("Toate versiunile de deviz ale firmei (pentru comparație)")
            .Produces<IReadOnlyList<VersiuneCatalogDto>>());
        Cere(g.MapGet("/resurse", Resurse).WithName("CatalogResurse")
            .WithSummary("Resursele din devize (cod + UM) cu prețul minim / mediu / maxim / ultimul; cauta, tip, toate=true (și versiunile înlocuite)")
            .Produces<IReadOnlyList<StatisticaPret>>());
        Cere(g.MapGet("/resurse/preturi", PreturiResursa).WithName("PreturiResursa").WithSummary("Prețurile unei resurse în fiecare deviz")
            .Produces<IReadOnlyList<ObservatiePretDto>>());
        Cere(g.MapGet("/articole", Articole).WithName("CatalogArticole")
            .WithSummary("Articolele (activitățile) din devize (simbol + UM) cu prețul unitar minim / mediu / maxim / ultimul")
            .Produces<IReadOnlyList<StatisticaPret>>());
        Cere(g.MapGet("/articole/preturi", PreturiArticol).WithName("PreturiArticol")
            .WithSummary("Prețurile unitare ale unui articol în fiecare deviz, pe componente").Produces<IReadOnlyList<ObservatiePretDto>>());
        Cere(g.MapGet("/resurse/achizitii", Achizitii).WithName("AchizitiiResurse")
            .WithSummary("Prețurile de achiziție (recepții) ale resurselor legate de materiale din stoc, în UM-ul resursei: ultimul, media pe ultimele ?luni=6")
            .Produces<IReadOnlyList<StatisticaAchizitie>>());
        Cere(g.MapGet("/resurse/achizitii/detalii", DetaliiAchizitii).WithName("DetaliiAchizitiiResursa")
            .WithSummary("Achizițiile materialului legat de resursă (cod + UM), cu documentul și furnizorul").Produces<IReadOnlyList<AchizitieResursaDto>>());
        Cere(g.MapGet("/comparatie", Comparatie).WithName("ComparatiePreturi")
            .WithSummary("Prețurile articolelor (ce=articole) sau resurselor (ce=resurse) din versiunile date (versiuni=id1,id2,…), una lângă alta")
            .Produces<ComparatiePreturiDto>().Produces(StatusCodes.Status400BadRequest));
    }

    /// <summary>Componentele articolelor versiunii (extrasul de resurse pe articol); pe grupul versiunii, cu dreptul proiectului.</summary>
    public static void MapeazaPeVersiune(RouteGroupBuilder grup) =>
        grup.MapGet("/{versiuneId:guid}/resurse-articole", ResurseArticole).WithName("ResurseArticoleDeviz")
            .WithSummary("Resursele fiecărui articol al versiunii: cod, denumire, tip, UM, consum unitar, cantitate totală, preț, valoare")
            .Produces<IReadOnlyList<ConsumArticolDto>>().Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare));

    private static async Task<IResult> ResurseArticole(Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct)
    {
        if (!await db.Versiuni.AnyAsync(v => v.Id == versiuneId && v.ProiectId == proiectId, ct))
            return Results.NotFound();
        var consumuri = await (from c in db.Consumuri
                               join a in db.Articole on c.ArticolId equals a.Id
                               join k in db.Capitole on a.CapitolId equals k.Id
                               where k.VersiuneId == versiuneId
                               orderby c.ArticolId, c.Ordine
                               select c).AsNoTracking().ToListAsync(ct);
        return Results.Ok(consumuri.Select(c => new ConsumArticolDto(c.Id, c.ArticolId, c.CodResursa, c.Denumire, c.Tip.ToString(), c.UnitateMasura,
            c.ConsumUnitar, c.CantitateTotala, c.PretUnitar, c.ConsumUnitar * c.PretUnitar, c.CantitateTotala * c.PretUnitar, c.ResursaCatalogId)).ToList());
    }

    /// <summary>Resursele legate de un material din stoc, cu factorul de conversie (dat sau propus); fără factor nu se pot converti.</summary>
    internal static async Task<List<(ResursaCatalog Resursa, decimal Factor)>> ResurseLegateAsync(DevizeDbContext db, string? cod, CancellationToken ct)
    {
        var q = db.Resurse.AsNoTracking().Where(r => r.ArticolStocId != null && !r.FaraMapare);
        if (cod is not null)
            q = q.Where(r => r.Cod == cod);
        var resurse = await q.ToListAsync(ct);
        return resurse
            .Select(r => (Resursa: r, Factor: r.FactorConversie ?? ConversieUm.Propune(r.UnitateMasura, r.ArticolStocUnitateMasura)))
            .Where(x => x.Factor is > 0)
            .Select(x => (x.Resursa, x.Factor!.Value))
            .ToList();
    }

    internal static async Task<IReadOnlyList<PretAchizitie>> PreturiAsync(IEnumerable<ISursaPreturi> surse, IReadOnlyCollection<Guid> articole, CancellationToken ct)
    {
        var toate = new List<PretAchizitie>();
        foreach (var s in surse)
            toate.AddRange(await s.PreturiAsync(articole, null, ct));
        return toate;
    }

    private static async Task<IResult> Achizitii(int? luni, DevizeDbContext db, IEnumerable<ISursaPreturi> surse, ICeas ceas, CancellationToken ct)
    {
        var legate = await ResurseLegateAsync(db, null, ct);
        var preturi = (await PreturiAsync(surse, legate.Select(x => x.Resursa.ArticolStocId!.Value).Distinct().ToList(), ct))
            .ToLookup(p => p.ArticolStocId);
        var azi = DateOnly.FromDateTime(ceas.Acum.DateTime);
        return Results.Ok(legate
            .Select(x => CatalogPreturi.Achizitii(x.Resursa.Cod, x.Resursa.UnitateMasura, x.Factor, preturi[x.Resursa.ArticolStocId!.Value], azi, Math.Clamp(luni ?? 6, 1, 60)))
            .Where(s => s is not null)
            .ToList());
    }

    private static async Task<IResult> DetaliiAchizitii(string cod, string um, DevizeDbContext db, IEnumerable<ISursaPreturi> surse, CancellationToken ct)
    {
        var cheie = CatalogPreturi.Cheie(cod, um);
        var legate = (await ResurseLegateAsync(db, ResursaCatalog.CheieCod(cod), ct))
            .Where(x => CatalogPreturi.Cheie(x.Resursa.Cod, x.Resursa.UnitateMasura) == cheie).ToList();
        if (legate.Count == 0)
            return Results.Ok(Array.Empty<AchizitieResursaDto>());
        var (r, factor) = legate[0];
        var preturi = await PreturiAsync(surse, [r.ArticolStocId!.Value], ct);
        return Results.Ok(preturi.Where(p => p.PretUnitar > 0).OrderByDescending(p => p.Data)
            .Select(p => new AchizitieResursaDto(p.Data, p.Sursa.ToString(), p.Document, p.DocumentId, p.Furnizor, p.ProiectId,
                CatalogPreturi.RotunjirePret.Aplica(p.PretUnitar * factor), p.Cantitate / factor, r.ArticolStocDenumire ?? string.Empty, factor))
            .ToList());
    }

    private static async Task<IResult> Versiuni(DevizeDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Versiuni.AsNoTracking().OrderByDescending(v => v.CreatLa)
            .Select(v => new VersiuneCatalogDto(v.Id, v.ProiectId, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString(), v.CreatLa,
                v.Stare != StareVersiuneDeviz.Inlocuita && !(v.Stare == StareVersiuneDeviz.Ciorna && v.VersiuneAnterioaraId != null)))
            .ToListAsync(ct));

    /// <summary>Versiunile luate în calcul: cele în vigoare, sau toate.</summary>
    private static IQueryable<VersiuneDeviz> VersiuniIncluse(DevizeDbContext db, bool toate) =>
        toate ? db.Versiuni : db.Versiuni.Where(v => v.Stare != StareVersiuneDeviz.Inlocuita
                                                   && !(v.Stare == StareVersiuneDeviz.Ciorna && v.VersiuneAnterioaraId != null));

    /// <summary>Consumurile cu preț din versiunile date; filtrele se aplică pe tabel, înainte de proiecție (traductibile în SQL).</summary>
    private static IQueryable<ObservatiePret> ObservatiiResurse(
        DevizeDbContext db, IQueryable<VersiuneDeviz> versiuni, string? tipar = null, TipResursa? tip = null, string? cod = null) =>
        from c in db.Consumuri.Where(c => (tipar == null || EF.Functions.ILike(c.CodResursa, tipar) || EF.Functions.ILike(c.Denumire, tipar))
                                          && (tip == null || c.Tip == tip)
                                          && (cod == null || c.CodResursa.ToUpper() == cod))
        join a in db.Articole on c.ArticolId equals a.Id
        join cap in db.Capitole on a.CapitolId equals cap.Id
        join v in versiuni on cap.VersiuneId equals v.Id
        where c.PretUnitar > 0
        select new ObservatiePret(c.CodResursa, c.Denumire, c.UnitateMasura, c.Tip.ToString(), c.PretUnitar, c.CantitateTotala, v.Id, v.ProiectId, v.CreatLa, null);

    private sealed record RandArticol(string Simbol, string Denumire, string Um, decimal Cantitate, decimal Material, decimal Manopera,
        decimal Utilaj, decimal Transport, decimal Echipament, Guid VersiuneId, Guid ProiectId, DateTimeOffset Data);

    private static IQueryable<RandArticol> RanduriArticole(DevizeDbContext db, IQueryable<VersiuneDeviz> versiuni, string? tipar = null, string? simbol = null) =>
        from a in db.Articole.Where(a => (tipar == null || EF.Functions.ILike(a.Simbol, tipar) || EF.Functions.ILike(a.Denumire, tipar))
                                         && (simbol == null || a.Simbol.ToUpper() == simbol))
        join cap in db.Capitole on a.CapitolId equals cap.Id
        join v in versiuni on cap.VersiuneId equals v.Id
        select new RandArticol(a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretMaterial, a.PretManopera, a.PretUtilaj, a.PretTransport,
            a.PretEchipament, v.Id, v.ProiectId, v.CreatLa);

    private static IEnumerable<ObservatiePret> CaObservatii(IEnumerable<RandArticol> randuri) =>
        randuri.Select(r =>
        {
            var pu = new ValoriResurse(r.Material, r.Manopera, r.Utilaj, r.Transport, r.Echipament);
            return new ObservatiePret(r.Simbol, r.Denumire, r.Um, CatalogPreturi.CapitolIndicator(r.Simbol), pu.Total, r.Cantitate,
                r.VersiuneId, r.ProiectId, r.Data, pu);
        }).Where(o => o.Pret > 0);

    private static string? Tipar(string? cauta) =>
        string.IsNullOrWhiteSpace(cauta) ? null : $"%{cauta.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

    private static async Task<IResult> Resurse(string? cauta, string? tip, bool? toate, DevizeDbContext db, CancellationToken ct)
    {
        TipResursa? filtruTip = Enum.TryParse<TipResursa>(tip, true, out var tr) ? tr : null;
        var lista = await ObservatiiResurse(db, VersiuniIncluse(db, toate == true), Tipar(cauta), filtruTip).ToListAsync(ct);
        return Results.Ok(CatalogPreturi.Agrega(lista));
    }

    private static async Task<IResult> PreturiResursa(string cod, string um, bool? toate, DevizeDbContext db, CancellationToken ct)
    {
        var lista = await ObservatiiResurse(db, VersiuniIncluse(db, toate == true), cod: cod.Trim().ToUpperInvariant()).ToListAsync(ct);
        var cheie = CatalogPreturi.Cheie(cod, um);
        return Results.Ok(lista.Where(o => CatalogPreturi.Cheie(o.Cod, o.UnitateMasura) == cheie).OrderByDescending(o => o.Data)
            .Select(o => new ObservatiePretDto(o.VersiuneId, o.ProiectId, o.Data, o.Denumire, o.Pret, o.Cantitate, null)).ToList());
    }

    private static async Task<IResult> Articole(string? cauta, bool? toate, DevizeDbContext db, CancellationToken ct)
    {
        var randuri = await RanduriArticole(db, VersiuniIncluse(db, toate == true), Tipar(cauta)).ToListAsync(ct);
        return Results.Ok(CatalogPreturi.Agrega(CaObservatii(randuri)));
    }

    private static async Task<IResult> PreturiArticol(string simbol, string um, bool? toate, DevizeDbContext db, CancellationToken ct)
    {
        var lista = CaObservatii(await RanduriArticole(db, VersiuniIncluse(db, toate == true), simbol: simbol.Trim().ToUpperInvariant()).ToListAsync(ct));
        var cheie = CatalogPreturi.Cheie(simbol, um);
        return Results.Ok(lista.Where(o => CatalogPreturi.Cheie(o.Cod, o.UnitateMasura) == cheie).OrderByDescending(o => o.Data)
            .Select(o => new ObservatiePretDto(o.VersiuneId, o.ProiectId, o.Data, o.Denumire, o.Pret, o.Cantitate, o.Componente)).ToList());
    }

    private static async Task<IResult> Comparatie(string? versiuni, string? ce, DevizeDbContext db, CancellationToken ct)
    {
        var ids = (versiuni ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Distinct().ToList();
        if (ids.Count < 2 || ids.Contains(Guid.Empty) || ids.Count > 10)
            return Results.BadRequest(new { mesaj = "Alege între 2 și 10 versiuni de deviz de comparat." });

        var gasite = await db.Versiuni.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new VersiuneCatalogDto(v.Id, v.ProiectId, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString(), v.CreatLa,
                v.Stare != StareVersiuneDeviz.Inlocuita && !(v.Stare == StareVersiuneDeviz.Ciorna && v.VersiuneAnterioaraId != null)))
            .ToListAsync(ct);
        if (gasite.Count != ids.Count)
            return Results.BadRequest(new { mesaj = "Unele versiuni nu există." });

        var incluse = db.Versiuni.Where(v => ids.Contains(v.Id));
        IEnumerable<ObservatiePret> observatii = ce == "resurse"
            ? await ObservatiiResurse(db, incluse).ToListAsync(ct)
            : CaObservatii(await RanduriArticole(db, incluse).ToListAsync(ct));
        return Results.Ok(new ComparatiePreturiDto(ids.Select(id => gasite.Single(v => v.Id == id)).ToList(), CatalogPreturi.Compara(ids, observatii)));
    }
}
