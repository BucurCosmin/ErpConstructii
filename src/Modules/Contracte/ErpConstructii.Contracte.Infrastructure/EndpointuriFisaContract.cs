using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Contracte.Infrastructure;

public sealed record ObligatieDto(Guid Id, int Ordine, string Denumire, string? Temei, string? Termen, DateOnly? DataTermen, string? Stare, string? Responsabil, string? Observatii);

public sealed record ActiuneDto(Guid Id, int Ordine, string Descriere, string Prioritate, string Stare, Guid? ResponsabilId, string? ResponsabilNume, DateOnly? Termen, string? Observatii);

public sealed record FazaDto(Guid Id, int Ordine, string Denumire, DateOnly? Start, DateOnly? Final, string? Conditii, string? Stare, string? Observatii);

public sealed record RiscDto(Guid Id, int Ordine, string Element, string? Prevedere, string? Stare);

public sealed record InstrumentDto(
    Guid Id, int Ordine, string Tip, string? Descriere, string? Emitent, string? Numar, string? Beneficiar, decimal? Valoare, string? Moneda,
    DateOnly? DataEmitere, DateOnly? DataExpirare, string? Stare, string? Observatii);

public sealed record SursaDto(Guid Id, int Ordine, string Document, DateOnly? Data, string? Observatii);

public sealed record CorectieDto(Guid Id, string Motiv, IReadOnlyList<string> Modificari, string CorectatDe, DateTimeOffset La);

/// <summary>Registrele fișei de urmărire a contractului.</summary>
public sealed record RegistreFisaDto(
    IReadOnlyList<ObligatieDto> Obligatii, IReadOnlyList<ActiuneDto> Actiuni, IReadOnlyList<FazaDto> Faze, IReadOnlyList<RiscDto> Riscuri,
    IReadOnlyList<InstrumentDto> Instrumente, IReadOnlyList<CorectieDto> Corecturi, IReadOnlyList<SursaDto>? Surse = null);

/// <summary>
/// Un rând al unui registru al fișei; câmpurile folosite depind de registru: obligatii (denumire, temei, termen, dataTermen,
/// stare, responsabil, observatii), actiuni (descriere, prioritate, stare, responsabilId, responsabil, dataTermen,
/// observatii), faze (denumire, start, final, conditii, stare, observatii), riscuri (element, prevedere, stare), instrumente
/// (tip, descriere, emitent, numar, beneficiar, valoare, moneda, dataEmitere, dataExpirare, stare, observatii).
/// </summary>
public sealed record RandFisaCerere(
    string? Denumire = null, string? Temei = null, string? Termen = null, DateOnly? DataTermen = null, string? Stare = null, string? Responsabil = null,
    Guid? ResponsabilId = null, string? Observatii = null, string? Descriere = null, string? Prioritate = null, DateOnly? Start = null,
    DateOnly? Final = null, string? Conditii = null, string? Element = null, string? Prevedere = null, string? Tip = null, string? Emitent = null,
    string? Numar = null, string? Beneficiar = null, decimal? Valoare = null, string? Moneda = null, DateOnly? DataEmitere = null,
    DateOnly? DataExpirare = null, int? Ordine = null);

public sealed record SemnareActCerere(DateOnly Data);

/// <summary>
/// Registrele fișei de urmărire (D-56): obligații și termene, acțiuni, faze de execuție, aspecte de clarificat, instrumente
/// de garanție. Se editează oricând (și pe contractele semnate / închise: sunt evidența urmăririi, nu condițiile
/// contractului); modificările rămân în jurnalul de audit. Plus semnarea actelor adiționale propuse.
/// </summary>
internal static class EndpointuriFisaContract
{
    private static readonly string[] Registre = ["obligatii", "actiuni", "faze", "riscuri", "instrumente", "surse"];

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var editare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Editare);
        var aprobare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Aprobare);

        g.MapPost("/proiecte/{proiectId:guid}/{id:guid}/fisa/{registru}", Adauga).WithName("AdaugaRandFisa")
            .WithSummary("Adaugă un rând într-un registru al fișei: obligatii | actiuni | faze | riscuri | instrumente")
            .Produces<RegistreFisaDto>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare);
        g.MapPut("/proiecte/{proiectId:guid}/{id:guid}/fisa/{registru}/{randId:guid}", Modifica).WithName("ModificaRandFisa")
            .WithSummary("Modifică un rând al registrului").Produces<RegistreFisaDto>().Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapDelete("/proiecte/{proiectId:guid}/{id:guid}/fisa/{registru}/{randId:guid}", Sterge).WithName("StergeRandFisa")
            .WithSummary("Șterge un rând al registrului").Produces<RegistreFisaDto>().Produces(StatusCodes.Status404NotFound).CerePermisiune(editare);
        g.MapPost("/proiecte/{proiectId:guid}/{id:guid}/acte-aditionale/{actId:guid}/semnare", SemneazaAct).WithName("SemneazaActAditional")
            .WithSummary("Actul adițional propus a fost semnat: intră în valoarea și termenul contractului")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(aprobare);
    }

    /// <summary>Registrele contractelor date (pentru detalii și export).</summary>
    internal static async Task<Dictionary<Guid, RegistreFisaDto>> IncarcaAsync(ContracteDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var obligatii = (await db.Obligatii.AsNoTracking().Where(x => ids.Contains(x.ContractId)).OrderBy(x => x.Ordine).ToListAsync(ct)).ToLookup(x => x.ContractId);
        var actiuni = (await db.Actiuni.AsNoTracking().Where(x => ids.Contains(x.ContractId)).OrderBy(x => x.Ordine).ToListAsync(ct)).ToLookup(x => x.ContractId);
        var faze = (await db.Faze.AsNoTracking().Where(x => ids.Contains(x.ContractId)).OrderBy(x => x.Ordine).ToListAsync(ct)).ToLookup(x => x.ContractId);
        var riscuri = (await db.Riscuri.AsNoTracking().Where(x => ids.Contains(x.ContractId)).OrderBy(x => x.Ordine).ToListAsync(ct)).ToLookup(x => x.ContractId);
        var instrumente = (await db.Instrumente.AsNoTracking().Where(x => ids.Contains(x.ContractId)).OrderBy(x => x.Ordine).ToListAsync(ct)).ToLookup(x => x.ContractId);
        var surse = (await db.Surse.AsNoTracking().Where(x => ids.Contains(x.ContractId)).OrderBy(x => x.Ordine).ToListAsync(ct)).ToLookup(x => x.ContractId);
        var corecturi = (await db.Corecturi.AsNoTracking().Where(x => ids.Contains(x.ContractId)).OrderByDescending(x => x.La).ToListAsync(ct)).ToLookup(x => x.ContractId);
        return ids.ToDictionary(id => id, id => new RegistreFisaDto(
            obligatii[id].Select(x => new ObligatieDto(x.Id, x.Ordine, x.Denumire, x.Temei, x.Termen, x.DataTermen, x.Stare, x.Responsabil, x.Observatii)).ToList(),
            actiuni[id].Select(x => new ActiuneDto(x.Id, x.Ordine, x.Descriere, x.Prioritate.ToString(), x.Stare.ToString(), x.ResponsabilId, x.ResponsabilNume, x.Termen, x.Observatii)).ToList(),
            faze[id].Select(x => new FazaDto(x.Id, x.Ordine, x.Denumire, x.Start, x.Final, x.Conditii, x.Stare, x.Observatii)).ToList(),
            riscuri[id].Select(x => new RiscDto(x.Id, x.Ordine, x.Element, x.Prevedere, x.Stare)).ToList(),
            instrumente[id].Select(x => new InstrumentDto(x.Id, x.Ordine, x.Tip.ToString(), x.Descriere, x.Emitent, x.Numar, x.Beneficiar, x.Valoare, x.Moneda,
                x.DataEmitere, x.DataExpirare, x.Stare, x.Observatii)).ToList(),
            corecturi[id].Select(x => new CorectieDto(x.Id, x.Motiv, x.Modificari.Split('\n', StringSplitOptions.RemoveEmptyEntries), x.CorectatDeNume, x.La)).ToList(),
            surse[id].Select(x => new SursaDto(x.Id, x.Ordine, x.Document, x.Data, x.Observatii)).ToList()));
    }

    /// <summary>Creează rândul registrului din cerere (validarea e în domeniu).</summary>
    internal static Rezultat<RandFisaContract> Creeaza(string registru, Guid contractId, int ordine, RandFisaCerere c) => registru switch
    {
        "obligatii" => Ca(ObligatieContract.Creeaza(contractId, ordine, Obligatie(c))),
        "actiuni" => DinActiune(c) is { Succes: true } a ? Ca(ActiuneContract.Creeaza(contractId, ordine, a.Valoare)) : Rezultat.Esuat<RandFisaContract>(DinActiune(c).Eroare),
        "faze" => Ca(FazaExecutie.Creeaza(contractId, ordine, Faza(c))),
        "riscuri" => Ca(RiscContract.Creeaza(contractId, ordine, Risc(c))),
        "instrumente" => Instrument(c) is { Succes: true } i ? Ca(InstrumentGarantie.Creeaza(contractId, ordine, i.Valoare)) : Rezultat.Esuat<RandFisaContract>(Instrument(c).Eroare),
        "surse" => Ca(SursaContract.Creeaza(contractId, ordine, Sursa(c))),
        _ => Rezultat.Esuat<RandFisaContract>(Eroare.NuExista("Registrul")),
    };

    private static Rezultat<RandFisaContract> Ca<T>(Rezultat<T> r) where T : RandFisaContract =>
        r.Succes ? Rezultat.Ok<RandFisaContract>(r.Valoare) : Rezultat.Esuat<RandFisaContract>(r.Eroare);

    private static DateObligatie Obligatie(RandFisaCerere c) =>
        new(c.Denumire ?? string.Empty, c.Temei, c.Termen, c.DataTermen, c.Stare, c.Responsabil, c.Observatii);

    private static Rezultat<DateActiune> DinActiune(RandFisaCerere c)
    {
        var prioritate = PrioritateActiune.Normala;
        var stare = StareActiune.Deschisa;
        if (!string.IsNullOrWhiteSpace(c.Prioritate) && !Enum.TryParse(c.Prioritate, true, out prioritate))
            return Rezultat.Esuat<DateActiune>(Eroare.Validare("Prioritatea nu e validă (Critica, Ridicata, Medie, Normala)."));
        if (!string.IsNullOrWhiteSpace(c.Stare) && !Enum.TryParse(c.Stare, true, out stare))
            return Rezultat.Esuat<DateActiune>(Eroare.Validare("Starea nu e validă (Deschisa, InLucru, Inchisa)."));
        return Rezultat.Ok(new DateActiune(c.Descriere ?? string.Empty, prioritate, stare, c.ResponsabilId, c.Responsabil, c.DataTermen, c.Observatii));
    }

    private static DateFaza Faza(RandFisaCerere c) => new(c.Denumire ?? string.Empty, c.Start, c.Final, c.Conditii, c.Stare, c.Observatii);

    private static DateRisc Risc(RandFisaCerere c) => new(c.Element ?? string.Empty, c.Prevedere, c.Stare);

    private static DateSursa Sursa(RandFisaCerere c) => new(c.Denumire ?? string.Empty, c.DataTermen, c.Observatii);

    private static Rezultat<DateInstrument> Instrument(RandFisaCerere c)
    {
        var tip = TipInstrument.Altul;
        if (!string.IsNullOrWhiteSpace(c.Tip) && !Enum.TryParse(c.Tip, true, out tip))
            return Rezultat.Esuat<DateInstrument>(Eroare.Validare("Tipul instrumentului nu e valid."));
        return Rezultat.Ok(new DateInstrument(tip, c.Descriere, c.Emitent, c.Numar, c.Beneficiar, c.Valoare, c.Moneda, c.DataEmitere, c.DataExpirare,
            c.Stare, c.Observatii));
    }

    private static Rezultat ModificaRand(RandFisaContract r, RandFisaCerere c) => r switch
    {
        ObligatieContract o => o.Modifica(Obligatie(c)),
        ActiuneContract a => DinActiune(c) is { Succes: true } d ? a.Modifica(d.Valoare) : Rezultat.Esuat(DinActiune(c).Eroare),
        FazaExecutie f => f.Modifica(Faza(c)),
        RiscContract x => x.Modifica(Risc(c)),
        InstrumentGarantie i => Instrument(c) is { Succes: true } d ? i.Modifica(d.Valoare) : Rezultat.Esuat(Instrument(c).Eroare),
        SursaContract x => x.Modifica(Sursa(c)),
        _ => Rezultat.Esuat(Eroare.NuExista("Registrul")),
    };

    private static IQueryable<RandFisaContract>? Tabel(ContracteDbContext db, string registru) => registru switch
    {
        "obligatii" => db.Obligatii,
        "actiuni" => db.Actiuni,
        "faze" => db.Faze,
        "riscuri" => db.Riscuri,
        "instrumente" => db.Instrumente,
        "surse" => db.Surse,
        _ => null,
    };

    private static async Task<IResult> Adauga(Guid proiectId, Guid id, string registru, RandFisaCerere cerere, ContracteDbContext db, CancellationToken ct)
    {
        if (!Registre.Contains(registru))
            return Results.NotFound(new { mesaj = "Registrul nu există." });
        if (!await db.Contracte.AnyAsync(c => c.Id == id && c.ProiectId == proiectId, ct))
            return Results.NotFound();
        var ordine = (await Tabel(db, registru)!.Where(r => r.ContractId == id).MaxAsync(r => (int?)r.Ordine, ct) ?? 0) + 1;
        var r = Creeaza(registru, id, cerere.Ordine ?? ordine, cerere);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/contracte/proiecte/{proiectId}/{id}", (await IncarcaAsync(db, [id], ct))[id]);
    }

    private static async Task<IResult> Modifica(
        Guid proiectId, Guid id, string registru, Guid randId, RandFisaCerere cerere, ContracteDbContext db, CancellationToken ct)
    {
        if (Tabel(db, registru) is not { } tabel || !await db.Contracte.AnyAsync(c => c.Id == id && c.ProiectId == proiectId, ct))
            return Results.NotFound();
        var rand = await tabel.FirstOrDefaultAsync(r => r.Id == randId && r.ContractId == id, ct);
        if (rand is null)
            return Results.NotFound();
        var r = ModificaRand(rand, cerere);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        if (cerere.Ordine is { } o)
            rand.Muta(o);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await IncarcaAsync(db, [id], ct))[id]);
    }

    private static async Task<IResult> Sterge(Guid proiectId, Guid id, string registru, Guid randId, ContracteDbContext db, CancellationToken ct)
    {
        if (Tabel(db, registru) is not { } tabel || !await db.Contracte.AnyAsync(c => c.Id == id && c.ProiectId == proiectId, ct))
            return Results.NotFound();
        var rand = await tabel.FirstOrDefaultAsync(r => r.Id == randId && r.ContractId == id, ct);
        if (rand is null)
            return Results.NotFound();
        db.Remove(rand);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await IncarcaAsync(db, [id], ct))[id]);
    }

    private static async Task<IResult> SemneazaAct(
        Guid proiectId, Guid id, Guid actId, SemnareActCerere cerere, ContracteDbContext db, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        if (!await db.Contracte.AnyAsync(c => c.Id == id && c.ProiectId == proiectId, ct))
            return Results.NotFound();
        var act = await db.ActeAditionale.FirstOrDefaultAsync(a => a.Id == actId && a.ContractId == id, ct);
        if (act is null)
            return Results.NotFound();
        if (cerere.Data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data semnării nu poate fi în viitor." });
        var r = act.Semneaza(cerere.Data, utilizator.Nume ?? string.Empty);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
