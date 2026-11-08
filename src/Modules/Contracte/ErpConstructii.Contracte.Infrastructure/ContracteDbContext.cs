using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Contracte.Infrastructure;

public sealed class ContracteDbContext(DbContextOptions<ContracteDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "contracte";

    protected override string Schema => NumeSchema;

    public DbSet<ContractPartener> Contracte => Set<ContractPartener>();
    public DbSet<ActAditional> ActeAditionale => Set<ActAditional>();
    public DbSet<InchidereContract> Inchideri => Set<InchidereContract>();
    public DbSet<DocumentContract> Documente => Set<DocumentContract>();
}

internal sealed class ConfigurareContract : IEntityTypeConfiguration<ContractPartener>
{
    public void Configure(EntityTypeBuilder<ContractPartener> b)
    {
        b.ToTable("contracte");
        b.HasKey(x => x.Id);
        b.Property(x => x.NumarExtern).HasMaxLength(50);
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.PartenerDenumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.PartenerCui).HasMaxLength(20);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Obiect).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Moneda).HasMaxLength(3).IsRequired();
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.Avans).HasPrecision(18, 2);
        b.Property(x => x.ProcentGarantie).HasPrecision(7, 4);
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.Property(x => x.Stare).HasConversion<string>().HasMaxLength(20).IsRequired(); // fără valoare implicită: Ciorna (0) se scrie explicit
        b.Property(x => x.CreatDeNume).HasMaxLength(200).IsRequired();
        b.Property(x => x.ActivatDeNume).HasMaxLength(200);
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.EsteBlocat);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId });
        b.HasIndex(x => new { x.FirmaId, x.PartenerId });
    }
}

internal sealed class ConfigurareActAditional : IEntityTypeConfiguration<ActAditional>
{
    public void Configure(EntityTypeBuilder<ActAditional> b)
    {
        b.ToTable("acte_aditionale");
        b.HasKey(x => x.Id);
        b.Property(x => x.NumarExtern).HasMaxLength(50);
        b.Property(x => x.Obiect).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DiferentaValoare).HasPrecision(18, 2);
        b.Property(x => x.CreatDeNume).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.ContractId, x.Numar }).IsUnique();
    }
}

internal sealed class ConfigurareInchidereContract : IEntityTypeConfiguration<InchidereContract>
{
    public void Configure(EntityTypeBuilder<InchidereContract> b)
    {
        b.ToTable("inchideri");
        b.HasKey(x => x.Id);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Motiv).HasMaxLength(1000);
        b.Property(x => x.InchisDeNume).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.ContractId }).IsUnique(); // un contract se închide o singură dată
    }
}

internal sealed class ConfigurareDocumentContract : IEntityTypeConfiguration<DocumentContract>
{
    public void Configure(EntityTypeBuilder<DocumentContract> b)
    {
        b.ToTable("documente");
        b.HasKey(x => x.Id);
        b.Property(x => x.NumeFisier).HasMaxLength(260).IsRequired();
        b.Property(x => x.TipContinut).HasMaxLength(100).IsRequired();
        b.Property(x => x.HashSha256).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Continut).IsRequired();
        b.Property(x => x.IncarcatDeNume).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
    }
}
