using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

internal sealed class InterogareDevize(DevizeDbContext db) : IInterogareDevize
{
    /// <summary>
    /// Versiunea de referință a proiectului (aceeași regulă pentru consumuri și buget): bugetul de execuție aprobat, altfel
    /// contractul, …, altfel cea mai recentă ciornă. Ciornele de lucru (D-53) nu intră: repetă versiunea din care pornesc.
    /// </summary>
    internal static async Task<VersiuneDeviz?> ReferintaAsync(DevizeDbContext db, Guid proiectId, CancellationToken ct)
    {
        var versiuni = await db.Versiuni.AsNoTracking()
            .Where(v => v.ProiectId == proiectId && v.Stare != StareVersiuneDeviz.Inlocuita
                        && !(v.Stare == StareVersiuneDeviz.Ciorna && v.VersiuneAnterioaraId != null))
            .Select(v => new { v.Id, v.Tip, v.Stare, v.CreatLa }).ToListAsync(ct);
        var aleasa = versiuni
            .OrderBy(v => v.Stare == StareVersiuneDeviz.Aprobata ? 0 : 1)
            .ThenBy(v => v.Tip switch { TipVersiuneDeviz.BugetExecutie => 0, TipVersiuneDeviz.Contract => 1, TipVersiuneDeviz.ActAditional => 2, _ => 3 })
            .ThenByDescending(v => v.CreatLa)
            .FirstOrDefault();
        return aleasa is null ? null : await ModulDevize.IncarcaAsync(db, aleasa.Id, ct);
    }

    public async Task<DevizReferinta?> DevizDeReferintaAsync(Guid proiectId, CancellationToken ct = default)
    {
        var v = await ReferintaAsync(db, proiectId, ct);
        if (v is null)
        {
            return null;
        }

        // „Obiect” = capitolul de pe primul nivel, „Categorie” = capitolul direct al articolului (arborele are oricâte niveluri).
        var capitole = v.Capitole.ToDictionary(c => c.Id);
        var articole = v.Articole()
            .Select(a =>
            {
                var cale = v.Cale(capitole[a.CapitolId]);
                return new ArticolDevizReferinta(a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretMaterial,
                    cale[0].Denumire, cale[^1].Denumire);
            })
            .ToList();
        return new DevizReferinta(v.Id, v.Denumire, v.Tip, v.Stare, articole);
    }
}
