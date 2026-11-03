using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ErpConstructii.Identitate.Infrastructure;

/// <summary>Încarcă permisiunile efective ale unui utilizator și le ține în cache.</summary>
internal sealed class ServiciuPermisiuni(IdentitateDbContext db, IMemoryCache cache, ICeas ceas) : IServiciuPermisiuni
{
    private static readonly TimeSpan Durata = TimeSpan.FromMinutes(10);

    public async Task<EvaluatorPermisiuni> PentruUtilizatorAsync(Guid utilizatorId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Cheie(utilizatorId), out EvaluatorPermisiuni? dinCache) && dinCache is not null)
        {
            return dinCache;
        }

        var atribuiri = await db.Atribuiri.AsNoTracking()
            .Where(a => a.UtilizatorId == utilizatorId)
            .ToListAsync(ct);
        var rolIds = atribuiri.Select(a => a.RolId).Distinct().ToList();
        var roluri = await db.Roluri.AsNoTracking()
            .Where(r => rolIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        var evaluator = EvaluatorPermisiuni.Construieste(atribuiri, roluri, ceas.Azi);
        cache.Set(Cheie(utilizatorId), evaluator, Durata);
        return evaluator;
    }

    public void Invalideaza(Guid utilizatorId) => cache.Remove(Cheie(utilizatorId));

    private static string Cheie(Guid utilizatorId) => $"permisiuni:{utilizatorId}";
}
