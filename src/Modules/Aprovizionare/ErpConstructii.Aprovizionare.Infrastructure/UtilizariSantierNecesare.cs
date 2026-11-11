using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Aprovizionare.Infrastructure;

/// <summary>Necesarele de materiale emise pe un șantier: numărate și mutate când șantierul se șterge (Proiecte).</summary>
internal sealed class UtilizariSantierNecesare(AprovizionareDbContext db, ICeas ceas) : IUtilizariSantier
{
    public Task<int> NumaraAsync(Guid santierId, CancellationToken ct = default) =>
        db.NecesareMateriale.CountAsync(n => n.SantierId == santierId, ct);

    public async Task MutaAsync(Guid santierId, LocSolicitant tinta, CancellationToken ct = default)
    {
        var necesare = await db.NecesareMateriale.Where(n => n.SantierId == santierId).ToListAsync(ct);
        foreach (var n in necesare)
        {
            var r = n.MutaPeSantier(new LocCandidat(tinta.Id, true, tinta.Denumire), ceas.Acum);
            if (r.Esec)
                throw new InvalidOperationException(r.Eroare.Mesaj);
            db.MutariSantier.Add(r.Valoare);
        }

        await db.SaveChangesAsync(ct);
    }
}
