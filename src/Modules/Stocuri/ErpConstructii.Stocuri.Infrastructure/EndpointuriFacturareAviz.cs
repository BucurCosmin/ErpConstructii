using ErpConstructii.Aprovizionare;
using ErpConstructii.Facturi;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <param name="Ramas">Cât din linie nu e încă acoperit de recepții (pe factură sau pe aviz).</param>
public sealed record LinieFacturaDeLegatDto(int Index, string Denumire, string UnitateMasura, decimal Cantitate, decimal? PretUnitar, decimal Ramas);

/// <param name="IndexAviz">Cheia liniei în NIR (poziția pe aviz).</param>
/// <param name="IndexFacturaPropus">Linia facturii propusă (după materialul recunoscut sau denumire); null = de ales.</param>
public sealed record LinieNirDeLegatDto(
    int IndexAviz, string ArticolCod, string Denumire, string DenumireAviz, string UnitateMasura, decimal Cantitate, decimal? PretUnitar, int? IndexFacturaPropus);

public sealed record NirDeLegatDto(Guid NirId, string Numar, DateOnly Data, string AvizNumar, string Gestiune, IReadOnlyList<LinieNirDeLegatDto> Linii);

public sealed record FacturareAvizDto(
    Guid FacturaId, string Numar, DateOnly Data, string FurnizorDenumire, string? NotaComanda, bool VedePreturi,
    IReadOnlyList<LinieFacturaDeLegatDto> LiniiFactura, IReadOnlyList<NirDeLegatDto> Nir);

/// <param name="PretUnitar">Prețul pe UM-ul NIR-ului, când factura are altă UM (ex. tone față de kg pe aviz); implicit prețul liniei facturii.</param>
public sealed record LinieFacturareCerere(int IndexAviz, int IndexFactura, decimal? PretUnitar = null);

public sealed record NirFacturatCerere(Guid NirId, IReadOnlyList<LinieFacturareCerere> Linii);

public sealed record FacturareAvizCerere(Guid FacturaId, DateOnly? Data, IReadOnlyList<NirFacturatCerere> Nir);

/// <summary>
/// Factura venită după marfa recepționată pe aviz (D-40): NIR-urile pe avizele notei de comandă a facturii primesc factura și
/// prețurile ei (<see cref="Nir.Factureaza"/>, cu corectură în istoric), iar liniile facturii acoperite de ele nu mai sunt de
/// recepționat. Diferența de valoare intră în stoc unde se află acum materialul: în depozitul recepției sau, dacă a plecat
/// de acolo, în șantierul proiectului; materialul deja consumat nu mai primește diferența (avertisment).
/// </summary>
internal static class EndpointuriFacturareAviz
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);
        g.MapGet("/receptii/facturare/{facturaId:guid}", Propunere).WithName("PropunereFacturareAviz")
            .WithSummary("NIR-urile pe avizele notei de comandă a facturii, cu linia facturii propusă pentru fiecare material")
            .Produces<FacturareAvizDto>().Produces(StatusCodes.Status404NotFound).CerePermisiune(creare);
        g.MapPost("/receptii/facturare", Factureaza).WithName("FactureazaAvize")
            .WithSummary("Leagă factura de NIR-urile pe aviz: primesc prețurile facturii, stocul primește diferența de valoare")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).CerePermisiune(creare);
    }

    private static async Task<IResult> Propunere(
        Guid facturaId, StocuriDbContext db, IInterogareFacturiPrimite facturi, IEnumerable<IInterogareAvize> avize, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        var f = await facturi.PentruReceptieAsync(facturaId, ct);
        if (f is null)
            return Results.NotFound(new { mesaj = "Factura nu există, a fost anulată sau nu are materiale de stoc." });
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var (candidati, avizeNir) = await NirDeLegatAsync(db, f, avize, ct);
        var stare = await StareFacturaAsync(db, f, ct);
        var (aliasuri, articole) = await CandidatiAsync(db, ct);

        // Materialul recunoscut pe fiecare linie a facturii (aliasurile furnizorului, denumirea).
        var articolFactura = f.Linii.ToDictionary(l => l.Index, l => PotrivireArticol.Propune(l.Denumire, f.FurnizorId, aliasuri, articole)?.Articol.Id);
        int? Propune(LinieNir l, string denumireAviz)
        {
            var dupaArticol = f.Linii.Where(x => articolFactura[x.Index] == l.ArticolId).ToList();
            if (dupaArticol.Count == 1)
                return dupaArticol[0].Index;
            var cheie = PotrivireArticol.Cheie(denumireAviz);
            var dupaText = f.Linii.Where(x => PotrivireArticol.Cheie(x.Denumire) == cheie || PotrivireArticol.Cheie(x.Denumire) == PotrivireArticol.Cheie(l.Denumire)).ToList();
            if (dupaText.Count == 1)
                return dupaText[0].Index;
            return f.Linii.Count == 1 ? f.Linii[0].Index : null;
        }

        return Results.Ok(new FacturareAvizDto(f.Id, f.Numar, f.DataEmitere, f.FurnizorDenumire, f.NotaComandaNumar, preturi,
            f.Linii.Select(l => new LinieFacturaDeLegatDto(l.Index, l.Denumire, l.UnitateMasura, l.Cantitate, preturi ? l.PretUnitar : null,
                stare.GetValueOrDefault(l.Index)?.Ramas ?? l.Cantitate)).ToList(),
            candidati.Select(n =>
            {
                var a = avizeNir[n.AvizId!.Value];
                return new NirDeLegatDto(n.Id, n.NumarAfisat, n.Data, a.NumarExtern, n.GestiuneDenumire,
                    n.Linii.Where(l => l.CantitateReceptionata > 0).Select(l =>
                    {
                        var denumireAviz = a.Linii.FirstOrDefault(x => x.Index == l.Cheie)?.Denumire ?? l.Denumire;
                        return new LinieNirDeLegatDto(l.Cheie, l.ArticolCod, l.Denumire, denumireAviz, l.UnitateMasura, l.CantitateReceptionata,
                            preturi ? l.PretUnitar : null, Propune(l, denumireAviz));
                    }).ToList());
            }).ToList()));
    }

    private static async Task<IResult> Factureaza(
        FacturareAvizCerere cerere, StocuriDbContext db, IInterogareFacturiPrimite facturi, IEnumerable<IInterogareAvize> avize, IServiciuPermisiuni permisiuni,
        IFirmaCurenta firma, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        var data = cerere.Data ?? ceas.Azi;
        if (data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data nu poate fi în viitor." });
        if (cerere.Nir is not { Count: > 0 })
            return Results.BadRequest(new { mesaj = "Alege cel puțin un NIR pe aviz." });
        var f = await facturi.PentruReceptieAsync(cerere.FacturaId, ct);
        if (f is null)
            return Results.NotFound(new { mesaj = "Factura nu există, a fost anulată sau nu are materiale de stoc." });
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var liniiFactura = f.Linii.ToDictionary(l => l.Index);

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var (candidati, avizeNir) = await NirDeLegatAsync(db, f, avize, ct);
        var permise = candidati.Select(n => n.Id).ToHashSet();
        var ids = cerere.Nir.Select(n => n.NirId).ToList();
        if (ids.Any(id => !permise.Contains(id)))
            return Results.Conflict(new { mesaj = "Un NIR ales nu e pe avizele notei de comandă a facturii sau are deja factură." });
        var nirUri = await db.Nir.Where(n => ids.Contains(n.Id)).ToListAsync(ct);
        if (await ModulStocuri.InventarDeschisAsync(db, ct, nirUri.Select(n => n.GestiuneId).Distinct().ToArray()) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });

        // Cantitățile acoperite nu pot depăși ce a rămas din liniile facturii (când UM-ul e același).
        var stare = await StareFacturaAsync(db, f, ct);
        var acoperit = new Dictionary<int, decimal>();
        foreach (var c in cerere.Nir)
        {
            var nir = nirUri.First(n => n.Id == c.NirId);
            foreach (var l in c.Linii ?? [])
            {
                if (!liniiFactura.TryGetValue(l.IndexFactura, out var lf))
                    return Results.BadRequest(new { mesaj = "O linie aleasă nu e o linie de stoc a facturii." });
                var ln = nir.Linii.FirstOrDefault(x => x.Cheie == l.IndexAviz);
                if (ln is null)
                    return Results.BadRequest(new { mesaj = $"O linie aleasă nu e pe {nir.NumarAfisat}." });
                if (AceeasiUm(ln.UnitateMasura, lf.UnitateMasura))
                    acoperit[l.IndexFactura] = acoperit.GetValueOrDefault(l.IndexFactura) + ln.CantitateReceptionata;
            }
        }

        foreach (var (index, cantitate) in acoperit)
        {
            var ramas = stare.GetValueOrDefault(index)?.Ramas ?? liniiFactura[index].Cantitate;
            if (cantitate > ramas * 1.0001m)
                return Results.Conflict(new
                {
                    mesaj = $"Linia facturii „{liniiFactura[index].Denumire}” mai acoperă {ramas:0.####} {liniiFactura[index].UnitateMasura}, " +
                            $"iar NIR-urile alese au {cantitate:0.####}. Verifică liniile (sau corectează întâi factura / NIR-ul).",
                });
        }

        var avertismente = new List<string>();
        var corectate = new List<string>();
        var (aliasuri, _) = await CandidatiAsync(db, ct);
        foreach (var c in cerere.Nir)
        {
            var nir = nirUri.First(n => n.Id == c.NirId);
            var linii = (c.Linii ?? []).Select(l =>
            {
                var lf = liniiFactura[l.IndexFactura];
                var pret = preturi && l.PretUnitar is { } p ? p : lf.PretUnitar;
                var ln = nir.Linii.First(x => x.Cheie == l.IndexAviz);
                if (!AceeasiUm(ln.UnitateMasura, lf.UnitateMasura) && (l.PretUnitar is null || !preturi))
                    avertismente.Add($"{nir.NumarAfisat} „{ln.Denumire}”: UM-ul din NIR ({ln.UnitateMasura}) diferă de cel din factură ({lf.UnitateMasura}); " +
                                     "prețul a fost preluat ca atare. Verifică valoarea (corectează NIR-ul dacă e nevoie).");
                return new LinieFacturareAviz(l.IndexAviz, l.IndexFactura, pret);
            }).ToList();

            var r = nir.Factureaza(f.Id, f.Numar, f.DataEmitere, linii, data < nir.Data ? nir.Data : data, uid, utilizator.Nume);
            if (r.Esec)
                return Results.BadRequest(new { mesaj = $"{nir.NumarAfisat}: {r.Eroare.Mesaj}" });
            if (r.Valoare is { } corectie)
            {
                db.CorectiiNir.Add(corectie);
                avertismente.AddRange(await DiferentaInStocAsync(db, nir, corectie, ct));
                corectate.Add(nir.NumarAfisat);
            }

            // Textul facturii → materialul NIR-ului, memorat pe furnizor: factura următoare îl recunoaște singură.
            foreach (var l in linii)
            {
                var text = liniiFactura[l.IndexFactura].Denumire;
                var cheie = PotrivireArticol.Cheie(text);
                var articol = nir.Linii.First(x => x.Cheie == l.IndexAviz).ArticolId;
                if (cheie.Length > 0 && !aliasuri.Any(a => a.FurnizorId == f.FurnizorId && a.Cheie == cheie)
                    && !db.Aliasuri.Local.Any(a => a.FurnizorId == f.FurnizorId && a.Cheie == cheie))
                    db.Aliasuri.Add(new AliasArticol(f.FurnizorId, text, articol));
            }
        }

        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(new
        {
            mesaj = $"Factura {f.Numar} a fost legată de {string.Join(", ", nirUri.Select(n => n.NumarAfisat))}" +
                    (corectate.Count > 0 ? $"; prețurile facturii au intrat în stoc ({string.Join(", ", corectate)})." : "."),
            avertismente,
            nir = nirUri.Select(n => ModulStocuri.Dto(n, preturi)).ToList(),
        });
    }

    /// <summary>
    /// Diferența de valoare a corecturii, pe material: în depozitul recepției dacă materialul mai e acolo, altfel în șantierul
    /// proiectului (unde a fost transferat); materialul consumat nu mai poate primi diferența.
    /// </summary>
    private static async Task<List<string>> DiferentaInStocAsync(StocuriDbContext db, Nir nir, CorectieNir corectie, CancellationToken ct)
    {
        var avertismente = new List<string>();
        var santier = await db.Gestiuni.AsNoTracking().FirstOrDefaultAsync(x => x.ProiectId == nir.ProiectId, ct);
        foreach (var g in corectie.Linii.GroupBy(l => l.ArticolId))
        {
            var dv = g.Sum(l => l.DiferentaValoare);
            if (dv == 0)
                continue;
            var aplicat = false;
            foreach (var gestiune in new[] { (Guid?)nir.GestiuneId, santier?.Id }.Where(x => x is not null).Select(x => x!.Value).Distinct())
            {
                var sold = await ModulStocuri.SoldAsync(db, gestiune, g.Key, ct);
                if (sold.Cantitate <= 0)
                    continue;
                var curent = sold.Sold;
                var nou = CalculStoc.Corectie(curent, 0, dv, g.First().Denumire);
                if (nou.Esec)
                    continue;
                sold.Seteaza(nou.Valoare);
                db.Miscari.Add(new MiscareStoc(gestiune, g.Key, corectie.Data, TipMiscare.CorectieNir, 0, nou.Valoare.Valoare - curent.Valoare, nir.Id, nir.NumarAfisat));
                aplicat = true;
                break;
            }

            if (!aplicat)
                avertismente.Add($"„{g.First().Denumire}” ({nir.NumarAfisat}): materialul nu mai e în depozit și nici în șantier (a fost consumat); " +
                                 $"diferența de {dv:N2} lei dintre prețul facturii și cel al recepției nu a mai intrat în stoc.");
        }

        return avertismente;
    }

    /// <summary>
    /// NIR-urile active pe aviz, încă fără factură, ale avizelor notei de comandă a facturii (fără notă: ale furnizorului, pe proiect).
    /// Avizele din urmă fără notă (D-45) ale aceluiași furnizor, pe proiect, se pot lega de orice factură a lui.
    /// </summary>
    private static async Task<(List<Nir> Nir, Dictionary<Guid, AvizPentruReceptie> Avize)> NirDeLegatAsync(
        StocuriDbContext db, FacturaPentruReceptie f, IEnumerable<IInterogareAvize> avize, CancellationToken ct)
    {
        if (avize.FirstOrDefault() is not { } q)
            return ([], []);
        var aleFurnizorului = (await q.ToateAsync(ct)).Where(a => a.FurnizorId == f.FurnizorId && a.ProiectId == f.ProiectId);
        var lista = f.NotaComandaId is { } nc
            ? (await q.AleNotelorAsync([nc], ct)).Concat(aleFurnizorului.Where(a => a.NotaComandaId is null)).ToList()
            : aleFurnizorului.ToList();
        var dupaId = lista.ToDictionary(a => a.Id);
        var ids = lista.Select(a => (Guid?)a.Id).ToList();
        var nir = ids.Count == 0
            ? []
            : await db.Nir.Where(n => ids.Contains(n.AvizId) && n.FacturaId == null && !db.StornariNir.Any(s => s.NirId == n.Id))
                .OrderBy(n => n.Data).ThenBy(n => n.Numar).ToListAsync(ct);
        return (nir, dupaId);
    }

    private static async Task<Dictionary<int, StareLinieReceptie>> StareFacturaAsync(StocuriDbContext db, FacturaPentruReceptie f, CancellationToken ct)
    {
        var receptii = await db.Nir.AsNoTracking().Where(n => n.FacturaId == f.Id && !db.StornariNir.Any(s => s.NirId == n.Id)).ToListAsync(ct);
        return StareReceptie.CalculeazaFactura(f.Linii.Select(l => (l.Index, l.Cantitate)), receptii).ToDictionary(s => s.Index);
    }

    private static async Task<(List<AliasCandidat>, List<ArticolCandidat>)> CandidatiAsync(StocuriDbContext db, CancellationToken ct) =>
        (await db.Aliasuri.AsNoTracking().Select(a => new AliasCandidat(a.FurnizorId, a.Cheie, a.ArticolId)).ToListAsync(ct),
         await db.Articole.AsNoTracking().Where(a => a.Activ).Select(a => new ArticolCandidat(a.Id, a.Cod, a.Denumire, a.UnitateMasura)).ToListAsync(ct));

    private static bool AceeasiUm(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('.'), b.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
}
