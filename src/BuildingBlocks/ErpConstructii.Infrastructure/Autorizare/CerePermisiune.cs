using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace ErpConstructii.Infrastructure.Autorizare;

/// <summary>
/// Cerința de autorizare: utilizatorul are permisiunea pe firma activă (și pe proiectul din
/// rută, dacă endpoint-ul are parametrul <c>proiectId</c>). Handler-ul este în modulul Identitate.
/// </summary>
/// <param name="DoarPeFirma">
/// Permisiunea trebuie dată pe toată firma (nu pe un proiect). Ex.: administrarea utilizatorilor.
/// </param>
/// <param name="Alternative">Alte permisiuni care ajung în locul celei principale (oricare dintre ele).</param>
public sealed record CerintaPermisiune(string Permisiune, bool DoarPeFirma = false, IReadOnlyList<string>? Alternative = null) : IAuthorizationRequirement
{
    public IEnumerable<string> Toate => Alternative is null ? [Permisiune] : [Permisiune, .. Alternative];
}

public static class ExtensiiAutorizare
{
    /// <summary>
    /// Folosire: <c>grup.MapGet("/", ...).CerePermisiune(Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Vizualizare))</c>.
    /// Fiecare endpoint de business TREBUIE să ceară o permisiune (REQ-DRP-01).
    /// </summary>
    public static TBuilder CerePermisiune<TBuilder>(this TBuilder builder, string permisiune)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.AddRequirements(new CerintaPermisiune(permisiune)));

    /// <summary>
    /// Oricare dintre permisiuni (ex. centrul de import: facturi cu <c>financiar.creare</c>, comenzi cu
    /// <c>aprovizionare.creare</c>). Fiecare operație de după își verifică permisiunea ei.
    /// </summary>
    public static TBuilder CereOricarePermisiune<TBuilder>(this TBuilder builder, string permisiune, params string[] alternative)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.AddRequirements(new CerintaPermisiune(permisiune, Alternative: alternative)));

    /// <summary>Ca <see cref="CerePermisiune{TBuilder}"/>, dar permisiunea trebuie să fie pe toată firma activă.</summary>
    public static TBuilder CerePermisiunePeFirma<TBuilder>(this TBuilder builder, string permisiune)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.AddRequirements(new CerintaPermisiune(permisiune, DoarPeFirma: true)));
}
