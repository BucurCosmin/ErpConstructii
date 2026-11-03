using ErpConstructii.Infrastructure;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Identitate.Infrastructure;

public sealed record ProfilDto(Guid Id, string Email, string Nume, string Tip, Guid? FirmaActiva);

public sealed class ModulIdentitate : IModul
{
    public string Nume => IdentitateDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AdaugaDbContextModul<IdentitateDbContext>(configurare, Nume);
        servicii.AddMemoryCache();
        servicii.AddScoped<IServiciuPermisiuni, ServiciuPermisiuni>();
        servicii.AddScoped<IAuthorizationHandler, HandlerPermisiune>();
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/identitate").WithTags("Identitate");

        // Profilul utilizatorului curent (antetul aplicației web).
        grup.MapGet("/eu", async (IdentitateDbContext db, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct) =>
        {
            if (utilizator.UtilizatorId is not { } id)
            {
                return Results.Unauthorized();
            }

            var u = await db.Utilizatori.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return u is null
                ? Results.NotFound()
                : Results.Ok(new ProfilDto(u.Id, u.Email, u.Nume, u.Tip.ToString(), firma.FirmaId));
        });

        // Permisiunile utilizatorului curent pe firma activă: frontend-ul le folosește
        // ca să ascundă butoane, dar verificarea reală rămâne pe server.
        grup.MapGet("/eu/permisiuni", async (
            IServiciuPermisiuni permisiuni,
            IUtilizatorCurent utilizator,
            IFirmaCurenta firma,
            CancellationToken ct) =>
        {
            if (utilizator.UtilizatorId is not { } id || firma.FirmaId is not { } firmaId)
            {
                return Results.Unauthorized();
            }

            var evaluator = await permisiuni.PentruUtilizatorAsync(id, ct);
            var toate = RoluriPredefinite.Toate.SelectMany(r => r.ToatePermisiunile()).Distinct();
            var acordate = toate
                .Where(p => evaluator.ProiecteAccesibile(p, firmaId) is not { Count: 0 })
                .Order(StringComparer.Ordinal)
                .ToList();
            return Results.Ok(acordate);
        });

        // TODO faza 0: administrare utilizatori, roluri, atribuiri (permisiunea administrare.*),
        // cu invalidarea cache-ului de permisiuni după fiecare schimbare.
    }
}
