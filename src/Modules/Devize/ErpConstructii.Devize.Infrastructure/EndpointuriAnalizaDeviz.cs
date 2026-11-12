using ErpConstructii.Devize.Analize;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

/// <summary>Propunere informativă de modificare a cantității unui articol, identificat prin cheia sa stabilă.</summary>
public sealed record CorecturaAnalizaDto(Guid Articol, decimal CantitateVeche, decimal CantitateNoua);

/// <summary>O suspiciune experimentală, cu explicația, dovezile și eventualul impact estimat în lei.</summary>
public sealed record ConstatareAnalizaDto(
    string Cod, SeveritateAnaliza Severitate, decimal Incredere, IReadOnlyList<Guid> Articole,
    string Explicatie, IReadOnlyList<DovadaAnaliza> Dovezi, CorecturaAnalizaDto? Corectura,
    decimal? ImpactCostDirect, decimal? ImpactOferta, string Amprenta);

/// <summary>Rezultatul unui analizor, inclusiv verificările care nu sunt încă disponibile.</summary>
public sealed record RezultatAnalizorDto(
    string Cod, string Versiune, IReadOnlyList<ConstatareAnalizaDto> Constatari, IReadOnlyList<string> Limitari);

/// <summary>Articolul afișat în raport, identificat prin cheia sa stabilă.</summary>
public sealed record ArticolAnalizaDto(Guid CheieStabila, string Simbol, string? CodWbs, string UnitateMasura);

/// <summary>Identifică versiunea analizată.</summary>
public sealed record VersiuneAnalizaDto(Guid Id, Guid ProiectId, string Denumire, string Tip, int Numar, string Stare);

/// <summary>Previzualizare nesalvată, cu amprenta pentru verificarea raportului PDF.</summary>
public sealed record PrevizualizareAnalizaDevizDto(
    VersiuneAnalizaDto Versiune, IReadOnlyList<ArticolAnalizaDto> Articole,
    IReadOnlyList<RezultatAnalizorDto> Analizori, string Amprenta);

/// <summary>Amprenta previzualizării pentru care se cere PDF-ul.</summary>
public sealed record RaportAnalizaCerere(string? Amprenta);

internal static class EndpointuriAnalizaDeviz
{
    public static void Mapeaza(RouteGroupBuilder grup)
    {
        grup.MapPost("/{versiuneId:guid}/analize/previzualizare", Analizeaza)
            .WithName("PrevizualizareAnalizaDeviz")
            .WithSummary("Rulează experimental verificările disponibile, fără a salva sau modifica devizul")
            .Produces<PrevizualizareAnalizaDevizDto>()
            .Produces(StatusCodes.Status404NotFound)
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare))
            .CerePermisiune(Permisiuni.PreturiCost);
        grup.MapPost("/{versiuneId:guid}/analize/raport/pdf", Pdf)
            .WithName("PdfAnalizaDeviz")
            .WithSummary("Descarcă raportul experimental al previzualizării, dacă rezultatele nu s-au schimbat")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare))
            .CerePermisiune(Permisiuni.PreturiCost);
    }

    private static async Task<Results<Ok<PrevizualizareAnalizaDevizDto>, NotFound>> Analizeaza(
        Guid proiectId, Guid versiuneId, DevizeDbContext db, RegistruAnalizori registru, CancellationToken ct)
    {
        var versiune = await Incarca(proiectId, versiuneId, db, ct);
        return versiune is null ? TypedResults.NotFound() : TypedResults.Ok(RaportAnalizaDeviz.Previzualizeaza(versiune, registru));
    }

    private static async Task<IResult> Pdf(
        Guid proiectId, Guid versiuneId, RaportAnalizaCerere cerere, DevizeDbContext db, RegistruAnalizori registru,
        IServiciuDocumente documente, IInterogareFirma firme, ICeas ceas, CancellationToken ct)
    {
        if (cerere.Amprenta is not { Length: 64 } amprenta || !amprenta.All(Uri.IsHexDigit))
            return Results.BadRequest(new { mesaj = "Amprenta previzualizării este invalidă. Rulează din nou verificarea." });
        var versiune = await Incarca(proiectId, versiuneId, db, ct);
        if (versiune is null)
            return Results.NotFound();
        var rezultat = RaportAnalizaDeviz.Previzualizeaza(versiune, registru);
        if (!string.Equals(rezultat.Amprenta, amprenta, StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { mesaj = "Devizul sau rezultatele verificării s-au schimbat. Rulează din nou verificarea înainte de descărcarea PDF-ului." });

        var f = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa.Formatata());
        var (aspect, date) = RaportAnalizaDeviz.PentruPdf(rezultat, ceas.Acum);
        var doc = await documente.PdfAsync("raport-analiza-deviz", antet, aspect, date, $"Verificare_deviz_{versiune.Numar}.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private static Task<VersiuneDeviz?> Incarca(Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct) =>
        db.Versiuni.AsNoTracking()
            .Include(v => v.Capitole).ThenInclude(c => c.Articole).ThenInclude(a => a.Consumuri)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == versiuneId && v.ProiectId == proiectId, ct);
}
