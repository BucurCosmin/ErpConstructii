using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Identitate.Infrastructure;

internal sealed class InterogareUtilizatori(IdentitateDbContext db, IFirmaCurenta firma, ICeas ceas) : IInterogareUtilizatori
{
    // Schema identitate nu are RLS: filtrul pe firma activă e explicit.
    public async Task<IReadOnlyList<UtilizatorFirma>> UtilizatoriFirmaAsync(CancellationToken ct = default)
    {
        if (firma.FirmaId is not { } id)
            return [];
        var azi = ceas.Azi;
        var atribuiri = await db.Atribuiri.AsNoTracking().Where(a => a.FirmaId == id).ToListAsync(ct);
        var valabili = atribuiri.Where(a => a.EsteValabila(azi)).Select(a => a.UtilizatorId).Distinct().ToList();
        return await db.Utilizatori.AsNoTracking().Where(u => valabili.Contains(u.Id) && u.Activ).OrderBy(u => u.Nume)
            .Select(u => new UtilizatorFirma(u.Id, u.Nume, u.Email)).ToListAsync(ct);
    }
}
