using ErpConstructii.Devize;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

public sealed record ArticolDevizDto(Guid Cheie, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, string Obiect, string Categorie);

public sealed record DevizReferintaDto(Guid VersiuneId, string Denumire, string Tip, string Stare, IReadOnlyList<ArticolDevizDto> Articole);

/// <param name="ArticolDevizCheie">Articolul de deviz; null = cheltuieli generale de șantier.</param>
public sealed record LinieConsumCerere(Guid ArticolId, decimal Cantitate, Guid? ArticolDevizCheie);

public sealed record ConsumCerere(DateOnly Data, IReadOnlyList<LinieConsumCerere> Linii, string? Observatii);

public sealed record LinieConsumDto(string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate, decimal? PretUnitar, decimal? Valoare,
    Guid? ArticolDevizCheie, string? ArticolDevizSimbol, string? ArticolDevizDenumire);

public sealed record BonConsumDto(Guid Id, string Numar, DateOnly Data, Guid ProiectId, string ProiectCod, IReadOnlyList<LinieConsumDto> Linii, decimal? Valoare,
    string EmisDe, string? Observatii);

/// <param name="Cantitate">Cât s-a recepționat din material prin NIR-urile facturii (în depozitul <paramref name="Depozit"/>).</param>
/// <param name="InSantier">Cât e acum în șantierul proiectului (stocul din care se consumă).</param>
/// <param name="InDepozit">Cât e acum în depozitul recepției (de unde se poate transfera ce lipsește în șantier).</param>
public sealed record LinieFacturaConsumDto(Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate,
    decimal InSantier, Guid DepozitId, string Depozit, decimal InDepozit);

/// <param name="AcelasiProiect">Factura e înregistrată pe proiectul pe care se consumă.</param>
public sealed record FacturaPentruConsumDto(Guid FacturaId, string Numar, DateOnly Data, string FurnizorDenumire, string ProiectCod, bool AcelasiProiect,
    IReadOnlyList<LinieFacturaConsumDto> Linii);

public sealed record ReturCerere(Guid GestiuneDestinatieId, DateOnly Data, IReadOnlyList<LinieTransferCerere> Linii, string? Observatii);

public sealed record MaterialConsumatDto(string Cod, string Denumire, string UnitateMasura, decimal Cantitate, decimal? Valoare);

public sealed record RandConsumArticolDto(Guid? Cheie, string Simbol, string Denumire, decimal? BugetMaterial, decimal? Consumat, decimal? Diferenta,
    decimal? ProcentConsumat, IReadOnlyList<MaterialConsumatDto> Materiale);

public sealed record ConsumPeArticoleDto(string? Deviz, IReadOnlyList<RandConsumArticolDto> Articole, decimal? TotalBuget, decimal? TotalConsumat);

/// <summary>
/// Consumul din șantier pe articolele devizului (bon de consum), returul în depozit și raportul consum real / buget material.
/// Articolele de deviz se referă prin cheia stabilă (D-05), așa că rămân valabile după o versiune nouă a devizului.
/// </summary>
internal static class EndpointuriConsum
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);

        g.MapGet("/proiecte/{proiectId:guid}/deviz", Deviz).WithName("DevizReferintaStocuri")
            .WithSummary("Articolele devizului de referință al proiectului (pentru consum)").Produces<DevizReferintaDto>().Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);
        g.MapPost("/proiecte/{proiectId:guid}/consumuri", Consuma).WithName("BonConsum")
            .WithSummary("Bon de consum: materiale din șantier consumate pe articolele devizului, la CMP")
            .Produces<BonConsumDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(creare);
        g.MapGet("/proiecte/{proiectId:guid}/consum/facturi", FacturiPentruConsum).WithName("FacturiPentruConsum")
            .WithSummary("Facturile recepționate (NIR), cu materialele lor și stocul din șantier / depozit: consumul unei facturi întregi")
            .Produces<IReadOnlyList<FacturaPentruConsumDto>>().CerePermisiune(vizualizare);
        g.MapGet("/consumuri", ListaConsumuri).WithName("ListaBonuriConsum").WithSummary("Bonurile de consum").Produces<IReadOnlyList<BonConsumDto>>().CerePermisiune(vizualizare);
        g.MapPost("/proiecte/{proiectId:guid}/retururi", Returneaza).WithName("ReturInDepozit")
            .WithSummary("Retur: materiale nefolosite din șantier înapoi în depozit, la CMP-ul din șantier")
            .Produces<TransferDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(creare);
        g.MapGet("/proiecte/{proiectId:guid}/consum-pe-articole", ConsumPeArticole).WithName("ConsumPeArticole")
            .WithSummary("Consumul real de materiale pe articolele devizului, față de bugetul de materiale").Produces<ConsumPeArticoleDto>().CerePermisiune(vizualizare);
    }

    private static async Task<IResult> Deviz(Guid proiectId, IInterogareDevize devize, CancellationToken ct)
    {
        var d = await devize.DevizDeReferintaAsync(proiectId, ct);
        return d is null
            ? Results.NotFound(new { mesaj = "Proiectul nu are încă un deviz." })
            : Results.Ok(new DevizReferintaDto(d.VersiuneId, d.Denumire, d.Tip.ToString(), d.Stare.ToString(),
                d.Articole.Select(a => new ArticolDevizDto(a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.Obiect, a.Categorie)).ToList()));
    }

    private static async Task<IResult> Consuma(
        Guid proiectId, ConsumCerere cerere, StocuriDbContext db, IInterogareProiecte proiecte, IInterogareDevize devize, IServiciuPermisiuni permisiuni,
        IFirmaCurenta firma, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        if (cerere.Data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data consumului nu poate fi în viitor." });
        if (cerere.Linii is null || cerere.Linii.Count == 0)
            return Results.BadRequest(new { mesaj = "Alege cel puțin un material." });

        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        if (proiect.Arhivat)
            return Results.Conflict(new { mesaj = proiect.MesajArhivat });
        var articoleDeviz = (await devize.DevizDeReferintaAsync(proiectId, ct))?.Articole.ToDictionary(a => a.CheieStabila) ?? [];

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var santier = await db.Gestiuni.FirstOrDefaultAsync(x => x.ProiectId == proiectId, ct);
        if (santier is null)
            return Results.BadRequest(new { mesaj = "Proiectul nu are încă materiale în șantier: transferă-le întâi din depozit." });
        if (await ModulStocuri.InventarDeschisAsync(db, ct, santier.Id) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });

        // Același material pe mai multe articole: ieșirile se calculează pe rând din același sold.
        var linii = new List<LinieConsum>();
        var solduri = new Dictionary<Guid, (SoldStoc Sold, Sold Curent)>();
        foreach (var lc in cerere.Linii)
        {
            var articol = await db.Articole.AsNoTracking().FirstOrDefaultAsync(a => a.Id == lc.ArticolId, ct);
            if (articol is null)
                return Results.BadRequest(new { mesaj = "Un material ales nu există." });
            ArticolDevizReferinta? ad = null;
            if (lc.ArticolDevizCheie is { } cheie && !articoleDeviz.TryGetValue(cheie, out ad))
                return Results.BadRequest(new { mesaj = $"Articolul de deviz ales pentru „{articol.Denumire}” nu e în devizul de referință al proiectului." });

            if (!solduri.TryGetValue(articol.Id, out var s))
            {
                var sold = await ModulStocuri.SoldAsync(db, santier.Id, articol.Id, ct);
                s = (sold, sold.Sold);
            }

            var iesire = CalculStoc.Iesire(s.Curent, lc.Cantitate, articol.Denumire);
            if (iesire.Esec)
                return Results.BadRequest(new { mesaj = iesire.Eroare.Mesaj });
            var (ramas, valoare) = iesire.Valoare;
            solduri[articol.Id] = (s.Sold, ramas);
            linii.Add(new LinieConsum(articol.Id, articol.Cod, articol.Denumire, articol.UnitateMasura, lc.Cantitate,
                Math.Round(valoare / lc.Cantitate, 4, MidpointRounding.AwayFromZero), valoare, ad?.CheieStabila, ad?.Simbol, ad?.Denumire));
        }

        var an = cerere.Data.Year;
        var numar = (await db.BonuriConsum.Where(b => b.An == an).MaxAsync(b => (int?)b.Numar, ct) ?? 0) + 1;
        var r = BonConsum.Emite(an, numar, cerere.Data, santier, proiect.Cod, linii, cerere.Observatii, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        var bc = r.Valoare;
        db.BonuriConsum.Add(bc);
        foreach (var (sold, curent) in solduri.Values)
            sold.Seteaza(curent);
        foreach (var l in bc.Linii)
            db.Miscari.Add(new MiscareStoc(santier.Id, l.ArticolId, bc.Data, TipMiscare.IesireConsum, -l.Cantitate, -l.Valoare, bc.Id, bc.NumarAfisat));

        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created($"/api/stocuri/consumuri/{bc.Id}", Dto(bc, await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct)));
    }

    /// <summary>
    /// Facturile recepționate în stoc, cele de pe proiect întâi: materialele fiecăreia (din NIR-urile active), cu ce e în
    /// șantierul proiectului și ce a rămas în depozitul recepției. Interfața completează bonul de consum cu toată factura și
    /// transferă întâi din depozit ce lipsește în șantier.
    /// </summary>
    private static async Task<IResult> FacturiPentruConsum(Guid proiectId, string? cauta, StocuriDbContext db, CancellationToken ct)
    {
        var q = db.Nir.AsNoTracking().Where(n => !db.StornariNir.Any(s => s.NirId == n.Id));
        if (!string.IsNullOrWhiteSpace(cauta))
        {
            var t = $"%{cauta.Trim()}%";
            q = q.Where(n => EF.Functions.ILike(n.FacturaNumar!, t) || EF.Functions.ILike(n.AvizNumar!, t) || EF.Functions.ILike(n.FurnizorDenumire, t));
        }

        var nir = await q.OrderByDescending(n => n.ProiectId == proiectId).ThenByDescending(n => n.FacturaData ?? n.AvizData).Take(300).ToListAsync(ct);
        var santier = await db.Gestiuni.AsNoTracking().FirstOrDefaultAsync(x => x.ProiectId == proiectId, ct);
        var depozite = await db.Gestiuni.AsNoTracking().Where(x => x.ProiectId == null).ToDictionaryAsync(x => x.Id, x => x.Denumire, ct);
        var articole = nir.SelectMany(n => n.Linii).Select(l => l.ArticolId).Distinct().ToList();
        var solduri = await db.Solduri.AsNoTracking().Where(s => articole.Contains(s.ArticolId)).ToListAsync(ct);
        decimal Sold(Guid? gestiune, Guid articol) => solduri.Where(s => s.GestiuneId == gestiune && s.ArticolId == articol).Sum(s => s.Cantitate);

        // Marfa pe aviz încă nefacturată apare cu avizul (D-40).
        var rezultat = nir.GroupBy(n => n.FacturaId ?? n.AvizId ?? n.Id).Select(g =>
        {
            var primul = g.First();
            var linii = g.SelectMany(n => n.Linii.Where(l => l.CantitateReceptionata > 0).Select(l => (n.GestiuneId, Linie: l)))
                .GroupBy(x => (x.Linie.ArticolId, x.GestiuneId))
                .Select(x =>
                {
                    var l = x.First().Linie;
                    return new LinieFacturaConsumDto(l.ArticolId, l.ArticolCod, l.Denumire, l.UnitateMasura, x.Sum(y => y.Linie.CantitateReceptionata),
                        santier is null ? 0 : Sold(santier.Id, l.ArticolId), x.Key.GestiuneId, depozite.GetValueOrDefault(x.Key.GestiuneId, "depozit"), Sold(x.Key.GestiuneId, l.ArticolId));
                })
                .ToList();
            return new FacturaPentruConsumDto(g.Key, primul.FacturaNumar ?? $"aviz {primul.AvizNumar}", primul.FacturaData ?? primul.AvizData ?? primul.Data,
                primul.FurnizorDenumire, primul.ProiectCod, primul.ProiectId == proiectId, linii);
        }).Where(f => f.Linii.Count > 0).Take(100).ToList();
        return Results.Ok(rezultat);
    }

    private static async Task<IResult> ListaConsumuri(
        Guid? proiectId, StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        var preturi = evaluator.Are(Permisiuni.PreturiCost, new Domeniu(fid));
        var accesibile = evaluator.ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);
        var q = db.BonuriConsum.AsNoTracking();
        if (proiectId is { } pid)
            q = q.Where(b => b.ProiectId == pid);
        if (accesibile is not null)
        {
            var ids = accesibile.ToList();
            q = q.Where(b => ids.Contains(b.ProiectId));
        }

        var lista = await q.OrderByDescending(b => b.An).ThenByDescending(b => b.Numar).Take(500).ToListAsync(ct);
        return Results.Ok(lista.Select(b => Dto(b, preturi)).ToList());
    }

    private static async Task<IResult> Returneaza(
        Guid proiectId, ReturCerere cerere, StocuriDbContext db, IInterogareProiecte proiecte, IServiciuPermisiuni permisiuni, IFirmaCurenta firma,
        IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        if (cerere.Data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data returului nu poate fi în viitor." });
        if (cerere.Linii is null || cerere.Linii.Count == 0)
            return Results.BadRequest(new { mesaj = "Alege cel puțin un material." });
        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var santier = await db.Gestiuni.FirstOrDefaultAsync(x => x.ProiectId == proiectId, ct);
        var depozit = await db.Gestiuni.FirstOrDefaultAsync(x => x.Id == cerere.GestiuneDestinatieId, ct);
        if (santier is null)
            return Results.BadRequest(new { mesaj = "Proiectul nu are materiale în șantier." });
        if (depozit is null)
            return Results.NotFound(new { mesaj = "Depozitul nu există." });
        if (await ModulStocuri.InventarDeschisAsync(db, ct, santier.Id, depozit.Id) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });

        var linii = new List<LinieTransfer>();
        var miscari = new List<(SoldStoc Sursa, Sold Ramas, Guid ArticolId, decimal Cantitate, decimal Valoare)>();
        foreach (var lc in cerere.Linii)
        {
            var articol = await db.Articole.AsNoTracking().FirstOrDefaultAsync(a => a.Id == lc.ArticolId, ct);
            if (articol is null)
                return Results.BadRequest(new { mesaj = "Un material ales nu există." });
            var sold = await ModulStocuri.SoldAsync(db, santier.Id, articol.Id, ct);
            var iesire = CalculStoc.Iesire(sold.Sold, lc.Cantitate, articol.Denumire);
            if (iesire.Esec)
                return Results.BadRequest(new { mesaj = iesire.Eroare.Mesaj });
            var (ramas, valoare) = iesire.Valoare;
            linii.Add(new LinieTransfer(articol.Id, articol.Cod, articol.Denumire, articol.UnitateMasura, lc.Cantitate,
                Math.Round(valoare / lc.Cantitate, 4, MidpointRounding.AwayFromZero), valoare));
            miscari.Add((sold, ramas, articol.Id, lc.Cantitate, valoare));
        }

        var an = cerere.Data.Year;
        var numar = (await db.Transferuri.Where(t => t.An == an).MaxAsync(t => (int?)t.Numar, ct) ?? 0) + 1;
        var r = TransferStoc.Emite(an, numar, cerere.Data, santier, depozit, proiect.Cod, linii, cerere.Observatii, uid, utilizator.Nume, DirectieTransfer.Retur);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        var bt = r.Valoare;
        db.Transferuri.Add(bt);
        foreach (var (sursa, ramas, articolId, cantitate, valoare) in miscari)
        {
            sursa.Seteaza(ramas);
            var dest = await ModulStocuri.SoldAsync(db, depozit.Id, articolId, ct);
            dest.Seteaza(CalculStoc.Intrare(dest.Sold, cantitate, valoare).Valoare);
            db.Miscari.Add(new MiscareStoc(santier.Id, articolId, bt.Data, TipMiscare.IesireTransfer, -cantitate, -valoare, bt.Id, bt.NumarAfisat));
            db.Miscari.Add(new MiscareStoc(depozit.Id, articolId, bt.Data, TipMiscare.IntrareTransfer, cantitate, valoare, bt.Id, bt.NumarAfisat));
        }

        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created($"/api/stocuri/transferuri/{bt.Id}", ModulStocuri.Dto(bt, await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct)));
    }

    private static async Task<IResult> ConsumPeArticole(
        Guid proiectId, StocuriDbContext db, IInterogareDevize devize, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma,
        CancellationToken ct)
    {
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var deviz = await devize.DevizDeReferintaAsync(proiectId, ct);
        var bonuri = await db.BonuriConsum.AsNoTracking().Where(b => b.ProiectId == proiectId).ToListAsync(ct);
        var consumuri = bonuri.SelectMany(b => b.Linii)
            .Select(l => new ConsumMaterial(l.ArticolDevizCheie, l.ArticolCod, l.Denumire, l.UnitateMasura, l.Cantitate, l.Valoare)).ToList();
        var buget = (deviz?.Articole ?? [])
            .Select(a => new ArticolBuget(a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.Cantitate * a.PretMaterial)).ToList();
        var randuri = RaportConsum.PeArticole(buget, consumuri);

        decimal? Bani(decimal v) => preturi ? v : null;
        return Results.Ok(new ConsumPeArticoleDto(
            deviz?.Denumire,
            randuri.Select(x => new RandConsumArticolDto(x.Cheie, x.Simbol, x.Denumire, Bani(x.BugetMaterial), Bani(x.Consumat), Bani(x.Diferenta),
                preturi ? x.ProcentConsumat : null,
                x.Materiale.Select(m => new MaterialConsumatDto(m.Cod, m.Denumire, m.UnitateMasura, m.Cantitate, Bani(m.Valoare))).ToList())).ToList(),
            Bani(randuri.Sum(x => x.BugetMaterial)),
            Bani(randuri.Sum(x => x.Consumat))));
    }

    private static BonConsumDto Dto(BonConsum b, bool preturi) => new(b.Id, b.NumarAfisat, b.Data, b.ProiectId, b.ProiectCod,
        b.Linii.Select(l => new LinieConsumDto(l.ArticolCod, l.Denumire, l.UnitateMasura, l.Cantitate, preturi ? l.PretUnitar : null, preturi ? l.Valoare : null,
            l.ArticolDevizCheie, l.ArticolDevizSimbol, l.ArticolDevizDenumire)).ToList(),
        preturi ? b.Valoare : null, b.EmisDeNume, b.Observatii);
}
