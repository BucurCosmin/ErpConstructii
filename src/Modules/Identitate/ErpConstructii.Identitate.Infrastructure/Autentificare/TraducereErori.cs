using Microsoft.AspNetCore.Identity;

namespace ErpConstructii.Identitate.Infrastructure.Autentificare;

/// <summary>Mesajele ASP.NET Core Identity despre parolă, în română (pentru utilizator).</summary>
internal static class TraducereErori
{
    public static string Parola(IdentityError eroare) => eroare.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) => "Parola trebuie să aibă cel puțin 12 caractere.",
        nameof(IdentityErrorDescriber.PasswordRequiresDigit) => "Parola trebuie să conțină cel puțin o cifră.",
        nameof(IdentityErrorDescriber.PasswordRequiresLower) => "Parola trebuie să conțină cel puțin o literă mică.",
        nameof(IdentityErrorDescriber.PasswordRequiresUpper) => "Parola trebuie să conțină cel puțin o literă mare.",
        nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => "Parola are prea puține caractere distincte.",
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName) =>
            "Există deja un cont cu această adresă de email.",
        nameof(IdentityErrorDescriber.InvalidEmail) => "Adresa de email nu este validă.",
        _ => eroare.Description,
    };
}
