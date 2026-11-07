using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ErpConstructii.Infrastructure.Persistenta;

/// <summary>
/// La salvare: completează firma pe entitățile noi, câmpurile de audit și blochează
/// modificarea documentelor aprobate.
///
/// TODO (faza 0): scrierea jurnalului de audit append-only (cine, când, entitate, valori
/// vechi/noi în JSONB) în schema <c>audit</c>. Utilizatorul de aplicație are doar INSERT
/// pe acel tabel (vezi db/init/01-roluri.sql).
/// </summary>
public sealed class InterceptorAudit(IFirmaCurenta firmaCurenta, IUtilizatorCurent utilizator, ICeas ceas)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Aplica(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Aplica(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Aplica(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

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
}
