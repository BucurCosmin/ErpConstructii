using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Devize.Infrastructure;

public sealed class DevizeDbContext(DbContextOptions<DevizeDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "devize";

    protected override string Schema => NumeSchema;

    public DbSet<VersiuneDeviz> Versiuni => Set<VersiuneDeviz>();
    public DbSet<CapitolDeviz> Capitole => Set<CapitolDeviz>();
    public DbSet<ArticolDeviz> Articole => Set<ArticolDeviz>();
    public DbSet<ConsumNormat> Consumuri => Set<ConsumNormat>();
    public DbSet<ResursaCatalog> Resurse => Set<ResursaCatalog>();
    public DbSet<FisierSursaDeviz> Fisiere => Set<FisierSursaDeviz>();
    public DbSet<InlocuireVersiuneDeviz> Inlocuiri => Set<InlocuireVersiuneDeviz>();
}

/// <summary>Istoricul înlocuirii versiunilor aprobate (corectura care permite starea „înlocuită”).</summary>
internal sealed class ConfigurareInlocuire : IEntityTypeConfiguration<InlocuireVersiuneDeviz>
{
    public void Configure(EntityTypeBuilder<InlocuireVersiuneDeviz> b)
    {
        b.ToTable("inlocuiri_versiuni");
        b.HasKey(x => x.Id);
        b.Ignore(x => x.DocumentCorectatId);
        b.HasIndex(x => x.VersiuneInlocuitaId);
        b.HasIndex(x => x.VersiuneNouaId);
    }
}

/// <summary>Fișierele originale ale versiunilor importate (arhivate, cu amprenta SHA-256).</summary>
internal sealed class ConfigurareFisierSursa : IEntityTypeConfiguration<FisierSursaDeviz>
{
    public void Configure(EntityTypeBuilder<FisierSursaDeviz> b)
    {
        b.ToTable("fisiere_sursa");
        b.HasKey(x => x.Id);
        b.Property(x => x.NumeFisier).HasMaxLength(260).IsRequired();
        b.Property(x => x.TipContinut).HasMaxLength(100).IsRequired();
        b.Property(x => x.Amprenta).HasMaxLength(64).IsRequired();
        b.Property(x => x.Continut).IsRequired();
        b.HasIndex(x => x.VersiuneId);
        b.HasIndex(x => new { x.FirmaId, x.Amprenta });
        b.HasOne<VersiuneDeviz>().WithMany().HasForeignKey(x => x.VersiuneId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Precizia coloanelor (D-50, docs/import-isdp.md §6.2): prețuri, valori și cantități numeric(19,6), consumuri unitare
/// numeric(24,12). Calculul nu rotunjește pe parcurs la versiunile din ISDP; rotunjirea la bani e doar la afișare.
/// </summary>
internal static class Precizie
{
    public static PropertyBuilder<decimal> Suma(this PropertyBuilder<decimal> p) => p.HasPrecision(19, 6);

    public static PropertyBuilder<decimal?> Suma(this PropertyBuilder<decimal?> p) => p.HasPrecision(19, 6);
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
        b.Property(x => x.Moneda).HasMaxLength(3).HasDefaultValue("RON").IsRequired();
        b.Property(x => x.Curs).HasPrecision(19, 6);
        b.Property(x => x.PoliticaRotunjire).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(Calcul.PoliticaRotunjire.Standard).HasSentinel((Calcul.PoliticaRotunjire)(-1));
        b.Property(x => x.Obiectiv).HasMaxLength(500);
        b.Property(x => x.Obiect).HasMaxLength(500);
        b.Property(x => x.StadiuFizic).HasMaxLength(500);
        b.Property(x => x.TotaluriDeclarateJson).HasColumnType("jsonb");
        b.Ignore(x => x.EsteBlocat);
        b.Ignore(x => x.Radacini);
        b.HasMany(x => x.Capitole).WithOne().HasForeignKey(c => c.VersiuneId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Capitole).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConfigurareCapitol : IEntityTypeConfiguration<CapitolDeviz>
{
    public void Configure(EntityTypeBuilder<CapitolDeviz> b)
    {
        b.ToTable("capitole");
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(40);
        b.Property(x => x.CodWbs).HasMaxLength(100);
        b.Property(x => x.Denumire).HasMaxLength(500);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.VersiuneId, x.ParinteId, x.Ordine });
        // Arborele: părintele e alt capitol al aceleiași versiuni. NO ACTION (verificat la sfârșitul instrucțiunii): ștergerea
        // versiunii șterge în cascadă toate capitolele ei dintr-odată.
        b.HasOne<CapitolDeviz>().WithMany().HasForeignKey(x => x.ParinteId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(x => x.Articole).WithOne().HasForeignKey(a => a.CapitolId).OnDelete(DeleteBehavior.Cascade);
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
        b.Property(x => x.SimbolBaza).HasMaxLength(40);
        b.Property(x => x.TipArticol).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(TipArticol.Normat).HasSentinel((TipArticol)(-1));
        b.Property(x => x.CodWbsSursa).HasMaxLength(100);
        b.Property(x => x.Denumire).HasMaxLength(1000);
        b.Property(x => x.UnitateMasura).HasMaxLength(20);
        b.Property(x => x.Cantitate).Suma();
        b.Property(x => x.PretMaterial).Suma();
        b.Property(x => x.PretManopera).Suma();
        b.Property(x => x.PretUtilaj).Suma();
        b.Property(x => x.PretTransport).Suma();
        b.Property(x => x.PretEchipament).Suma();
        b.Property(x => x.PretOfertaMaterial).Suma();
        b.Property(x => x.PretOfertaManopera).Suma();
        b.Property(x => x.PretOfertaUtilaj).Suma();
        b.Property(x => x.PretOfertaTransport).Suma();
        b.Property(x => x.PretOfertaEchipament).Suma();
        b.Property(x => x.ValoareOfertaMaterial).Suma();
        b.Property(x => x.ValoareOfertaManopera).Suma();
        b.Property(x => x.ValoareOfertaUtilaj).Suma();
        b.Property(x => x.ValoareOfertaTransport).Suma();
        b.Property(x => x.ValoareOfertaEchipament).Suma();
        b.Ignore(x => x.PretUnitar);
        b.Ignore(x => x.PretOfertaUnitar);
        b.Ignore(x => x.ValoareOferta);
        b.Ignore(x => x.ArePretOferta);
        b.HasMany(x => x.Consumuri).WithOne().HasForeignKey(c => c.ArticolId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Consumuri).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConfigurareConsum : IEntityTypeConfiguration<ConsumNormat>
{
    public void Configure(EntityTypeBuilder<ConsumNormat> b)
    {
        b.ToTable("consum_normat");
        b.HasKey(x => x.Id);
        b.Property(x => x.CodResursa).HasMaxLength(40);
        b.Property(x => x.Denumire).HasMaxLength(500);
        b.Property(x => x.UnitateMasura).HasMaxLength(20);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ConsumUnitar).HasPrecision(24, 12);
        b.Property(x => x.CantitateTotala).Suma();
        b.Property(x => x.PretUnitar).Suma();
        b.HasIndex(x => x.ResursaCatalogId);
        b.HasOne<ResursaCatalog>().WithMany().HasForeignKey(x => x.ResursaCatalogId).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Nomenclatorul de resurse al firmei (REQ-IMP-04): un cod pe sursă.</summary>
internal sealed class ConfigurareResursaCatalog : IEntityTypeConfiguration<ResursaCatalog>
{
    public void Configure(EntityTypeBuilder<ResursaCatalog> b)
    {
        b.ToTable("resurse_catalog");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sursa).HasMaxLength(30).IsRequired();
        b.Property(x => x.Cod).HasMaxLength(40).IsRequired();
        b.Property(x => x.Denumire).HasMaxLength(500).IsRequired();
        b.Property(x => x.UnitateMasura).HasMaxLength(20).IsRequired();
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ArticolStocDenumire).HasMaxLength(300);
        b.Property(x => x.Meserie).HasMaxLength(100);
        b.Property(x => x.Categorie).HasMaxLength(50);
        b.Property(x => x.Utilaj).HasMaxLength(200);
        b.Property(x => x.MapataDeNume).HasMaxLength(200);
        b.Ignore(x => x.EsteMapata);
        b.HasIndex(x => new { x.FirmaId, x.Sursa, x.Cod }).IsUnique();
    }
}
