using ErpConstructii.Api.Autentificare;
using ErpConstructii.Devize.Import.WinDoc;
using ErpConstructii.Devize.Infrastructure;
using ErpConstructii.Formulare.Infrastructure;
using ErpConstructii.Identitate.Infrastructure;
using ErpConstructii.Infrastructure;
using ErpConstructii.Proiecte.Infrastructure;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Authentication;

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
// Autentificare.
// Development: schema „Dev” citește utilizatorul și firma din headere (doar pentru teste locale).
// Producție: TODO faza 0 — server de identitate ASP.NET Core Identity + OpenIddict
// (vezi docs/decizii.md, D-07). Până atunci aplicația refuză să pornească în afara Development.
// ---------------------------------------------------------------------------
if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddAuthentication(AutentificareDev.Schema)
        .AddScheme<AuthenticationSchemeOptions, AutentificareDev>(AutentificareDev.Schema, null);
}
else
{
    throw new InvalidOperationException(
        "Autentificarea de producție (OpenIddict) nu este încă implementată. Vezi CLAUDE.md, faza 0.");
}

builder.Services.AddAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/sanatate", () => Results.Ok(new { stare = "ok" })).AllowAnonymous();

// Toate rutele de business sunt sub /api și cer autentificare; fiecare endpoint
// cere în plus o permisiune specifică (vezi ErpConstructii.Identitate.Permisiuni).
var api = app.MapGroup("/api").RequireAuthorization();
foreach (var modul in module)
{
    modul.MapeazaEndpointuri(api);
}

app.Run();

/// <summary>Expus pentru testele de integrare (WebApplicationFactory).</summary>
public partial class Program;
