using ErpConstructii.Identitate.Infrastructure.Autentificare;
using Microsoft.AspNetCore.Identity;

namespace ErpConstructii.Api.Administrare;

/// <summary>
/// Comandă de consolă pentru administrarea unui cont, fără interfață (ex. contul blocat sau parola uitată pe un server):
///   ErpConstructii.Api --reseteaza-parola &lt;email&gt; [--reseteaza-mfa]
/// Setează o parolă temporară (schimbată obligatoriu la primul login), deblochează contul și închide sesiunile; opțional
/// dezactivează MFA (telefon pierdut). Folosită de deploy\reseteaza-parola.ps1. Rulează cu configurarea mediului curent.
/// </summary>
internal static class ComandaCont
{
    private const string Comanda = "--reseteaza-parola";

    public static bool EsteComanda(string[] args) => args.Contains(Comanda, StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RuleazaAsync(IServiceProvider radacina, string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals(Comanda, StringComparison.OrdinalIgnoreCase));
        var email = i + 1 < args.Length ? args[i + 1].Trim() : string.Empty;
        if (email.Length == 0 || email.StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Utilizare: {Comanda} <email> [--reseteaza-mfa]");
            return 2;
        }

        await using var scope = radacina.CreateAsyncScope();
        var conturi = scope.ServiceProvider.GetRequiredService<UserManager<ContUtilizator>>();
        var serviciu = scope.ServiceProvider.GetRequiredService<ServiciuConturi>();

        var cont = await conturi.FindByEmailAsync(email);
        if (cont is null)
        {
            Console.Error.WriteLine($"Nu există niciun cont cu adresa {email} în această bază.");
            return 1;
        }

        var parola = await serviciu.ReseteazaParolaAsync(cont.Id, CancellationToken.None);
        var mfa = args.Contains("--reseteaza-mfa", StringComparer.OrdinalIgnoreCase);
        if (mfa)
        {
            await serviciu.ReseteazaMfaAsync(cont.Id, CancellationToken.None);
        }

        Console.WriteLine($"Contul {email} a fost deblocat și are parola temporară:");
        Console.WriteLine();
        Console.WriteLine($"    {parola}");
        Console.WriteLine();
        Console.WriteLine("La primul login se cere o parolă nouă." + (mfa ? " MFA a fost dezactivat: se configurează din nou la login." : string.Empty));
        return 0;
    }
}
