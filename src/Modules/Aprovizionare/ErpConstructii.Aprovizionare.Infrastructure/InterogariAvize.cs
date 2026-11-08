using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Aprovizionare.Infrastructure;

/// <summary>Avizele, pentru recepția în stoc (modulul Stocuri). Lucrează pe firma activă (RLS).</summary>
internal sealed class InterogareAvize(AprovizionareDbContext db) : IInterogareAvize
{
    public async Task<IReadOnlyList<AvizPentruReceptie>> ToateAsync(CancellationToken ct = default) =>
        await PentruReceptieAsync(db.Avize.AsNoTracking().OrderByDescending(a => a.Data).ThenByDescending(a => a.Numar), ct);

    public async Task<AvizPentruReceptie?> PentruReceptieAsync(Guid avizId, CancellationToken ct = default) =>
        (await PentruReceptieAsync(db.Avize.AsNoTracking().Where(a => a.Id == avizId), ct)).FirstOrDefault();

    public async Task<IReadOnlyList<AvizPentruReceptie>> AleNotelorAsync(IReadOnlyCollection<Guid> noteComandaIds, CancellationToken ct = default)
    {
        var ids = noteComandaIds.Distinct().ToList();
        return ids.Count == 0 ? [] : await PentruReceptieAsync(db.Avize.AsNoTracking().Where(a => ids.Contains(a.NotaComandaId)), ct);
    }

    private async Task<IReadOnlyList<AvizPentruReceptie>> PentruReceptieAsync(IQueryable<AvizInsotire> q, CancellationToken ct)
    {
        var avize = await q.ToListAsync(ct);
        var noteIds = avize.Select(a => a.NotaComandaId).Distinct().ToList();
        // Prețul din comandă (dacă are) pentru același material: NIR-ul pe aviz intră la el până vine factura.
        var preturi = (await db.NoteComanda.AsNoTracking().Where(n => noteIds.Contains(n.Id)).ToListAsync(ct))
            .ToDictionary(n => n.Id, n => n.Linii.Where(l => l.PretUnitar is > 0)
                .GroupBy(l => RaportNoteComanda.CheieMaterial(l.Denumire)).ToDictionary(g => g.Key, g => g.First().PretUnitar));
        return avize.Select(a => new AvizPentruReceptie(
            a.Id, a.NumarAfisat, a.NumarExtern, a.Data, a.FurnizorId, a.FurnizorDenumire, a.ProiectId, a.ProiectCod, a.NotaComandaId, a.NotaComandaNumar,
            a.Linii.Select((l, i) => new LinieAvizPentruReceptie(i, l.Denumire, l.UnitateMasura, l.Cantitate,
                preturi.GetValueOrDefault(a.NotaComandaId)?.GetValueOrDefault(RaportNoteComanda.CheieMaterial(l.Denumire)))).ToList())).ToList();
    }
}

/// <summary>Notele de comandă emise, pentru legarea facturilor (modulul Facturi, D-40).</summary>
internal sealed class InterogareNoteComanda(AprovizionareDbContext db) : IInterogareNoteComanda
{
    public async Task<NotaComandaPentruFactura?> CautaAsync(Guid id, CancellationToken ct = default) =>
        (await DinAsync(db.NoteComanda.AsNoTracking().Where(n => n.Id == id), ct)).FirstOrDefault();

    public async Task<IReadOnlyList<NotaComandaPentruFactura>> EmiseAsync(Guid? proiectId, CancellationToken ct = default)
    {
        var q = db.NoteComanda.AsNoTracking().Where(n => n.Stare == StareNotaComanda.Emisa);
        if (proiectId is { } pid)
            q = q.Where(n => n.ProiectId == pid);
        return await DinAsync(q.OrderByDescending(n => n.Data).ThenByDescending(n => n.Numar).Take(1000), ct);
    }

    private async Task<IReadOnlyList<NotaComandaPentruFactura>> DinAsync(IQueryable<NotaComanda> q, CancellationToken ct)
    {
        var note = await q.ToListAsync(ct);
        var ids = note.Select(n => n.Id).ToList();
        var avize = await db.Avize.AsNoTracking().Where(a => ids.Contains(a.NotaComandaId)).GroupBy(a => a.NotaComandaId)
            .Select(g => new { g.Key, Numar = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Numar, ct);
        return note.Select(n => new NotaComandaPentruFactura(
            n.Id, n.NumarAfisat, n.NumarExtern, n.Data, n.ProiectId, n.ProiectCod, n.FurnizorId, n.FurnizorDenumire, n.FurnizorCui,
            avize.GetValueOrDefault(n.Id), Materiale(n))).ToList();
    }

    private static string Materiale(NotaComanda n)
    {
        var text = string.Join(", ", n.Linii.Select(l => l.Denumire));
        return text.Length <= 200 ? text : text[..199] + "…";
    }
}
