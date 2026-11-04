using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Devize.Infrastructure;

public sealed record ProblemaDto(string Severitate, string Locatie, string Mesaj);

public sealed record PrevizualizareImportDto(
    string Format,
    string Denumire,
    int Obiecte,
    int Articole,
    RecapitulatieDeviz? Recapitulatie,
    IReadOnlyList<ProblemaDto> Probleme,
    bool PoateFiConfirmat);

public sealed record VersiuneDto(Guid Id, string Tip, int Numar, string Denumire, string Stare, string Sursa);

public sealed record ArticolDto(
    Guid Id,
    string Simbol,
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    ValoriResurse PretUnitar,
    ValoriResurse Valoare);

public sealed record CategorieDto(Guid Id, string Cod, string Denumire, bool Subcontractata, decimal Total, IReadOnlyList<ArticolDto> Articole);

public sealed record ObiectDto(Guid Id, string Cod, string Denumire, decimal Total, IReadOnlyList<CategorieDto> Categorii);

public sealed record StructuraDevizDto(
    VersiuneDto Versiune,
    ParametriRecapitulatie Parametri,
    RecapitulatieDeviz Recapitulatie,
    IReadOnlyList<ObiectDto> Obiecte);

public sealed class ModulDevize : IModul
{
    public string Nume => DevizeDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AdaugaDbContextModul<DevizeDbContext>(configurare, Nume);
        servicii.AddScoped<IInterogareDevize, InterogareDevize>();
        // Importatorii concreți (WinDoc, Excel...) se înregistrează din proiectele lor,
        // ca IImportatorDeviz; registrul îi primește pe toți.
        servicii.AddScoped<RegistruImportatori>();
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/proiecte/{proiectId:guid}/devize").WithTags("Devize");
        var vizualizare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Creare);

        grup.MapGet("/", async (Guid proiectId, DevizeDbContext db, CancellationToken ct) =>
            await db.Versiuni.AsNoTracking()
                .Where(v => v.ProiectId == proiectId)
                .OrderBy(v => v.Tip).ThenBy(v => v.Numar)
                .Select(v => new VersiuneDto(v.Id, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString(), v.Sursa))
                .ToListAsync(ct))
            .CerePermisiune(vizualizare);

        // Structura completă obiect → categorie → articol, cu valorile calculate (pentru afișare).
        grup.MapGet("/{versiuneId:guid}", async (Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct) =>
        {
            var v = await IncarcaCompletAsync(db, proiectId, versiuneId, ct);
            return v is null ? Results.NotFound() : Results.Ok(Structura(v));
        }).CerePermisiune(vizualizare);

        grup.MapGet("/{versiuneId:guid}/recapitulatie", async (Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct) =>
        {
            var versiune = await IncarcaCompletAsync(db, proiectId, versiuneId, ct);
            return versiune is null ? Results.NotFound() : Results.Ok(versiune.Recapitulatie());
        }).CerePermisiune(vizualizare);

        // Pașii 1–3 + 5 din REQ-IMP-03: detectare, citire, validare, previzualizare. Nu salvează nimic.
        grup.MapPost("/import/previzualizare", async (Guid proiectId, IFormFile fisier, RegistruImportatori registru, CancellationToken ct) =>
        {
            var (sursa, rezultat, importator, motive) = await CitesteAsync(fisier, registru, ct);
            if (importator is null)
            {
                return Results.UnprocessableEntity(new { mesaj = "Format de deviz nerecunoscut.", motive });
            }

            return Results.Ok(Previzualizare(importator.FormatId, rezultat!));
        }).CerePermisiune(creare).DisableAntiforgery();

        // Pasul 6: confirmare → versiune nouă de deviz (ciornă).
        // TODO faza 0: arhivarea fișierului original (modulul Documente) și setarea FisierSursaId;
        // reimport cu diferențe față de versiunea existentă și păstrarea CheieStabila pe articole.
        grup.MapPost("/import", async (
            Guid proiectId,
            IFormFile fisier,
            TipVersiuneDeviz tip,
            RegistruImportatori registru,
            DevizeDbContext db,
            CancellationToken ct) =>
        {
            var (_, rezultat, importator, motive) = await CitesteAsync(fisier, registru, ct);
            if (importator is null)
            {
                return Results.UnprocessableEntity(new { mesaj = "Format de deviz nerecunoscut.", motive });
            }

            var previzualizare = Previzualizare(importator.FormatId, rezultat!);
            if (!previzualizare.PoateFiConfirmat)
            {
                return Results.UnprocessableEntity(previzualizare);
            }

            var deviz = rezultat!.Valoare!;
            if (deviz.Parametri is null)
            {
                // TODO: cotele implicite din setările firmei (modul Setări). Până atunci, obligatorii în fișier.
                return Results.UnprocessableEntity(new { mesaj = "Fișierul nu conține cotele recapitulației și firma nu are cote implicite." });
            }

            var numar = await db.Versiuni.CountAsync(v => v.ProiectId == proiectId && v.Tip == tip, ct) + 1;
            var versiune = ConvertorDevizCanonic.CaVersiune(deviz, proiectId, tip, numar, importator.FormatId, null, deviz.Parametri);
            db.Versiuni.Add(versiune);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/proiecte/{proiectId}/devize/{versiune.Id}", new { versiune.Id, numar });
        }).CerePermisiune(creare).DisableAntiforgery();
    }

    private static async Task<(SursaImport Sursa, RezultatImport<DevizCanonic>? Rezultat, IImportatorDeviz? Importator, IReadOnlyList<string> Motive)>
        CitesteAsync(IFormFile fisier, RegistruImportatori registru, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await fisier.CopyToAsync(ms, ct);
        var sursa = new SursaImport(fisier.FileName, ms.ToArray());

        var (importator, motive) = registru.Alege(sursa);
        if (importator is null)
        {
            return (sursa, null, null, motive);
        }

        return (sursa, await importator.CitesteAsync(sursa, ct), importator, motive);
    }

    private static PrevizualizareImportDto Previzualizare(string format, RezultatImport<DevizCanonic> rezultat)
    {
        var probleme = rezultat.Probleme.ToList();
        RecapitulatieDeviz? recapitulatie = null;
        var deviz = rezultat.Valoare;

        if (deviz is not null)
        {
            probleme.AddRange(ValidatorDevizCanonic.Valideaza(deviz));
            if (deviz.Parametri is not null)
            {
                recapitulatie = CalculatorDeviz.Calculeaza(
                    deviz.Obiecte.SelectMany(o => o.Categorii).SelectMany(c => c.Articole).Select(a => new LinieDeviz(a.Cantitate, a.PretUnitar)),
                    deviz.Parametri);
            }
        }

        return new PrevizualizareImportDto(
            format,
            deviz?.Denumire ?? string.Empty,
            deviz?.Obiecte.Count ?? 0,
            deviz?.Obiecte.Sum(o => o.Categorii.Sum(c => c.Articole.Count)) ?? 0,
            recapitulatie,
            probleme.Select(p => new ProblemaDto(p.Severitate.ToString(), p.Locatie, p.Mesaj)).ToList(),
            deviz is not null && probleme.All(p => p.Severitate != Severitate.Eroare));
    }

    private static StructuraDevizDto Structura(VersiuneDeviz v)
    {
        var parametri = v.Parametri();
        var obiecte = v.Obiecte.OrderBy(o => o.Ordine).Select(o =>
        {
            var categorii = o.Categorii.OrderBy(c => c.Ordine).Select(c =>
            {
                var articole = c.Articole.OrderBy(a => a.Ordine).Select(a => new ArticolDto(
                    a.Id, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretUnitar,
                    CalculatorDeviz.ValoareLinie(a.CaLinie(), parametri.RotunjireLinie))).ToList();
                return new CategorieDto(c.Id, c.Cod, c.Denumire, c.Subcontractata, articole.Sum(a => a.Valoare.Total), articole);
            }).ToList();
            return new ObiectDto(o.Id, o.Cod, o.Denumire, categorii.Sum(c => c.Total), categorii);
        }).ToList();

        return new StructuraDevizDto(
            new VersiuneDto(v.Id, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString(), v.Sursa),
            parametri,
            v.Recapitulatie(),
            obiecte);
    }

    private static Task<VersiuneDeviz?> IncarcaCompletAsync(DevizeDbContext db, Guid proiectId, Guid versiuneId, CancellationToken ct) =>
        db.Versiuni.AsNoTracking()
            .Include(v => v.Obiecte).ThenInclude(o => o.Categorii).ThenInclude(c => c.Articole)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == versiuneId && v.ProiectId == proiectId, ct);
}
