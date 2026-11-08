using ErpConstructii.Aprovizionare;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Facturi.Infrastructure;

/// <summary>Facturile legate de o notă de comandă (pentru ștergerea avizului și a notei generate din el, D-40).</summary>
internal sealed class InterogareFacturiNotaComanda(FacturiDbContext db) : IInterogareFacturiNotaComanda
{
    public Task<int> FacturiAsync(Guid notaComandaId, CancellationToken ct = default) =>
        db.FacturiPrimite.CountAsync(f => f.NotaComandaId == notaComandaId && !db.Anulari.Any(a => a.FacturaId == f.Id), ct);
}

internal sealed class InterogareFacturiPrimite(FacturiDbContext db) : IInterogareFacturiPrimite
{
    public async Task<IReadOnlyList<FacturaPentruReceptie>> CuMaterialeDeStocAsync(CancellationToken ct = default)
    {
        var facturi = await Neanulate().Where(f => f.TotalDePlata > 0).OrderByDescending(f => f.DataEmitere).ToListAsync(ct);
        return facturi.Select(CuLiniiDeStoc).Where(f => f is not null).Select(f => f!).ToList();
    }

    public async Task<FacturaPentruReceptie?> PentruReceptieAsync(Guid facturaId, CancellationToken ct = default)
    {
        var f = await Neanulate().FirstOrDefaultAsync(x => x.Id == facturaId, ct);
        return f is null ? null : CuLiniiDeStoc(f);
    }

    public async Task<IReadOnlySet<Guid>> FacturiAnulateAsync(CancellationToken ct = default) =>
        (await db.Anulari.AsNoTracking().Select(a => a.FacturaId).ToListAsync(ct)).ToHashSet();

    private IQueryable<FacturaPrimita> Neanulate() =>
        db.FacturiPrimite.AsNoTracking().Where(f => !db.Anulari.Any(a => a.FacturaId == f.Id));

    private static FacturaPentruReceptie? CuLiniiDeStoc(FacturaPrimita f)
    {
        var linii = f.Linii
            .Select((l, i) => (l, i))
            .Where(x => x.l.IntraInStoc && x.l.Cantitate > 0)
            .Select(x => new LinieFacturaPentruReceptie(x.i, x.l.Denumire, x.l.UnitateMasura, x.l.Cantitate, x.l.PretUnitar, x.l.ValoareFaraTva))
            .ToList();
        return linii.Count == 0
            ? null
            : new FacturaPentruReceptie(f.Id, f.Numar, f.DataEmitere, f.FurnizorId, f.FurnizorDenumire, f.ProiectId, f.ProiectCod, linii,
                f.NotaComandaId, f.NotaComandaNumar);
    }
}
