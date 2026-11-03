using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Infrastructure.Persistenta;

/// <summary>
/// Bază pentru DbContext-ul fiecărui modul. Fiecare modul are schema lui
/// (ex. <c>devize</c>, <c>proiecte</c>), iar istoricul migrărilor stă tot în schema modulului,
/// ca modulele să poată evolua independent.
///
/// Denumirile din baza de date sunt snake_case (configurat în <see cref="InregistrareInfrastructura"/>).
/// </summary>
public abstract class DbContextModul(DbContextOptions optiuni) : DbContext(optiuni)
{
    /// <summary>Schema PostgreSQL a modulului.</summary>
    protected abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        // Fiecare proiect de infrastructură de modul conține un singur DbContext,
        // deci toate configurările IEntityTypeConfiguration din assembly îi aparțin.
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Toate sumele: numeric(18,4) implicit; câmpurile de bani pot cere explicit (18,2).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        base.ConfigureConventions(configurationBuilder);
    }
}
