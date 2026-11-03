using System.Security.Claims;
using System.Text.Encodings.Web;
using ErpConstructii.Identitate;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ErpConstructii.Api.Autentificare;

/// <summary>
/// Autentificare DOAR pentru dezvoltare locală: identitatea vine din headere.
///   X-Dev-Utilizator: GUID-ul utilizatorului
///   X-Dev-Firma:      GUID-ul firmei active
///   X-Dev-Nume:       numele afișat pe documente (opțional)
/// Este înregistrată numai când mediul este Development (vezi Program.cs).
/// </summary>
public sealed class AutentificareDev(
    IOptionsMonitor<AuthenticationSchemeOptions> optiuni,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(optiuni, logger, encoder)
{
    public const string Schema = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Dev-Utilizator", out var utilizator)
            || !Guid.TryParse(utilizator, out var utilizatorId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claimuri = new List<Claim> { new(ClaimuriErp.Utilizator, utilizatorId.ToString()) };
        if (Request.Headers.TryGetValue("X-Dev-Firma", out var firma) && Guid.TryParse(firma, out var firmaId))
        {
            claimuri.Add(new Claim(ClaimuriErp.Firma, firmaId.ToString()));
        }

        // Numele apare pe documente (emitent, aprobator); opțional, cu o valoare implicită.
        var nume = Request.Headers.TryGetValue("X-Dev-Nume", out var n) && !string.IsNullOrWhiteSpace(n) ? n.ToString() : "Utilizator dezvoltare";
        claimuri.Add(new Claim(ClaimuriErp.Nume, nume));

        var identitate = new ClaimsIdentity(claimuri, Schema);
        var tichet = new AuthenticationTicket(new ClaimsPrincipal(identitate), Schema);
        return Task.FromResult(AuthenticateResult.Success(tichet));
    }
}
