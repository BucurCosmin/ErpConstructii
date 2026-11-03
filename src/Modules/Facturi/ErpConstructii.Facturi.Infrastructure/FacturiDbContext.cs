using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Facturi.Infrastructure;

public sealed class FacturiDbContext(DbContextOptions<FacturiDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "facturi";

    protected override string Schema => NumeSchema;

    public DbSet<FacturaPrimita> FacturiPrimite => Set<FacturaPrimita>();
    public DbSet<DocumentFactura> Documente => Set<DocumentFactura>();
    public DbSet<AnulareFacturaPrimita> Anulari => Set<AnulareFacturaPrimita>();
}

internal sealed class ConfigurareAnulare : IEntityTypeConfiguration<AnulareFacturaPrimita>
{
    public void Configure(EntityTypeBuilder<AnulareFacturaPrimita> b)
    {
        b.ToTable("anulari_facturi_primite");
        b.HasKey(x => x.Id);
        b.Property(x => x.Motiv).HasMaxLength(1000).IsRequired();
        b.Property(x => x.AnulataDeNume).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.FacturaId).IsUnique(); // o factură se anulează o singură dată
        b.HasOne<FacturaPrimita>().WithMany().HasForeignKey(x => x.FacturaId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfigurareFacturaPrimita : IEntityTypeConfiguration<FacturaPrimita>
{
    public void Configure(EntityTypeBuilder<FacturaPrimita> b)
    {
        b.ToTable("facturi_primite");
        b.HasKey(x => x.Id);
        b.Property(x => x.Numar).HasMaxLength(50).IsRequired();
        b.Property(x => x.Moneda).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.FurnizorDenumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.FurnizorCui).HasMaxLength(20);
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.TotalFaraTva).HasPrecision(18, 2);
        b.Property(x => x.TotalTva).HasPrecision(18, 2);
        b.Property(x => x.TotalDePlata).HasPrecision(18, 2);
        b.Property(x => x.Referinta).HasMaxLength(200);
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.Property(x => x.Sursa).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.InregistrataDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.EsteStorno);
        b.Ignore(x => x.EsteBlocat);
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.DataEmitere });
        b.HasIndex(x => new { x.FirmaId, x.FurnizorCui, x.Numar });
    }
}

internal sealed class ConfigurareDocumentFactura : IEntityTypeConfiguration<DocumentFactura>
{
    public void Configure(EntityTypeBuilder<DocumentFactura> b)
    {
        b.ToTable("documente");
        b.HasKey(x => x.Id);
        b.Property(x => x.NumeFisier).HasMaxLength(260).IsRequired();
        b.Property(x => x.TipContinut).HasMaxLength(100).IsRequired();
        b.Property(x => x.HashSha256).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Continut).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.HashSha256 }).IsUnique();
    }
}
