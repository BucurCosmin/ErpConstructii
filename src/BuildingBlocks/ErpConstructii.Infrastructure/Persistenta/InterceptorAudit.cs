using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace ErpConstructii.Infrastructure.Persistenta;

/// <summary>
/// La salvare: completează firma pe entitățile noi, câmpurile de audit și blochează modificarea documentelor aprobate
/// (cu excepția celor corectate în aceeași salvare, D-33). Scrie jurnalul de modificări append-only (<c>audit.jurnal</c>):
/// pentru fiecare entitate de firmă adăugată, modificată sau ștearsă — cine, când, ce tabel, ce document și valorile
/// vechi / noi (JSONB). Jurnalul se scrie în aceeași tranzacție cu modificarea: dacă salvarea eșuează, nu rămâne nimic.
/// Utilizatorul de aplicație are doar SELECT / INSERT pe tabel (db/init/01-roluri.sql).
/// </summary>
public sealed class InterceptorAudit(IFirmaCurenta firmaCurenta, IUtilizatorCurent utilizator, ICeas ceas)
    : SaveChangesInterceptor
{
    private const int LungimeMaximaValoare = 2000;
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private static readonly HashSet<string> CampuriTehnice = ["Id", "FirmaId", "CreatLa", "CreatDe", "ModificatLa", "ModificatDe"];

    private sealed record IntrarePregatita(EntityEntry Intrare, string Schema, string Tabel, string Actiune, string? Document, string Modificari);

    private sealed class Salvare
    {
        public List<IntrarePregatita> Intrari { get; } = [];
        public IDbContextTransaction? TranzactiePropie { get; set; }
    }

    private readonly ConcurrentDictionary<DbContext, Salvare> _salvari = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Aplica(context);
            if (Pregateste(context) is { } s && context.Database.CurrentTransaction is null)
                s.TranzactiePropie = context.Database.BeginTransaction();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            Aplica(context);
            if (Pregateste(context) is { } s && context.Database.CurrentTransaction is null)
                s.TranzactiePropie = await context.Database.BeginTransactionAsync(cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context && _salvari.TryRemove(context, out var s))
        {
            context.Database.ExecuteSqlRaw(Insert(s, out var parametri), parametri);
            s.TranzactiePropie?.Commit();
            s.TranzactiePropie?.Dispose();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && _salvari.TryRemove(context, out var s))
        {
            await context.Database.ExecuteSqlRawAsync(Insert(s, out var parametri), parametri, cancellationToken);
            if (s.TranzactiePropie is { } t)
            {
                await t.CommitAsync(cancellationToken);
                await t.DisposeAsync();
            }
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context && _salvari.TryRemove(context, out var s) && s.TranzactiePropie is { } t)
        {
            t.Rollback();
            t.Dispose();
        }
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && _salvari.TryRemove(context, out var s) && s.TranzactiePropie is { } t)
        {
            await t.RollbackAsync(cancellationToken);
            await t.DisposeAsync();
        }
    }

    private void Aplica(DbContext context)
    {
        var acum = ceas.Acum;
        // Documentele blocate care primesc o corectură (entitate separată) în această salvare.
        var corectate = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added && e.Entity is ICorectieDocument)
            .Select(e => ((ICorectieDocument)e.Entity).DocumentCorectatId)
            .ToHashSet();
        foreach (var intrare in context.ChangeTracker.Entries())
        {
            if (intrare.State == EntityState.Added && intrare.Entity is IApartineFirmei deFirma)
            {
                var firma = firmaCurenta.FirmaId
                    ?? throw new InvalidOperationException("Nu se poate salva o entitate de firmă fără firmă curentă.");
                deFirma.SeteazaFirma(firma);
            }

            // Un document care ERA blocat (după valorile originale din baza de date) nu se mai modifică.
            // Informațiile care se schimbă după aprobare (ex. starea în e-Factura, plățile) stau în
            // entități separate, nu pe documentul aprobat.
            if (intrare.State == EntityState.Modified && intrare.Entity is IDocumentCuStare
                && intrare.OriginalValues.ToObject() is IDocumentCuStare { EsteBlocat: true }
                && !(intrare.Entity is Entitate e && corectate.Contains(e.Id)))
            {
                throw new InvalidOperationException(
                    $"{intrare.Entity.GetType().Name} este aprobat și nu mai poate fi modificat. Folosește storno sau o versiune nouă.");
            }

            if (intrare.Entity is not IAuditabil auditabil)
            {
                continue;
            }

            switch (intrare.State)
            {
                case EntityState.Added:
                    auditabil.CreatLa = acum;
                    auditabil.CreatDe = utilizator.UtilizatorId;
                    break;
                case EntityState.Modified:
                    auditabil.ModificatLa = acum;
                    auditabil.ModificatDe = utilizator.UtilizatorId;
                    break;
            }
        }
    }

    /// <summary>Valorile vechi / noi se citesc înainte de salvare (după salvare, valorile originale devin cele curente).</summary>
    private Salvare? Pregateste(DbContext context)
    {
        var s = new Salvare();
        var intrari = context.ChangeTracker.Entries().ToList();
        // O corectură (D-33) poartă în jurnal numărul documentului corectat, salvat odată cu ea.
        var dupaId = intrari.Where(e => e.Entity is Entitate).GroupBy(e => ((Entitate)e.Entity).Id).ToDictionary(g => g.Key, g => g.First().Entity);
        foreach (var intrare in intrari)
        {
            if (intrare.Entity is not IApartineFirmei || intrare.Entity is IFaraJurnal
                || intrare.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            var schema = intrare.Metadata.GetSchema() ?? intrare.Metadata.GetDefaultSchema() ?? string.Empty;
            if (schema == "audit")
                continue;

            var modificari = new Dictionary<string, object?>();
            foreach (var p in intrare.Properties)
            {
                var nume = p.Metadata.Name;
                if (CampuriTehnice.Contains(nume) || p.Metadata.ClrType == typeof(byte[]))
                    continue;
                switch (intrare.State)
                {
                    case EntityState.Added when p.CurrentValue is not null:
                        modificari[nume] = Valoare(p.CurrentValue);
                        break;
                    case EntityState.Modified when p.IsModified && !Equals(p.OriginalValue, p.CurrentValue):
                        modificari[nume] = new[] { Valoare(p.OriginalValue), Valoare(p.CurrentValue) };
                        break;
                    // La ștergere rămân în jurnal valorile pe care le avea înregistrarea.
                    case EntityState.Deleted when p.OriginalValue is not null:
                        modificari[nume] = Valoare(p.OriginalValue);
                        break;
                }
            }

            // Proprietățile complexe (ex. adresa) apar ca un singur câmp text.
            foreach (var c in intrare.ComplexProperties)
            {
                if (intrare.State == EntityState.Added && c.CurrentValue is not null)
                    modificari[c.Metadata.Name] = Valoare(c.CurrentValue);
            }

            if (intrare.State == EntityState.Modified && modificari.Count == 0)
                continue;
            var actiune = intrare.State switch { EntityState.Added => "Adaugat", EntityState.Modified => "Modificat", _ => "Sters" };
            s.Intrari.Add(new IntrarePregatita(intrare, schema, intrare.Metadata.GetTableName() ?? intrare.Metadata.ClrType.Name, actiune,
                intrare.Entity is ICorectieDocument cd && dupaId.TryGetValue(cd.DocumentCorectatId, out var corectat) ? Document(corectat) : Document(intrare.Entity),
                JsonSerializer.Serialize(modificari, Json)));
        }

        if (s.Intrari.Count == 0)
            return null;
        _salvari[context] = s;
        return s;
    }

    private string Insert(Salvare s, out object[] parametri)
    {
        var firma = firmaCurenta.FirmaId ?? throw new InvalidOperationException("Jurnalul de modificări cere firma curentă.");
        var sql = new StringBuilder(
            "INSERT INTO audit.jurnal (id, firma_id, moment, utilizator_id, utilizator_nume, schema_modul, tabel, entitate_id, actiune, document, modificari) VALUES ");
        var lista = new List<object>();
        var moment = ceas.Acum;
        for (var i = 0; i < s.Intrari.Count; i++)
        {
            var x = s.Intrari[i];
            var entitateId = x.Intrare.Entity is Entitate e ? e.Id : Guid.Empty;
            if (i > 0)
                sql.Append(", ");
            var b = i * 11;
            sql.Append($"(@p{b}, @p{b + 1}, @p{b + 2}, @p{b + 3}, @p{b + 4}, @p{b + 5}, @p{b + 6}, @p{b + 7}, @p{b + 8}, @p{b + 9}, @p{b + 10})");
            lista.AddRange(
            [
                new NpgsqlParameter($"p{b}", Guid.CreateVersion7()),
                new NpgsqlParameter($"p{b + 1}", firma),
                new NpgsqlParameter($"p{b + 2}", moment),
                new NpgsqlParameter($"p{b + 3}", (object?)utilizator.UtilizatorId ?? DBNull.Value),
                new NpgsqlParameter($"p{b + 4}", (object?)Taie(utilizator.Nume, 200) ?? DBNull.Value),
                new NpgsqlParameter($"p{b + 5}", x.Schema),
                new NpgsqlParameter($"p{b + 6}", x.Tabel),
                new NpgsqlParameter($"p{b + 7}", entitateId),
                new NpgsqlParameter($"p{b + 8}", x.Actiune),
                new NpgsqlParameter($"p{b + 9}", (object?)Taie(x.Document, 300) ?? DBNull.Value),
                new NpgsqlParameter($"p{b + 10}", NpgsqlDbType.Jsonb) { Value = x.Modificari },
            ]);
        }

        parametri = [.. lista];
        return sql.ToString();
    }

    /// <summary>Numărul documentului / denumirea, pentru a-l recunoaște în raport (NIR/2026/0004, numărul facturii, codul materialului…).</summary>
    private static string? Document(object entitate)
    {
        var tip = entitate.GetType();
        foreach (var nume in new[] { "NumarAfisat", "DocumentNumar", "NirNumar", "Numar", "Cod", "Denumire" })
        {
            if (tip.GetProperty(nume)?.GetValue(entitate) is { } v && v.ToString() is { Length: > 0 } text && text != "0")
            {
                var denumire = nume is "Cod" or "Numar" or "NumarAfisat" && tip.GetProperty("Denumire")?.GetValue(entitate) is string d && d.Length > 0 ? $" {d}" : string.Empty;
                var furnizor = tip.GetProperty("FurnizorDenumire")?.GetValue(entitate) is string f && f.Length > 0 ? $" ({f})" : string.Empty;
                return text + denumire + furnizor;
            }
        }

        return null;
    }

    private static object? Valoare(object? v) => v switch
    {
        null => null,
        string s => Taie(s, LungimeMaximaValoare),
        decimal or int or long or bool or Guid or DateOnly or DateTime or DateTimeOffset or double => v,
        Enum e => e.ToString(),
        _ => Taie(v.ToString(), LungimeMaximaValoare),
    };

    private static string? Taie(string? s, int max) => s is null || s.Length <= max ? s : s[..max] + "…";
}
