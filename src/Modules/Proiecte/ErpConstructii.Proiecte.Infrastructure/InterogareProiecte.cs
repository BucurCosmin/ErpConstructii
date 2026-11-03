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

    public async Task<IReadOnlyList<DateProiectPentruDocument>> ListaActiveAsync(CancellationToken ct = default) =>
        await db.Proiecte.AsNoTracking()
            .Where(p => p.Stare != StareProiect.Inchis && p.Stare != StareProiect.Pierdut)
            .OrderBy(p => p.Cod)
            .Select(p => new DateProiectPentruDocument(p.Id, p.Cod, p.Denumire, p.AdresaSantier))
            .ToListAsync(ct);
}
