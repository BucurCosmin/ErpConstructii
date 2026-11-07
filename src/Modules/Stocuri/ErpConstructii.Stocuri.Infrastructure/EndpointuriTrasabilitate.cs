using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <summary>O cantitate din stocul curent, cu documentul prin care a intrat: NIR-ul și factura (sau plusul de inventar).</summary>
/// <param name="Origine">„NIR” sau „Inventar” (plus la inventar); „Transfer” doar dacă originea nu se mai poate reconstitui.</param>
/// <param name="PretIntrare">Prețul din NIR / inventar (nu CMP-ul); null fără dreptul „prețuri de cost”.</param>
public sealed record LotStocDto(
    Guid GestiuneId, string Gestiune, string GestiuneTip, Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate,
    string Origine, Guid DocumentId, string DocumentNumar, DateOnly DataIntrare,
    Guid? FacturaId, string? FacturaNumar, DateOnly? FacturaData, string? FurnizorDenumire, string? FacturaProiectCod,
    decimal? PretIntrare, decimal? Valoare);

/// <summary>
/// Trasabilitatea stocului: pe ce NIR (deci pe ce factură, de la ce furnizor) a intrat fiecare cantitate aflată acum
/// într-o gestiune (depozit sau șantier). Calculul: <see cref="TrasabilitateStoc"/> (ieșiri atribuite FIFO).
/// </summary>
internal static class EndpointuriTrasabilitate
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        g.MapGet("/trasabilitate", Trasabilitate).WithName("TrasabilitateStoc")
            .WithSummary("Proveniența stocului: pe ce NIR / factură a intrat fiecare cantitate din gestiuni")
            .Produces<IReadOnlyList<LotStocDto>>().CerePermisiune(vizualizare);
    }

    /// <param name="gestiuneId">O singură gestiune; altfel toate (depozitele și șantierele proiectelor accesibile).</param>
    /// <param name="tip">„Depozit” sau „Santier”.</param>
    /// <param name="material">Denumirea sau codul materialului (fără diacritice / majuscule).</param>
    /// <param name="cauta">Numărul facturii / NIR-ului sau furnizorul.</param>
    private static async Task<IResult> Trasabilitate(
        Guid? gestiuneId, string? tip, string? material, string? cauta, StocuriDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator,
        IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return Results.Unauthorized();

        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        var preturi = evaluator.Are(Permisiuni.PreturiCost, new Domeniu(fid));
        var accesibile = evaluator.ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);

        var gestiuni = await db.Gestiuni.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var articole = await db.Articole.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);

        // Originea trece prin transferuri, deci se calculează pe toate gestiunile; filtrul pe material reduce doar articolele.
        var miscari = db.Miscari.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(material))
        {
            var termen = Text.FaraDiacritice(material.Trim());
            var alese = articole.Values.Where(a => Text.FaraDiacritice(a.Denumire).Contains(termen, StringComparison.Ordinal)
                || a.Cod.Contains(termen, StringComparison.OrdinalIgnoreCase)).Select(a => a.Id).ToList();
            miscari = miscari.Where(m => alese.Contains(m.ArticolId));
        }

        var lista = await miscari.OrderBy(m => m.CreatLa).ThenBy(m => m.Id).ToListAsync(ct);
        var stornari = await db.StornariNir.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NirId, ct);
        var loturi = TrasabilitateStoc.Calculeaza(
            lista.Select(m => new MiscareTrasabilitate(m.GestiuneId, m.ArticolId, m.Tip, m.Cantitate, m.Valoare, m.DocumentId, m.DocumentNumar, m.Data)),
            stornari);

        var idNir = loturi.Where(l => l.TipOrigine == TipMiscare.IntrareNir).Select(l => l.DocumentOrigineId).Distinct().ToList();
        var nir = await db.Nir.AsNoTracking().Where(n => idNir.Contains(n.Id)).ToDictionaryAsync(n => n.Id, ct);

        var rezultat = loturi
            .Where(l => gestiuni.ContainsKey(l.GestiuneId) && articole.ContainsKey(l.ArticolId))
            .Select(l => (Lot: l, Gestiune: gestiuni[l.GestiuneId], Articol: articole[l.ArticolId], Nir: nir.GetValueOrDefault(l.DocumentOrigineId)))
            .Where(x => gestiuneId is null || x.Gestiune.Id == gestiuneId)
            .Where(x => !Enum.TryParse<TipGestiune>(tip, true, out var t) || x.Gestiune.Tip == t)
            .Where(x => x.Gestiune.ProiectId is null || accesibile is null || accesibile.Contains(x.Gestiune.ProiectId.Value))
            .Where(x => string.IsNullOrWhiteSpace(cauta) || Potriveste(cauta, x.Lot.DocumentOrigineNumar, x.Nir?.FacturaNumar, x.Nir?.FurnizorDenumire))
            .OrderBy(x => x.Gestiune.Tip).ThenBy(x => x.Gestiune.Cod).ThenBy(x => x.Articol.Denumire).ThenBy(x => x.Lot.DataIntrare)
            .Select(x => new LotStocDto(
                x.Gestiune.Id, x.Gestiune.Denumire, x.Gestiune.Tip.ToString(), x.Articol.Id, x.Articol.Cod, x.Articol.Denumire, x.Articol.UnitateMasura,
                x.Lot.Cantitate,
                x.Lot.TipOrigine switch { TipMiscare.IntrareNir => "NIR", TipMiscare.PlusInventar => "Inventar", _ => "Transfer" },
                x.Lot.DocumentOrigineId, x.Lot.DocumentOrigineNumar, x.Lot.DataIntrare,
                x.Nir?.FacturaId, x.Nir?.FacturaNumar, x.Nir?.FacturaData, x.Nir?.FurnizorDenumire, x.Nir?.ProiectCod,
                preturi ? x.Lot.PretUnitar : null, preturi ? Rotunjire.Lei.Aplica(x.Lot.Cantitate * x.Lot.PretUnitar) : null))
            .ToList();
        return Results.Ok(rezultat);
    }

    private static bool Potriveste(string cauta, params string?[] campuri)
    {
        var t = Text.FaraDiacritice(cauta.Trim());
        return campuri.Any(c => c is not null && Text.FaraDiacritice(c).Contains(t, StringComparison.Ordinal));
    }
}

internal static class Text
{
    public static string FaraDiacritice(string text)
    {
        var fd = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(fd.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}
