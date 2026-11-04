using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

internal sealed class InterogareDevize(DevizeDbContext db) : IInterogareDevize
{
    public async Task<DevizReferinta?> DevizDeReferintaAsync(Guid proiectId, CancellationToken ct = default)
    {
        var versiuni = await db.Versiuni.AsNoTracking().Where(v => v.ProiectId == proiectId && v.Stare != StareVersiuneDeviz.Inlocuita)
            .Select(v => new { v.Id, v.Tip, v.Stare, v.CreatLa }).ToListAsync(ct);
        var aleasa = versiuni
            .OrderBy(v => v.Stare == StareVersiuneDeviz.Aprobata ? 0 : 1)
            .ThenBy(v => v.Tip switch { TipVersiuneDeviz.BugetExecutie => 0, TipVersiuneDeviz.Contract => 1, TipVersiuneDeviz.ActAditional => 2, _ => 3 })
            .ThenByDescending(v => v.CreatLa)
            .FirstOrDefault();
        if (aleasa is null)
        {
            return null;
        }

        var v = await db.Versiuni.AsNoTracking()
            .Include(x => x.Obiecte).ThenInclude(o => o.Categorii).ThenInclude(c => c.Articole)
            .AsSplitQuery()
            .FirstAsync(x => x.Id == aleasa.Id, ct);
        var articole = v.Obiecte.OrderBy(o => o.Ordine)
            .SelectMany(o => o.Categorii.OrderBy(c => c.Ordine).SelectMany(c => c.Articole.OrderBy(a => a.Ordine)
                .Select(a => new ArticolDevizReferinta(a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretMaterial, o.Denumire, c.Denumire))))
            .ToList();
        return new DevizReferinta(v.Id, v.Denumire, v.Tip, v.Stare, articole);
    }
}
