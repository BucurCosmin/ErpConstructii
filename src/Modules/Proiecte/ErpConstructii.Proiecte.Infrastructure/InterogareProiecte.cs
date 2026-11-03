using ErpConstructii.Proiecte;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Proiecte.Infrastructure;

internal sealed class InterogareProiecte(ProiecteDbContext db) : IInterogareProiecte
{
    public Task<DateProiectPentruDocument?> CautaPentruDocumentAsync(Guid proiectId, CancellationToken ct = default) =>
        db.Proiecte.AsNoTracking()
            .Where(p => p.Id == proiectId)
            .Select(p => new DateProiectPentruDocument(p.Id, p.Cod, p.Denumire, p.AdresaSantier))
            .FirstOrDefaultAsync(ct);
}
