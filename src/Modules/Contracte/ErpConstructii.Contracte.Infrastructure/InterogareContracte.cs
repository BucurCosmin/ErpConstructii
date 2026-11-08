using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Contracte.Infrastructure;

/// <summary>Contractele, pentru legarea facturilor de servicii (modulul Facturi, D-41). Lucrează pe firma activă (RLS).</summary>
internal sealed class InterogareContracte(ContracteDbContext db) : IInterogareContracte
{
    public async Task<ContractPentruFactura?> CautaAsync(Guid id, CancellationToken ct = default) =>
        (await DinAsync(db.Contracte.AsNoTracking().Where(c => c.Id == id), ct)).FirstOrDefault();

    public async Task<IReadOnlyList<ContractPentruFactura>> DeschiseAsync(Guid proiectId, CancellationToken ct = default) =>
        (await DinAsync(db.Contracte.AsNoTracking().Where(c => c.ProiectId == proiectId && c.Stare == StareContract.Activ), ct)).Where(c => c.Deschis).ToList();

    private async Task<IReadOnlyList<ContractPentruFactura>> DinAsync(IQueryable<ContractPartener> q, CancellationToken ct)
    {
        var contracte = await q.OrderByDescending(c => c.DataSemnarii).ToListAsync(ct);
        var ids = contracte.Select(c => c.Id).ToList();
        var acte = await db.ActeAditionale.AsNoTracking().Where(a => ids.Contains(a.ContractId)).GroupBy(a => a.ContractId)
            .Select(g => new { g.Key, Suma = g.Sum(a => a.DiferentaValoare) }).ToDictionaryAsync(x => x.Key, x => x.Suma, ct);
        var inchise = (await db.Inchideri.AsNoTracking().Where(i => ids.Contains(i.ContractId)).Select(i => i.ContractId).ToListAsync(ct)).ToHashSet();
        return contracte.Select(c => new ContractPentruFactura(
            c.Id, c.NumarAfisat, c.NumarExtern, c.DataSemnarii, c.ProiectId, c.ProiectCod, c.PartenerId, c.PartenerDenumire, c.PartenerCui, c.Tip, c.Obiect,
            c.Moneda, c.Valoare + acte.GetValueOrDefault(c.Id), c.ProcentGarantie, c.TaxareInversa,
            c.Stare == StareContract.Activ && !inchise.Contains(c.Id))).ToList();
    }
}
