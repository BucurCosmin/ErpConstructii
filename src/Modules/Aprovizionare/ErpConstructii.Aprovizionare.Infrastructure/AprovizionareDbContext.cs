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
    public DbSet<AvizInsotire> Avize => Set<AvizInsotire>();
    public DbSet<CorectieNotaComanda> CorectiiNoteComanda => Set<CorectieNotaComanda>();
    public DbSet<StergereNotaComanda> StergeriNoteComanda => Set<StergereNotaComanda>();
    public DbSet<ErpConstructii.SharedKernel.RenumerotareDocument> Renumerotari => Set<ErpConstructii.SharedKernel.RenumerotareDocument>();
    public DbSet<MutareSantierNecesar> MutariSantier => Set<MutareSantierNecesar>();
}

/// <summary>Necesarele mutate de pe un șantier șters pe alt șantier al proiectului.</summary>
internal sealed class ConfigurareMutareSantierNecesar : IEntityTypeConfiguration<MutareSantierNecesar>
{
    public void Configure(EntityTypeBuilder<MutareSantierNecesar> b)
    {
        b.ToTable("mutari_santier");
        b.HasKey(x => x.Id);
        b.Property(x => x.NecesarNumar).HasMaxLength(30).IsRequired();
        b.Property(x => x.SantierVechi).HasMaxLength(300).IsRequired();
        b.Property(x => x.SantierNou).HasMaxLength(300).IsRequired();
        b.Ignore(x => x.DocumentCorectatId);
        b.HasIndex(x => new { x.FirmaId, x.NecesarId });
    }
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

internal sealed class ConfigurareCorectieNotaComanda : IEntityTypeConfiguration<CorectieNotaComanda>
{
    public void Configure(EntityTypeBuilder<CorectieNotaComanda> b)
    {
        b.ToTable("corectii_note_comanda");
        b.HasKey(x => x.Id);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Motiv).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DateVechiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.DateNoiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Rezumat).HasMaxLength(4000).IsRequired();
        b.Property(x => x.CorectatDeNume).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.NotaComandaId });
    }
}

internal sealed class ConfigurareStergereNotaComanda : IEntityTypeConfiguration<StergereNotaComanda>
{
    public void Configure(EntityTypeBuilder<StergereNotaComanda> b)
    {
        b.ToTable("stergeri_note_comanda");
        b.HasKey(x => x.Id);
        b.Property(x => x.Numar).HasMaxLength(20).IsRequired();
        b.Property(x => x.NumarExtern).HasMaxLength(50);
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.FurnizorDenumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.Motiv).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Efect).HasMaxLength(500);
        b.Property(x => x.DateJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.StearsaDeNume).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.StearsaLa });
    }
}

internal sealed class ConfigurareAvizInsotire : IEntityTypeConfiguration<AvizInsotire>
{
    public void Configure(EntityTypeBuilder<AvizInsotire> b)
    {
        b.ToTable("avize");
        b.HasKey(x => x.Id);
        b.Property(x => x.NumarExtern).HasMaxLength(50).IsRequired();
        b.Property(x => x.ProiectCod).HasMaxLength(40).IsRequired();
        b.Property(x => x.Lucrare).HasMaxLength(500);
        b.Property(x => x.FurnizorDenumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.FurnizorCui).HasMaxLength(20);
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Observatii).HasMaxLength(1000);
        b.Property(x => x.NotaComandaNumar).HasMaxLength(30);
        b.Property(x => x.InregistratDeNume).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.EsteBlocat);
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.NotaComandaId });
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.Data });
        b.HasIndex(x => new { x.FirmaId, x.FurnizorId, x.NumarExtern });
    }
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
        b.Property(x => x.FurnizorAdresa).HasMaxLength(500);
        b.Property(x => x.FurnizorTelefon).HasMaxLength(100);
        b.Property(x => x.FurnizorEmail).HasMaxLength(200);
        b.Property(x => x.LiniiJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ConditiiJson).HasColumnType("jsonb");
        b.Property(x => x.Observatii).HasMaxLength(1000);
        b.Property(x => x.Sursa).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.InregistrataDeNume).HasMaxLength(200).IsRequired();
        b.Property(x => x.Stare).HasConversion<string>().HasMaxLength(20).IsRequired(); // fără valoare implicită în model: DeAcceptat (0) trebuie scris explicit
        b.Property(x => x.AcceptataDeNume).HasMaxLength(200);
        b.Property(x => x.MotivRespingere).HasMaxLength(1000);
        b.HasIndex(x => new { x.FirmaId, x.Stare });
        b.HasIndex(x => new { x.FirmaId, x.NotaSursaId });
        b.Ignore(x => x.NumarAfisat);
        b.Ignore(x => x.Linii);
        b.Ignore(x => x.Conditii);
        b.HasIndex(x => new { x.FirmaId, x.NecesarId });
        b.Ignore(x => x.EsteBlocat);
        b.Ignore(x => x.RevineLaAchizitiiLaStergere);
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
        b.HasIndex(x => new { x.FirmaId, x.SantierId });
        b.HasIndex(x => new { x.FirmaId, x.CompartimentId });
        b.HasIndex(x => new { x.FirmaId, x.An, x.Numar }).IsUnique();
        b.HasIndex(x => new { x.FirmaId, x.ProiectId, x.Data });
    }
}
