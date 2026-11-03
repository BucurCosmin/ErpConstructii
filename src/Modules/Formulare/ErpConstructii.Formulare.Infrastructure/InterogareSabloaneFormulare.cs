using ErpConstructii.Formulare;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Formulare.Infrastructure;

internal sealed class InterogareSabloaneFormulare(FormulareDbContext db) : IInterogareSabloaneFormulare
{
    public async Task<ContinutSablonFormular?> SablonActivAsync(string codTip, CancellationToken ct = default)
    {
        var sablon = await db.Sabloane.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CodTip == codTip && s.Activ, ct);
        return sablon is null ? null : LaContinut(sablon);
    }

    public async Task<ContinutSablonFormular?> SablonAsync(Guid id, int versiune, CancellationToken ct = default)
    {
        var sablon = await db.Sabloane.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.Versiune == versiune, ct);
        return sablon is null ? null : LaContinut(sablon);
    }

    private static ContinutSablonFormular LaContinut(SablonFormular sablon) =>
        new(sablon.Id, sablon.CodTip, sablon.Versiune, sablon.NumeFisier, sablon.Continut);
}
