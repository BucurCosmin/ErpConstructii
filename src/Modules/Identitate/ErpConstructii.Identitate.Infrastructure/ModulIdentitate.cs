using ErpConstructii.Identitate.Infrastructure.Autentificare;
using ErpConstructii.Infrastructure;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Identitate.Infrastructure;

public sealed record FirmaDto(Guid Id, string Denumire, string? Cui);

public sealed record ProfilDto(
    Guid Id,
    string Email,
    string Nume,
    string Tip,
    Guid? FirmaActiva,
    IReadOnlyList<FirmaDto> Firme,
    bool MfaActivat);

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

        // Profilul utilizatorului curent și firmele lui (antetul aplicației web, selectorul de firmă).
        grup.MapGet("/eu", async (
            IdentitateDbContext db,
            UserManager<ContUtilizator> conturi,
            IUtilizatorCurent utilizator,
            IFirmaCurenta firma,
            ICeas ceas,
            CancellationToken ct) =>
        {
            if (utilizator.UtilizatorId is not { } id)
            {
                return Results.Unauthorized();
            }

            var u = await db.Utilizatori.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (u is null)
            {
                return Results.NotFound();
            }

            var atribuiri = await db.Atribuiri.AsNoTracking().Where(a => a.UtilizatorId == id).ToListAsync(ct);
            var idFirme = AccesFirme.Accesibile(atribuiri, ceas.Azi).ToList();
            var firme = await db.Firme.AsNoTracking()
                .Where(f => idFirme.Contains(f.Id) && f.Activa)
                .OrderBy(f => f.Denumire)
                .Select(f => new FirmaDto(f.Id, f.Denumire, f.Cui))
                .ToListAsync(ct);
            var cont = await conturi.FindByIdAsync(id.ToString());

            return Results.Ok(new ProfilDto(u.Id, u.Email, u.Nume, u.Tip.ToString(), firma.FirmaId, firme, cont?.TwoFactorEnabled ?? false));
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

        // Administrarea utilizatorilor, rolurilor, atribuirilor și firmelor.
        EndpointuriAdministrare.Mapeaza(rute);
    }
}
