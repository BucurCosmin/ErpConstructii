using System.Security.Claims;
using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ErpConstructii.Identitate.Infrastructure.Autentificare;

/// <summary>Ce trebuie știut despre un utilizator ca să i se emită un token.</summary>
internal sealed record StareUtilizator(
    Utilizator Utilizator,
    IReadOnlyList<Firma> Firme,
    bool PoateIntra,
    bool MfaObligatoriu);

/// <summary>Încarcă starea de autentificare a unui utilizator și construiește identitatea din token.</summary>
internal sealed class ContextSesiune(IdentitateDbContext db, ICeas ceas)
{
    public async Task<StareUtilizator?> IncarcaAsync(Guid utilizatorId, CancellationToken ct)
    {
        var utilizator = await db.Utilizatori.AsNoTracking().FirstOrDefaultAsync(u => u.Id == utilizatorId, ct);
        if (utilizator is null)
        {
            return null;
        }

        var atribuiri = await db.Atribuiri.AsNoTracking().Where(a => a.UtilizatorId == utilizatorId).ToListAsync(ct);
        var idFirme = AccesFirme.Accesibile(atribuiri, ceas.Azi).ToList();
        var firme = await db.Firme.AsNoTracking()
            .Where(f => idFirme.Contains(f.Id) && f.Activa)
            .OrderBy(f => f.Denumire)
            .ToListAsync(ct);

        var idRoluri = atribuiri.Where(a => a.EsteValabila(ceas.Azi)).Select(a => a.RolId).Distinct().ToList();
        var permisiuni = await db.Roluri.AsNoTracking()
            .Where(r => idRoluri.Contains(r.Id))
            .ToListAsync(ct);

        return new StareUtilizator(
            utilizator,
            firme,
            AccesFirme.PoateIntra(utilizator, ceas.Azi),
            PoliticaMfa.EsteObligatorie(utilizator.Tip, permisiuni.SelectMany(r => r.Permisiuni)));
    }

    /// <summary>Identitatea pusă în token: utilizatorul, firma activă și scopurile cerute.</summary>
    public static ClaimsIdentity Identitate(StareUtilizator stare, Firma firma, IEnumerable<string> scopuri)
    {
        var identitate = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identitate.SetClaim(Claims.Subject, stare.Utilizator.Id.ToString())
            .SetClaim(Claims.Name, stare.Utilizator.Nume)
            .SetClaim(Claims.Email, stare.Utilizator.Email)
            .SetClaim(ClaimuriErp.Firma, firma.Id.ToString());

        identitate.SetScopes(scopuri);
        identitate.SetDestinations(claim => claim.Type switch
        {
            Claims.Name or Claims.Email when identitate.HasScope(Scopes.Profile) || identitate.HasScope(Scopes.Email) =>
                [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Subject or ClaimuriErp.Firma => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken],
        });

        return identitate;
    }
}
