using ErpConstructii.Devize;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <summary>
/// Articolele de stoc pentru maparea resurselor din devize (contractul Devize, REQ-IMP-04): articolul ales și propunerile
/// după denumire (potrivirea exactă / aliasurile de la recepții, apoi asemănarea, apoi cuvintele întregi și numerele comune).
/// </summary>
internal sealed class ArticoleStocPentruMapare(StocuriDbContext db) : IArticoleStocPentruMapare
{
    public async Task<ArticolStocMapare?> ArticolAsync(Guid id, CancellationToken ct = default) =>
        await db.Articole.AsNoTracking().Where(a => a.Id == id && a.Activ)
            .Select(a => new ArticolStocMapare(a.Id, a.Cod, a.Denumire, a.UnitateMasura)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ArticolStocMapare>> PropuneAsync(string denumire, string? unitateMasura, int maxim, CancellationToken ct = default)
    {
        var articole = await db.Articole.AsNoTracking().Where(a => a.Activ)
            .Select(a => new ArticolCandidat(a.Id, a.Cod, a.Denumire, a.UnitateMasura)).ToListAsync(ct);
        var aliasuri = await db.Aliasuri.AsNoTracking().Select(a => new AliasCandidat(a.FurnizorId, a.Cheie, a.ArticolId)).ToListAsync(ct);

        var rezultat = new List<ArticolCandidat>();
        if (PotrivireArticol.PropuneSauAsemanator(denumire, unitateMasura, aliasuri, articole) is { } p)
            rezultat.Add(p.Articol);

        // Apoi articolele după cuvintele întregi și numerele comune (dimensiuni), cu aceeași UM înainte.
        rezultat.AddRange(PotrivireArticol.Ordoneaza(denumire, unitateMasura, articole));

        return rezultat.DistinctBy(a => a.Id).Take(Math.Clamp(maxim, 1, 50))
            .Select(a => new ArticolStocMapare(a.Id, a.Cod, a.Denumire, a.UnitateMasura)).ToList();
    }
}
