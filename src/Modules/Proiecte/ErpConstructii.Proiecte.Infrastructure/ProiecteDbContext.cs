using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Proiecte.Infrastructure;

public sealed class ProiecteDbContext(DbContextOptions<ProiecteDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "proiecte";

    protected override string Schema => NumeSchema;

    public DbSet<Proiect> Proiecte => Set<Proiect>();
    public DbSet<Santier> Santiere => Set<Santier>();
    public DbSet<Compartiment> Compartimente => Set<Compartiment>();
}

internal sealed class ConfigurareSantier : IEntityTypeConfiguration<Santier>
{
    public void Configure(EntityTypeBuilder<Santier> b)
    {
        b.ToTable("santiere");
        b.HasKey(x => x.Id);
        b.Property(x => x.Denumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.Adresa).HasMaxLength(500);
        b.HasIndex(x => new { x.FirmaId, x.ProiectId });
    }
}

internal sealed class ConfigurareCompartiment : IEntityTypeConfiguration<Compartiment>
{
    public void Configure(EntityTypeBuilder<Compartiment> b)
    {
        b.ToTable("compartimente");
        b.HasKey(x => x.Id);
        b.Property(x => x.Denumire).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.Denumire }).IsUnique();
    }
}

internal sealed class ConfigurareProiect : IEntityTypeConfiguration<Proiect>
{
    public void Configure(EntityTypeBuilder<Proiect> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(40).IsRequired();
        // Codul e unic în cadrul firmei.
        b.HasIndex(x => new { x.FirmaId, x.Cod }).IsUnique();
        b.Property(x => x.Denumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.Beneficiar).HasMaxLength(300).IsRequired();
        b.Property(x => x.CuiBeneficiar).HasMaxLength(20);
        b.Property(x => x.AdresaSantier).HasMaxLength(500);
        b.Property(x => x.Stare).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TipProiect.Lucrare).HasSentinel((TipProiect)(-1));
        b.Ignore(x => x.EsteSediu);
        b.Property(x => x.ArhivatDeNume).HasMaxLength(200);
    }
}
