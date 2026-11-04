using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Stocuri.Infrastructure;

public sealed class StocuriDbContext(DbContextOptions<StocuriDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "stocuri";

    protected override string Schema => NumeSchema;

    public DbSet<Gestiune> Gestiuni => Set<Gestiune>();
    public DbSet<ArticolStoc> Articole => Set<ArticolStoc>();
    public DbSet<AliasArticol> Aliasuri => Set<AliasArticol>();
    public DbSet<Nir> Nir => Set<Nir>();
    public DbSet<TransferStoc> Transferuri => Set<TransferStoc>();
    public DbSet<MiscareStoc> Miscari => Set<MiscareStoc>();
    public DbSet<SoldStoc> Solduri => Set<SoldStoc>();
}

internal sealed class ConfigurareGestiune : IEntityTypeConfiguration<Gestiune>
{
    public void Configure(EntityTypeBuilder<Gestiune> b)
    {
        b.ToTable("gestiuni");
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(20).IsRequired();
        b.Property(x => x.Denumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.Cod }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId }).IsUnique().HasFilter("proiect_id IS NOT NULL"); // o gestiune de șantier pe proiect
    }
}

internal sealed class ConfigurareArticol : IEntityTypeConfiguration<ArticolStoc>
{
    public void Configure(EntityTypeBuilder<ArticolStoc> b)
    {
        b.ToTable("articole");
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(30).IsRequired();
        b.Property(x => x.Denumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.UnitateMasura).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.Cod }).IsUnique();
    }
}

internal sealed class ConfigurareAlias : IEntityTypeConfiguration<AliasArticol>
{
    public void Configure(EntityTypeBuilder<AliasArticol> b)
    {
        b.ToTable("aliasuri_articole");
        b.HasKey(x => x.Id);
        b.Property(x => x.Cheie).HasMaxLength(300).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.FurnizorId, x.Cheie }).IsUnique().AreNullsDistinct(false);
    }
}

internal sealed class ConfigurareNir : IEntityTypeConfiguration<Nir>
{
    public void Configure(EntityTypeBuilder<Nir> b)
    {
        b.ToTable("nir");
        b.HasKey(x => x.Id);
        b.Property(x => x.GestiuneDenumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.FacturaNumar).HasMaxLength(60).IsRequired();
        b.Property(x => x.FurnizorDenumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.Observatii).HasMaxLength(1000);
        b.Property(x => x.ReceptionatDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.AreDiferente);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.FacturaId }).IsUnique(); // o singură recepție pe factură
    }
}

internal sealed class ConfigurareTransfer : IEntityTypeConfiguration<TransferStoc>
{
    public void Configure(EntityTypeBuilder<TransferStoc> b)
    {
        b.ToTable("transferuri");
        b.HasKey(x => x.Id);
        b.Property(x => x.GestiuneSursaDenumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.GestiuneDestinatieDenumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.Observatii).HasMaxLength(1000);
        b.Property(x => x.EmisDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.Data });
    }
}

internal sealed class ConfigurareMiscare : IEntityTypeConfiguration<MiscareStoc>
{
    public void Configure(EntityTypeBuilder<MiscareStoc> b)
    {
        b.ToTable("miscari");
        b.HasKey(x => x.Id);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Cantitate).HasPrecision(18, 4);
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.DocumentNumar).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.GestiuneId, x.ArticolId, x.Data });
    }
}

internal sealed class ConfigurareSold : IEntityTypeConfiguration<SoldStoc>
{
    public void Configure(EntityTypeBuilder<SoldStoc> b)
    {
        b.ToTable("solduri");
        b.HasKey(x => x.Id);
        b.Property(x => x.Cantitate).HasPrecision(18, 4);
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Ignore(x => x.Sold);
        b.HasIndex(x => new { x.FirmaId, x.GestiuneId, x.ArticolId }).IsUnique();
    }
}
