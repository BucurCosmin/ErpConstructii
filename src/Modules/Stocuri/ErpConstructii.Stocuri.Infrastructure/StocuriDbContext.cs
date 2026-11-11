using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Stocuri.Infrastructure;

public sealed class StocuriDbContext(DbContextOptions<StocuriDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "stocuri";

    protected override string Schema => NumeSchema;

    public DbSet<Depozit> Depozite => Set<Depozit>();
    public DbSet<Gestiune> Gestiuni => Set<Gestiune>();
    public DbSet<ArticolStoc> Articole => Set<ArticolStoc>();
    public DbSet<AliasArticol> Aliasuri => Set<AliasArticol>();
    public DbSet<Nir> Nir => Set<Nir>();
    public DbSet<TransferStoc> Transferuri => Set<TransferStoc>();
    public DbSet<BonConsum> BonuriConsum => Set<BonConsum>();
    public DbSet<StornoNir> StornariNir => Set<StornoNir>();
    public DbSet<CorectieNir> CorectiiNir => Set<CorectieNir>();
    public DbSet<Inventar> Inventare => Set<Inventar>();
    public DbSet<SoldInitialStoc> SolduriInitiale => Set<SoldInitialStoc>();
    public DbSet<SoldInitialAnulat> SolduriInitialeAnulate => Set<SoldInitialAnulat>();
    public DbSet<MiscareStoc> Miscari => Set<MiscareStoc>();
    public DbSet<SoldStoc> Solduri => Set<SoldStoc>();
    public DbSet<ErpConstructii.SharedKernel.RenumerotareDocument> Renumerotari => Set<ErpConstructii.SharedKernel.RenumerotareDocument>();
}

/// <summary>Istoricul renumerotărilor numerelor de pe hârtie.</summary>
internal sealed class ConfigurareRenumerotareDocument : IEntityTypeConfiguration<ErpConstructii.SharedKernel.RenumerotareDocument>
{
    public void Configure(EntityTypeBuilder<ErpConstructii.SharedKernel.RenumerotareDocument> b)
    {
        b.ToTable("renumerotari");
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentNumar).HasMaxLength(30).IsRequired();
        b.Property(x => x.NumarVechi).HasMaxLength(50);
        b.Property(x => x.NumarNou).HasMaxLength(50).IsRequired();
        b.Property(x => x.Motiv).HasMaxLength(500).IsRequired();
        b.Property(x => x.UtilizatorNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.DocumentCorectatId);
        b.HasIndex(x => new { x.FirmaId, x.DocumentId });
    }
}

internal sealed class ConfigurareDepozit : IEntityTypeConfiguration<Depozit>
{
    public void Configure(EntityTypeBuilder<Depozit> b)
    {
        b.ToTable("depozite");
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(20).IsRequired();
        b.Property(x => x.Denumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.Adresa).HasMaxLength(300);
        b.HasIndex(x => new { x.FirmaId, x.Cod }).IsUnique();
    }
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
        b.Property(x => x.GestionarNume).HasMaxLength(200);
        b.HasIndex(x => new { x.FirmaId, x.Cod }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.DepozitId });
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
        b.Property(x => x.CodExtern).HasMaxLength(30);
        b.Property(x => x.Categorie).HasMaxLength(100);
        b.HasIndex(x => new { x.FirmaId, x.Cod }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.CodExtern });
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
        b.Property(x => x.FacturaNumar).HasMaxLength(60);
        b.Property(x => x.AvizNumar).HasMaxLength(60);
        b.Property(x => x.FurnizorDenumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.Observatii).HasMaxLength(1000);
        b.Property(x => x.ReceptionatDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.AreDiferente);
        b.Ignore(x => x.AsteaptaFactura);
        b.Ignore(x => x.Document);
        b.HasIndex(x => new { x.FirmaId, x.AvizId });
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.FacturaId }); // o factură poate avea mai multe recepții (parțiale); liniile deschise se verifică sub blocarea stocului
    }
}

internal sealed class ConfigurareTransfer : IEntityTypeConfiguration<TransferStoc>
{
    public void Configure(EntityTypeBuilder<TransferStoc> b)
    {
        b.ToTable("transferuri");
        b.HasKey(x => x.Id);
        b.Property(x => x.Directie).HasConversion<string>().HasMaxLength(20).HasDefaultValue(DirectieTransfer.SpreSantier).IsRequired();
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

internal sealed class ConfigurareBonConsum : IEntityTypeConfiguration<BonConsum>
{
    public void Configure(EntityTypeBuilder<BonConsum> b)
    {
        b.ToTable("bonuri_consum");
        b.HasKey(x => x.Id);
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.Observatii).HasMaxLength(1000);
        b.Property(x => x.EmisDeNume).HasMaxLength(200).IsRequired();
        b.Property(x => x.NumarExtern).HasMaxLength(50);
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.Data });
    }
}

internal sealed class ConfigurareStornoNir : IEntityTypeConfiguration<StornoNir>
{
    public void Configure(EntityTypeBuilder<StornoNir> b)
    {
        b.ToTable("storno_nir");
        b.HasKey(x => x.Id);
        b.Property(x => x.NirNumar).HasMaxLength(30).IsRequired();
        b.Property(x => x.Motiv).HasMaxLength(1000).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.StornatDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.Linii);
        b.HasIndex(x => new { x.FirmaId, x.NirId }).IsUnique(); // un NIR se stornează o singură dată
    }
}

internal sealed class ConfigurareCorectieNir : IEntityTypeConfiguration<CorectieNir>
{
    public void Configure(EntityTypeBuilder<CorectieNir> b)
    {
        b.ToTable("corectii_nir");
        b.HasKey(x => x.Id);
        b.Property(x => x.NirNumar).HasMaxLength(30).IsRequired();
        b.Property(x => x.Motiv).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Rezumat).HasMaxLength(4000).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.DiferentaValoare).HasPrecision(18, 2);
        b.Property(x => x.CorectatDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.DocumentCorectatId);
        b.HasIndex(x => new { x.FirmaId, x.NirId });
        b.HasIndex(x => new { x.FirmaId, x.FacturaId });
    }
}

internal sealed class ConfigurareInventar : IEntityTypeConfiguration<Inventar>
{
    public void Configure(EntityTypeBuilder<Inventar> b)
    {
        b.ToTable("inventare");
        b.HasKey(x => x.Id);
        b.Property(x => x.GestiuneDenumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.Stare).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Comisie).HasMaxLength(500);
        b.Property(x => x.DeschisDeNume).HasMaxLength(200).IsRequired();
        b.Property(x => x.FinalizatDeNume).HasMaxLength(200);
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.EsteBlocat);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.GestiuneId }).IsUnique().HasFilter("stare = 'Deschis'"); // un singur inventar deschis pe gestiune
    }
}

internal sealed class ConfigurareSoldInitial : IEntityTypeConfiguration<SoldInitialStoc>
{
    public void Configure(EntityTypeBuilder<SoldInitialStoc> b)
    {
        b.ToTable("solduri_initiale");
        b.HasKey(x => x.Id);
        b.Property(x => x.GestiuneDenumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.GestiuneExterna).HasMaxLength(50).IsRequired();
        b.Property(x => x.Categorie).HasMaxLength(100);
        b.Property(x => x.NumeFisier).HasMaxLength(260).IsRequired();
        b.Property(x => x.FisierSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.ImportatDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.EsteBlocat);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.FisierSha256, x.GestiuneExterna }).IsUnique(); // același fișier o singură dată pe gestiune
    }
}

internal sealed class ConfigurareSoldInitialAnulat : IEntityTypeConfiguration<SoldInitialAnulat>
{
    public void Configure(EntityTypeBuilder<SoldInitialAnulat> b)
    {
        b.ToTable("solduri_initiale_anulate");
        b.HasKey(x => x.Id);
        b.Property(x => x.GestiuneDenumire).HasMaxLength(200).IsRequired();
        b.Property(x => x.Categorie).HasMaxLength(100);
        b.Property(x => x.NumeFisier).HasMaxLength(260).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.AnulatDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.NumarAfisat);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
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
