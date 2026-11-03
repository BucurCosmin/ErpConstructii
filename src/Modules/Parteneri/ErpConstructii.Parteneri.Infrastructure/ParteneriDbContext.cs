using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Parteneri.Infrastructure;

public sealed class ParteneriDbContext(DbContextOptions<ParteneriDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "parteneri";

    protected override string Schema => NumeSchema;

    public DbSet<Partener> Parteneri => Set<Partener>();
}

internal sealed class ConfigurarePartener : IEntityTypeConfiguration<Partener>
{
    public void Configure(EntityTypeBuilder<Partener> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Denumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.Cui).HasMaxLength(20);
        b.Property(x => x.NrRegCom).HasMaxLength(40);
        b.Property(x => x.PlatitorTva).IsRequired();
        b.ComplexProperty(x => x.Adresa, a =>
        {
            a.Property(x => x.Strada).HasColumnName("adresa_strada").HasMaxLength(300);
            a.Property(x => x.Localitate).HasColumnName("adresa_localitate").HasMaxLength(100);
            a.Property(x => x.Judet).HasColumnName("adresa_judet").HasMaxLength(3);
            a.Property(x => x.CodPostal).HasColumnName("adresa_cod_postal").HasMaxLength(10);
            a.Property(x => x.Tara).HasColumnName("adresa_tara").HasMaxLength(2);
        });
        b.Property(x => x.Telefon).HasMaxLength(60);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.PersoanaContact).HasMaxLength(200);
        b.Property(x => x.TelefonContact).HasMaxLength(60);
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.ComplexCollection(x => x.ConturiBancare, c => c.ToJson("conturi_bancare"));
        b.HasIndex(x => new { x.FirmaId, x.Cui })
            .IsUnique()
            .HasFilter("cui IS NOT NULL");
        b.HasIndex(x => new { x.FirmaId, x.EsteFurnizor, x.Denumire });
    }
}
