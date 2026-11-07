using System.Globalization;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Infrastructure.Persistenta;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Audit.Infrastructure;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> optiuni) : DbContextModul(optiuni)
{
    public const string NumeSchema = "audit";

    protected override string Schema => NumeSchema;

    public DbSet<IntrareJurnal> Jurnal => Set<IntrareJurnal>();
}

internal sealed class ConfigurareIntrareJurnal : IEntityTypeConfiguration<IntrareJurnal>
{
    public void Configure(EntityTypeBuilder<IntrareJurnal> b)
    {
        b.ToTable("jurnal");
        b.HasKey(x => x.Id);
        b.Property(x => x.UtilizatorNume).HasMaxLength(200);
        b.Property(x => x.SchemaModul).HasMaxLength(63).IsRequired();
        b.Property(x => x.Tabel).HasMaxLength(63).IsRequired();
        b.Property(x => x.Actiune).HasMaxLength(20).IsRequired();
        b.Property(x => x.Document).HasMaxLength(320);
        b.Property(x => x.Modificari).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => new { x.FirmaId, x.Moment });
        b.HasIndex(x => new { x.FirmaId, x.EntitateId });
        b.HasIndex(x => new { x.FirmaId, x.SchemaModul, x.Tabel, x.Moment });
    }
}

/// <param name="Categorie">Facturi | NIR | Stocuri | Aprovizionare; gol = toate categoriile la care utilizatorul are acces.</param>
/// <param name="Document">Numărul / denumirea documentului (conține).</param>
public sealed record FiltreJurnal(
    string? Categorie, DateOnly? DataDe, DateOnly? DataPanaLa, string? Utilizator, string? Document, Guid? EntitateId, string? Actiune);

public sealed record IntrareJurnalDto(
    Guid Id, DateTimeOffset Moment, string? Utilizator, string Categorie, string Entitate, string Actiune, string? Document, string Descriere, Guid EntitateId);

public sealed record RaportJurnalCerere(FiltreJurnal Filtre, string? DescriereFiltre);

/// <summary>
/// Jurnalul modificărilor (backlog faza 0, punctul 5): ce s-a adăugat, modificat sau șters în facturi, NIR-uri, stocuri și
/// aprovizionare, cine și când, cu valorile vechi / noi. Scris de <see cref="InterceptorAudit"/> la fiecare salvare; aici doar
/// se citește (listă și raport PDF). Fiecare categorie cere dreptul de vizualizare al modulului ei (administratorii le văd pe toate).
/// </summary>
public sealed class ModulAudit : IModul
{
    private const int Maxim = 5000;
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");
    private static readonly TimeZoneInfo OraRomaniei = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");

    public string Nume => AuditDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare) =>
        servicii.AdaugaDbContextModul<AuditDbContext>(configurare, Nume);

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/audit").WithTags("Audit");
        var financiar = Permisiuni.Pentru(ModulErp.Financiar, Actiune.Vizualizare);
        var aprovizionare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var administrare = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Vizualizare);
        g.MapGet("/jurnal", Lista).WithName("JurnalModificari")
            .WithSummary("Jurnalul modificărilor (facturi, NIR, stocuri, aprovizionare): cine, când, ce s-a schimbat")
            .Produces<IReadOnlyList<IntrareJurnalDto>>().CereOricarePermisiune(financiar, aprovizionare, administrare);
        g.MapPost("/jurnal/raport/pdf", Raport).WithName("RaportJurnalModificari")
            .WithSummary("Raportul modificărilor în PDF, pe categorie sau toate laolaltă")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").CereOricarePermisiune(financiar, aprovizionare, administrare);
    }

    private static async Task<IResult> Lista(
        [AsParameters] FiltreJurnal filtre, AuditDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        var intrari = await CitesteAsync(filtre, db, permisiuni, utilizator, firma, ct);
        return intrari is null ? Results.Unauthorized() : Results.Ok(intrari);
    }

    private static async Task<IResult> Raport(
        RaportJurnalCerere cerere, AuditDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma,
        IServiciuDocumente documente, IInterogareFirma firme, ICeas ceas, CancellationToken ct)
    {
        var intrari = await CitesteAsync(cerere.Filtre ?? new FiltreJurnal(null, null, null, null, null, null, null), db, permisiuni, utilizator, firma, ct);
        if (intrari is null)
            return Results.Unauthorized();
        var f = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa.Formatata());
        var categorie = cerere.Filtre?.Categorie;
        var campuri = new Dictionary<string, string>
        {
            ["categorie"] = string.IsNullOrWhiteSpace(categorie) ? "toate (facturi, NIR, stocuri, aprovizionare)" : categorie,
            ["perioada"] = intrari.Count == 0 ? "—" : $"{Ora(intrari.Min(i => i.Moment)):dd.MM.yyyy} – {Ora(intrari.Max(i => i.Moment)):dd.MM.yyyy}",
            ["numar"] = $"{intrari.Count} modificări{(intrari.Count >= Maxim ? $" (primele {Maxim}; restrânge filtrele)" : string.Empty)}",
            ["observatii"] = string.IsNullOrWhiteSpace(cerere.DescriereFiltre) ? string.Empty : $"Filtre: {cerere.DescriereFiltre.Trim()}",
        };
        var linii = intrari.Select((i, n) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
        {
            ["numar_crt"] = (n + 1).ToString(Ro),
            ["moment"] = Ora(i.Moment).ToString("dd.MM.yyyy HH:mm", Ro),
            ["utilizator"] = i.Utilizator ?? "—",
            ["categorie"] = i.Categorie,
            ["entitate"] = i.Entitate,
            ["document"] = i.Document ?? string.Empty,
            ["actiune"] = CatalogJurnal.Actiune(i.Actiune),
            ["descriere"] = i.Descriere,
        }).ToList();
        var aspect = new AspectDocument(
            "Jurnalul modificărilor", null, $"Întocmit la {ceas.Azi.ToString("dd.MM.yyyy", Ro)}",
            [new("Categorie", "categorie"), new("Perioada", "perioada"), new("Înregistrări", "numar")],
            "linii",
            [
                new("numar_crt", "Nr.", 0.5, AliniereColoana.Centru), new("moment", "Data, ora", 1.3), new("utilizator", "Utilizator", 1.5),
                new("categorie", "Categorie", 1.0), new("entitate", "Ce", 1.5), new("document", "Document", 2.2), new("actiune", "Operație", 0.9),
                new("descriere", "Modificări", 5.0),
            ],
            [], ["Întocmit"], "observatii", Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync("raport-jurnal", antet, aspect, date, "Jurnal_modificari.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    /// <summary>Intrările filtrate, doar din categoriile permise utilizatorului (null = neautentificat).</summary>
    private static async Task<List<IntrareJurnalDto>?> CitesteAsync(
        FiltreJurnal filtre, AuditDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return null;
        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        var domeniu = new Domeniu(fid);
        var admin = evaluator.Are(Permisiuni.Pentru(ModulErp.Administrare, Actiune.Vizualizare), domeniu);
        var fin = admin || evaluator.Are(Permisiuni.Pentru(ModulErp.Financiar, Actiune.Vizualizare), domeniu);
        var aprov = admin || evaluator.Are(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), domeniu);
        var scheme = new List<string>();
        if (fin) scheme.Add("facturi");
        if (aprov) scheme.AddRange(["stocuri", "aprovizionare", "parteneri"]);

        var q = db.Jurnal.AsNoTracking();
        if (!admin)
            q = q.Where(i => scheme.Contains(i.SchemaModul));
        var nir = CatalogJurnal.TabeleNir.ToList();
        q = filtre.Categorie switch
        {
            CategorieJurnal.Facturi => q.Where(i => i.SchemaModul == "facturi"),
            CategorieJurnal.Nir => q.Where(i => i.SchemaModul == "stocuri" && nir.Contains(i.Tabel)),
            CategorieJurnal.Stocuri => q.Where(i => i.SchemaModul == "stocuri" && !nir.Contains(i.Tabel)),
            CategorieJurnal.Aprovizionare => q.Where(i => i.SchemaModul == "aprovizionare"),
            _ => q,
        };
        if (filtre.DataDe is { } de)
        {
            var inceput = new DateTimeOffset(de.ToDateTime(TimeOnly.MinValue), OraRomaniei.GetUtcOffset(de.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
            q = q.Where(i => i.Moment >= inceput);
        }

        if (filtre.DataPanaLa is { } pana)
        {
            var sfarsit = new DateTimeOffset(pana.AddDays(1).ToDateTime(TimeOnly.MinValue), OraRomaniei.GetUtcOffset(pana.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
            q = q.Where(i => i.Moment < sfarsit);
        }

        if (!string.IsNullOrWhiteSpace(filtre.Utilizator))
            q = q.Where(i => i.UtilizatorNume != null && EF.Functions.ILike(i.UtilizatorNume, $"%{filtre.Utilizator.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filtre.Document))
            q = q.Where(i => i.Document != null && EF.Functions.ILike(i.Document, $"%{filtre.Document.Trim()}%"));
        if (filtre.EntitateId is { } eid)
            q = q.Where(i => i.EntitateId == eid);
        if (!string.IsNullOrWhiteSpace(filtre.Actiune))
            q = q.Where(i => i.Actiune == filtre.Actiune);

        var lista = await q.OrderByDescending(i => i.Moment).ThenBy(i => i.Id).Take(Maxim).ToListAsync(ct);
        return lista.Select(i => new IntrareJurnalDto(
            i.Id, i.Moment, i.UtilizatorNume, CatalogJurnal.Categorie(i.SchemaModul, i.Tabel), CatalogJurnal.Entitate(i.Tabel), i.Actiune, i.Document,
            CatalogJurnal.Descriere(i.Actiune, i.Modificari), i.EntitateId)).ToList();
    }

    private static DateTime Ora(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, OraRomaniei).DateTime;
}
