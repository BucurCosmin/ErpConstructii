using ErpConstructii.Devize;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <summary>
/// Prețurile de achiziție din recepții (contractul Devize, D-55): liniile NIR-urilor nestornate, la prețul facturii (cu
/// corecturile NIR deja aplicate pe linii). Recepțiile pe aviz încă nefacturate (preț 0) nu dau preț.
/// </summary>
internal sealed class SursaPreturiReceptii(StocuriDbContext db) : ISursaPreturi
{
    public async Task<IReadOnlyList<PretAchizitie>> PreturiAsync(IReadOnlyCollection<Guid> articoleStoc, DateOnly? dela, CancellationToken ct = default)
    {
        if (articoleStoc.Count == 0)
            return [];
        var stornate = db.StornariNir.Select(s => s.NirId);
        var q = db.Nir.AsNoTracking().Where(n => !stornate.Contains(n.Id));
        if (dela is { } d)
            q = q.Where(n => n.Data >= d);
        var cautate = articoleStoc.ToHashSet();
        var nir = await q.ToListAsync(ct);
        return nir
            .SelectMany(n => n.Linii.Where(l => cautate.Contains(l.ArticolId) && l.PretUnitar > 0 && l.CantitateReceptionata > 0)
                .Select(l => new PretAchizitie(l.ArticolId, l.PretUnitar, l.CantitateReceptionata, n.Data, SursaPret.Receptie,
                    n.FacturaNumar is null ? n.NumarAfisat : $"{n.NumarAfisat} · factura {n.FacturaNumar}", n.Id,
                    string.IsNullOrWhiteSpace(n.FurnizorDenumire) ? null : n.FurnizorDenumire, n.ProiectId)))
            .ToList();
    }
}

/// <summary>
/// Costurile proiectului din stoc (contractul Devize, bugetul proiectului): materialele consumate pe articolele devizului
/// (bonurile de consum, la CMP); consumul fără articol intră la cheltuielile generale.
/// </summary>
internal sealed class SursaCosturiConsum(StocuriDbContext db) : ISursaCosturi
{
    public async Task<IReadOnlyList<CostRealizat>> CosturiAsync(Guid proiectId, CancellationToken ct = default)
    {
        var bonuri = await db.BonuriConsum.AsNoTracking().Where(b => b.ProiectId == proiectId).ToListAsync(ct);
        return bonuri
            .SelectMany(b => b.Linii.Select(l => new CostRealizat(l.ArticolDevizCheie, NaturaCost.Consum, Devize.Calcul.TipResursa.Material, l.Valoare,
                b.Data, b.NumarAfisat, b.Id, $"{l.ArticolCod} {l.Denumire}: {l.Cantitate:0.###} {l.UnitateMasura}")))
            .ToList();
    }
}
