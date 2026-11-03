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

namespace ErpConstructii.Proiecte.Infrastructure;

public sealed record ProiectDto(Guid Id, string Cod, string Denumire, string Beneficiar, string Stare, DateOnly? TermenContractual);

public sealed record CreeazaProiectCerere(string Cod, string Denumire, string Beneficiar, string? CuiBeneficiar, string AdresaSantier);

public sealed class ModulProiecte : IModul
{
    public string Nume => ProiecteDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare) =>
        servicii.AdaugaDbContextModul<ProiecteDbContext>(configurare, Nume);

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/proiecte").WithTags("Proiecte");
        var vizualizare = Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Creare);

        // Lista: doar proiectele pe care utilizatorul are drept (firma vine deja filtrată prin RLS).
        grup.MapGet("/", async (
            ProiecteDbContext db,
            IServiciuPermisiuni permisiuni,
            IUtilizatorCurent utilizator,
            IFirmaCurenta firma,
            CancellationToken ct) =>
        {
            var evaluator = await permisiuni.PentruUtilizatorAsync(utilizator.UtilizatorId!.Value, ct);
            var accesibile = evaluator.ProiecteAccesibile(vizualizare, firma.FirmaId!.Value);

            var interogare = db.Proiecte.AsNoTracking();
            if (accesibile is not null)
            {
                // Listă concretă: EF traduce List.Contains în „id = ANY(@lista)”.
                var lista = accesibile.ToList();
                interogare = interogare.Where(p => lista.Contains(p.Id));
            }

            return await interogare
                .OrderBy(p => p.Cod)
                .Select(p => new ProiectDto(p.Id, p.Cod, p.Denumire, p.Beneficiar, p.Stare.ToString(), p.TermenContractual))
                .ToListAsync(ct);
        }).CerePermisiune(vizualizare);

        grup.MapGet("/{proiectId:guid}", async (Guid proiectId, ProiecteDbContext db, CancellationToken ct) =>
        {
            var p = await db.Proiecte.AsNoTracking().FirstOrDefaultAsync(x => x.Id == proiectId, ct);
            return p is null
                ? Results.NotFound()
                : Results.Ok(new ProiectDto(p.Id, p.Cod, p.Denumire, p.Beneficiar, p.Stare.ToString(), p.TermenContractual));
        }).CerePermisiune(vizualizare);

        grup.MapPost("/", async (CreeazaProiectCerere cerere, ProiecteDbContext db, CancellationToken ct) =>
        {
            var cod = cerere.Cod.Trim().ToUpperInvariant();
            if (await db.Proiecte.AnyAsync(p => p.Cod == cod, ct))
            {
                return Results.Conflict(new { mesaj = $"Există deja un proiect cu codul {cod}." });
            }

            var proiect = new Proiect(cerere.Cod, cerere.Denumire, cerere.Beneficiar, cerere.CuiBeneficiar, cerere.AdresaSantier);
            db.Proiecte.Add(proiect);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/proiecte/{proiect.Id}", new { proiect.Id });
        }).CerePermisiune(creare);
    }
}
