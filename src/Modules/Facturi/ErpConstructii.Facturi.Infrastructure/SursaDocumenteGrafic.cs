using ErpConstructii.Grafic;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Facturi.Infrastructure;

/// <summary>
/// Facturile primite ale proiectului, pentru alocarea pe graficul de execuție (D-57, etapa 2). Liniile „intră în stoc” nu se
/// alocă: materialele devin cost la consum (bonul de consum), altfel ar fi numărate de două ori. Facturile anulate nu apar.
/// Gruparea pentru propuneri: contractul facturii, altfel furnizorul.
/// </summary>
internal sealed class SursaDocumenteFacturi(FacturiDbContext db) : ISursaDocumenteCost
{
    public SursaDocumentCost Sursa => SursaDocumentCost.Factura;

    public async Task<IReadOnlyList<DocumentCost>> DocumenteAsync(Guid proiectId, CancellationToken ct = default)
    {
        var anulate = db.Anulari.Select(a => a.FacturaId);
        var facturi = await db.FacturiPrimite.AsNoTracking()
            .Where(f => f.ProiectId == proiectId && !anulate.Contains(f.Id)).OrderBy(f => f.DataEmitere).ToListAsync(ct);
        return facturi.Select(f =>
        {
            var grupare = f.ContractId is { } c ? $"contract:{c}" : $"furnizor:{f.FurnizorId?.ToString() ?? f.FurnizorDenumire.Trim().ToUpperInvariant()}";
            var detalii = string.Join(" · ", new[] { f.ContractNumar is null ? null : $"contract {f.ContractNumar}", f.NotaComandaNumar is null ? null : $"NC {f.NotaComandaNumar}" }
                .Where(x => x is not null));
            return new DocumentCost(SursaDocumentCost.Factura, f.Id, f.Numar, f.DataEmitere, f.FurnizorDenumire, f.Moneda,
                f.Linii.Select((l, i) => new LinieDocumentCost(i, l.Denumire, l.UnitateMasura, l.Cantitate, l.ValoareFaraTva, !l.IntraInStoc,
                    l.IntraInStoc ? "material de stoc: costul intră prin bonul de consum" : null, l.IntraInStoc ? null : grupare)).ToList(),
                detalii.Length == 0 ? null : detalii);
        }).ToList();
    }
}
