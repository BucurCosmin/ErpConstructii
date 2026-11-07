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

    public async Task<LocSolicitant?> LocSolicitantAsync(Guid proiectId, Guid? santierId, Guid? compartimentId, CancellationToken ct = default)
    {
        if (santierId is { } sid)
            return await db.Santiere.AsNoTracking().Where(s => s.Id == sid && s.ProiectId == proiectId)
                .Select(s => new LocSolicitant(s.Id, true, s.Denumire, s.Activ)).FirstOrDefaultAsync(ct);
        if (compartimentId is { } cid)
            return await db.Compartimente.AsNoTracking().Where(c => c.Id == cid)
                .Select(c => new LocSolicitant(c.Id, false, c.Denumire, c.Activ)).FirstOrDefaultAsync(ct);
        return null;
    }

    public async Task<IReadOnlyList<LocSolicitant>> LocuriSolicitantAsync(Guid proiectId, CancellationToken ct = default)
    {
        var santiere = await db.Santiere.AsNoTracking().Where(s => s.ProiectId == proiectId && s.Activ)
            .OrderBy(s => s.Denumire).Select(s => new LocSolicitant(s.Id, true, s.Denumire, true)).ToListAsync(ct);
        var compartimente = await db.Compartimente.AsNoTracking().Where(c => c.Activ)
            .OrderBy(c => c.Denumire).Select(c => new LocSolicitant(c.Id, false, c.Denumire, true)).ToListAsync(ct);
        return [.. santiere, .. compartimente];
    }
}
