using ErpConstructii.Facturi;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

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
