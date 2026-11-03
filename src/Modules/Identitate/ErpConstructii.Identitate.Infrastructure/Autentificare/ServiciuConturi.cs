using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace ErpConstructii.Identitate.Infrastructure.Autentificare;

/// <summary>
/// Operațiile administratorului pe contul de autentificare: cont nou cu parolă temporară,
/// resetare parolă, resetare MFA. După resetare, sesiunile existente ale utilizatorului se închid.
/// </summary>
public sealed class ServiciuConturi(UserManager<ContUtilizator> conturi, IOpenIddictTokenManager tokenuri)
{
    // Fără caractere ușor de confundat (0/O, 1/l/I), ca parola să poată fi dictată sau copiată de pe hârtie.
    private const string Litere = "abcdefghijkmnpqrstuvwxyz";
    private const string Majuscule = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Cifre = "23456789";

    /// <summary>Parolă temporară de 16 caractere, cu litere mari, mici și cifre (politica cere minim 12).</summary>
    public static string ParolaTemporara()
    {
        var toate = Litere + Majuscule + Cifre;
        var caractere = new[]
            {
                Litere[RandomNumberGenerator.GetInt32(Litere.Length)],
                Majuscule[RandomNumberGenerator.GetInt32(Majuscule.Length)],
                Cifre[RandomNumberGenerator.GetInt32(Cifre.Length)],
            }
            .Concat(Enumerable.Range(0, 13).Select(_ => toate[RandomNumberGenerator.GetInt32(toate.Length)]))
            .ToArray();
        RandomNumberGenerator.Shuffle(caractere.AsSpan());
        return new string(caractere);
    }

    /// <summary>Creează contul de autentificare pentru un utilizator nou; întoarce parola temporară.</summary>
    public async Task<string> CreeazaAsync(Utilizator utilizator)
    {
        var parola = ParolaTemporara();
        var cont = new ContUtilizator
        {
            Id = utilizator.Id,
            UserName = utilizator.Email,
            Email = utilizator.Email,
            EmailConfirmed = true,
            TrebuieSchimbataParola = true,
        };
        Verifica(await conturi.CreateAsync(cont, parola));
        return parola;
    }

    /// <summary>Parolă temporară nouă, deblocare și închiderea sesiunilor.</summary>
    public async Task<string> ReseteazaParolaAsync(Guid utilizatorId, CancellationToken ct)
    {
        var cont = await GasesteAsync(utilizatorId);
        var parola = ParolaTemporara();
        var token = await conturi.GeneratePasswordResetTokenAsync(cont);
        Verifica(await conturi.ResetPasswordAsync(cont, token, parola));

        cont.TrebuieSchimbataParola = true;
        Verifica(await conturi.UpdateAsync(cont));
        Verifica(await conturi.SetLockoutEndDateAsync(cont, null));
        Verifica(await conturi.ResetAccessFailedCountAsync(cont));
        await InchideSesiunileAsync(cont, ct);
        return parola;
    }

    /// <summary>Pentru telefon pierdut: dezactivează MFA; la următorul login se configurează din nou (dacă e obligatoriu).</summary>
    public async Task ReseteazaMfaAsync(Guid utilizatorId, CancellationToken ct)
    {
        var cont = await GasesteAsync(utilizatorId);
        Verifica(await conturi.SetTwoFactorEnabledAsync(cont, false));
        Verifica(await conturi.ResetAuthenticatorKeyAsync(cont));
        await InchideSesiunileAsync(cont, ct);
    }

    /// <summary>Contul dezactivat nu mai primește token-uri; închidem și sesiunile deschise.</summary>
    public async Task InchideSesiunileAsync(Guid utilizatorId, CancellationToken ct) =>
        await InchideSesiunileAsync(await GasesteAsync(utilizatorId), ct);

    public async Task<ContUtilizator?> CitesteAsync(Guid utilizatorId) => await conturi.FindByIdAsync(utilizatorId.ToString());

    private async Task InchideSesiunileAsync(ContUtilizator cont, CancellationToken ct)
    {
        // Cookie-ul de login devine invalid la schimbarea security stamp-ului; refresh token-urile se revocă.
        Verifica(await conturi.UpdateSecurityStampAsync(cont));
        await tokenuri.RevokeBySubjectAsync(cont.Id.ToString(), ct);
    }

    private async Task<ContUtilizator> GasesteAsync(Guid utilizatorId) =>
        await conturi.FindByIdAsync(utilizatorId.ToString())
        ?? throw new InvalidOperationException($"Utilizatorul {utilizatorId} nu are cont de autentificare.");

    private static void Verifica(IdentityResult rezultat)
    {
        if (!rezultat.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", rezultat.Errors.Select(e => e.Description)));
        }
    }
}
