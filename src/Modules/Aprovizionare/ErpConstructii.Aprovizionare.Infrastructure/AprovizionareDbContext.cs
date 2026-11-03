using ErpConstructii.Aprovizionare;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Aprovizionare.Infrastructure;

public sealed class AprovizionareDbContext(DbContextOptions<AprovizionareDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "aprovizionare";

    protected override string Schema => NumeSchema;

    public DbSet<NotaComanda> NoteComanda => Set<NotaComanda>();
}

internal sealed class ConfigurareNotaComanda : IEntityTypeConfiguration<NotaComanda>
{
    public void Configure(EntityTypeBuilder<NotaComanda> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.An).IsRequired();
        b.Property(x => x.Numar).IsRequired();
        b.Property(x => x.ProiectId).IsRequired();
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.Lucrare).HasMaxLength(300).IsRequired();
        b.Property(x => x.CompartimentSantier).HasMaxLength(300).IsRequired();
        b.Property(x => x.Data).IsRequired();
        b.Property(x => x.EmitentId).IsRequired();
        b.Property(x => x.EmitentNume).HasMaxLength(200).HasDefaultValue("Necunoscut").IsRequired();
        b.Property(x => x.DecisaDeNume).HasMaxLength(200);
        b.Property(x => x.SablonId).IsRequired();
        b.Property(x => x.VersiuneSablon).IsRequired();
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Stare).HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.MotivRespingere).HasMaxLength(1000);
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.Data });
    }
}
