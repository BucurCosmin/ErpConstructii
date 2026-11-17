using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Grafic.Infrastructure;

public sealed class GraficDbContext(DbContextOptions<GraficDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "grafic";

    protected override string Schema => NumeSchema;

    public DbSet<GraficExecutie> Grafice => Set<GraficExecutie>();
    public DbSet<VersiuneGrafic> Versiuni => Set<VersiuneGrafic>();
    public DbSet<FisierVersiuneGrafic> Fisiere => Set<FisierVersiuneGrafic>();
    public DbSet<NodWbsGrafic> Noduri => Set<NodWbsGrafic>();
    public DbSet<ActivitateGrafic> Activitati => Set<ActivitateGrafic>();

    // Alocarea documentelor de cost pe activități / noduri WBS (D-57, etapa 2).
    public DbSet<AlocareCost> Alocari => Set<AlocareCost>();
}

internal sealed class ConfigurareAlocare : IEntityTypeConfiguration<AlocareCost>
{
    public void Configure(EntityTypeBuilder<AlocareCost> b)
    {
        b.ToTable("alocari_cost");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sursa).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.DocumentNumar).HasMaxLength(100).IsRequired();
        b.Property(x => x.LinieDenumire).HasMaxLength(500).IsRequired();
        b.Property(x => x.TipTinta).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Cheie).HasMaxLength(500).IsRequired();
        b.Property(x => x.DenumireTinta).HasMaxLength(500).IsRequired();
        b.Property(x => x.Procent).HasPrecision(9, 4);
        b.Property(x => x.CreatDeNume).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.GraficId, x.Sursa, x.DocumentId });
        b.HasIndex(x => new { x.GraficId, x.Sursa, x.DocumentId, x.LinieIndex, x.TipTinta, x.Cheie }).IsUnique();
    }
}

internal sealed class ConfigurareGrafic : IEntityTypeConfiguration<GraficExecutie>
{
    public void Configure(EntityTypeBuilder<GraficExecutie> b)
    {
        b.ToTable("grafice");
        b.HasKey(x => x.Id);
        b.Property(x => x.Denumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.CodSursa).HasMaxLength(100);
        b.Property(x => x.Moneda).HasMaxLength(3);
        b.Property(x => x.Format).HasMaxLength(30).IsRequired();
        b.Property(x => x.CreatDeNume).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId });
    }
}

internal sealed class ConfigurareVersiune : IEntityTypeConfiguration<VersiuneGrafic>
{
    public void Configure(EntityTypeBuilder<VersiuneGrafic> b)
    {
        b.ToTable("versiuni");
        b.HasKey(x => x.Id);
        b.Property(x => x.Denumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.NumeFisier).HasMaxLength(300).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ImportatDeNume).HasMaxLength(200).IsRequired();
        b.Property(x => x.Procent).HasPrecision(7, 2);
        b.Property(x => x.CostPlanificat).HasPrecision(18, 2);
        b.Property(x => x.CostRealizat).HasPrecision(18, 2);
        b.Property(x => x.CostRamas).HasPrecision(18, 2);
        b.Property(x => x.CoduriJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ComparatieJson).HasColumnType("jsonb");
        b.Ignore(x => x.Coduri);
        b.Ignore(x => x.Comparatie);
        b.HasIndex(x => new { x.GraficId, x.Numar }).IsUnique();
    }
}

internal sealed class ConfigurareFisier : IEntityTypeConfiguration<FisierVersiuneGrafic>
{
    public void Configure(EntityTypeBuilder<FisierVersiuneGrafic> b)
    {
        b.ToTable("fisiere");
        b.HasKey(x => x.Id);
        b.Property(x => x.Continut).IsRequired();
        b.HasIndex(x => x.VersiuneId).IsUnique();
    }
}

internal sealed class ConfigurareNod : IEntityTypeConfiguration<NodWbsGrafic>
{
    public void Configure(EntityTypeBuilder<NodWbsGrafic> b)
    {
        b.ToTable("noduri_wbs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Cheie).HasMaxLength(500).IsRequired();
        b.Property(x => x.Cod).HasMaxLength(100).IsRequired();
        b.Property(x => x.Denumire).HasMaxLength(500).IsRequired();
        b.Property(x => x.CheieParinte).HasMaxLength(500);
        b.HasIndex(x => new { x.VersiuneId, x.Cheie }).IsUnique();
    }
}

internal sealed class ConfigurareActivitate : IEntityTypeConfiguration<ActivitateGrafic>
{
    public void Configure(EntityTypeBuilder<ActivitateGrafic> b)
    {
        b.ToTable("activitati");
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(100).IsRequired();
        b.Property(x => x.Denumire).HasMaxLength(500).IsRequired();
        b.Property(x => x.CheieWbs).HasMaxLength(500);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Stadiu).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Procent).HasPrecision(7, 2);
        b.Property(x => x.DurataZile).HasPrecision(10, 2);
        b.Property(x => x.DurataRamasaZile).HasPrecision(10, 2);
        b.Property(x => x.RezervaTotalaZile).HasPrecision(10, 2);
        b.Property(x => x.Calendar).HasMaxLength(200);
        b.Property(x => x.Constrangere).HasMaxLength(100);
        b.Property(x => x.CostPlanificat).HasPrecision(18, 2);
        b.Property(x => x.CostRealizat).HasPrecision(18, 2);
        b.Property(x => x.CostRamas).HasPrecision(18, 2);
        b.Property(x => x.DependenteJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CoduriJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ResurseJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.PasiJson).HasColumnType("jsonb").IsRequired();
        b.Ignore(x => x.Dependente);
        b.Ignore(x => x.Coduri);
        b.Ignore(x => x.Resurse);
        b.Ignore(x => x.Pasi);
        b.HasIndex(x => new { x.VersiuneId, x.Cod }).IsUnique();
    }
}
