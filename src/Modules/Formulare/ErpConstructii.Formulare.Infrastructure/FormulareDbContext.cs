using ErpConstructii.Formulare;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Formulare.Infrastructure;

public sealed class FormulareDbContext(DbContextOptions<FormulareDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "formulare";

    protected override string Schema => NumeSchema;

    public DbSet<SablonFormular> Sabloane => Set<SablonFormular>();
    public DbSet<ImportFormular> Importuri => Set<ImportFormular>();
}

internal sealed class ConfigurareImportFormular : IEntityTypeConfiguration<ImportFormular>
{
    public void Configure(EntityTypeBuilder<ImportFormular> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.CodTip).HasMaxLength(100).IsRequired();
        b.Property(x => x.NumeFisier).HasMaxLength(255).IsRequired();
        b.Property(x => x.HashSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.Original).IsRequired();
        b.Property(x => x.DateJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Stare).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.CodTip, x.CreatLa });
    }
}

internal sealed class ConfigurareSablonFormular : IEntityTypeConfiguration<SablonFormular>
{
    public void Configure(EntityTypeBuilder<SablonFormular> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.CodTip).HasMaxLength(100).IsRequired();
        b.Property(x => x.Versiune).IsRequired();
        b.Property(x => x.NumeFisier).HasMaxLength(255).IsRequired();
        b.Property(x => x.HashSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.Continut).IsRequired();
        b.Property(x => x.Activ).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.CodTip, x.Versiune }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.CodTip })
            .IsUnique()
            .HasFilter("activ");
    }
}
