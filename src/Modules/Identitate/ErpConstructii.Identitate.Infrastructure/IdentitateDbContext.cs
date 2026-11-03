using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpConstructii.Identitate.Infrastructure;

/// <summary>
/// Schema <c>identitate</c>. EXCEPȚIE de la RLS pe firmă: la login trebuie citite atribuirile
/// utilizatorului pe toate firmele lui, înainte să existe o firmă activă. Accesul la aceste
/// tabele trece exclusiv prin <see cref="ServiciuPermisiuni"/>.
/// </summary>
public sealed class IdentitateDbContext(DbContextOptions<IdentitateDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "identitate";

    protected override string Schema => NumeSchema;

    public DbSet<Firma> Firme => Set<Firma>();
    public DbSet<Utilizator> Utilizatori => Set<Utilizator>();
    public DbSet<Rol> Roluri => Set<Rol>();
    public DbSet<Atribuire> Atribuiri => Set<Atribuire>();
}

internal sealed class ConfigurareFirma : IEntityTypeConfiguration<Firma>
{
    public void Configure(EntityTypeBuilder<Firma> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Denumire).HasMaxLength(300).IsRequired();
        b.Property(x => x.Cui).HasMaxLength(20);
        b.Property(x => x.NrRegCom).HasMaxLength(40);
        b.Property(x => x.Telefon).HasMaxLength(60);
        b.Property(x => x.Fax).HasMaxLength(60);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Website).HasMaxLength(256);
        b.ComplexProperty(x => x.Adresa, a =>
        {
            a.Property(x => x.Strada).HasColumnName("adresa_strada").HasMaxLength(300);
            a.Property(x => x.Localitate).HasColumnName("adresa_localitate").HasMaxLength(100);
            a.Property(x => x.Judet).HasColumnName("adresa_judet").HasMaxLength(3);
            a.Property(x => x.CodPostal).HasColumnName("adresa_cod_postal").HasMaxLength(10);
            a.Property(x => x.Tara).HasColumnName("adresa_tara").HasMaxLength(2);
        });
        // Conturile bancare: listă mică, citită mereu cu firma → jsonb.
        b.ComplexCollection(x => x.ConturiBancare, c => c.ToJson("conturi_bancare"));
    }
}

internal sealed class ConfigurareUtilizator : IEntityTypeConfiguration<Utilizator>
{
    public void Configure(EntityTypeBuilder<Utilizator> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.Nume).HasMaxLength(200).IsRequired();
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(40);
    }
}

internal sealed class ConfigurareRol : IEntityTypeConfiguration<Rol>
{
    public void Configure(EntityTypeBuilder<Rol> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Cod).HasMaxLength(60).IsRequired();
        b.Property(x => x.Denumire).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.Cod }).IsUnique();
        b.HasOne<Firma>().WithMany().HasForeignKey(x => x.FirmaId).OnDelete(DeleteBehavior.Restrict);
        // Lista de permisiuni ca text[] în PostgreSQL (Npgsql mapează nativ colecțiile primitive).
        b.Property<List<string>>("_permisiuni").HasColumnName("permisiuni");
        b.Ignore(x => x.Permisiuni);
    }
}

internal sealed class ConfigurareAtribuire : IEntityTypeConfiguration<Atribuire>
{
    public void Configure(EntityTypeBuilder<Atribuire> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UtilizatorId, x.FirmaId });
        b.HasOne<Utilizator>().WithMany().HasForeignKey(x => x.UtilizatorId);
        b.HasOne<Rol>().WithMany().HasForeignKey(x => x.RolId);
        b.HasOne<Firma>().WithMany().HasForeignKey(x => x.FirmaId).OnDelete(DeleteBehavior.Restrict);
    }
}
