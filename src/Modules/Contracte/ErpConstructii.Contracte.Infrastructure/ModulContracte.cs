using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Parteneri;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Contracte.Infrastructure;

// ---------------------------------------------------------------- DTO-uri

public sealed record LinieContractDto(string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal Valoare);

/// <param name="PartenerId">Partenerul din nomenclator; fără el, se caută / adaugă după <paramref name="PartenerDenumire"/> și <paramref name="PartenerCui"/>.</param>
/// <param name="ProcentGarantie">Fracție (0,05 = 5%).</param>
public sealed record ContractCerere(
    string? NumarExtern, DateOnly DataSemnarii, Guid? PartenerId, string? PartenerDenumire, string? PartenerCui, string Tip, string Obiect, string? Moneda,
    decimal Valoare, IReadOnlyList<LinieContractDto>? Linii, DateOnly? DataInceput, DateOnly? DataSfarsit, int? TermenPlataZile, decimal ProcentGarantie,
    decimal Avans, bool TaxareInversa, string? Observatii);

public sealed record SituatieContractDto(
    decimal ValoareInitiala, decimal ValoareActualizata, decimal Facturat, decimal Rest, decimal ProcentFacturat, decimal GarantieRetinuta, bool Depasit,
    DateOnly? Termen, bool TermenDepasit);

public sealed record ActAditionalDto(Guid Id, int Numar, string? NumarExtern, DateOnly Data, string Obiect, decimal DiferentaValoare, DateOnly? DataSfarsitNoua,
    string CreatDe, DateTimeOffset CreatLa);

public sealed record InchidereDto(string Tip, DateOnly Data, string? Motiv, string InchisDe);

public sealed record DocumentContractDto(Guid Id, string NumeFisier, long Dimensiune, Guid? ActAditionalId, string IncarcatDe, DateTimeOffset IncarcatLa);

public sealed record FacturaContractDto(Guid Id, string Numar, DateOnly Data, decimal TotalFaraTva, string Moneda);

/// <param name="Stare">Ciorna, Activ, Finalizat sau Reziliat.</param>
public sealed record ContractDto(
    Guid Id, string Numar, string? NumarExtern, DateOnly DataSemnarii, Guid ProiectId, string ProiectCod, Guid PartenerId, string PartenerDenumire,
    string? PartenerCui, string Tip, string Obiect, string Moneda, decimal Valoare, IReadOnlyList<LinieContractDto> Linii, DateOnly? DataInceput,
    DateOnly? DataSfarsit, int? TermenPlataZile, decimal ProcentGarantie, decimal Avans, bool TaxareInversa, string? Observatii, string Stare,
    string CreatDe, DateTimeOffset CreatLa, DateTimeOffset? ActivatLa, string? ActivatDe, InchidereDto? Inchidere, SituatieContractDto Situatie,
    IReadOnlyList<ActAditionalDto>? Acte = null, IReadOnlyList<FacturaContractDto>? Facturi = null, IReadOnlyList<DocumentContractDto>? Documente = null);

public sealed record ActAditionalCerere(string? NumarExtern, DateOnly Data, string Obiect, decimal DiferentaValoare, DateOnly? DataSfarsitNoua);

public sealed record InchidereCerere(string Tip, DateOnly Data, string? Motiv);

public sealed record PartenerContractDto(Guid Id, string Denumire, string? Cui, bool EsteSubantreprenor, bool EsteFurnizor);

/// <summary>
/// Contractele proiectului cu subantreprenori și parteneri (D-41): ciornă → semnat (activ, imuabil) → acte adiționale →
/// închidere (finalizat / reziliat); documentele semnate atașate; situația (facturat din facturile legate, rest, garanția
/// reținută, termenul). Facturile de servicii se leagă de contract la import (modulul Facturi). Drepturi: <c>contracte.*</c>
/// pe proiect; semnarea și închiderea cer <c>contracte.aprobare</c>.
/// </summary>
public sealed class ModulContracte : IModul
{
    public string Nume => ContracteDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AdaugaDbContextModul<ContracteDbContext>(configurare, Nume);
        servicii.AddScoped<IInterogareContracte, InterogareContracte>();
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/contracte").WithTags("Contracte");
        var vizualizare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Creare);
        var editare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Editare);
        var aprobare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Aprobare);

        g.MapGet("/", Lista).WithName("ListaContracte").WithSummary("Contractele din proiectele accesibile, cu situația lor")
            .Produces<IReadOnlyList<ContractDto>>().CerePermisiune(vizualizare);
        g.MapGet("/parteneri", Parteneri).WithName("ParteneriContracte").WithSummary("Partenerii activi, pentru alegerea pe contract")
            .Produces<IReadOnlyList<PartenerContractDto>>().CerePermisiune(vizualizare);
        g.MapPost("/proiecte/{proiectId:guid}", Creeaza).WithName("CreeazaContract").WithSummary("Contract nou (ciornă) pe proiect")
            .Produces<ContractDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).CerePermisiune(creare);
        g.MapGet("/proiecte/{proiectId:guid}/{id:guid}", Detalii).WithName("DetaliiContract")
            .WithSummary("Contractul, cu actele adiționale, facturile legate și documentele").Produces<ContractDto>().Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);
        g.MapPut("/proiecte/{proiectId:guid}/{id:guid}", Modifica).WithName("ModificaContract").WithSummary("Modifică o ciornă de contract")
            .Produces<ContractDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(editare);
        g.MapDelete("/proiecte/{proiectId:guid}/{id:guid}", Sterge).WithName("StergeContract").WithSummary("Șterge o ciornă de contract")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).CerePermisiune(editare);
        g.MapPost("/proiecte/{proiectId:guid}/{id:guid}/activare", Activeaza).WithName("ActiveazaContract")
            .WithSummary("Contractul a fost semnat: devine activ (nu se mai modifică decât prin act adițional)")
            .Produces<ContractDto>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).CerePermisiune(aprobare);
        g.MapPost("/proiecte/{proiectId:guid}/{id:guid}/acte-aditionale", AdaugaAct).WithName("AdaugaActAditional")
            .WithSummary("Act adițional: valoare suplimentară / diminuată, termen prelungit")
            .Produces<ContractDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare);
        g.MapPost("/proiecte/{proiectId:guid}/{id:guid}/inchidere", Inchide).WithName("InchideContract")
            .WithSummary("Finalizează sau reziliază contractul: nu se mai leagă facturi noi").Produces<ContractDto>()
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(aprobare);
        g.MapPost("/proiecte/{proiectId:guid}/{id:guid}/documente", IncarcaDocument).WithName("IncarcaDocumentContract")
            .WithSummary("Atașează contractul semnat, un act adițional scanat sau o anexă").Produces<DocumentContractDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentContract.DimensiuneMaxima + 100_000)).CerePermisiune(editare);
        g.MapGet("/proiecte/{proiectId:guid}/{id:guid}/documente/{documentId:guid}", DescarcaDocument).WithName("DescarcaDocumentContract")
            .WithSummary("Descarcă un document al contractului").Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);
    }

    // ---------------------------------------------------------------- lista și detaliile

    /// <param name="stare">Ciorna, Activ, Finalizat, Reziliat sau Deschise (active neînchise).</param>
    private static async Task<IResult> Lista(
        Guid? proiectId, Guid? partenerId, string? stare, string? cauta, ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi,
        IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        var accesibile = (await permisiuni.PentruUtilizatorAsync(uid, ct)).ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Contracte, Actiune.Vizualizare), fid);
        var q = db.Contracte.AsNoTracking();
        if (accesibile is not null)
        {
            var ids = accesibile.ToList();
            q = q.Where(c => ids.Contains(c.ProiectId));
        }

        if (proiectId is { } pid) q = q.Where(c => c.ProiectId == pid);
        if (partenerId is { } prt) q = q.Where(c => c.PartenerId == prt);
        if (!string.IsNullOrWhiteSpace(cauta))
        {
            var t = $"%{cauta.Trim()}%";
            var cifre = new string(cauta.Where(char.IsAsciiDigit).ToArray());
            var numar = cifre.Length is > 0 and < 7 ? int.Parse(cifre, System.Globalization.CultureInfo.InvariantCulture) : -1;
            q = q.Where(c => EF.Functions.ILike(c.PartenerDenumire, t) || EF.Functions.ILike(c.Obiect, t) || (c.NumarExtern != null && EF.Functions.ILike(c.NumarExtern, t)) ||
                             c.Numar == numar);
        }

        var lista = await q.OrderByDescending(c => c.DataSemnarii).ThenByDescending(c => c.Numar).Take(2000).ToListAsync(ct);
        var dto = await DtoAsync(db, lista, facturi, ceas, detalii: false, ct);
        return Results.Ok(string.IsNullOrWhiteSpace(stare) ? dto
            : string.Equals(stare, "Deschise", StringComparison.OrdinalIgnoreCase) ? dto.Where(c => c.Stare == "Activ").ToList()
            : dto.Where(c => string.Equals(c.Stare, stare, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private static async Task<IResult> Detalii(
        Guid proiectId, Guid id, ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi, ICeas ceas, CancellationToken ct)
    {
        var c = await db.Contracte.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        return c is null ? Results.NotFound() : Results.Ok((await DtoAsync(db, [c], facturi, ceas, detalii: true, ct))[0]);
    }

    private static async Task<IResult> Parteneri(IServiciuParteneri parteneri, CancellationToken ct) =>
        Results.Ok((await parteneri.ActiviAsync(ct)).Select(p => new PartenerContractDto(p.Id, p.Denumire, p.Cui, p.EsteSubantreprenor, p.EsteFurnizor)).ToList());

    // ---------------------------------------------------------------- ciorna

    private static async Task<IResult> Creeaza(
        Guid proiectId, ContractCerere cerere, ContracteDbContext db, IInterogareProiecte proiecte, IServiciuParteneri parteneri,
        IEnumerable<IInterogareFacturiContract> facturi, IFirmaCurenta firma, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        if (proiect.Arhivat)
            return Results.Conflict(new { mesaj = proiect.MesajArhivat });

        var (date, eroare) = await DateAsync(cerere, proiect.Id, proiect.Cod, parteneri, ct);
        if (eroare is not null)
            return Results.BadRequest(new { mesaj = eroare });

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await BlocheazaAsync(db, tranzactie, fid, ct);
        var an = date!.DataSemnarii.Year;
        var numar = (await db.Contracte.Where(c => c.An == an).MaxAsync(c => (int?)c.Numar, ct) ?? 0) + 1;
        var r = ContractPartener.Creeaza(an, numar, date, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.Contracte.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created($"/api/contracte/proiecte/{proiectId}/{r.Valoare.Id}", (await DtoAsync(db, [r.Valoare], facturi, ceas, true, ct))[0]);
    }

    private static async Task<IResult> Modifica(
        Guid proiectId, Guid id, ContractCerere cerere, ContracteDbContext db, IServiciuParteneri parteneri, IEnumerable<IInterogareFacturiContract> facturi,
        ICeas ceas, CancellationToken ct)
    {
        var c = await db.Contracte.FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (c is null)
            return Results.NotFound();
        if (c.Stare != StareContract.Ciorna)
            return Results.Conflict(new { mesaj = $"Contractul {c.NumarAfisat} e semnat: se modifică doar prin act adițional." });
        var (date, eroare) = await DateAsync(cerere, c.ProiectId, c.ProiectCod, parteneri, ct);
        if (eroare is not null)
            return Results.BadRequest(new { mesaj = eroare });
        var r = c.Modifica(date!);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.Ok((await DtoAsync(db, [c], facturi, ceas, true, ct))[0]);
    }

    private static async Task<IResult> Sterge(Guid proiectId, Guid id, ContracteDbContext db, CancellationToken ct)
    {
        var c = await db.Contracte.FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (c is null)
            return Results.NotFound();
        if (c.Stare != StareContract.Ciorna)
            return Results.Conflict(new { mesaj = $"Contractul {c.NumarAfisat} e semnat: nu se șterge (se finalizează sau se reziliază)." });
        db.Documente.RemoveRange(await db.Documente.Where(d => d.ContractId == id).ToListAsync(ct));
        db.Contracte.Remove(c);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- semnare, acte adiționale, închidere

    private static async Task<IResult> Activeaza(
        Guid proiectId, Guid id, ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi, IUtilizatorCurent utilizator, ICeas ceas,
        CancellationToken ct)
    {
        var c = await db.Contracte.FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (c is null)
            return Results.NotFound();
        var r = c.Activeaza(utilizator.Nume ?? string.Empty, ceas.Acum);
        if (r.Esec)
            return Results.Conflict(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.Ok((await DtoAsync(db, [c], facturi, ceas, true, ct))[0]);
    }

    private static async Task<IResult> AdaugaAct(
        Guid proiectId, Guid id, ActAditionalCerere cerere, ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi, IFirmaCurenta firma,
        IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await BlocheazaAsync(db, tranzactie, fid, ct);
        var c = await db.Contracte.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (c is null)
            return Results.NotFound();
        var acte = await db.ActeAditionale.AsNoTracking().Where(a => a.ContractId == id).ToListAsync(ct);
        var inchidere = await db.Inchideri.AsNoTracking().FirstOrDefaultAsync(i => i.ContractId == id, ct);
        var r = ActAditional.Creeaza(c, inchidere, acte.Count == 0 ? 1 : acte.Max(a => a.Numar) + 1, cerere.NumarExtern, cerere.Data, cerere.Obiect ?? string.Empty,
            cerere.DiferentaValoare, cerere.DataSfarsitNoua, c.Valoare + acte.Sum(a => a.DiferentaValoare), utilizator.Nume ?? string.Empty);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.ActeAditionale.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created($"/api/contracte/proiecte/{proiectId}/{id}", (await DtoAsync(db, [c], facturi, ceas, true, ct))[0]);
    }

    private static async Task<IResult> Inchide(
        Guid proiectId, Guid id, InchidereCerere cerere, ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi, IUtilizatorCurent utilizator,
        ICeas ceas, CancellationToken ct)
    {
        var c = await db.Contracte.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (c is null)
            return Results.NotFound();
        if (!Enum.TryParse<TipInchidere>(cerere.Tip, true, out var tip))
            return Results.BadRequest(new { mesaj = "Alege „Finalizat” sau „Reziliat”." });
        if (cerere.Data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data închiderii nu poate fi în viitor." });
        var r = InchidereContract.Creeaza(c, await db.Inchideri.AnyAsync(i => i.ContractId == id, ct), tip, cerere.Data, cerere.Motiv, utilizator.Nume ?? string.Empty);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.Inchideri.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await DtoAsync(db, [c], facturi, ceas, true, ct))[0]);
    }

    // ---------------------------------------------------------------- documente

    private static async Task<IResult> IncarcaDocument(
        Guid proiectId, Guid id, IFormFile fisier, [FromForm] Guid? actAditionalId, ContracteDbContext db, IUtilizatorCurent utilizator, CancellationToken ct)
    {
        if (!await db.Contracte.AnyAsync(c => c.Id == id && c.ProiectId == proiectId, ct))
            return Results.NotFound();
        if (fisier is null || fisier.Length == 0)
            return Results.BadRequest(new { mesaj = "Alege fișierul." });
        if (fisier.Length > DocumentContract.DimensiuneMaxima)
            return Results.BadRequest(new { mesaj = "Fișierul depășește 25 MB." });
        if (actAditionalId is { } act && !await db.ActeAditionale.AnyAsync(a => a.Id == act && a.ContractId == id, ct))
            return Results.BadRequest(new { mesaj = "Actul adițional ales nu e al acestui contract." });
        using var ms = new MemoryStream();
        await fisier.CopyToAsync(ms, ct);
        var nume = Path.GetFileName(fisier.FileName);
        var d = new DocumentContract(id, actAditionalId, nume.Length > 260 ? nume[^260..] : nume,
            string.IsNullOrWhiteSpace(fisier.ContentType) ? "application/octet-stream" : fisier.ContentType, ms.ToArray(), utilizator.Nume ?? "?");
        db.Documente.Add(d);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/contracte/proiecte/{proiectId}/{id}/documente/{d.Id}",
            new DocumentContractDto(d.Id, d.NumeFisier, d.Dimensiune, d.ActAditionalId, d.IncarcatDeNume, d.CreatLa));
    }

    private static async Task<IResult> DescarcaDocument(Guid proiectId, Guid id, Guid documentId, ContracteDbContext db, CancellationToken ct)
    {
        if (!await db.Contracte.AnyAsync(c => c.Id == id && c.ProiectId == proiectId, ct))
            return Results.NotFound();
        var d = await db.Documente.AsNoTracking().FirstOrDefaultAsync(x => x.Id == documentId && x.ContractId == id, ct);
        return d is null ? Results.NotFound() : Results.File(d.Continut, d.TipContinut, d.NumeFisier, enableRangeProcessing: true);
    }

    // ---------------------------------------------------------------- utilitare

    private static async Task<(DateContract? Date, string? Eroare)> DateAsync(
        ContractCerere c, Guid proiectId, string proiectCod, IServiciuParteneri parteneri, CancellationToken ct)
    {
        if (!Enum.TryParse<TipContract>(c.Tip, true, out var tip) || !Enum.IsDefined(tip))
            return (null, "Tipul contractului nu este valid.");
        PartenerRezumat? partener;
        if (c.PartenerId is { } pid)
        {
            partener = await parteneri.CautaDupaIdAsync(pid, ct);
            if (partener is null)
                return (null, "Partenerul ales nu există.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(c.PartenerDenumire))
                return (null, "Partenerul este obligatoriu.");
            var cui = string.IsNullOrWhiteSpace(c.PartenerCui) ? null : c.PartenerCui.Trim();
            if (cui is not null && !ValidareFiscala.EsteCuiValid(cui))
                return (null, $"CUI-ul „{cui}” nu este valid.");
            partener = await parteneri.CautaFurnizorAsync(cui, c.PartenerDenumire, ct);
            if (partener is null)
            {
                var r = await parteneri.AsiguraFurnizorAsync(c.PartenerDenumire.Trim(), cui, cui?.StartsWith("RO", StringComparison.OrdinalIgnoreCase) == true, ct);
                if (r.Esec)
                    return (null, r.Eroare.Mesaj);
                partener = r.Valoare;
            }
        }

        return (new DateContract(
            c.NumarExtern, c.DataSemnarii, proiectId, proiectCod, partener.Id, partener.Denumire, partener.Cui, tip, c.Obiect ?? string.Empty,
            string.IsNullOrWhiteSpace(c.Moneda) ? "RON" : c.Moneda, c.Valoare,
            (c.Linii ?? []).Select(l => new LinieContract(l.Denumire ?? string.Empty, l.UnitateMasura ?? string.Empty, l.Cantitate, l.PretUnitar, 0)).ToList(),
            c.DataInceput, c.DataSfarsit, c.TermenPlataZile, c.ProcentGarantie, c.Avans, c.TaxareInversa, c.Observatii), null);
    }

    private static async Task<List<ContractDto>> DtoAsync(
        ContracteDbContext db, IReadOnlyList<ContractPartener> contracte, IEnumerable<IInterogareFacturiContract> facturi, ICeas ceas, bool detalii, CancellationToken ct)
    {
        var ids = contracte.Select(c => c.Id).ToList();
        var acte = (await db.ActeAditionale.AsNoTracking().Where(a => ids.Contains(a.ContractId)).OrderBy(a => a.Numar).ToListAsync(ct)).ToLookup(a => a.ContractId);
        var inchideri = await db.Inchideri.AsNoTracking().Where(i => ids.Contains(i.ContractId)).ToDictionaryAsync(i => i.ContractId, ct);
        var peContract = facturi.FirstOrDefault() is { } f ? await f.PeContracteAsync(ids, ct) : new Dictionary<Guid, IReadOnlyList<FacturaPeContract>>();
        var documente = detalii
            ? (await db.Documente.AsNoTracking().Where(d => ids.Contains(d.ContractId))
                .Select(d => new { d.Id, d.ContractId, d.NumeFisier, d.Dimensiune, d.ActAditionalId, d.IncarcatDeNume, d.CreatLa }).ToListAsync(ct))
                .ToLookup(d => d.ContractId)
            : null;
        return contracte.Select(c =>
        {
            var inchidere = inchideri.GetValueOrDefault(c.Id);
            var fact = peContract.GetValueOrDefault(c.Id) ?? [];
            var s = SituatieContract.Calculeaza(c, acte[c.Id], fact, inchidere is not null, ceas.Azi);
            return new ContractDto(
                c.Id, c.NumarAfisat, c.NumarExtern, c.DataSemnarii, c.ProiectId, c.ProiectCod, c.PartenerId, c.PartenerDenumire, c.PartenerCui, c.Tip.ToString(),
                c.Obiect, c.Moneda, c.Valoare, c.Linii.Select(l => new LinieContractDto(l.Denumire, l.UnitateMasura, l.Cantitate, l.PretUnitar, l.Valoare)).ToList(),
                c.DataInceput, c.DataSfarsit, c.TermenPlataZile, c.ProcentGarantie, c.Avans, c.TaxareInversa, c.Observatii,
                inchidere?.Tip.ToString() ?? c.Stare.ToString(), c.CreatDeNume, c.CreatLa, c.ActivatLa, c.ActivatDeNume,
                inchidere is null ? null : new InchidereDto(inchidere.Tip.ToString(), inchidere.Data, inchidere.Motiv, inchidere.InchisDeNume),
                new SituatieContractDto(s.ValoareInitiala, s.ValoareActualizata, s.Facturat, s.Rest, s.ProcentFacturat, s.GarantieRetinuta, s.Depasit, s.Termen, s.TermenDepasit),
                detalii ? acte[c.Id].Select(a => new ActAditionalDto(a.Id, a.Numar, a.NumarExtern, a.Data, a.Obiect, a.DiferentaValoare, a.DataSfarsitNoua, a.CreatDeNume, a.CreatLa)).ToList() : null,
                detalii ? fact.OrderBy(x => x.Data).Select(x => new FacturaContractDto(x.Id, x.Numar, x.Data, x.TotalFaraTva, x.Moneda)).ToList() : null,
                detalii ? documente![c.Id].Select(d => new DocumentContractDto(d.Id, d.NumeFisier, d.Dimensiune, d.ActAditionalId, d.IncarcatDeNume, d.CreatLa)).ToList() : null);
        }).ToList();
    }

    /// <summary>Numerotarea și actele adiționale ale unei firme se serializează.</summary>
    private static async Task BlocheazaAsync(ContracteDbContext db, IDbContextTransaction tranzactie, Guid firmaId, CancellationToken ct)
    {
        await using var comanda = db.Database.GetDbConnection().CreateCommand();
        comanda.Transaction = tranzactie.GetDbTransaction();
        comanda.CommandText = "SELECT pg_advisory_xact_lock(hashtext(@cheie))";
        var p = comanda.CreateParameter();
        p.ParameterName = "cheie";
        p.Value = firmaId.ToString("N") + ":contracte";
        comanda.Parameters.Add(p);
        await comanda.ExecuteScalarAsync(ct);
    }
}
