using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ErpConstructii.Identitate.Infrastructure.Autentificare;

/// <summary>
/// Clienții OAuth (aplicația web, ulterior cea mobilă) vin din configurare și sunt sincronizați
/// în baza de date la pornire. Toți sunt clienți publici cu PKCE obligatoriu (fără secret).
///
/// Configurare:
///   "Autentificare:Clienti:erp-web": { "Denumire": "...", "RedirectUris": [...], "PostLogoutRedirectUris": [...] }
/// </summary>
public static class ClientiOAuth
{
    public static async Task SincronizeazaAsync(IServiceProvider radacina, IConfiguration configurare, CancellationToken ct = default)
    {
        await using var scope = radacina.CreateAsyncScope();
        var aplicatii = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var sectiune in configurare.GetSection("Autentificare:Clienti").GetChildren())
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = sectiune.Key,
                DisplayName = sectiune["Denumire"] ?? sectiune.Key,
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit,
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Profile,
                    Permissions.Scopes.Email,
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            };

            foreach (var uri in sectiune.GetSection("RedirectUris").Get<string[]>() ?? [])
            {
                descriptor.RedirectUris.Add(new Uri(uri));
            }

            foreach (var uri in sectiune.GetSection("PostLogoutRedirectUris").Get<string[]>() ?? [])
            {
                descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
            }

            var existent = await aplicatii.FindByClientIdAsync(sectiune.Key, ct);
            if (existent is null)
            {
                await aplicatii.CreateAsync(descriptor, ct);
            }
            else
            {
                await aplicatii.UpdateAsync(existent, descriptor, ct);
            }
        }
    }
}
