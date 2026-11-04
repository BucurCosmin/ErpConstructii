using System.Text.Json.Serialization;
using ErpConstructii.Api.Autentificare;
using ErpConstructii.Api.Dezvoltare;
using ErpConstructii.Aprovizionare.Infrastructure;
using ErpConstructii.Devize.Import.WinDoc;
using ErpConstructii.Devize.Infrastructure;
using ErpConstructii.Facturi.Infrastructure;
using ErpConstructii.Stocuri.Infrastructure;
using ErpConstructii.Formulare.Infrastructure;
using ErpConstructii.Identitate.Infrastructure;
using ErpConstructii.Identitate.Infrastructure.Autentificare;
using ErpConstructii.Infrastructure;
using ErpConstructii.Parteneri.Infrastructure;
using ErpConstructii.Proiecte.Infrastructure;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Modulele aplicației. Un modul nou se adaugă AICI și ca referință în .csproj.
// Ordinea contează doar pentru maparea rutelor (nu pentru dependențe).
// ---------------------------------------------------------------------------
IModul[] module =
[
    new ModulIdentitate(),
    new ModulProiecte(),
    new ModulDevize(),
    new ModulFormulare(),
    new ModulParteneri(),
    new ModulAprovizionare(),
    new ModulFacturi(),
    new ModulStocuri(),
];

// Context de cerere: firma și utilizatorul curent, citite din token.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IFirmaCurenta, FirmaCurentaHttp>();
builder.Services.AddScoped<IUtilizatorCurent, UtilizatorCurentHttp>();
builder.Services.AddSingleton<ICeas, CeasSistem>();

builder.Services.AdaugaInfrastructuraComuna();
foreach (var modul in module)
{
    modul.AdaugaServicii(builder.Services, builder.Configuration);
}

// Importatori de devize (module de import, REQ-IMP-01). Un format nou = un pachet nou + o linie aici.
builder.Services.AdaugaImportatorWinDoc();

// ---------------------------------------------------------------------------
// Autentificare (D-07, REQ-UTL-02).
// Serverul OAuth2/OIDC e în aplicație (ASP.NET Core Identity + OpenIddict, modulul Identitate).
// API-ul acceptă access token-uri emise de el. În Development, pentru teste cu curl, se acceptă
// și headerele X-Dev-Utilizator / X-Dev-Firma (schema „Dev”, înregistrată DOAR în Development).
// ---------------------------------------------------------------------------
const string schemaErp = "Erp";
var esteDezvoltare = builder.Environment.IsDevelopment();

var autentificare = builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = schemaErp;
    o.DefaultChallengeScheme = schemaErp;
    o.DefaultForbidScheme = schemaErp;
});
autentificare.AddPolicyScheme(schemaErp, "ERP", o => o.ForwardDefaultSelector = ctx =>
    esteDezvoltare && ctx.Request.Headers.ContainsKey("X-Dev-Utilizator")
        ? AutentificareDev.Schema
        : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
if (esteDezvoltare)
{
    autentificare.AddScheme<AuthenticationSchemeOptions, AutentificareDev>(AutentificareDev.Schema, null);
}

builder.Services.AdaugaAutentificare(builder.Configuration, esteDezvoltare);

builder.Services.AddAuthorization();
// Enum-urile circulă ca text în JSON („Intern”, „Oferta”), la fel în cereri și în răspunsuri.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Date demo pentru aplicația web. Dacă baza nu e pregătită (migrări nerulate), aplicația pornește oricum.
    try
    {
        await DateDemo.SeteazaAsync(app.Services);
    }
    catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
    {
        app.Logger.LogWarning(ex, "Datele demo nu au putut fi create. Ai rulat migrările? (vezi README)");
    }
}

// Clienții OAuth (aplicația web etc.) din configurare. În Development, o bază nepregătită nu oprește pornirea.
try
{
    await ClientiOAuth.SincronizeazaAsync(app.Services, app.Configuration);
}
catch (Exception ex) when (esteDezvoltare && ex is Npgsql.NpgsqlException or InvalidOperationException)
{
    app.Logger.LogWarning(ex, "Clienții OAuth nu au putut fi înregistrați. Ai rulat migrările? (vezi README)");
}

app.MapeazaEndpointuriAutentificare();
app.MapGet("/sanatate", () => Results.Ok(new { stare = "ok" })).AllowAnonymous();

// Toate rutele de business sunt sub /api și cer autentificare; fiecare endpoint
// cere în plus o permisiune specifică (vezi ErpConstructii.Identitate.Permisiuni).
var api = app.MapGroup("/api").RequireAuthorization();
foreach (var modul in module)
{
    modul.MapeazaEndpointuri(api);
}

await app.RunAsync();

/// <summary>Expus pentru testele de integrare (WebApplicationFactory).</summary>
public partial class Program;
