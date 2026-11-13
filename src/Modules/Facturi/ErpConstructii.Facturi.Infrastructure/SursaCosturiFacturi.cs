using ErpConstructii.Devize;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Facturi.Infrastructure;

/// <summary>
/// Costurile proiectului din facturile primite (contractul Devize, bugetul proiectului): liniile „intră în stoc” sunt
/// materiale cumpărate (devin cost la consum, prin bonurile de consum), celelalte servicii / lucrări încă nerepartizate pe
/// articole. Doar facturile neanulate în lei: celelalte monede nu au încă curs în aplicație.
/// </summary>
internal sealed class SursaCosturiFacturi(FacturiDbContext db) : ISursaCosturi
{
    public async Task<IReadOnlyList<CostRealizat>> CosturiAsync(Guid proiectId, CancellationToken ct = default)
    {
        var anulate = db.Anulari.Select(a => a.FacturaId);
        var facturi = await db.FacturiPrimite.AsNoTracking()
            .Where(f => f.ProiectId == proiectId && f.Moneda == "RON" && !anulate.Contains(f.Id)).ToListAsync(ct);
        return facturi
            .SelectMany(f => f.Linii.Select(l => new CostRealizat(null, l.IntraInStoc ? NaturaCost.AchizitieMateriale : NaturaCost.Serviciu, null,
                l.ValoareFaraTva, f.DataEmitere, $"factura {f.Numar} · {f.FurnizorDenumire}", f.Id, l.Denumire)))
            .ToList();
    }
}
