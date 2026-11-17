using ErpConstructii.Grafic;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <summary>
/// Bonurile de consum ale proiectului (materialele consumate, la CMP, în lei), pentru alocarea pe graficul de execuție
/// (D-57, etapa 2). Gruparea pentru propuneri: articolul de deviz pe care s-a consumat.
/// </summary>
internal sealed class SursaDocumenteConsum(StocuriDbContext db) : ISursaDocumenteCost
{
    public SursaDocumentCost Sursa => SursaDocumentCost.BonConsum;

    public async Task<IReadOnlyList<DocumentCost>> DocumenteAsync(Guid proiectId, CancellationToken ct = default)
    {
        var bonuri = await db.BonuriConsum.AsNoTracking().Where(b => b.ProiectId == proiectId).OrderBy(b => b.Data).ToListAsync(ct);
        var gestiuni = await db.Gestiuni.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Denumire, ct);
        return bonuri.Select(b => new DocumentCost(
            SursaDocumentCost.BonConsum, b.Id, b.NumarExtern is { Length: > 0 } ne ? $"{b.NumarAfisat} ({ne})" : b.NumarAfisat, b.Data,
            gestiuni.GetValueOrDefault(b.GestiuneId), "RON",
            b.Linii.Select((l, i) => new LinieDocumentCost(i, $"{l.ArticolCod} {l.Denumire}", l.UnitateMasura, l.Cantitate, l.Valoare, true,
                l.ArticolDevizSimbol is null ? null : $"articol {l.ArticolDevizSimbol}",
                l.ArticolDevizCheie is { } a ? $"articol:{a}" : null)).ToList(),
            b.Observatii)).ToList();
    }
}
