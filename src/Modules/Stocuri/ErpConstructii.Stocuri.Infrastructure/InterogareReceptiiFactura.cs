using ErpConstructii.Aprovizionare;
using ErpConstructii.Facturi;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <summary>Starea în stoc a avizelor (pentru lista avizelor din Aprovizionare, D-40).</summary>
internal sealed class InterogareReceptiiAviz(StocuriDbContext db) : IInterogareReceptiiAviz
{
    public async Task<IReadOnlyDictionary<Guid, StareStocAviz>> StariAsync(CancellationToken ct = default)
    {
        var nir = await db.Nir.AsNoTracking()
            .Where(n => n.AvizId != null && !db.StornariNir.Any(s => s.NirId == n.Id))
            .Select(n => new { AvizId = n.AvizId!.Value, n.FacturaId, n.FacturaNumar })
            .ToListAsync(ct);
        return nir.GroupBy(n => n.AvizId).ToDictionary(g => g.Key,
            g => new StareStocAviz(g.Count(), g.All(n => n.FacturaId != null), string.Join(", ", g.Select(n => n.FacturaNumar).Where(f => f != null).Distinct())
                is { Length: > 0 } f ? f : null));
    }
}

/// <summary>Liniile facturii primite prin NIR-uri nestornate (pentru corectura facturii, în modulul Facturi).</summary>
internal sealed class InterogareReceptiiFactura(StocuriDbContext db) : IInterogareReceptiiFactura
{
    public async Task<IReadOnlySet<int>> LiniiReceptionateAsync(Guid facturaId, CancellationToken ct = default)
    {
        var nir = await db.Nir.AsNoTracking()
            .Where(n => n.FacturaId == facturaId && !db.StornariNir.Any(s => s.NirId == n.Id))
            .ToListAsync(ct);
        return nir.SelectMany(n => n.Linii).Where(l => l.CantitateReceptionata > 0).Select(l => l.IndexFactura).ToHashSet();
    }
}
