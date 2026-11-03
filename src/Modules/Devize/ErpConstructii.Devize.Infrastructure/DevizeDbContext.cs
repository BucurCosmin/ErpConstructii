using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Devize.Infrastructure;

public sealed class DevizeDbContext(DbContextOptions<DevizeDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "devize";

    protected override string Schema => NumeSchema;

    public DbSet<VersiuneDeviz> Versiuni => Set<VersiuneDeviz>();
    public DbSet<ArticolDeviz> Articole => Set<ArticolDeviz>();
}

internal sealed class ConfigurareVersiune : IEntityTypeConfiguration<VersiuneDeviz>
{
    public void Configure(EntityTypeBuilder<VersiuneDeviz> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.Tip, x.Numar }).IsUnique();
        b.Property(x => x.Denumire).HasMaxLength(300);
        b.Property(x => x.Sursa).HasMaxLength(100);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Stare).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.CotaContributiiManopera).HasPrecision(9, 6);
        b.Property(x => x.CotaCheltuieliIndirecte).HasPrecision(9, 6);
        b.Property(x => x.CotaProfit).HasPrecision(9, 6);
        b.Property(x => x.CotaTva).HasPrecision(9, 6);
        b.Ignore(x => x.EsteBlocat);
        b.HasMany(x => x.Obiecte).WithOne().HasForeignKey(o => o.VersiuneId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Obiecte).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConfigurareObiect : IEntityTypeConfiguration<ObiectDeviz>
{
    public void Configure(EntityTypeBuilder<ObiectDeviz> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(40);
        b.Property(x => x.Denumire).HasMaxLength(300);
        b.HasMany(x => x.Categorii).WithOne().HasForeignKey(c => c.ObiectId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Categorii).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConfigurareCategorie : IEntityTypeConfiguration<CategorieLucrari>
{
    public void Configure(EntityTypeBuilder<CategorieLucrari> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(40);
        b.Property(x => x.Denumire).HasMaxLength(300);
        b.HasMany(x => x.Articole).WithOne().HasForeignKey(a => a.CategorieId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Articole).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConfigurareArticol : IEntityTypeConfiguration<ArticolDeviz>
{
    public void Configure(EntityTypeBuilder<ArticolDeviz> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.CheieStabila);
        b.Property(x => x.Simbol).HasMaxLength(40);
        b.Property(x => x.Denumire).HasMaxLength(1000);
        b.Property(x => x.UnitateMasura).HasMaxLength(20);
        b.Property(x => x.Cantitate).HasPrecision(18, 4);
        b.Property(x => x.PretMaterial).HasPrecision(18, 4);
        b.Property(x => x.PretManopera).HasPrecision(18, 4);
        b.Property(x => x.PretUtilaj).HasPrecision(18, 4);
        b.Property(x => x.PretTransport).HasPrecision(18, 4);
        b.Ignore(x => x.PretUnitar);
        b.HasMany(x => x.Consumuri).WithOne().HasForeignKey(c => c.ArticolId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Consumuri).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConfigurareConsum : IEntityTypeConfiguration<ConsumNormat>
{
    public void Configure(EntityTypeBuilder<ConsumNormat> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.CodResursa).HasMaxLength(40);
        b.Property(x => x.Denumire).HasMaxLength(500);
        b.Property(x => x.UnitateMasura).HasMaxLength(20);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ConsumUnitar).HasPrecision(18, 6);
    }
}
