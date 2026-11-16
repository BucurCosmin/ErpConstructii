using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Contracte.Infrastructure;

public sealed record FacturaFisaDto(
    Guid Id, int Ordine, string Membru, Guid? FacturaId, string Numar, DateOnly Data, string Tip, int? TransaAvans, string? SituatiePlata, string? LunaExecutie,
    decimal Valoare, decimal StornoAvans, decimal? Curs, decimal NetRon, decimal TvaRon, decimal TotalRon, DateOnly? Scadenta, string? Observatii, decimal Sold);

public sealed record PlataDto(
    Guid Id, int Ordine, Guid? FacturaContractId, DateOnly Data, decimal SumaRon, string Instrument, string? Numar, DateOnly? Scadenta, bool Confirmata, string? Observatii);

/// <param name="Tip">Avans, Lucrari, Livrare, Altele.</param>
public sealed record FacturaFisaCerere(
    string? Membru, Guid? FacturaId, string? Numar, DateOnly Data, string? Tip, int? TransaAvans, string? SituatiePlata, string? LunaExecutie, decimal Valoare,
    decimal StornoAvans, decimal? Curs, decimal NetRon, decimal TvaRon, decimal TotalRon, DateOnly? Scadenta, string? Observatii);

/// <param name="Instrument">OrdinDePlata, BiletLaOrdin, Cec, Compensare, Altul.</param>
public sealed record PlataCerere(Guid? FacturaContractId, DateOnly Data, decimal SumaRon, string? Instrument, string? Numar, DateOnly? Scadenta, bool Confirmata, string? Observatii);

/// <summary>
/// Facturile și plățile din fișa contractului (D-56, etapa 2). Facturile firmei active vin din modulul Facturi (cele legate
/// de contract; rândul se propune din liniile lor: stornările = avans recuperat); cele ale celuilalt membru al asocierii
/// se introduc direct. Se editează oricând (evidența urmăririi); jurnalul de audit păstrează modificările.
/// </summary>
internal static class EndpointuriFacturiPlati
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Editare);
        const string baza = "/proiecte/{proiectId:guid}/{id:guid}";

        g.MapGet($"{baza}/facturi-fisa/propunere", Propunere).WithName("PropunereFacturaFisa")
            .WithSummary("Rândul propus pentru o factură legată de contract (?facturaId=&curs=): tip, lucrări / storno avans în moneda contractului, RON")
            .Produces<FacturaFisaCerere>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapPost($"{baza}/facturi-fisa", AdaugaFactura).WithName("AdaugaFacturaFisa").WithSummary("Adaugă o factură în fișa contractului")
            .Produces(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapPut($"{baza}/facturi-fisa/{{facturaFisaId:guid}}", ModificaFactura).WithName("ModificaFacturaFisa").WithSummary("Modifică o factură din fișă")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapDelete($"{baza}/facturi-fisa/{{facturaFisaId:guid}}", StergeFactura).WithName("StergeFacturaFisa")
            .WithSummary("Scoate factura din fișă (plățile ei rămân, neatribuite)").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare);
        g.MapPost($"{baza}/plati", AdaugaPlata).WithName("AdaugaPlataContract").WithSummary("Adaugă o plată (OP, bilet la ordin, CEC, compensare)")
            .Produces(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapPut($"{baza}/plati/{{plataId:guid}}", ModificaPlata).WithName("ModificaPlataContract").WithSummary("Modifică o plată (ex. biletul la ordin încasat → confirmată)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapDelete($"{baza}/plati/{{plataId:guid}}", StergePlata).WithName("StergePlataContract").WithSummary("Șterge o plată")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
    }

    internal static FacturaFisaDto Dto(FacturaContract f, decimal sold) => new(
        f.Id, f.Ordine, f.Membru, f.FacturaId, f.Numar, f.Data, f.Tip.ToString(), f.TransaAvans, f.SituatiePlata, f.LunaExecutie, f.Valoare, f.StornoAvans, f.Curs,
        f.NetRon, f.TvaRon, f.TotalRon, f.Scadenta, f.Observatii, sold);

    internal static PlataDto Dto(PlataContract p) =>
        new(p.Id, p.Ordine, p.FacturaContractId, p.Data, p.SumaRon, p.Instrument.ToString(), p.Numar, p.Scadenta, p.Confirmata, p.Observatii);

    private static async Task<ContractPartener?> ContractAsync(ContracteDbContext db, Guid proiectId, Guid id, CancellationToken ct) =>
        await db.Contracte.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.ProiectId == proiectId, ct);

    /// <summary>Membrul firmei active (facturile din modulul Facturi îi aparțin): cel marcat, altfel primul, altfel „Firma”.</summary>
    private static string MembruFirma(ContractPartener c) =>
        c.Membri.FirstOrDefault(m => m.EsteFirmaActiva)?.Denumire ?? c.Membri.FirstOrDefault()?.Denumire ?? "Firma";

    private static async Task<IResult> Propunere(
        Guid proiectId, Guid id, Guid facturaId, decimal? curs, ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi, CancellationToken ct)
    {
        var c = await ContractAsync(db, proiectId, id, ct);
        if (c is null)
            return Results.NotFound();
        if (facturi.FirstOrDefault() is not { } interogare || await interogare.PentruFisaAsync(facturaId, ct) is not { } f)
            return Results.NotFound(new { mesaj = "Factura nu există (sau e anulată)." });
        if (f.ContractId != c.Id)
            return Results.BadRequest(new { mesaj = $"Factura {f.Numar} nu e legată de contractul {c.NumarAfisat}." });
        var p = PropunereFacturaFisa.Din(f, MembruFirma(c), c.Moneda, curs, c.TermenPlataZile);
        return Results.Ok(new FacturaFisaCerere(p.Membru, p.FacturaId, p.Numar, p.Data, p.Tip.ToString(), p.TransaAvans, p.SituatiePlata, p.LunaExecutie,
            p.Valoare, p.StornoAvans, p.Curs, p.NetRon, p.TvaRon, p.TotalRon, p.Scadenta, p.Observatii));
    }

    private static Rezultat<DateFacturaContract> Date(FacturaFisaCerere c)
    {
        if (!Enum.TryParse<TipFacturaContract>(c.Tip ?? nameof(TipFacturaContract.Lucrari), true, out var tip))
            return Rezultat.Esuat<DateFacturaContract>(Eroare.Validare("Tipul facturii nu e valid (Avans, Lucrari, Livrare, Altele)."));
        return Rezultat.Ok(new DateFacturaContract(c.Membru ?? string.Empty, c.FacturaId, c.Numar ?? string.Empty, c.Data, tip, c.TransaAvans, c.SituatiePlata,
            c.LunaExecutie, c.Valoare, c.StornoAvans, c.Curs, c.NetRon, c.TvaRon, c.TotalRon, c.Scadenta, c.Observatii));
    }

    /// <summary>Factura din modulul Facturi trebuie să fie a contractului și să nu fie deja în fișă.</summary>
    private static async Task<string?> VerificaLegaturaAsync(
        ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi, ContractPartener c, Guid? facturaId, Guid? exceptRand, CancellationToken ct)
    {
        if (facturaId is not { } fid)
            return null;
        if (facturi.FirstOrDefault() is not { } interogare || await interogare.PentruFisaAsync(fid, ct) is not { } f)
            return "Factura din modulul Facturi nu există (sau e anulată).";
        if (f.ContractId != c.Id)
            return $"Factura {f.Numar} nu e legată de contractul {c.NumarAfisat}.";
        if (await db.FacturiFisa.AnyAsync(x => x.ContractId == c.Id && x.FacturaId == fid && x.Id != exceptRand, ct))
            return $"Factura {f.Numar} e deja în fișa contractului.";
        return null;
    }

    private static async Task<IResult> AdaugaFactura(
        Guid proiectId, Guid id, FacturaFisaCerere cerere, ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi, CancellationToken ct)
    {
        var c = await ContractAsync(db, proiectId, id, ct);
        if (c is null)
            return Results.NotFound();
        if (await VerificaLegaturaAsync(db, facturi, c, cerere.FacturaId, null, ct) is { } eroare)
            return Results.BadRequest(new { mesaj = eroare });
        var d = Date(cerere);
        if (d.Esec)
            return Results.BadRequest(new { mesaj = d.Eroare.Mesaj });
        var ordine = (await db.FacturiFisa.Where(x => x.ContractId == id).MaxAsync(x => (int?)x.Ordine, ct) ?? 0) + 1;
        var r = FacturaContract.Creeaza(id, ordine, d.Valoare);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.FacturiFisa.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/contracte/proiecte/{proiectId}/{id}", Dto(r.Valoare, r.Valoare.TotalRon));
    }

    private static async Task<IResult> ModificaFactura(
        Guid proiectId, Guid id, Guid facturaFisaId, FacturaFisaCerere cerere, ContracteDbContext db, IEnumerable<IInterogareFacturiContract> facturi, CancellationToken ct)
    {
        var c = await ContractAsync(db, proiectId, id, ct);
        var f = c is null ? null : await db.FacturiFisa.FirstOrDefaultAsync(x => x.Id == facturaFisaId && x.ContractId == id, ct);
        if (c is null || f is null)
            return Results.NotFound();
        if (await VerificaLegaturaAsync(db, facturi, c, cerere.FacturaId, f.Id, ct) is { } eroare)
            return Results.BadRequest(new { mesaj = eroare });
        var d = Date(cerere);
        var r = d.Succes ? f.Modifica(d.Valoare) : Rezultat.Esuat(d.Eroare);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> StergeFactura(Guid proiectId, Guid id, Guid facturaFisaId, ContracteDbContext db, CancellationToken ct)
    {
        if (await ContractAsync(db, proiectId, id, ct) is null)
            return Results.NotFound();
        var f = await db.FacturiFisa.FirstOrDefaultAsync(x => x.Id == facturaFisaId && x.ContractId == id, ct);
        if (f is null)
            return Results.NotFound();
        // Plățile facturii rămân (neatribuite), ca să nu se piardă evidența banilor.
        foreach (var p in await db.Plati.Where(p => p.FacturaContractId == f.Id).ToListAsync(ct))
            p.Modifica(new DatePlata(null, p.Data, p.SumaRon, p.Instrument, p.Numar, p.Scadenta, p.Confirmata, p.Observatii));
        db.FacturiFisa.Remove(f);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Rezultat<DatePlata> Date(PlataCerere c) =>
        Enum.TryParse<InstrumentPlata>(c.Instrument ?? nameof(InstrumentPlata.OrdinDePlata), true, out var instrument)
            ? Rezultat.Ok(new DatePlata(c.FacturaContractId, c.Data, c.SumaRon, instrument, c.Numar, c.Scadenta, c.Confirmata, c.Observatii))
            : Rezultat.Esuat<DatePlata>(Eroare.Validare("Instrumentul nu e valid (OrdinDePlata, BiletLaOrdin, Cec, Compensare, Altul)."));

    private static async Task<IResult> AdaugaPlata(Guid proiectId, Guid id, PlataCerere cerere, ContracteDbContext db, CancellationToken ct)
    {
        if (await ContractAsync(db, proiectId, id, ct) is null)
            return Results.NotFound();
        if (cerere.FacturaContractId is { } fid && !await db.FacturiFisa.AnyAsync(f => f.Id == fid && f.ContractId == id, ct))
            return Results.BadRequest(new { mesaj = "Factura aleasă nu e în fișa acestui contract." });
        var d = Date(cerere);
        if (d.Esec)
            return Results.BadRequest(new { mesaj = d.Eroare.Mesaj });
        var ordine = (await db.Plati.Where(x => x.ContractId == id).MaxAsync(x => (int?)x.Ordine, ct) ?? 0) + 1;
        var r = PlataContract.Creeaza(id, ordine, d.Valoare);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.Plati.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/contracte/proiecte/{proiectId}/{id}", Dto(r.Valoare));
    }

    private static async Task<IResult> ModificaPlata(Guid proiectId, Guid id, Guid plataId, PlataCerere cerere, ContracteDbContext db, CancellationToken ct)
    {
        if (await ContractAsync(db, proiectId, id, ct) is null)
            return Results.NotFound();
        var p = await db.Plati.FirstOrDefaultAsync(x => x.Id == plataId && x.ContractId == id, ct);
        if (p is null)
            return Results.NotFound();
        if (cerere.FacturaContractId is { } fid && !await db.FacturiFisa.AnyAsync(f => f.Id == fid && f.ContractId == id, ct))
            return Results.BadRequest(new { mesaj = "Factura aleasă nu e în fișa acestui contract." });
        var d = Date(cerere);
        var r = d.Succes ? p.Modifica(d.Valoare) : Rezultat.Esuat(d.Eroare);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> StergePlata(Guid proiectId, Guid id, Guid plataId, ContracteDbContext db, CancellationToken ct)
    {
        if (await ContractAsync(db, proiectId, id, ct) is null)
            return Results.NotFound();
        var p = await db.Plati.FirstOrDefaultAsync(x => x.Id == plataId && x.ContractId == id, ct);
        if (p is null)
            return Results.NotFound();
        db.Plati.Remove(p);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
