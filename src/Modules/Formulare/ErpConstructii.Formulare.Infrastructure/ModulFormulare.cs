using ErpConstructii.Formulare.Infrastructure.Word;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Formulare.Infrastructure;

public sealed class ModulFormulare : IModul
{
    public string Nume => "formulare";

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AddSingleton<IGeneratorFormular, GeneratorWord>();
        servicii.AddSingleton<ICititorFormular, CititorWord>();
        servicii.AddSingleton<IValidatorSablon, ValidatorSablonWord>();

        var urlGotenberg = configurare["Formulare:GotenbergUrl"] ?? "http://localhost:3000";
        servicii.AddHttpClient<IConvertorPdf, ConvertorPdfGotenberg>(c =>
        {
            c.BaseAddress = new Uri(urlGotenberg.TrimEnd('/') + "/");
            c.Timeout = TimeSpan.FromSeconds(60);
        });

        // TODO faza 1: DbContext „formulare” cu șabloanele firmei (versionate), seriile de numerotare,
        // rezervările de numere și arhiva documentelor generate (SHA-256, imuabile).
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/formulare").WithTags("Formulare");

        grup.MapGet("/tipuri", () => CatalogFormulare.Toate.Select(t => new { t.Cod, t.Denumire, t.ModulErp, t.SePoateImporta }))
            .CerePermisiune(Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Vizualizare));

        // Pașii 2 + 4 din importul formularelor făcute manual: citire + validare, fără salvare.
        // Potrivirea (furnizor după CUI, materiale după cod) și ciorna intră în modulul fiecărui tip.
        grup.MapPost("/import/previzualizare", async (IFormFile fisier, ICititorFormular cititor, CancellationToken ct) =>
        {
            using var ms = new MemoryStream();
            await fisier.CopyToAsync(ms, ct);
            var citit = cititor.Citeste(ms.ToArray());

            var probleme = citit.Probleme.ToList();
            var tip = citit.Metadate is null ? null : CatalogFormulare.DupaCod(citit.Metadate.CodTip);
            if (citit.Metadate is not null && tip is null)
            {
                probleme.Add(new ProblemaImport(Severitate.Eroare, "fișier", $"Tip de formular necunoscut: {citit.Metadate.CodTip}."));
            }

            if (tip is not null)
            {
                probleme.AddRange(ValidatorDateFormular.Valideaza(tip, citit.Date));
            }

            return Results.Ok(new
            {
                tip = tip?.Cod,
                citit.Metadate,
                citit.Date,
                probleme,
                poateFiImportat = tip is not null && probleme.All(p => p.Severitate != Severitate.Eroare),
            });
        }).CerePermisiune(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare)).DisableAntiforgery();
    }
}
