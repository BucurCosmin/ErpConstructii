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
    public DbSet<CorectieContract> Corecturi => Set<CorectieContract>();

    // Registrele fișei de urmărire (D-56).
    public DbSet<ObligatieContract> Obligatii => Set<ObligatieContract>();
    public DbSet<ActiuneContract> Actiuni => Set<ActiuneContract>();
    public DbSet<FazaExecutie> Faze => Set<FazaExecutie>();
    public DbSet<RiscContract> Riscuri => Set<RiscContract>();
    public DbSet<InstrumentGarantie> Instrumente => Set<InstrumentGarantie>();

    // Facturile și plățile din fișă (D-56, etapa 2).
    public DbSet<FacturaContract> FacturiFisa => Set<FacturaContract>();
    public DbSet<PlataContract> Plati => Set<PlataContract>();
    public DbSet<SursaContract> Surse => Set<SursaContract>();
}

internal sealed class ConfigurareSursa : IEntityTypeConfiguration<SursaContract>
{
    public void Configure(EntityTypeBuilder<SursaContract> b)
    {
        b.ToTable("surse");
        b.HasKey(x => x.Id);
        b.Property(x => x.Document).HasMaxLength(500).IsRequired();
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
    }
}

internal sealed class ConfigurareFacturaContract : IEntityTypeConfiguration<FacturaContract>
{
    public void Configure(EntityTypeBuilder<FacturaContract> b)
    {
        b.ToTable("facturi_fisa");
        b.HasKey(x => x.Id);
        b.Property(x => x.Membru).HasMaxLength(300).IsRequired();
        b.Property(x => x.Numar).HasMaxLength(50).IsRequired();
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.SituatiePlata).HasMaxLength(100);
        b.Property(x => x.LunaExecutie).HasMaxLength(50);
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.StornoAvans).HasPrecision(18, 2);
        b.Property(x => x.Curs).HasPrecision(12, 6);
        b.Property(x => x.NetRon).HasPrecision(18, 2);
        b.Property(x => x.TvaRon).HasPrecision(18, 2);
        b.Property(x => x.TotalRon).HasPrecision(18, 2);
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
        // O factură din modulul Facturi apare o singură dată în fișa unui contract.
        b.HasIndex(x => new { x.FirmaId, x.ContractId, x.FacturaId }).IsUnique().HasFilter("factura_id IS NOT NULL");
    }
}

internal sealed class ConfigurarePlata : IEntityTypeConfiguration<PlataContract>
{
    public void Configure(EntityTypeBuilder<PlataContract> b)
    {
        b.ToTable("plati");
        b.HasKey(x => x.Id);
        b.Property(x => x.SumaRon).HasPrecision(18, 2);
        b.Property(x => x.Instrument).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Numar).HasMaxLength(100);
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
        b.HasIndex(x => new { x.FirmaId, x.FacturaContractId });
    }
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
        b.Property(x => x.TipPret).HasMaxLength(200);
        b.Property(x => x.Durata).HasMaxLength(300);
        b.Property(x => x.Amplasament).HasMaxLength(300);
        b.Property(x => x.StareText).HasMaxLength(500);
        b.Property(x => x.MembriJson).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'[]'::jsonb");
        b.Property(x => x.TranseAvansJson).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'[]'::jsonb");
        b.Property(x => x.ProcentGarantieAvans).HasPrecision(7, 4);
        b.Property(x => x.ProcentGarantieTehnica).HasPrecision(7, 4);
        b.Property(x => x.PenalitateExecutieZi).HasPrecision(9, 6);
        b.Property(x => x.PlafonPenalitateExecutie).HasPrecision(7, 4);
        b.Property(x => x.PenalitatePlataZi).HasPrecision(9, 6);
        b.Property(x => x.PlafonPenalitatePlata).HasPrecision(7, 4);
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.Membri);
        b.Ignore(x => x.TranseAvans);
        b.Ignore(x => x.Conditii);
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
        // Actele existente înainte de D-56 erau toate semnate.
        b.Property(x => x.Semnat).HasDefaultValue(true);
        b.Property(x => x.SemnatDeNume).HasMaxLength(200);
        b.HasIndex(x => new { x.FirmaId, x.ContractId, x.Numar }).IsUnique();
    }
}

internal sealed class ConfigurareCorectieContract : IEntityTypeConfiguration<CorectieContract>
{
    public void Configure(EntityTypeBuilder<CorectieContract> b)
    {
        b.ToTable("corecturi");
        b.HasKey(x => x.Id);
        b.Property(x => x.Motiv).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Modificari).IsRequired();
        b.Property(x => x.CorectatDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.DocumentCorectatId);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
    }
}

internal sealed class ConfigurareObligatie : IEntityTypeConfiguration<ObligatieContract>
{
    public void Configure(EntityTypeBuilder<ObligatieContract> b)
    {
        b.ToTable("obligatii");
        b.HasKey(x => x.Id);
        b.Property(x => x.Denumire).HasMaxLength(500).IsRequired();
        b.Property(x => x.Temei).HasMaxLength(200);
        b.Property(x => x.Termen).HasMaxLength(500);
        b.Property(x => x.Stare).HasMaxLength(100);
        b.Property(x => x.Responsabil).HasMaxLength(200);
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
    }
}

internal sealed class ConfigurareActiune : IEntityTypeConfiguration<ActiuneContract>
{
    public void Configure(EntityTypeBuilder<ActiuneContract> b)
    {
        b.ToTable("actiuni");
        b.HasKey(x => x.Id);
        b.Property(x => x.Descriere).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Prioritate).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Stare).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ResponsabilNume).HasMaxLength(200);
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
    }
}

internal sealed class ConfigurareFaza : IEntityTypeConfiguration<FazaExecutie>
{
    public void Configure(EntityTypeBuilder<FazaExecutie> b)
    {
        b.ToTable("faze_executie");
        b.HasKey(x => x.Id);
        b.Property(x => x.Denumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.Conditii).HasMaxLength(500);
        b.Property(x => x.Stare).HasMaxLength(200);
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
    }
}

internal sealed class ConfigurareRisc : IEntityTypeConfiguration<RiscContract>
{
    public void Configure(EntityTypeBuilder<RiscContract> b)
    {
        b.ToTable("riscuri");
        b.HasKey(x => x.Id);
        b.Property(x => x.Element).HasMaxLength(300).IsRequired();
        b.Property(x => x.Prevedere).HasMaxLength(1000);
        b.Property(x => x.Stare).HasMaxLength(1000);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
    }
}

internal sealed class ConfigurareInstrument : IEntityTypeConfiguration<InstrumentGarantie>
{
    public void Configure(EntityTypeBuilder<InstrumentGarantie> b)
    {
        b.ToTable("instrumente");
        b.HasKey(x => x.Id);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Descriere).HasMaxLength(500);
        b.Property(x => x.Emitent).HasMaxLength(300);
        b.Property(x => x.Numar).HasMaxLength(100);
        b.Property(x => x.Beneficiar).HasMaxLength(300);
        b.Property(x => x.Valoare).HasPrecision(18, 2);
        b.Property(x => x.Moneda).HasMaxLength(3);
        b.Property(x => x.Stare).HasMaxLength(200);
        b.Property(x => x.Observatii).HasMaxLength(2000);
        b.HasIndex(x => new { x.FirmaId, x.ContractId });
        b.HasIndex(x => new { x.FirmaId, x.DataExpirare });
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
