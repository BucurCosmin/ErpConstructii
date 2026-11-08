using ErpConstructii.Facturi;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

/// <param name="PretUnitar">Ignorat fără dreptul „prețuri de cost” (prețul rămâne cel din NIR).</param>
public sealed record LinieCorectieNirCerere(int IndexFactura, decimal CantitateFactura, decimal CantitateReceptionata, decimal? PretUnitar, string? Observatii);

public sealed record CorecteazaNirCerere(IReadOnlyList<LinieCorectieNirCerere> Linii, string Motiv, DateOnly? Data);

/// <param name="FacturaCantitate">Ce scrie acum pe factură (după eventualele ei corecturi); null dacă linia nu mai e pe factură / factura e anulată.</param>
public sealed record LinieStareCorectieNirDto(
    int IndexFactura, string ArticolCod, string Denumire, string UnitateMasura, decimal CantitateFactura, decimal CantitateReceptionata,
    decimal? PretUnitar, decimal? Valoare, string? Observatii, bool Partiala,
    string? FacturaDenumire, decimal? FacturaCantitate, decimal? FacturaPret);

public sealed record CorectieNirDto(Guid Id, DateOnly Data, DateTimeOffset CorectatLa, string CorectatDe, string Motiv, string Rezumat, decimal? DiferentaValoare);

public sealed record StareCorectieNirDto(Guid NirId, string Numar, bool Stornat, bool VedePreturi, IReadOnlyList<LinieStareCorectieNirDto> Linii, IReadOnlyList<CorectieNirDto> Corectii);

/// <summary>
/// Corectura unui NIR recepționat greșit (D-35): cantitatea cf. factură, cantitatea recepționată, prețul și observațiile liniilor.
/// NIR-ul se modifică împreună cu o corectură append-only (<see cref="CorectieNir"/>), iar stocul depozitului primește diferențele
/// (<see cref="CalculStoc.Corectie"/>, mișcări <see cref="TipMiscare.CorectieNir"/>). Ecranul arată alături ce scrie acum pe
/// factură, ca recepția să poată fi adusă la factura corectată. Drept: <c>aprovizionare.editare</c>.
/// </summary>
internal static class EndpointuriCorectieNir
{
    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Editare);
        g.MapGet("/nir/{id:guid}/corectare", Stare).WithName("StareCorectieNir")
            .WithSummary("Liniile NIR-ului alături de factura actuală și istoricul corecturilor").Produces<StareCorectieNirDto>()
            .Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapPut("/nir/{id:guid}", Corecteaza).WithName("CorecteazaNir")
            .WithSummary("Corectează un NIR recepționat greșit; stocul primește diferențele")
            .Produces<NirDto>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(editare);
    }

    private static async Task<IResult> Stare(
        Guid id, StocuriDbContext db, IInterogareFacturiPrimite facturi, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma,
        CancellationToken ct)
    {
        var nir = await db.Nir.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nir is null)
            return Results.NotFound();
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);
        var factura = nir.FacturaId is { } facturaId ? await facturi.PentruReceptieAsync(facturaId, ct) : null;
        var liniiFactura = factura?.Linii.ToDictionary(l => l.Index) ?? [];
        var corectii = await db.CorectiiNir.AsNoTracking().Where(c => c.NirId == id).OrderByDescending(c => c.CreatLa).ToListAsync(ct);
        return Results.Ok(new StareCorectieNirDto(
            nir.Id, nir.NumarAfisat, await db.StornariNir.AnyAsync(s => s.NirId == id, ct), preturi,
            nir.Linii.Select(l =>
            {
                var f = liniiFactura.GetValueOrDefault(l.IndexFactura);
                return new LinieStareCorectieNirDto(l.Cheie, l.ArticolCod, l.Denumire, l.UnitateMasura, l.CantitateFactura, l.CantitateReceptionata,
                    preturi ? l.PretUnitar : null, preturi ? l.Valoare : null, l.Observatii, l.Partiala,
                    f?.Denumire, f?.Cantitate, preturi ? f?.PretUnitar : null);
            }).ToList(),
            corectii.Select(c => new CorectieNirDto(c.Id, c.Data, c.CreatLa, c.CorectatDeNume, c.Motiv, c.Rezumat, preturi ? c.DiferentaValoare : null)).ToList()));
    }

    private static async Task<IResult> Corecteaza(
        Guid id, CorecteazaNirCerere cerere, StocuriDbContext db, IServiciuPermisiuni permisiuni, IFirmaCurenta firma, IUtilizatorCurent utilizator,
        ICeas ceas, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        var data = cerere.Data ?? ceas.Azi;
        if (data > ceas.Azi)
            return Results.BadRequest(new { mesaj = "Data corecturii nu poate fi în viitor." });
        if (cerere.Linii is not { Count: > 0 })
            return Results.BadRequest(new { mesaj = "Nu ai trimis nicio linie de corectat." });
        var preturi = await ModulStocuri.VedePreturiAsync(permisiuni, utilizator, firma, ct);

        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulStocuri.BlocheazaStoculAsync(db, tranzactie, fid, ct);
        var nir = await db.Nir.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nir is null)
            return Results.NotFound();
        if (await db.StornariNir.AnyAsync(s => s.NirId == id, ct))
            return Results.Conflict(new { mesaj = $"{nir.NumarAfisat} e stornat: nu se mai corectează (fă o recepție nouă)." });
        if (await ModulStocuri.InventarDeschisAsync(db, ct, nir.GestiuneId) is { } blocat)
            return Results.Conflict(new { mesaj = blocat });

        var vechi = nir.Linii.ToDictionary(l => l.Cheie);
        var linii = cerere.Linii.Select(l => new LinieNirCorectata(
            l.IndexFactura, l.CantitateFactura, l.CantitateReceptionata,
            preturi && l.PretUnitar is { } p ? p : vechi.TryGetValue(l.IndexFactura, out var v) ? v.PretUnitar : 0m,
            l.Observatii)).ToList();
        var r = nir.Corecteaza(linii, data, cerere.Motiv ?? string.Empty, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        var corectie = r.Valoare;

        // Diferențele pe material (o factură poate avea același material pe mai multe linii) intră în soldul depozitului.
        foreach (var g in corectie.Linii.GroupBy(l => l.ArticolId))
        {
            var dq = g.Sum(l => l.DiferentaCantitate);
            var dv = g.Sum(l => l.DiferentaValoare);
            if (dq == 0 && dv == 0)
                continue;
            var sold = await ModulStocuri.SoldAsync(db, nir.GestiuneId, g.Key, ct);
            var curent = sold.Sold;
            var nou = CalculStoc.Corectie(curent, dq, dv, g.First().Denumire);
            if (nou.Esec)
                return Results.Conflict(new { mesaj = nou.Eroare.Mesaj });
            sold.Seteaza(nou.Valoare);
            // Mișcarea poartă NIR-ul (proveniența rămâne pe el); valoarea e schimbarea reală a soldului.
            db.Miscari.Add(new MiscareStoc(nir.GestiuneId, g.Key, data, TipMiscare.CorectieNir, dq, nou.Valoare.Valoare - curent.Valoare, nir.Id, nir.NumarAfisat));
        }

        db.CorectiiNir.Add(corectie);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(new { mesaj = $"{nir.NumarAfisat} a fost corectat.", rezumat = corectie.Rezumat, nir = ModulStocuri.Dto(nir, preturi) });
    }
}
