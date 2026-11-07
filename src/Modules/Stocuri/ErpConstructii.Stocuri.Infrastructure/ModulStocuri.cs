using ErpConstructii.Facturi;
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

namespace ErpConstructii.Stocuri.Infrastructure;

// ---------------------------------------------------------------- DTO-uri

public sealed record GestiuneDto(Guid Id, string Cod, string Denumire, string Tip, Guid? ProiectId, bool Activa);

public sealed record GestiuneCerere(string Cod, string Denumire);

public sealed record ArticolDto(Guid Id, string Cod, string Denumire, string UnitateMasura, bool Activ);

public sealed record ArticolCerere(string Denumire, string UnitateMasura, bool Activ);

/// <param name="Cmp">Costul mediu ponderat; null fără dreptul de a vedea prețurile de cost.</param>
public sealed record SoldDto(Guid GestiuneId, string GestiuneDenumire, string GestiuneTip, Guid? ProiectId, Guid ArticolId, string ArticolCod, string Denumire,
    string UnitateMasura, decimal Cantitate, decimal? Cmp, decimal? Valoare);

public sealed record MiscareDto(Guid Id, DateOnly Data, string Tip, string DocumentNumar, Guid DocumentId, decimal Cantitate, decimal? Valoare);

/// <param name="Linii">Liniile de stoc ale facturii; <paramref name="LiniiDeschise"/> = cele încă de recepționat (parțial sau deloc).</param>
public sealed record FacturaDeReceptionatDto(Guid FacturaId, string Numar, DateOnly Data, string FurnizorDenumire, string ProiectCod, int Linii, decimal? Valoare,
    int LiniiDeschise = 0, bool ReceptionataPartial = false, IReadOnlyList<string>? MaterialeGasite = null);

public sealed record ArticolPropusDto(Guid Id, string Cod, string Denumire, string UnitateMasura, string Sursa);

/// <param name="Cantitate">Cantitatea de pe factură; <paramref name="Receptionat"/> = primită prin NIR-urile anterioare; <paramref name="Ramas"/> = de recepționat.</param>
public sealed record LiniePropunereDto(int IndexFactura, string Denumire, string UnitateMasura, decimal Cantitate, decimal? PretUnitar, decimal? Valoare, ArticolPropusDto? Articol,
    decimal Receptionat = 0, decimal Ramas = 0, bool Inchisa = false);

public sealed record PropunereReceptieDto(Guid FacturaId, string Numar, DateOnly Data, Guid? FurnizorId, string FurnizorDenumire, string ProiectCod, IReadOnlyList<LiniePropunereDto> Linii);

/// <param name="ArticolId">Articolul ales; dacă lipsește, se creează unul nou din <paramref name="ArticolNouDenumire"/> / <paramref name="ArticolNouUm"/>.</param>
/// <param name="RestulVine">Se primește doar o parte acum, restul liniei rămâne de recepționat; fără, linia se închide.</param>
public sealed record LinieReceptieCerere(int IndexFactura, Guid? ArticolId, string? ArticolNouDenumire, string? ArticolNouUm, decimal CantitateReceptionata, string? Observatii,
    bool RestulVine = false);

public sealed record ReceptieCerere(Guid FacturaId, Guid GestiuneId, DateOnly Data, IReadOnlyList<LinieReceptieCerere> Linii, string? Observatii);

public sealed record LinieNirDto(string ArticolCod, string Denumire, string UnitateMasura, decimal CantitateFactura, decimal CantitateReceptionata, decimal? PretUnitar, decimal? Valoare, string? Observatii);

public sealed record NirDto(Guid Id, string Numar, DateOnly Data, string Gestiune, Guid FacturaId, string FacturaNumar, string FurnizorDenumire, string ProiectCod,
    IReadOnlyList<LinieNirDto> Linii, decimal? Valoare, bool AreDiferente, string ReceptionatDe, string? Observatii,
    StornoDto? Storno = null, bool FacturaAnulata = false, IReadOnlyList<string>? MaterialeGasite = null, int Corectii = 0);

public sealed record StornoDto(DateOnly Data, string Motiv, string StornatDe);

public sealed record LinieTransferCerere(Guid ArticolId, decimal Cantitate);

public sealed record TransferCerere(Guid GestiuneSursaId, DateOnly Data, IReadOnlyList<LinieTransferCerere> Linii, string? Observatii);

public sealed record LinieTransferDto(string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate, decimal? PretUnitar, decimal? Valoare);

public sealed record TransferDto(Guid Id, string Numar, string Directie, DateOnly Data, string GestiuneSursa, string GestiuneDestinatie, Guid ProiectId, string ProiectCod,
    IReadOnlyList<LinieTransferDto> Linii, decimal? Valoare, string EmisDe, string? Observatii);

public sealed record FiltreSold(Guid? GestiuneId, string? Tip, Guid? ProiectId, string? Cauta);

/// <param name="Cauta">Numărul NIR-ului / facturii sau furnizorul.</param>
/// <param name="Material">Un material din liniile NIR-ului: denumire sau cod (fără diacritice / majuscule).</param>
/// <param name="Stare">„Activ”, „Stornat” sau „CuDiferente”.</param>
public sealed record FiltreNir(string? Cauta, string? Material, DateOnly? DataDe, DateOnly? DataPanaLa, Guid? GestiuneId, Guid? ProiectId, string? Stare, Guid? FacturaId = null);

/// <summary>
/// Stocurile: gestiuni (depozite + „Materiale în șantier” pe proiect), nomenclatorul de materiale, recepția facturilor
/// (NIR, confirmat de gestionar), transferul în șantier (bon de transfer) și soldurile evaluate la CMP.
/// Drepturile: cele ale Aprovizionării (NIR-ul și gestiunile de șantier țin de ea); valorile se văd doar cu
/// dreptul „prețuri de cost”.
/// </summary>
public sealed class ModulStocuri : IModul
{
    public string Nume => StocuriDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AdaugaDbContextModul<StocuriDbContext>(configurare, Nume);
        servicii.AddScoped<IInterogareReceptiiFactura, InterogareReceptiiFactura>();
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/stocuri").WithTags("Stocuri");
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);
        var editare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Editare);

        g.MapGet("/gestiuni", ListaGestiuni).WithName("ListaGestiuni").WithSummary("Depozitele și gestiunile de șantier").Produces<IReadOnlyList<GestiuneDto>>().CerePermisiune(vizualizare);
        g.MapPost("/gestiuni", CreeazaDepozit).WithName("CreeazaDepozit").WithSummary("Adaugă un depozit").Produces<GestiuneDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status409Conflict).CerePermisiune(editare);

        g.MapGet("/articole", ListaArticole).WithName("ListaArticoleStoc").WithSummary("Nomenclatorul de materiale").Produces<IReadOnlyList<ArticolDto>>().CerePermisiune(vizualizare);
        g.MapPut("/articole/{id:guid}", ModificaArticol).WithName("ModificaArticolStoc").WithSummary("Corectează denumirea / UM unui material")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);

        g.MapGet("/sold", Sold).WithName("SoldStocuri").WithSummary("Stocul pe gestiuni: cantitate, CMP, valoare").Produces<IReadOnlyList<SoldDto>>().CerePermisiune(vizualizare);
        g.MapGet("/gestiuni/{gestiuneId:guid}/articole/{articolId:guid}/miscari", FisaMagazie).WithName("FisaMagazie")
            .WithSummary("Fișa de magazie: mișcările unui material într-o gestiune").Produces<IReadOnlyList<MiscareDto>>().CerePermisiune(vizualizare);

        g.MapGet("/receptii/de-facut", FacturiDeReceptionat).WithName("FacturiDeReceptionat")
            .WithSummary("Facturile cu materiale de stoc încă nerecepționate").Produces<IReadOnlyList<FacturaDeReceptionatDto>>().CerePermisiune(vizualizare);
        g.MapGet("/receptii/de-facut/{facturaId:guid}", PropunereReceptie).WithName("PropunereReceptie")
            .WithSummary("Liniile de stoc ale facturii, cu articolul propus").Produces<PropunereReceptieDto>().Produces(StatusCodes.Status404NotFound).CerePermisiune(creare);
        g.MapPost("/receptii", Receptioneaza).WithName("Receptioneaza").WithSummary("Emite NIR-ul: materialele recepționate intră în stoc")
            .Produces<NirDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).CerePermisiune(creare);
        g.MapGet("/nir", ListaNir).WithName("ListaNir").WithSummary("NIR-urile emise").Produces<IReadOnlyList<NirDto>>().CerePermisiune(vizualizare);

        g.MapPost("/proiecte/{proiectId:guid}/transferuri", Transfera).WithName("TransferInSantier")
            .WithSummary("Bon de transfer: materiale din depozit în „Materiale în șantier” ale proiectului, la CMP")
            .Produces<TransferDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(creare);
        // Consum pe articolele devizului, retur în depozit, consum real față de bugetul de materiale.
        EndpointuriConsum.Mapeaza(g);
        EndpointuriImportBonConsum.Mapeaza(g);

        // Inventarul gestiunilor și stornarea NIR-urilor.
        EndpointuriInventar.Mapeaza(g);
        EndpointuriCorectieNir.Mapeaza(g);

        // Proveniența stocului: pe ce NIR / factură a intrat ce e acum în gestiuni.
        EndpointuriTrasabilitate.Mapeaza(g);

        // PDF-urile documentelor (NIR, bon de consum, bon de transfer).
        DocumenteStocPdf.Mapeaza(g);

        g.MapGet("/transferuri", ListaTransferuri).WithName("ListaTransferuri").WithSummary("Bonurile de transfer").Produces<IReadOnlyList<TransferDto>>().CerePermisiune(vizualizare);
    }

    // ---------------------------------------------------------------- gestiuni și articole

    private static async Task<IResult> ListaGestiuni(StocuriDbContext db, CancellationToken ct)
    {
        await AsiguraDepozitCentralAsync(db, ct);
        var gestiuni = await db.Gestiuni.AsNoTracking().OrderBy(x => x.Tip).ThenBy(x => x.Cod).ToListAsync(ct);
        return Results.Ok(gestiuni.Select(Dto).ToList());
    }

    private static async Task<IResult> CreeazaDepozit(GestiuneCerere cerere, StocuriDbContext db, CancellationToken ct)
    {
        var r = Gestiune.CreeazaDepozit(cerere.Cod ?? string.Empty, cerere.Denumire ?? string.Empty);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        if (await db.Gestiuni.AnyAsync(x => x.Cod == r.Valoare.Cod, ct))
            return Results.Conflict(new { mesaj = $"Există deja o gestiune cu codul {r.Valoare.Cod}." });
        db.Gestiuni.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/stocuri/gestiuni/{r.Valoare.Id}", Dto(r.Valoare));
    }

    private static async Task<IResult> ListaArticole(string? cauta, StocuriDbContext db, CancellationToken ct)
    {
        var q = db.Articole.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(cauta))
            q = q.Where(a => EF.Functions.ILike(a.Denumire, $"%{cauta.Trim()}%") || EF.Functions.ILike(a.Cod, $"%{cauta.Trim()}%"));
        var articole = await q.OrderBy(a => a.Denumire).Take(2000).ToListAsync(ct);
        return Results.Ok(articole.Select(a => new ArticolDto(a.Id, a.Cod, a.Denumire, a.UnitateMasura, a.Activ)).ToList());
    }

    private static async Task<IResult> ModificaArticol(Guid id, ArticolCerere cerere, StocuriDbContext db, CancellationToken ct)
    {
        var a = await db.Articole.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null)
            return Results.NotFound();
        var r = a.Modifica(cerere.Denumire ?? string.Empty, cerere.UnitateMasura ?? string.Empty, cerere.Activ);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- sold și fișa de magazie

    private static async Task<IResult> Sold(
        [AsParameters] FiltreSold filtre, StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();

        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        var preturi = evaluator.Are(Permisiuni.PreturiCost, new Domeniu(fid));
        var accesibile = evaluator.ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);

        var q = from s in db.Solduri.AsNoTracking()
                join gst in db.Gestiuni.AsNoTracking() on s.GestiuneId equals gst.Id
                join a in db.Articole.AsNoTracking() on s.ArticolId equals a.Id
                where s.Cantitate != 0
                select new { s, gst, a };
        if (filtre.GestiuneId is { } gid)
            q = q.Where(x => x.gst.Id == gid);
        if (Enum.TryParse<TipGestiune>(filtre.Tip, true, out var tip))
            q = q.Where(x => x.gst.Tip == tip);
        if (filtre.ProiectId is { } pid)
            q = q.Where(x => x.gst.ProiectId == pid);
        if (!string.IsNullOrWhiteSpace(filtre.Cauta))
            q = q.Where(x => EF.Functions.ILike(x.a.Denumire, $"%{filtre.Cauta.Trim()}%") || EF.Functions.ILike(x.a.Cod, $"%{filtre.Cauta.Trim()}%"));

        var randuri = await q.OrderBy(x => x.gst.Tip).ThenBy(x => x.gst.Cod).ThenBy(x => x.a.Denumire).ToListAsync(ct);
        return Results.Ok(randuri
            .Where(x => x.gst.ProiectId is null || accesibile is null || accesibile.Contains(x.gst.ProiectId.Value)) // șantierele: doar proiectele accesibile
            .Select(x => new SoldDto(x.gst.Id, x.gst.Denumire, x.gst.Tip.ToString(), x.gst.ProiectId, x.a.Id, x.a.Cod, x.a.Denumire, x.a.UnitateMasura,
                x.s.Cantitate, preturi ? x.s.Sold.Cmp : null, preturi ? x.s.Valoare : null))
            .ToList());
    }

    private static async Task<IResult> FisaMagazie(
        Guid gestiuneId, Guid articolId, StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        var preturi = await VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var miscari = await db.Miscari.AsNoTracking().Where(m => m.GestiuneId == gestiuneId && m.ArticolId == articolId)
            .OrderBy(m => m.Data).ThenBy(m => m.CreatLa).ToListAsync(ct);
        return Results.Ok(miscari.Select(m => new MiscareDto(m.Id, m.Data, m.Tip.ToString(), m.DocumentNumar, m.DocumentId, m.Cantitate, preturi ? m.Valoare : null)).ToList());
    }

    // ---------------------------------------------------------------- recepții (NIR)

    /// <param name="cauta">Numărul facturii sau furnizorul.</param>
    /// <param name="material">Un material din liniile de stoc ale facturii (fără diacritice / majuscule).</param>
    private static async Task<IResult> FacturiDeReceptionat(
        string? cauta, string? material, StocuriDbContext db, IInterogareFacturiPrimite facturi, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        var preturi = await VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var receptii = await ReceptiiActiveAsync(db, null, ct);
        var lista = await facturi.CuMaterialeDeStocAsync(ct);
        var text = string.IsNullOrWhiteSpace(cauta) ? null : FaraDiacritice(cauta.Trim());
        var termen = string.IsNullOrWhiteSpace(material) ? null : FaraDiacritice(material.Trim());
        return Results.Ok(lista
            .Where(f => text is null || FaraDiacritice(f.Numar).Contains(text, StringComparison.Ordinal) || FaraDiacritice(f.FurnizorDenumire).Contains(text, StringComparison.Ordinal))
            .Select(f => (Factura: f, Stare: StareReceptie.Calculeaza(f.Linii.Select(l => (l.Index, l.Cantitate)), receptii.GetValueOrDefault(f.Id) ?? []),
                Gasite: termen is null ? null : f.Linii.Select(l => l.Denumire).Where(d => FaraDiacritice(d).Contains(termen, StringComparison.Ordinal)).Distinct().ToList()))
            .Where(x => x.Stare.Any(s => s.Deschisa) && (x.Gasite is null || x.Gasite.Count > 0))
            .Select(x => new FacturaDeReceptionatDto(x.Factura.Id, x.Factura.Numar, x.Factura.DataEmitere, x.Factura.FurnizorDenumire, x.Factura.ProiectCod,
                x.Factura.Linii.Count, preturi ? x.Factura.Linii.Sum(l => l.ValoareFaraTva) : null,
                x.Stare.Count(s => s.Deschisa), x.Stare.Any(s => s.Receptionat > 0 || s.Inchisa), x.Gasite))
            .ToList());
    }

    /// <summary>Liniile NIR-urilor nestornate, pe factură (o factură poate avea mai multe recepții).</summary>
    private static async Task<Dictionary<Guid, List<LinieNir>>> ReceptiiActiveAsync(StocuriDbContext db, Guid? facturaId, CancellationToken ct)
    {
        var q = db.Nir.AsNoTracking().Where(n => !db.StornariNir.Any(s => s.NirId == n.Id));
        if (facturaId is { } id)
            q = q.Where(n => n.FacturaId == id);
        return (await q.ToListAsync(ct)).GroupBy(n => n.FacturaId).ToDictionary(g => g.Key, g => g.SelectMany(n => n.Linii).ToList());
    }

    private static async Task<IResult> PropunereReceptie(
        Guid facturaId, StocuriDbContext db, IInterogareFacturiPrimite facturi, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma,
        CancellationToken ct)
    {
        var f = await facturi.PentruReceptieAsync(facturaId, ct);
        if (f is null)
            return Results.NotFound(new { mesaj = "Factura nu există, a fost anulată sau nu are materiale de stoc." });

        var preturi = await VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var (aliasuri, articole) = await CandidatiAsync(db, ct);
        var receptii = (await ReceptiiActiveAsync(db, f.Id, ct)).GetValueOrDefault(f.Id) ?? [];
        var stare = StareReceptie.Calculeaza(f.Linii.Select(l => (l.Index, l.Cantitate)), receptii).ToDictionary(s => s.Index);
        return Results.Ok(new PropunereReceptieDto(f.Id, f.Numar, f.DataEmitere, f.FurnizorId, f.FurnizorDenumire, f.ProiectCod,
            f.Linii.Select(l =>
            {
                // Materialul ales la recepția anterioară a aceleiași linii are prioritate.
                var anterior = receptii.LastOrDefault(r => r.IndexFactura == l.Index);
                var p = anterior is not null && articole.FirstOrDefault(a => a.Id == anterior.ArticolId) is { } a0
                    ? new ArticolPotrivit(a0, SursaPotrivire.AliasFurnizor)
                    : PotrivireArticol.Propune(l.Denumire, f.FurnizorId, aliasuri, articole);
                var s = stare[l.Index];
                return new LiniePropunereDto(l.Index, l.Denumire, l.UnitateMasura, l.Cantitate, preturi ? l.PretUnitar : null, preturi ? l.ValoareFaraTva : null,
                    p is null ? null : new ArticolPropusDto(p.Articol.Id, p.Articol.Cod, p.Articol.Denumire, p.Articol.UnitateMasura, p.Sursa.ToString()),
                    s.Receptionat, s.Ramas, s.Inchisa);
            }).ToList()));
    }

    private static async Task<IResult> Receptioneaza(
        ReceptieCerere cerere, StocuriDbContext db, IInterogareFacturiPrimite facturi, IServiciuPermisiuni permisiuni, IFirmaCurenta firma, IUtilizatorCurent utilizator,
        ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        if (cerere.Data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data recepției nu poate fi în viitor." });

        var f = await facturi.PentruReceptieAsync(cerere.FacturaId, ct);
        if (f is null)
            return Results.NotFound(new { mesaj = "Factura nu există, a fost anulată sau nu are materiale de stoc." });

        // Recepție parțială: doar liniile alese, fiecare încă deschisă (ce nu s-a primit rămâne pentru o recepție următoare).
        var liniiCerere = cerere.Linii ?? [];
        var dupaIndex = f.Linii.ToDictionary(l => l.Index);
        if (liniiCerere.Count == 0)
            return Results.BadRequest(new { mesaj = "Alege cel puțin o linie de recepționat." });
        if (liniiCerere.Any(l => !dupaIndex.ContainsKey(l.IndexFactura)))
            return Results.BadRequest(new { mesaj = "O linie aleasă nu e o linie de stoc a facturii." });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await BlocheazaStoculAsync(db, tranzactie, fid, ct);

        var stare = StareReceptie.Calculeaza(f.Linii.Select(l => (l.Index, l.Cantitate)), (await ReceptiiActiveAsync(db, f.Id, ct)).GetValueOrDefault(f.Id) ?? [])
            .ToDictionary(s => s.Index);
        if (liniiCerere.FirstOrDefault(l => !stare[l.IndexFactura].Deschisa) is { } inchisa)
            return Results.Conflict(new { mesaj = $"Linia „{dupaIndex[inchisa.IndexFactura].Denumire}” a fost deja recepționată complet." });
        var gestiune = await db.Gestiuni.FirstOrDefaultAsync(x => x.Id == cerere.GestiuneId, ct);
        if (gestiune is null)
            return Results.NotFound(new { mesaj = "Depozitul nu există." });
        if (await InventarDeschisAsync(db, ct, gestiune.Id) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });

        // Articolele: cele alese sau create acum; legătura text factură → articol se memorează pe furnizor.
        var liniiNir = new List<LinieNirCerere>();
        var urmatorulCod = await UrmatorulNumarArticolAsync(db, ct);
        foreach (var lc in liniiCerere)
        {
            var lf = dupaIndex[lc.IndexFactura];
            ArticolStoc? articol;
            if (lc.ArticolId is { } aid)
            {
                articol = await db.Articole.FirstOrDefaultAsync(a => a.Id == aid, ct);
                if (articol is null)
                    return Results.BadRequest(new { mesaj = $"Articolul ales pentru „{lf.Denumire}” nu există." });
            }
            else
            {
                var nou = ArticolStoc.Creeaza(ArticolStoc.CodGenerat(urmatorulCod++), lc.ArticolNouDenumire ?? lf.Denumire, lc.ArticolNouUm ?? lf.UnitateMasura);
                if (nou.Esec)
                    return Results.BadRequest(new { mesaj = $"„{lf.Denumire}”: {nou.Eroare.Mesaj}" });
                articol = nou.Valoare;
                db.Articole.Add(articol);
            }

            var cheie = PotrivireArticol.Cheie(lf.Denumire);
            var alias = db.Aliasuri.Local.FirstOrDefault(a => a.FurnizorId == f.FurnizorId && a.Cheie == cheie)
                        ?? await db.Aliasuri.FirstOrDefaultAsync(a => a.FurnizorId == f.FurnizorId && a.Cheie == cheie, ct);
            if (alias is null && cheie.Length > 0)
                db.Aliasuri.Add(new AliasArticol(f.FurnizorId, lf.Denumire, articol.Id));
            else if (alias is not null && alias.ArticolId != articol.Id)
            {
                db.Aliasuri.Remove(alias); // utilizatorul a ales alt articol: alegerea nouă câștigă
                db.Aliasuri.Add(new AliasArticol(f.FurnizorId, lf.Denumire, articol.Id));
            }

            liniiNir.Add(new LinieNirCerere(lf.Index, articol.Id, articol.Cod, articol.Denumire, articol.UnitateMasura,
                stare[lf.Index].Ramas, lc.CantitateReceptionata, lf.PretUnitar, lc.Observatii, lc.RestulVine));
        }

        var an = cerere.Data.Year;
        var numar = (await db.Nir.Where(n => n.An == an).MaxAsync(n => (int?)n.Numar, ct) ?? 0) + 1;
        var r = Nir.Emite(an, numar, cerere.Data, gestiune, f.Id, f.Numar, f.DataEmitere, f.FurnizorId, f.FurnizorDenumire, f.ProiectId, f.ProiectCod,
            liniiNir, cerere.Observatii, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        var nir = r.Valoare;
        db.Nir.Add(nir);

        foreach (var l in nir.Linii.Where(l => l.CantitateReceptionata > 0))
        {
            var sold = await SoldAsync(db, gestiune.Id, l.ArticolId, ct);
            var nouSold = CalculStoc.Intrare(sold.Sold, l.CantitateReceptionata, l.Valoare);
            if (nouSold.Esec)
                return Results.BadRequest(new { mesaj = nouSold.Eroare.Mesaj });
            sold.Seteaza(nouSold.Valoare);
            db.Miscari.Add(new MiscareStoc(gestiune.Id, l.ArticolId, nir.Data, TipMiscare.IntrareNir, l.CantitateReceptionata, l.Valoare, nir.Id, nir.NumarAfisat));
        }

        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        var preturi = await VedePreturiAsync(permisiuni, utilizator, firma, ct);
        return Results.Created($"/api/stocuri/nir/{nir.Id}", Dto(nir, preturi));
    }

    /// <summary>Registrul NIR-urilor, cu filtre (perioadă, depozit, proiect, furnizor / factură, material, stare).</summary>
    private static async Task<IResult> ListaNir(
        [AsParameters] FiltreNir filtre, StocuriDbContext db, IInterogareFacturiPrimite facturi, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator,
        IFirmaCurenta firma, CancellationToken ct)
    {
        var preturi = await VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var (cauta, material) = (filtre.Cauta, filtre.Material);
        var q = db.Nir.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(cauta))
        {
            var t = $"%{cauta.Trim()}%";
            // „NIR/2026/0003”, „0003” sau „3” găsesc NIR-ul după număr.
            var numar = int.TryParse(cauta.Trim().Split('/')[^1], out var nr) ? nr : (int?)null;
            q = q.Where(n => EF.Functions.ILike(n.FacturaNumar, t) || EF.Functions.ILike(n.FurnizorDenumire, t) || (numar != null && n.Numar == numar));
        }

        if (filtre.DataDe is { } d1) q = q.Where(n => n.Data >= d1);
        if (filtre.DataPanaLa is { } d2) q = q.Where(n => n.Data <= d2);
        if (filtre.GestiuneId is { } gid) q = q.Where(n => n.GestiuneId == gid);
        if (filtre.ProiectId is { } pid) q = q.Where(n => n.ProiectId == pid);
        if (filtre.FacturaId is { } fid) q = q.Where(n => n.FacturaId == fid);
        if (string.Equals(filtre.Stare, "Corectat", StringComparison.OrdinalIgnoreCase)) q = q.Where(n => db.CorectiiNir.Any(c => c.NirId == n.Id));
        if (string.Equals(filtre.Stare, "Stornat", StringComparison.OrdinalIgnoreCase)) q = q.Where(n => db.StornariNir.Any(s => s.NirId == n.Id));
        if (string.Equals(filtre.Stare, "Activ", StringComparison.OrdinalIgnoreCase)) q = q.Where(n => !db.StornariNir.Any(s => s.NirId == n.Id));

        string? termen = null;
        if (!string.IsNullOrWhiteSpace(material))
        {
            termen = FaraDiacritice(material.Trim());
            var tipar = $"%{termen}%";
            // Liniile stau în jsonb; textul se compară fără diacritice și fără majuscule.
            var cuMaterial = db.Database.SqlQuery<Guid>(
                $"""SELECT n.id AS "Value" FROM stocuri.nir n WHERE EXISTS (SELECT 1 FROM jsonb_array_elements(n.linii_json) e WHERE translate(lower(e->>'Denumire'), 'ăâîșşțţ', 'aaisstt') LIKE {tipar} OR lower(e->>'ArticolCod') LIKE {tipar})""");
            q = q.Where(n => cuMaterial.Contains(n.Id));
        }

        var lista = await q.OrderByDescending(n => n.An).ThenByDescending(n => n.Numar).Take(2000).ToListAsync(ct);
        if (string.Equals(filtre.Stare, "CuDiferente", StringComparison.OrdinalIgnoreCase))
            lista = lista.Where(n => n.AreDiferente).ToList(); // calculat din linii (jsonb)
        var ids = lista.Select(n => n.Id).ToList();
        var stornari = await db.StornariNir.AsNoTracking().Where(s => ids.Contains(s.NirId)).ToDictionaryAsync(s => s.NirId, ct);
        var anulate = await facturi.FacturiAnulateAsync(ct);
        var corectii = await db.CorectiiNir.AsNoTracking().Where(c => ids.Contains(c.NirId)).GroupBy(c => c.NirId)
            .Select(g => new { g.Key, Numar = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Numar, ct);
        return Results.Ok(lista.Select(n => Dto(n, preturi) with
        {
            Corectii = corectii.GetValueOrDefault(n.Id),
            Storno = stornari.TryGetValue(n.Id, out var s) ? new StornoDto(s.Data, s.Motiv, s.StornatDeNume) : null,
            FacturaAnulata = anulate.Contains(n.FacturaId),
            MaterialeGasite = termen is null ? null
                : n.Linii.Where(l => FaraDiacritice(l.Denumire).Contains(termen, StringComparison.Ordinal) || l.ArticolCod.Contains(termen, StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.Denumire).Distinct().ToList(),
        }).ToList());
    }

    private static string FaraDiacritice(string text) => Text.FaraDiacritice(text);

    // ---------------------------------------------------------------- transferuri în șantier

    private static async Task<IResult> Transfera(
        Guid proiectId, TransferCerere cerere, StocuriDbContext db, IInterogareProiecte proiecte, IServiciuPermisiuni permisiuni, IFirmaCurenta firma,
        IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        if (cerere.Data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data transferului nu poate fi în viitor." });
        if (cerere.Linii is null || cerere.Linii.Count == 0)
            return Results.BadRequest(new { mesaj = "Alege cel puțin un material." });

        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        if (proiect.Arhivat)
            return Results.Conflict(new { mesaj = proiect.MesajArhivat });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await BlocheazaStoculAsync(db, tranzactie, fid, ct);

        var sursa = await db.Gestiuni.FirstOrDefaultAsync(x => x.Id == cerere.GestiuneSursaId, ct);
        if (sursa is null)
            return Results.NotFound(new { mesaj = "Depozitul sursă nu există." });
        var destinatie = await db.Gestiuni.FirstOrDefaultAsync(x => x.ProiectId == proiectId, ct);
        if (await InventarDeschisAsync(db, ct, sursa.Id, destinatie?.Id ?? Guid.Empty) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });
        if (destinatie is null)
        {
            destinatie = Gestiune.Santier(proiect.Id, proiect.Cod, proiect.Denumire);
            if (await db.Gestiuni.AnyAsync(x => x.Cod == destinatie.Cod, ct))
                destinatie = Gestiune.Santier(proiect.Id, $"S-{proiect.Cod}", proiect.Denumire);
            db.Gestiuni.Add(destinatie);
        }

        var linii = new List<LinieTransfer>();
        var solduriNoi = new List<(SoldStoc Sursa, Sold SursaNou, Guid ArticolId, decimal Cantitate, decimal Valoare)>();
        foreach (var lc in cerere.Linii)
        {
            var articol = await db.Articole.AsNoTracking().FirstOrDefaultAsync(a => a.Id == lc.ArticolId, ct);
            if (articol is null)
                return Results.BadRequest(new { mesaj = "Un material ales nu există." });
            var soldSursa = await SoldAsync(db, sursa.Id, articol.Id, ct);
            var iesire = CalculStoc.Iesire(soldSursa.Sold, lc.Cantitate, articol.Denumire);
            if (iesire.Esec)
                return Results.BadRequest(new { mesaj = iesire.Eroare.Mesaj });
            var (ramas, valoare) = iesire.Valoare;
            linii.Add(new LinieTransfer(articol.Id, articol.Cod, articol.Denumire, articol.UnitateMasura, lc.Cantitate,
                Math.Round(valoare / lc.Cantitate, 4, MidpointRounding.AwayFromZero), valoare));
            solduriNoi.Add((soldSursa, ramas, articol.Id, lc.Cantitate, valoare));
        }

        var an = cerere.Data.Year;
        var numar = (await db.Transferuri.Where(t => t.An == an).MaxAsync(t => (int?)t.Numar, ct) ?? 0) + 1;
        var r = TransferStoc.Emite(an, numar, cerere.Data, sursa, destinatie, proiect.Cod, linii, cerere.Observatii, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        var bt = r.Valoare;
        db.Transferuri.Add(bt);

        foreach (var (soldSursa, ramas, articolId, cantitate, valoare) in solduriNoi)
        {
            soldSursa.Seteaza(ramas);
            var soldDest = await SoldAsync(db, destinatie.Id, articolId, ct);
            soldDest.Seteaza(CalculStoc.Intrare(soldDest.Sold, cantitate, valoare).Valoare);
            db.Miscari.Add(new MiscareStoc(sursa.Id, articolId, bt.Data, TipMiscare.IesireTransfer, -cantitate, -valoare, bt.Id, bt.NumarAfisat));
            db.Miscari.Add(new MiscareStoc(destinatie.Id, articolId, bt.Data, TipMiscare.IntrareTransfer, cantitate, valoare, bt.Id, bt.NumarAfisat));
        }

        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        var preturi = await VedePreturiAsync(permisiuni, utilizator, firma, ct);
        return Results.Created($"/api/stocuri/transferuri/{bt.Id}", Dto(bt, preturi));
    }

    private static async Task<IResult> ListaTransferuri(
        Guid? proiectId, StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        var preturi = evaluator.Are(Permisiuni.PreturiCost, new Domeniu(fid));
        var accesibile = evaluator.ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);
        var q = db.Transferuri.AsNoTracking();
        if (proiectId is { } pid)
            q = q.Where(t => t.ProiectId == pid);
        if (accesibile is not null)
        {
            var ids = accesibile.ToList();
            q = q.Where(t => ids.Contains(t.ProiectId));
        }

        var lista = await q.OrderByDescending(t => t.An).ThenByDescending(t => t.Numar).Take(500).ToListAsync(ct);
        return Results.Ok(lista.Select(t => Dto(t, preturi)).ToList());
    }

    // ---------------------------------------------------------------- utilitare

    /// <summary>Operațiile de stoc ale unei firme se serializează: soldurile și numerotarea rămân consecvente.</summary>
    internal static async Task BlocheazaStoculAsync(StocuriDbContext db, IDbContextTransaction tranzactie, Guid firmaId, CancellationToken ct)
    {
        await using var comanda = db.Database.GetDbConnection().CreateCommand();
        comanda.Transaction = tranzactie.GetDbTransaction();
        comanda.CommandText = "SELECT pg_advisory_xact_lock(hashtext(@cheie))";
        var p = comanda.CreateParameter();
        p.ParameterName = "cheie";
        p.Value = firmaId.ToString("N") + ":stoc";
        comanda.Parameters.Add(p);
        await comanda.ExecuteScalarAsync(ct);
    }

    /// <summary>Mesajul de blocare dacă una dintre gestiuni are un inventar deschis (numărătoarea în curs), altfel null.</summary>
    internal static async Task<string?> InventarDeschisAsync(StocuriDbContext db, CancellationToken ct, params Guid[] gestiuni)
    {
        var inv = await db.Inventare.AsNoTracking()
            .Where(i => gestiuni.Contains(i.GestiuneId) && i.Stare == StareInventar.Deschis)
            .Select(i => new { i.An, i.Numar, i.GestiuneDenumire }).FirstOrDefaultAsync(ct);
        return inv is null
            ? null
            : $"Gestiunea „{inv.GestiuneDenumire}” are inventarul INV/{inv.An}/{inv.Numar:D4} deschis: mișcările sunt blocate până la finalizarea lui.";
    }

    internal static async Task<SoldStoc> SoldAsync(StocuriDbContext db, Guid gestiuneId, Guid articolId, CancellationToken ct)
    {
        var sold = db.Solduri.Local.FirstOrDefault(s => s.GestiuneId == gestiuneId && s.ArticolId == articolId)
                   ?? await db.Solduri.FirstOrDefaultAsync(s => s.GestiuneId == gestiuneId && s.ArticolId == articolId, ct);
        if (sold is null)
        {
            sold = new SoldStoc(gestiuneId, articolId);
            db.Solduri.Add(sold);
        }

        return sold;
    }

    private static async Task<int> UrmatorulNumarArticolAsync(StocuriDbContext db, CancellationToken ct)
    {
        var coduri = await db.Articole.Where(a => a.Cod.StartsWith("MAT-")).Select(a => a.Cod).ToListAsync(ct);
        return coduri.Select(c => int.TryParse(c[4..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
    }

    private static async Task<(List<AliasCandidat>, List<ArticolCandidat>)> CandidatiAsync(StocuriDbContext db, CancellationToken ct) =>
        (await db.Aliasuri.AsNoTracking().Select(a => new AliasCandidat(a.FurnizorId, a.Cheie, a.ArticolId)).ToListAsync(ct),
         await db.Articole.AsNoTracking().Where(a => a.Activ).Select(a => new ArticolCandidat(a.Id, a.Cod, a.Denumire, a.UnitateMasura)).ToListAsync(ct));

    private static async Task AsiguraDepozitCentralAsync(StocuriDbContext db, CancellationToken ct)
    {
        if (!await db.Gestiuni.AnyAsync(g => g.Tip == TipGestiune.Depozit, ct))
        {
            db.Gestiuni.Add(Gestiune.DepozitCentral());
            await db.SaveChangesAsync(ct);
        }
    }

    internal static async Task<bool> VedePreturiAsync(IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct) =>
        utilizator.UtilizatorId is { } uid && firma.FirmaId is { } fid &&
        (await permisiuni.PentruUtilizatorAsync(uid, ct)).Are(Permisiuni.PreturiCost, new Domeniu(fid));

    private static GestiuneDto Dto(Gestiune g) => new(g.Id, g.Cod, g.Denumire, g.Tip.ToString(), g.ProiectId, g.Activa);

    internal static NirDto Dto(Nir n, bool preturi) => new(n.Id, n.NumarAfisat, n.Data, n.GestiuneDenumire, n.FacturaId, n.FacturaNumar, n.FurnizorDenumire, n.ProiectCod,
        n.Linii.Select(l => new LinieNirDto(l.ArticolCod, l.Denumire, l.UnitateMasura, l.CantitateFactura, l.CantitateReceptionata,
            preturi ? l.PretUnitar : null, preturi ? l.Valoare : null, l.Observatii)).ToList(),
        preturi ? n.Valoare : null, n.AreDiferente, n.ReceptionatDeNume, n.Observatii);

    internal static TransferDto Dto(TransferStoc t, bool preturi) => new(t.Id, t.NumarAfisat, t.Directie.ToString(), t.Data, t.GestiuneSursaDenumire, t.GestiuneDestinatieDenumire, t.ProiectId, t.ProiectCod,
        t.Linii.Select(l => new LinieTransferDto(l.ArticolCod, l.Denumire, l.UnitateMasura, l.Cantitate, preturi ? l.PretUnitar : null, preturi ? l.Valoare : null)).ToList(),
        preturi ? t.Valoare : null, t.EmisDeNume, t.Observatii);
}
