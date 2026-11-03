using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace ErpConstructii.Infrastructure.Autorizare;

/// <summary>
/// Cerința de autorizare: utilizatorul are permisiunea pe firma activă (și pe proiectul din
/// rută, dacă endpoint-ul are parametrul <c>proiectId</c>). Handler-ul este în modulul Identitate.
/// </summary>
public sealed record CerintaPermisiune(string Permisiune) : IAuthorizationRequirement;

public static class ExtensiiAutorizare
{
    /// <summary>
    /// Folosire: <c>grup.MapGet("/", ...).CerePermisiune(Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Vizualizare))</c>.
    /// Fiecare endpoint de business TREBUIE să ceară o permisiune (REQ-DRP-01).
    /// </summary>
    public static TBuilder CerePermisiune<TBuilder>(this TBuilder builder, string permisiune)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.AddRequirements(new CerintaPermisiune(permisiune)));
}
