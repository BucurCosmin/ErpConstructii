using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

internal sealed class InterogareDevize(DevizeDbContext db) : IInterogareDevize
{
    /// <summary>
    /// Versiunile de referință ale proiectului, câte una pe obiectiv (D-59; aceeași regulă pentru consumuri și buget): bugetul de
    /// execuție aprobat, altfel contractul, …, altfel cea mai recentă ciornă. Ciornele de lucru (D-53) nu intră: repetă versiunea
    /// din care pornesc. <paramref name="obiectivId"/> = doar obiectivul acela.
    /// </summary>
    internal static async Task<IReadOnlyList<(ObiectivDeviz Obiectiv, VersiuneDeviz Versiune)>> ReferinteAsync(
        DevizeDbContext db, Guid proiectId, Guid? obiectivId, CancellationToken ct)
    {
        var referinte = await EndpointuriObiective.ReferinteAsync(db, proiectId, obiectivId, ct);
        var obiective = await db.Obiective.AsNoTracking().Where(o => o.ProiectId == proiectId).ToDictionaryAsync(o => o.Id, ct);
        var rezultat = new List<(ObiectivDeviz, VersiuneDeviz)>();
        foreach (var o in obiective.Values.OrderBy(o => o.Ordine).ThenBy(o => o.CreatLa))
        {
            if (referinte.TryGetValue(o.Id, out var id) && await ModulDevize.IncarcaAsync(db, id, ct) is { } v)
                rezultat.Add((o, v));
        }

        return rezultat;
    }

    /// <summary>
    /// Articolele tuturor obiectivelor (versiunea de referință a fiecăruia), ca șantierul să consume pe oricare. Cu mai multe
    /// obiective, „Obiect” începe cu obiectivul.
    /// </summary>
    public async Task<DevizReferinta?> DevizDeReferintaAsync(Guid proiectId, CancellationToken ct = default)
    {
        var referinte = await ReferinteAsync(db, proiectId, null, ct);
        if (referinte.Count == 0)
            return null;

        var multe = referinte.Count > 1;
        var articole = new List<ArticolDevizReferinta>();
        foreach (var (o, v) in referinte)
        {
            // „Obiect” = capitolul de pe primul nivel, „Categorie” = capitolul direct al articolului (arborele are oricâte niveluri).
            var capitole = v.Capitole.ToDictionary(c => c.Id);
            articole.AddRange(v.Articole().Select(a =>
            {
                var cale = v.Cale(capitole[a.CapitolId]);
                return new ArticolDevizReferinta(a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretMaterial,
                    multe ? $"{o.Denumire} / {cale[0].Denumire}" : cale[0].Denumire, cale[^1].Denumire);
            }));
        }

        var (_, prima) = referinte[0];
        return new DevizReferinta(prima.Id, multe ? string.Join(" · ", referinte.Select(r => $"{r.Obiectiv.Denumire}: {r.Versiune.Denumire}")) : prima.Denumire,
            prima.Tip, prima.Stare, articole);
    }
}
