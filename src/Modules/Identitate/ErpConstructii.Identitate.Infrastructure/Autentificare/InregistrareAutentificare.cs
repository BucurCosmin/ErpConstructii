using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ErpConstructii.Identitate.Infrastructure.Autentificare;

/// <summary>
/// Serverul de autentificare din aplicație (D-07, REQ-UTL-02): ASP.NET Core Identity pentru conturi
/// (parolă, blocare, TOTP) și OpenIddict pentru token-uri OAuth2/OIDC.
///
/// Flux: Authorization Code + PKCE, pentru aplicația web și cea mobilă. Pagina de login e în aplicația
/// web (/autentificare); ea creează cookie-ul Identity, apoi /connect/authorize emite codul.
/// Access token: 10 minute. Refresh token: rotit la fiecare folosire (implicit în OpenIddict).
/// </summary>
public static class InregistrareAutentificare
{
    public const string RutaLogin = "/autentificare";
    public const string RutaConfigurareMfa = "/autentificare/configurare-mfa";

    public static IServiceCollection AdaugaAutentificare(
        this IServiceCollection servicii,
        IConfiguration configurare,
        bool esteDezvoltare)
    {
        var connectionString = configurare.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Lipsește ConnectionStrings:Erp.");

        servicii.AddScoped<ContextSesiune>();
        servicii.AddDbContext<AutentificareDbContext>(o =>
        {
            o.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(AutentificareDbContext.TabelIstoricMigrari, IdentitateDbContext.NumeSchema))
                .UseSnakeCaseNamingConvention()
                .UseOpenIddict<Guid>();
        });

        servicii
            .AddIdentityCore<ContUtilizator>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 12;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddEntityFrameworkStores<AutentificareDbContext>();

        servicii.AddAuthentication().AddIdentityCookies();
        servicii.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "erp.sesiune";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = esteDezvoltare ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromHours(10);
            o.SlidingExpiration = true;
            // Cookie-ul servește doar login-ului; API-ul răspunde cu coduri, nu cu redirecționări.
            o.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        servicii.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<AutentificareDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(o =>
            {
                o.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetEndSessionEndpointUris("connect/logout");

                o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                o.AllowRefreshTokenFlow();
                o.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess);

                o.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
                o.SetRefreshTokenLifetime(TimeSpan.FromHours(10));

                // Emitentul fix: în dezvoltare aplicația web și API-ul sunt pe porturi diferite (proxy Vite).
                if (configurare["Autentificare:Emitent"] is { Length: > 0 } emitent)
                {
                    o.SetIssuer(new Uri(emitent));
                }

                if (esteDezvoltare)
                {
                    o.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
                }
                else
                {
                    o.AddSigningCertificate(IncarcaCertificat(configurare, "Semnare"));
                    o.AddEncryptionCertificate(IncarcaCertificat(configurare, "Criptare"));
                }

                var aspNet = o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
                if (esteDezvoltare)
                {
                    aspNet.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseAspNetCore();
            });

        return servicii;
    }

    /// <summary>Certificatele de producție: fișiere .pfx din configurare (secrete, nu din git).</summary>
    private static X509Certificate2 IncarcaCertificat(IConfiguration configurare, string tip)
    {
        var cale = configurare[$"Autentificare:Certificate:{tip}:Cale"];
        if (string.IsNullOrWhiteSpace(cale))
        {
            throw new InvalidOperationException(
                $"Lipsește Autentificare:Certificate:{tip}:Cale (certificatul .pfx pentru {tip.ToLowerInvariant()}a token-urilor).");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(cale, configurare[$"Autentificare:Certificate:{tip}:Parola"]);
    }
}
