using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace ErpConstructii.Identitate.Infrastructure.Autentificare;

/// <summary>
/// Contul de autentificare (parolă, MFA, blocare). Are același Id ca <see cref="Utilizator"/>:
/// datele de business stau în Utilizator, cele de securitate aici (D-07).
/// </summary>
public sealed class ContUtilizator : IdentityUser<Guid>
{
    /// <summary>Ultima firmă activă, propusă la următorul login.</summary>
    public Guid? UltimaFirmaId { get; set; }

    /// <summary>Parolă temporară (cont nou sau resetat de administrator): se schimbă la primul login.</summary>
    public bool TrebuieSchimbataParola { get; set; }
}

/// <summary>
/// Tabelele ASP.NET Core Identity (doar utilizatori, fără rolurile Identity: drepturile sunt ale ERP-ului)
/// și cele OpenIddict (clienți, autorizări, token-uri). Schema <c>identitate</c>, fără RLS.
///
/// E un DbContext separat de <see cref="IdentitateDbContext"/> pentru că trebuie să derive din
/// IdentityUserContext; are propriul istoric de migrări (<c>__ef_migrari_autentificare</c>).
/// </summary>
public sealed class AutentificareDbContext(DbContextOptions<AutentificareDbContext> optiuni)
    : IdentityUserContext<ContUtilizator, Guid>(optiuni)
{
    public const string TabelIstoricMigrari = "__ef_migrari_autentificare";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(IdentitateDbContext.NumeSchema);
        builder.UseOpenIddict<Guid>();

        builder.Entity<ContUtilizator>().ToTable("conturi");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("conturi_revendicari");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("conturi_autentificari_externe");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("conturi_tokenuri");

        builder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("oidc_aplicatii");
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable("oidc_autorizari");
        builder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("oidc_scopuri");
        builder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable("oidc_tokenuri");
    }
}
