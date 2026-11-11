using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using ErpConstructii.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ErpConstructii.Identitate.Infrastructure.Autentificare;

public sealed record CerereAutentificare(string Email, string Parola);

public sealed record CerereCodMfa(string? Cod, string? CodRecuperare);

/// <param name="Stare">„ok” (sesiune creată) sau „mfa_necesar” (urmează codul din aplicația de autentificare).</param>
public sealed record RaspunsAutentificare(string Stare);

public sealed record CerereParolaNoua(string ParolaNoua);

public sealed record ConfigurareMfaDto(string Cheie, string UriOtp, bool Activat);

public sealed record MfaActivatDto(IReadOnlyList<string> CoduriRecuperare);

/// <summary>
/// Endpoint-urile serverului de autentificare. Sunt în afara grupului /api protejat de token:
/// /connect/* sunt endpoint-urile OAuth (OpenIddict), /api/cont/* sunt folosite de pagina de login.
/// </summary>
public static class EndpointuriAutentificare
{
    private const string AmrMfa = "mfa";
    private const string MesajCredentialeGresite = "Email sau parolă greșită.";

    public static void MapeazaEndpointuriAutentificare(this IEndpointRouteBuilder rute)
    {
        rute.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], Autorizeaza).AllowAnonymous().ExcludeFromDescription();
        rute.MapPost("/connect/token", EmiteToken).AllowAnonymous().ExcludeFromDescription();
        rute.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], Iesire).AllowAnonymous().ExcludeFromDescription();

        var cont = rute.MapGroup("/api/cont").WithTags("Cont");
        cont.MapPost("/autentificare", Autentifica).AllowAnonymous();
        cont.MapPost("/autentificare/mfa", AutentificaMfa).AllowAnonymous();

        var doarCookie = new Action<Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder>(p =>
            p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser());
        cont.MapGet("/mfa/configurare", CitesteConfigurareMfa).RequireAuthorization(doarCookie);
        cont.MapPost("/mfa/configurare", ActiveazaMfa).RequireAuthorization(doarCookie);
        cont.MapPost("/parola-noua", SeteazaParolaNoua).RequireAuthorization(doarCookie);
    }

    // ---------------------------------------------------------------- OAuth

    private static async Task<IResult> Autorizeaza(
        HttpContext ctx,
        UserManager<ContUtilizator> conturi,
        SignInManager<ContUtilizator> sesiuni,
        ContextSesiune context,
        CancellationToken ct)
    {
        var cerere = ctx.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Cererea OpenID Connect lipsește.");

        var cookie = await ctx.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var cont = cookie.Succeeded ? await conturi.GetUserAsync(cookie.Principal) : null;
        if (cont is null)
        {
            return Results.Redirect($"{InregistrareAutentificare.RutaLogin}?ReturnUrl={Uri.EscapeDataString(UrlAutorizare(ctx))}");
        }

        var stare = await context.IncarcaAsync(cont.Id, ct);
        if (stare is null || !stare.PoateIntra || await conturi.IsLockedOutAsync(cont))
        {
            await sesiuni.SignOutAsync();
            return Refuza(Errors.AccessDenied, "Contul este dezactivat, expirat sau blocat.");
        }

        // Parola temporară (cont nou sau resetat de administrator) se schimbă înainte de orice altceva.
        if (cont.TrebuieSchimbataParola)
        {
            return Results.Redirect($"{InregistrareAutentificare.RutaSchimbareParola}?ReturnUrl={Uri.EscapeDataString(UrlAutorizare(ctx))}");
        }

        if (stare.MfaObligatoriu && !cont.TwoFactorEnabled)
        {
            return Results.Redirect($"{InregistrareAutentificare.RutaConfigurareMfa}?ReturnUrl={Uri.EscapeDataString(UrlAutorizare(ctx))}");
        }

        // Sesiune deschisă cu parola înainte de activarea MFA: se cere din nou autentificarea completă.
        if (cont.TwoFactorEnabled && !cookie.Principal!.HasClaim("amr", AmrMfa))
        {
            await sesiuni.SignOutAsync();
            return Results.Redirect($"{InregistrareAutentificare.RutaLogin}?ReturnUrl={Uri.EscapeDataString(UrlAutorizare(ctx))}");
        }

        var firmaId = AccesFirme.Alege(stare.Firme.Select(f => f.Id).ToList(), ceruta: null, cont.UltimaFirmaId);
        if (firmaId is null)
        {
            return Refuza(Errors.AccessDenied, "Contul nu are acces la nicio firmă. Cere administratorului o atribuire.");
        }

        var identitate = ContextSesiune.Identitate(stare, stare.Firme.Single(f => f.Id == firmaId), cerere.GetScopes());
        return Results.SignIn(new ClaimsPrincipal(identitate), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> EmiteToken(
        HttpContext ctx,
        UserManager<ContUtilizator> conturi,
        ContextSesiune context,
        CancellationToken ct)
    {
        var cerere = ctx.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Cererea OpenID Connect lipsește.");
        if (!cerere.IsAuthorizationCodeGrantType() && !cerere.IsRefreshTokenGrantType())
        {
            return Refuza(Errors.UnsupportedGrantType, "Tip de cerere nesuportat.");
        }

        // Principalul din cod / refresh token, deja validat de OpenIddict.
        var rezultat = await ctx.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var anterior = rezultat.Principal;
        if (anterior is null || !Guid.TryParse(anterior.GetClaim(Claims.Subject), out var utilizatorId))
        {
            return Refuza(Errors.InvalidGrant, "Sesiunea a expirat. Autentifică-te din nou.");
        }

        // La fiecare reîmprospătare se reverifică totul: un cont dezactivat pierde accesul în cel mult 10 minute.
        var cont = await conturi.FindByIdAsync(utilizatorId.ToString());
        var stare = await context.IncarcaAsync(utilizatorId, ct);
        if (cont is null || stare is null || !stare.PoateIntra || await conturi.IsLockedOutAsync(cont))
        {
            return Refuza(Errors.InvalidGrant, "Contul nu mai este activ.");
        }

        // Schimbarea firmei active: refresh token + parametrul firma_id.
        var accesibile = stare.Firme.Select(f => f.Id).ToList();
        Guid? ceruta = Guid.TryParse((string?)cerere["firma_id"], out var c) ? c : null;
        if (ceruta is not null && !accesibile.Contains(ceruta.Value))
        {
            return Refuza(Errors.InvalidGrant, "Nu ai acces la firma cerută.");
        }

        Guid? actuala = Guid.TryParse(anterior.GetClaim(ClaimuriErp.Firma), out var a) ? a : null;
        var firmaId = AccesFirme.Alege(accesibile, ceruta ?? actuala, cont.UltimaFirmaId);
        if (firmaId is null)
        {
            return Refuza(Errors.InvalidGrant, "Contul nu mai are acces la nicio firmă.");
        }

        if (cont.UltimaFirmaId != firmaId)
        {
            cont.UltimaFirmaId = firmaId;
            await conturi.UpdateAsync(cont);
        }

        var identitate = ContextSesiune.Identitate(stare, stare.Firme.Single(f => f.Id == firmaId), anterior.GetScopes());
        return Results.SignIn(new ClaimsPrincipal(identitate), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> Iesire(SignInManager<ContUtilizator> sesiuni)
    {
        await sesiuni.SignOutAsync();
        // OpenIddict redirecționează la post_logout_redirect_uri (validat pe client).
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    // ---------------------------------------------------------------- Login (pagina web)

    private static async Task<IResult> Autentifica(
        CerereAutentificare cerere,
        UserManager<ContUtilizator> conturi,
        SignInManager<ContUtilizator> sesiuni,
        ContextSesiune context,
        CancellationToken ct)
    {
        var cont = await conturi.FindByEmailAsync(cerere.Email.Trim());
        if (cont is null)
        {
            return Eroare(MesajCredentialeGresite);
        }

        var stare = await context.IncarcaAsync(cont.Id, ct);
        if (stare is null || !stare.PoateIntra)
        {
            return Eroare("Contul este dezactivat sau expirat.");
        }

        var rezultat = await sesiuni.PasswordSignInAsync(cont, cerere.Parola, isPersistent: false, lockoutOnFailure: true);
        if (rezultat.Succeeded)
        {
            return Results.Ok(new RaspunsAutentificare("ok"));
        }

        if (rezultat.RequiresTwoFactor)
        {
            return Results.Ok(new RaspunsAutentificare("mfa_necesar"));
        }

        return Eroare(rezultat.IsLockedOut
            ? "Contul este blocat temporar după prea multe încercări greșite. Încearcă din nou peste 15 minute."
            : MesajCredentialeGresite);
    }

    private static async Task<IResult> AutentificaMfa(
        CerereCodMfa cerere, SignInManager<ContUtilizator> sesiuni, UserManager<ContUtilizator> conturi, ICeas ceas, ILoggerFactory jurnal)
    {
        var logger = jurnal.CreateLogger("ErpConstructii.Autentificare");
        SignInResult rezultat;
        if (!string.IsNullOrWhiteSpace(cerere.CodRecuperare))
        {
            rezultat = await sesiuni.TwoFactorRecoveryCodeSignInAsync(cerere.CodRecuperare.Replace(" ", string.Empty, StringComparison.Ordinal));
        }
        else if (!string.IsNullOrWhiteSpace(cerere.Cod))
        {
            rezultat = await sesiuni.TwoFactorAuthenticatorSignInAsync(
                cerere.Cod.Replace(" ", string.Empty, StringComparison.Ordinal), isPersistent: false, rememberClient: false);
        }
        else
        {
            return Eroare("Introdu codul din aplicația de autentificare.");
        }

        if (rezultat.Succeeded)
        {
            return Results.Ok(new RaspunsAutentificare("ok"));
        }

        if (rezultat.IsLockedOut)
        {
            return Eroare("Contul este blocat temporar după prea multe încercări greșite. Încearcă din nou peste 15 minute.");
        }

        // Pasul cu parola a expirat (cookie-ul de 2FA durează câteva minute) sau serverul a fost repornit între pași.
        var cont = await sesiuni.GetTwoFactorAuthenticationUserAsync();
        if (cont is null)
        {
            return Eroare("Sesiunea de login a expirat. Reia autentificarea cu parola.");
        }

        // Codul refuzat, dar corect cu alt ceas: ceasul serverului (sau al telefonului) nu e sincronizat.
        if (!string.IsNullOrWhiteSpace(cerere.Cod) && await conturi.GetAuthenticatorKeyAsync(cont) is { Length: > 0 } cheie
            && DecalajCeasTotp.Decalaj(cheie, cerere.Cod, ceas.Acum) is { } decalaj && Math.Abs(decalaj) > 60)
        {
            var minute = Math.Round(Math.Abs(decalaj) / 60.0);
            logger.LogWarning(
                "Cod MFA refuzat pentru {Email}, dar corect cu un decalaj de {Decalaj} s: ceasul serverului este {Sens} față de telefon. Sincronizează ora serverului (w32tm /resync).",
                cont.Email, decalaj, decalaj > 0 ? "în urmă" : "înainte");
            return Eroare($"Codul este corect, dar ceasul serverului diferă de al telefonului cu aproximativ {minute:0} minute. " +
                          "Administratorul trebuie să sincronizeze ora serverului (sau verifică ora automată pe telefon); nu e nevoie de resetarea parolei.");
        }

        logger.LogWarning("Cod MFA refuzat pentru {Email}: nu se potrivește cu cheia acestui server nici cu decalaj de ceas (altă intrare din Authenticator?).", cont.Email);
        return Eroare($"Cod incorect pentru acest server. Dacă în aplicația de autentificare ai mai multe intrări „ERP Construcții”, " +
                      $"folosește-o pe cea scanată pe acest server ({EmitentMfa(sesiuni.Context)}); intrările vechi fără server în nume pot fi ale altei instalări.");
    }

    private static async Task<IResult> CitesteConfigurareMfa(HttpContext ctx, UserManager<ContUtilizator> conturi)
    {
        var cont = await conturi.GetUserAsync(ctx.User);
        if (cont is null)
        {
            return Results.Unauthorized();
        }

        var cheie = await conturi.GetAuthenticatorKeyAsync(cont);
        if (string.IsNullOrEmpty(cheie))
        {
            await conturi.ResetAuthenticatorKeyAsync(cont);
            cheie = await conturi.GetAuthenticatorKeyAsync(cont)
                ?? throw new InvalidOperationException("Cheia TOTP nu a putut fi generată.");
        }

        // Numele intrării din Authenticator conține serverul: fiecare instalare (dezvoltare, producție) are altă cheie, iar unele
        // aplicații înlocuiesc intrarea cu același nume la scanarea unui cod nou, ceea ce strica MFA pe celălalt server.
        var emitent = EmitentMfa(ctx);
        var uri = $"otpauth://totp/{Uri.EscapeDataString(emitent)}:{Uri.EscapeDataString(cont.Email ?? string.Empty)}" +
                  $"?secret={cheie}&issuer={Uri.EscapeDataString(emitent)}&digits=6";
        var cheieFormatata = string.Join(' ', cheie.ToLowerInvariant().Chunk(4).Select(g => new string(g)));
        return Results.Ok(new ConfigurareMfaDto(cheieFormatata, uri, cont.TwoFactorEnabled));
    }

    /// <summary>„ERP Construcții (dezvoltare)” sau „ERP Construcții (sdaq.home.ro)”; configurabil cu Autentificare:EmitentMfa.</summary>
    private static string EmitentMfa(HttpContext ctx)
    {
        var configurat = ctx.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Autentificare:EmitentMfa"];
        if (!string.IsNullOrWhiteSpace(configurat))
            return configurat.Trim();
        var mediu = ctx.RequestServices.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
        return mediu.IsDevelopment() ? "ERP Construcții (dezvoltare)" : $"ERP Construcții ({ctx.Request.Host.Host})";
    }

    private static async Task<IResult> ActiveazaMfa(
        HttpContext ctx,
        CerereCodMfa cerere,
        UserManager<ContUtilizator> conturi,
        SignInManager<ContUtilizator> sesiuni)
    {
        var cont = await conturi.GetUserAsync(ctx.User);
        if (cont is null)
        {
            return Results.Unauthorized();
        }

        var cod = (cerere.Cod ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        var valid = await conturi.VerifyTwoFactorTokenAsync(cont, conturi.Options.Tokens.AuthenticatorTokenProvider, cod);
        if (!valid)
        {
            return Eroare("Cod incorect. Verifică ora telefonului și introdu codul afișat acum.");
        }

        await conturi.SetTwoFactorEnabledAsync(cont, true);
        var coduri = await conturi.GenerateNewTwoFactorRecoveryCodesAsync(cont, 10) ?? [];

        // Sesiunea curentă devine una completă (parolă + al doilea factor).
        await sesiuni.SignInWithClaimsAsync(cont, isPersistent: false, [new Claim("amr", AmrMfa)]);
        return Results.Ok(new MfaActivatDto(coduri.ToList()));
    }

    private static async Task<IResult> SeteazaParolaNoua(
        HttpContext ctx,
        CerereParolaNoua cerere,
        UserManager<ContUtilizator> conturi,
        SignInManager<ContUtilizator> sesiuni)
    {
        var cont = await conturi.GetUserAsync(ctx.User);
        if (cont is null)
        {
            return Results.Unauthorized();
        }

        // Endpoint-ul servește doar schimbării parolei temporare, imediat după login cu ea.
        if (!cont.TrebuieSchimbataParola)
        {
            return Eroare("Parola nu trebuie schimbată.");
        }

        if (await conturi.CheckPasswordAsync(cont, cerere.ParolaNoua))
        {
            return Eroare("Parola nouă trebuie să fie diferită de cea temporară.");
        }

        var token = await conturi.GeneratePasswordResetTokenAsync(cont);
        var rezultat = await conturi.ResetPasswordAsync(cont, token, cerere.ParolaNoua);
        if (!rezultat.Succeeded)
        {
            return Eroare(string.Join(" ", rezultat.Errors.Select(e => TraducereErori.Parola(e))));
        }

        cont.TrebuieSchimbataParola = false;
        await conturi.UpdateAsync(cont);

        // Resetarea schimbă security stamp-ul; sesiunea curentă se reface, păstrând al doilea factor dacă a existat.
        var amr = ctx.User.HasClaim("amr", AmrMfa) ? new[] { new Claim("amr", AmrMfa) } : [];
        await sesiuni.SignInWithClaimsAsync(cont, isPersistent: false, amr);
        return Results.Ok(new RaspunsAutentificare("ok"));
    }

    // ---------------------------------------------------------------- Utilitare

    private static IResult Eroare(string mesaj) => Results.BadRequest(new { mesaj });

    /// <summary>Refuz OAuth: OpenIddict îl transformă în răspunsul standard (redirect cu error / JSON).</summary>
    private static IResult Refuza(string eroare, string descriere) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = eroare,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = descriere,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    /// <summary>URL-ul cererii de autorizare, ca GET (și pentru cererile POST), pentru revenirea după login.</summary>
    private static string UrlAutorizare(HttpContext ctx)
    {
        var parametri = ctx.Request.HasFormContentType
            ? ctx.Request.Form.Select(p => KeyValuePair.Create(p.Key, p.Value))
            : ctx.Request.Query.Select(p => KeyValuePair.Create(p.Key, p.Value));
        return ctx.Request.PathBase + ctx.Request.Path + QueryString.Create(parametri.Select(p => KeyValuePair.Create(p.Key, (StringValues)p.Value)));
    }
}
