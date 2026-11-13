using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Analize;
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

/// <param name="Curs">Lei pentru o unitate din <paramref name="Moneda"/> (prețul de ofertă); null pentru RON.</param>
public sealed record VersiuneDto(
    Guid Id, string Tip, int Numar, string Denumire, string Stare, string Sursa, string Moneda = "RON", decimal? Curs = null,
    string PoliticaRotunjire = "Standard", string? Obiectiv = null, string? Obiect = null, string? StadiuFizic = null,
    Guid? VersiuneAnterioaraId = null);

/// <param name="PretUnitar">Costul direct unitar, în lei, pe resurse.</param>
/// <param name="Valoare">Costul direct al articolului (cantitate × preț unitar, cu rotunjirea versiunii).</param>
/// <param name="PretOfertaUnitar">Prețul de ofertă unitar, în moneda versiunii (null = fără preț de ofertă).</param>
public sealed record ArticolDto(
    Guid Id,
    Guid CheieStabila,
    string Simbol,
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    ValoriResurse PretUnitar,
    ValoriResurse Valoare,
    string TipArticol = "Normat",
    string? CodWbs = null,
    ValoriResurse? PretOfertaUnitar = null,
    ValoriResurse? ValoareOferta = null,
    int Resurse = 0);

/// <summary>Un capitol al arborelui, cu subcapitolele și articolele lui; <paramref name="Total"/> = costul direct al tuturor.</summary>
public sealed record CapitolDto(
    Guid Id, string Cod, string? CodWbs, string Denumire, string Tip, bool Subcontractat, decimal Total,
    IReadOnlyList<CapitolDto> Subcapitole, IReadOnlyList<ArticolDto> Articole);

public sealed record StructuraDevizDto(
    VersiuneDto Versiune,
    ParametriRecapitulatie Parametri,
    RecapitulatieDeviz Recapitulatie,
    IReadOnlyList<CapitolDto> Capitole);

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
        servicii.AddScoped<IAnalizorDeviz, AnalizorAmprentaNumerelor>();
        servicii.AddScoped<IAnalizorDeviz, AnalizorPreturiSpeciale>();
        servicii.AddScoped<RegistruAnalizori>();
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        // Nomenclatorul de resurse al firmei (REQ-IMP-04): lista, maparea, propunerile de articole de stoc.
        EndpointuriResurse.Mapeaza(rute);
        // Catalogul de prețuri din devizele firmei și compararea prețurilor între devize (D-54).
        EndpointuriCatalogPreturi.Mapeaza(rute);
        // Bugetul proiectului față de costurile reale (D-55; costurile vin prin ISursaCosturi).
        EndpointuriBuget.Mapeaza(rute);

        var grup = rute.MapGroup("/proiecte/{proiectId:guid}/devize").WithTags("Devize");
        var vizualizare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Creare);

        grup.MapGet("/", async (Guid proiectId, DevizeDbContext db, CancellationToken ct) =>
            await db.Versiuni.AsNoTracking()
                .Where(v => v.ProiectId == proiectId)
                .OrderBy(v => v.Tip).ThenBy(v => v.Numar)
                .Select(v => new VersiuneDto(v.Id, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString(), v.Sursa, v.Moneda, v.Curs,
                    v.PoliticaRotunjire.ToString(), v.Obiectiv, v.Obiect, v.StadiuFizic, v.VersiuneAnterioaraId))
                .ToListAsync(ct))
            .CerePermisiune(vizualizare);

        // Structura completă (arborele de capitole → articole), cu valorile calculate (pentru afișare).
        grup.MapGet("/{versiuneId:guid}", async (Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct) =>
        {
            var v = await IncarcaAsync(db, versiuneId, ct);
            return v is null || v.ProiectId != proiectId ? Results.NotFound() : Results.Ok(Structura(v));
        }).CerePermisiune(vizualizare);

        grup.MapGet("/{versiuneId:guid}/recapitulatie", async (Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct) =>
        {
            var versiune = await IncarcaAsync(db, versiuneId, ct);
            return versiune is null || versiune.ProiectId != proiectId ? Results.NotFound() : Results.Ok(versiune.Recapitulatie());
        }).CerePermisiune(vizualizare);

        // Importul (ISDP: deviz cu resurse + F3 opțional): previzualizare, confirmare, fișierele originale.
        EndpointuriImportDeviz.Mapeaza(grup);
        // Lucrul pe versiune: redenumire, ștergere, editarea articolelor, versiune nouă, aprobare, PDF, Excel.
        EndpointuriEditareDeviz.Mapeaza(grup);
        EndpointuriAnalizaDeviz.Mapeaza(grup);
        // Componentele (resursele) unui articol, ca în extrasul de resurse.
        EndpointuriCatalogPreturi.MapeazaPeVersiune(grup);
        // „Actualizează prețurile”: prețurile din deviz față de cele de achiziție, aplicate în ciorna de lucru (D-55).
        EndpointuriActualizarePreturi.Mapeaza(grup);
    }

    private static StructuraDevizDto Structura(VersiuneDeviz v)
    {
        var parametri = v.Parametri();

        CapitolDto Capitol(CapitolDeviz c)
        {
            var subcapitole = v.Copii(c.Id).Select(Capitol).ToList();
            var articole = c.Articole.OrderBy(a => a.Ordine).Select(a => new ArticolDto(
                a.Id, a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretUnitar,
                CalculatorDeviz.ValoareLinie(a.CaLinie(), parametri.RotunjireLinie), a.TipArticol.ToString(), a.CodWbsSursa,
                a.ArePretOferta ? a.PretOfertaUnitar : null, a.ArePretOferta ? a.ValoareOferta : null, a.Consumuri.Count)).ToList();
            return new CapitolDto(c.Id, c.Cod, c.CodWbs, c.Denumire, c.Tip.ToString(), c.Subcontractat,
                articole.Sum(a => a.Valoare.Total) + subcapitole.Sum(x => x.Total), subcapitole, articole);
        }

        return new StructuraDevizDto(
            new VersiuneDto(v.Id, v.Tip.ToString(), v.Numar, v.Denumire, v.Stare.ToString(), v.Sursa, v.Moneda, v.Curs,
                v.PoliticaRotunjire.ToString(), v.Obiectiv, v.Obiect, v.StadiuFizic, v.VersiuneAnterioaraId),
            parametri,
            v.Recapitulatie(),
            v.Radacini.Select(Capitol).ToList());
    }

    /// <summary>Versiunea cu tot arborele: capitole, articole și consumurile lor.</summary>
    internal static Task<VersiuneDeviz?> IncarcaAsync(DevizeDbContext db, Guid versiuneId, CancellationToken ct) =>
        db.Versiuni.AsNoTracking()
            .Include(v => v.Capitole).ThenInclude(c => c.Articole).ThenInclude(a => a.Consumuri)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == versiuneId, ct);
}
