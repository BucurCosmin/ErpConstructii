using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Parteneri;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Facturi.Infrastructure;

/// <summary>Datele corectate ale facturii (toate câmpurile, nu doar cele schimbate) și motivul corecturii.</summary>
/// <param name="Linii">TVA-ul liniei se recalculează din bază și cotă.</param>
public sealed record CorecteazaFacturaCerere(
    string Numar,
    DateOnly DataEmitere,
    DateOnly? DataScadenta,
    string Moneda,
    string FurnizorDenumire,
    string? FurnizorCui,
    Guid ProiectId,
    IReadOnlyList<LinieFacturaCerere> Linii,
    decimal TotalFaraTva,
    decimal TotalTva,
    decimal TotalDePlata,
    string? Referinta,
    string? Observatii,
    string Motiv);

public sealed record CorectieFacturaDto(Guid Id, DateTimeOffset CorectatLa, string CorectatDe, string Motiv, string Rezumat);

/// <param name="LiniiReceptionate">Pozițiile liniilor intrate în stoc (NIR activ): se pot corecta, dar nu se pot șterge (NIR-ul rămâne cu valorile lui).</param>
public sealed record StareCorectieFacturaDto(IReadOnlyList<int> LiniiReceptionate, IReadOnlyList<CorectieFacturaDto> Corectii);

/// <summary>
/// Corectura facturilor primite înregistrate cu date greșite (citire OCR, tastare): factura se modifică împreună cu o
/// corectură append-only (<see cref="CorectieFacturaPrimita"/>: înainte / după, motiv, autor). Orice câmp se poate corecta;
/// la liniile intrate în stoc răspunsul spune ce rămâne de verificat în NIR. Drept: <c>financiar.editare</c>.
/// </summary>
internal static class EndpointuriCorectieFacturi
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Financiar, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Financiar, Actiune.Editare);
        g.MapGet("/primite/{id:guid}/corectare", Stare).WithName("StareCorectieFactura")
            .WithSummary("Istoricul corecturilor facturii și liniile ei intrate în stoc").Produces<StareCorectieFacturaDto>().CerePermisiune(vizualizare);
        g.MapPut("/primite/{id:guid}", Corecteaza).WithName("CorecteazaFactura")
            .WithSummary("Corectează datele unei facturi primite (cu motiv; istoric păstrat)")
            .Produces<FacturaPrimitaDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).CerePermisiune(editare);
    }

    private static async Task<IReadOnlySet<int>> ReceptionateAsync(Guid id, IEnumerable<IInterogareReceptiiFactura> receptii, CancellationToken ct) =>
        receptii.FirstOrDefault() is { } r ? await r.LiniiReceptionateAsync(id, ct) : new HashSet<int>();

    private static async Task<IResult> Stare(Guid id, FacturiDbContext db, IEnumerable<IInterogareReceptiiFactura> receptii, CancellationToken ct)
    {
        if (!await db.FacturiPrimite.AnyAsync(f => f.Id == id, ct))
            return Results.NotFound();
        var corectii = await db.Corectii.AsNoTracking().Where(c => c.FacturaId == id).OrderByDescending(c => c.CorectatLa).ToListAsync(ct);
        return Results.Ok(new StareCorectieFacturaDto(
            (await ReceptionateAsync(id, receptii, ct)).Order().ToList(),
            corectii.Select(c => new CorectieFacturaDto(c.Id, c.CorectatLa, c.CorectatDeNume, c.Motiv, c.Rezumat)).ToList()));
    }

    private static async Task<IResult> Corecteaza(
        Guid id, CorecteazaFacturaCerere cerere, FacturiDbContext db, IEnumerable<IInterogareReceptiiFactura> receptii, IInterogareProiecte proiecte,
        IServiciuParteneri parteneri, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume) || firma.FirmaId is not { } fid)
            return Results.Unauthorized();
        var f = await db.FacturiPrimite.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
            return Results.NotFound(new { mesaj = "Factura nu există." });
        if (await db.Anulari.AnyAsync(a => a.FacturaId == id, ct))
            return Results.Conflict(new { mesaj = "Factura e anulată: nu se mai corectează." });

        // Dreptul pe proiectul facturii și pe cel nou (dacă se mută).
        var evaluator = await permisiuni.PentruUtilizatorAsync(uid, ct);
        var accesibile = evaluator.ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Financiar, Actiune.Editare), fid);
        if (accesibile is not null && (!accesibile.Contains(f.ProiectId) || !accesibile.Contains(cerere.ProiectId)))
            return Results.Forbid();
        var proiect = await proiecte.CautaPentruDocumentAsync(cerere.ProiectId, ct);
        if (proiect is null)
            return Results.BadRequest(new { mesaj = "Proiectul ales nu există." });
        if (proiect.Arhivat && proiect.Id != f.ProiectId)
            return Results.Conflict(new { mesaj = proiect.MesajArhivat });

        // Furnizorul: dacă s-a schimbat denumirea sau CUI-ul, se caută din nou în nomenclator.
        var vechi = f.Date();
        var furnizorId = vechi.FurnizorId;
        var cui = string.IsNullOrWhiteSpace(cerere.FurnizorCui) ? null : ValidareFiscala.NormalizeazaCui(cerere.FurnizorCui);
        if (cui != vechi.FurnizorCui || (cerere.FurnizorDenumire ?? string.Empty).Trim() != vechi.FurnizorDenumire)
            furnizorId = (await parteneri.CautaFurnizorAsync(cui, cerere.FurnizorDenumire ?? string.Empty, ct))?.Id;

        var date = new DateFacturaPrimita(
            cerere.Numar ?? string.Empty, cerere.DataEmitere, cerere.DataScadenta, cerere.Moneda ?? "RON", furnizorId,
            cerere.FurnizorDenumire ?? string.Empty, cerere.FurnizorCui, proiect.Id, proiect.Cod,
            (cerere.Linii ?? []).Select(l => new LinieFactura(l.Denumire ?? string.Empty, l.UnitateMasura ?? string.Empty, l.Cantitate, l.PretUnitar,
                l.ValoareFaraTva, l.CotaTva, VerificareTotaluriFactura.TvaLinie(l.ValoareFaraTva, l.CotaTva), l.IntraInStoc)).ToList(),
            cerere.TotalFaraTva, cerere.TotalTva, cerere.TotalDePlata, cerere.Referinta, cerere.Observatii);

        var receptionate = await ReceptionateAsync(id, receptii, ct);
        var r = f.Corecteaza(date, cerere.Motiv ?? string.Empty, receptionate, uid, utilizator.Nume, ceas.Acum);
        if (!r.Succes)
            return r.Eroare.Cod == "conflict" ? Results.Conflict(new { mesaj = r.Eroare.Mesaj }) : Results.BadRequest(new { mesaj = r.Eroare.Mesaj });

        db.Corectii.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new
        {
            mesaj = "Factura a fost corectată.",
            rezumat = r.Valoare.Rezumat,
            avertismente = FacturaPrimita.AvertismenteStoc(vechi, f.Date(), receptionate),
        });
    }
}
