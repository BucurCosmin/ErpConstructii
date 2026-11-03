using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Identitate.Infrastructure;

internal sealed class InterogareFirma(IdentitateDbContext db, IFirmaCurenta firma) : IInterogareFirma
{
    // Schema identitate nu are RLS: filtrul pe firma activă e explicit.
    public async Task<DateFirmaDocument?> FirmaActivaAsync(CancellationToken ct = default) =>
        firma.FirmaId is not { } id
            ? null
            : await db.Firme.AsNoTracking()
                .Where(f => f.Id == id)
                .Select(f => new DateFirmaDocument(f.Id, f.Denumire, f.Cui, f.NrRegCom, f.Adresa))
                .FirstOrDefaultAsync(ct);
}
