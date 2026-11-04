using ErpConstructii.Aprovizionare;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Aprovizionare.Infrastructure;

public sealed class AprovizionareDbContext(DbContextOptions<AprovizionareDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "aprovizionare";

    protected override string Schema => NumeSchema;

    public DbSet<NecesarMateriale> NecesareMateriale => Set<NecesarMateriale>();
    public DbSet<DocumentOriginal> DocumenteOriginale => Set<DocumentOriginal>();
    public DbSet<NotaComanda> NoteComanda => Set<NotaComanda>();
}

internal sealed class ConfigurareNotaComanda : IEntityTypeConfiguration<NotaComanda>
{
    public void Configure(EntityTypeBuilder<NotaComanda> b)
    {
        b.ToTable("note_comanda");
        b.HasKey(x => x.Id);
        b.Property(x => x.NumarExtern).HasMaxLength(50);
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.Lucrare).HasMaxLength(500);
        b.Property(x => x.FurnizorDenumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.FurnizorCui).HasMaxLength(20);
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Observatii).HasMaxLength(1000);
        b.Property(x => x.Sursa).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.InregistrataDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.EsteBlocat);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.Data });
        b.HasIndex(x => new { x.FirmaId, x.FurnizorId });
    }
}

internal sealed class ConfigurareDocumentOriginal : IEntityTypeConfiguration<DocumentOriginal>
{
    public void Configure(EntityTypeBuilder<DocumentOriginal> b)
    {
        b.ToTable("documente_originale");
        b.HasKey(x => x.Id);
        b.Property(x => x.NumeFisier).HasMaxLength(260).IsRequired();
        b.Property(x => x.TipContinut).HasMaxLength(100).IsRequired();
        b.Property(x => x.HashSha256).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Continut).IsRequired();
        // Același fișier nu se importă de două ori în aceeași firmă.
        b.HasIndex(x => new { x.FirmaId, x.HashSha256 }).IsUnique();
    }
}

internal sealed class ConfigurareNecesarMateriale : IEntityTypeConfiguration<NecesarMateriale>
{
    public void Configure(EntityTypeBuilder<NecesarMateriale> b)
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
        b.Property(x => x.Sursa).HasConversion<string>().HasMaxLength(20).HasDefaultValue(SursaDocument.Sistem).IsRequired();
        b.Property(x => x.NumarExtern).HasMaxLength(50);
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.Data });
    }
}
