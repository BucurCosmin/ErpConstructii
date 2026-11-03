using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ErpConstructii.Parteneri.Infrastructure;

/// <summary>Endpointurile și persistența fișelor de furnizori și parteneri.</summary>
public sealed class ModulParteneri : IModul
{
    public string Nume => ParteneriDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AdaugaDbContextModul<ParteneriDbContext>(configurare, Nume);
        servicii.AddScoped<IServiciuParteneri, ServiciuParteneri>();
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/parteneri").WithTags("Parteneri");
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);
        var editare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Editare);
        var anulare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Anulare);

        grup.MapGet("/furnizori", ListaFurnizori)
            .WithName("ListaFurnizori")
            .WithSummary("Listează furnizorii firmei")
            .Produces<IReadOnlyList<PartenerDto>>()
            .CerePermisiune(vizualizare);

        grup.MapGet("/furnizori/{id:guid}", CitesteFurnizor)
            .WithName("CitesteFurnizor")
            .WithSummary("Citește fișa unui furnizor")
            .Produces<PartenerDto>()
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(vizualizare);

        grup.MapPost("/furnizori", CreeazaFurnizor)
            .WithName("CreeazaFurnizor")
            .WithSummary("Adaugă un furnizor")
            .Produces<PartenerDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(creare);

        grup.MapPut("/furnizori/{id:guid}", ModificaFurnizor)
            .WithName("ModificaFurnizor")
            .WithSummary("Modifică fișa unui furnizor")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(editare);

        grup.MapPost("/furnizori/{id:guid}/dezactivare", (Guid id, ParteneriDbContext db, CancellationToken ct) =>
                SchimbaActivarea(id, false, db, ct))
            .WithName("DezactiveazaFurnizor")
            .WithSummary("Dezactivează un furnizor fără a șterge istoricul")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(anulare);

        grup.MapPost("/furnizori/{id:guid}/activare", (Guid id, ParteneriDbContext db, CancellationToken ct) =>
                SchimbaActivarea(id, true, db, ct))
            .WithName("ActiveazaFurnizor")
            .WithSummary("Reactivează un furnizor")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(editare);
    }

    private static async Task<IResult> ListaFurnizori(
        string? cauta,
        ParteneriDbContext db,
        CancellationToken ct,
        bool inclusivInactivi = false)
    {
        var interogare = db.Parteneri.AsNoTracking().Where(p => p.EsteFurnizor);
        if (!inclusivInactivi)
        {
            interogare = interogare.Where(p => p.Activ);
        }

        if (!string.IsNullOrWhiteSpace(cauta))
        {
            var termen = cauta.Trim();
            interogare = interogare.Where(p =>
                p.Denumire.Contains(termen)
                || (p.Cui != null && p.Cui.Contains(termen))
                || (p.NrRegCom != null && p.NrRegCom.Contains(termen)));
        }

        var parteneri = await interogare.OrderBy(p => p.Denumire).ToListAsync(ct);
        return Results.Ok(parteneri.Select(LaDto).ToList());
    }

    private static async Task<IResult> CitesteFurnizor(Guid id, ParteneriDbContext db, CancellationToken ct)
    {
        var partener = await db.Parteneri.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.EsteFurnizor, ct);
        return partener is null ? Results.NotFound() : Results.Ok(LaDto(partener));
    }

    private static async Task<IResult> CreeazaFurnizor(PartenerCerere cerere, ParteneriDbContext db, CancellationToken ct)
    {
        var date = LaDate(cerere);
        if (Partener.Valideaza(date) is { Count: > 0 } probleme)
        {
            return EroareValidare(probleme);
        }

        if (await ExistaCuiAsync(db, date.Cui, null, ct))
        {
            return Results.Conflict(new { mesaj = "Există deja un partener cu acest CUI." });
        }

        var rezultat = Partener.Creeaza(date);
        if (!rezultat.Succes)
        {
            return EroareValidare([rezultat.Eroare.Mesaj]);
        }

        var partener = rezultat.Valoare;
        db.Parteneri.Add(partener);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EsteDuplicatCui(ex))
        {
            return Results.Conflict(new { mesaj = "Există deja un partener cu acest CUI." });
        }

        return Results.Created($"/api/parteneri/furnizori/{partener.Id}", LaDto(partener));
    }

    private static async Task<IResult> ModificaFurnizor(
        Guid id,
        PartenerCerere cerere,
        ParteneriDbContext db,
        CancellationToken ct)
    {
        var partener = await db.Parteneri.FirstOrDefaultAsync(p => p.Id == id && p.EsteFurnizor, ct);
        if (partener is null)
        {
            return Results.NotFound();
        }

        var date = LaDate(cerere);
        if (Partener.Valideaza(date) is { Count: > 0 } probleme)
        {
            return EroareValidare(probleme);
        }

        if (await ExistaCuiAsync(db, date.Cui, id, ct))
        {
            return Results.Conflict(new { mesaj = "Există deja un partener cu acest CUI." });
        }

        var rezultat = partener.Modifica(date);
        if (!rezultat.Succes)
        {
            return EroareValidare([rezultat.Eroare.Mesaj]);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EsteDuplicatCui(ex))
        {
            return Results.Conflict(new { mesaj = "Există deja un partener cu acest CUI." });
        }

        return Results.NoContent();
    }

    private static async Task<IResult> SchimbaActivarea(Guid id, bool activ, ParteneriDbContext db, CancellationToken ct)
    {
        var partener = await db.Parteneri.FirstOrDefaultAsync(p => p.Id == id && p.EsteFurnizor, ct);
        if (partener is null)
        {
            return Results.NotFound();
        }

        if (activ)
        {
            partener.Activeaza();
        }
        else
        {
            partener.Dezactiveaza();
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<bool> ExistaCuiAsync(ParteneriDbContext db, string? cui, Guid? idExclus, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cui))
        {
            return false;
        }

        var cuiNormalizat = ValidareFiscala.NormalizeazaCui(cui);
        return await db.Parteneri.AnyAsync(p => p.Cui == cuiNormalizat && (idExclus == null || p.Id != idExclus), ct);
    }

    private static bool EsteDuplicatCui(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_parteneri_firma_id_cui",
        };

    private static DatePartener LaDate(PartenerCerere cerere) => new(
        cerere.Denumire,
        cerere.Cui,
        cerere.NrRegCom,
        cerere.PlatitorTva,
        cerere.Adresa ?? Adresa.Goala,
        cerere.Telefon,
        cerere.Email,
        cerere.PersoanaContact,
        cerere.TelefonContact,
        cerere.Observatii,
        cerere.ConturiBancare ?? [],
        EsteFurnizor: true,
        cerere.EsteClient,
        cerere.EsteSubantreprenor);

    private static PartenerDto LaDto(Partener p) => new(
        p.Id, p.Denumire, p.Cui, p.CodFiscal, p.NrRegCom, p.PlatitorTva,
        p.Adresa, p.Telefon, p.Email, p.PersoanaContact, p.TelefonContact, p.Observatii,
        p.ConturiBancare.ToList(), p.EsteClient, p.EsteSubantreprenor, p.Activ);

    private static IResult EroareValidare(IReadOnlyList<string> probleme) =>
        Results.BadRequest(new { mesaj = string.Join(" ", probleme) });
}

/// <summary>Datele trimise pentru crearea sau modificarea unui furnizor.</summary>
public sealed record PartenerCerere(
    string Denumire,
    string? Cui,
    string? NrRegCom,
    bool PlatitorTva,
    Adresa? Adresa,
    string? Telefon,
    string? Email,
    string? PersoanaContact,
    string? TelefonContact,
    string? Observatii,
    IReadOnlyList<ContBancar>? ConturiBancare,
    bool EsteClient,
    bool EsteSubantreprenor);

/// <summary>Fișa unui furnizor returnată de API.</summary>
public sealed record PartenerDto(
    Guid Id,
    string Denumire,
    string? Cui,
    string? CodFiscal,
    string? NrRegCom,
    bool PlatitorTva,
    Adresa Adresa,
    string? Telefon,
    string? Email,
    string? PersoanaContact,
    string? TelefonContact,
    string? Observatii,
    IReadOnlyList<ContBancar> ConturiBancare,
    bool EsteClient,
    bool EsteSubantreprenor,
    bool Activ);
