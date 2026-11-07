using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Identitate.Infrastructure;

internal sealed class InterogareFirma(IdentitateDbContext db, IFirmaCurenta firma) : IInterogareFirma
{
    // Schema identitate nu are RLS: filtrul pe firma activă e explicit.
    public async Task<DateFirmaDocument?> FirmaActivaAsync(CancellationToken ct = default)
    {
        if (firma.FirmaId is not { } id)
            return null;
        var f = await db.Firme.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
            return null;
        var conturi = f.ConturiBancare;
        var cont = conturi.FirstOrDefault(c => c.Principal) ?? conturi.FirstOrDefault(c => c.Moneda == "RON") ?? conturi.FirstOrDefault();
        return new DateFirmaDocument(f.Id, f.Denumire, f.Cui, f.NrRegCom, f.Adresa, f.Telefon, f.Fax, f.Email, cont);
    }
}
