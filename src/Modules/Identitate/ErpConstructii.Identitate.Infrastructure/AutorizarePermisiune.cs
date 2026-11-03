using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace ErpConstructii.Identitate.Infrastructure;

/// <summary>
/// Verifică <see cref="CerintaPermisiune"/> pe domeniul cererii:
///  - firma = firma activă din token;
///  - proiectul = valoarea de rută <c>proiectId</c>, dacă endpoint-ul o are.
/// Endpoint-urile de listă primesc doar verificarea că utilizatorul are dreptul pe cel puțin
/// un proiect; filtrarea efectivă se face în interogare, cu <see cref="EvaluatorPermisiuni.ProiecteAccesibile"/>.
/// </summary>
internal sealed class HandlerPermisiune(
    IServiciuPermisiuni permisiuni,
    IFirmaCurenta firma,
    IUtilizatorCurent utilizator) : AuthorizationHandler<CerintaPermisiune>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CerintaPermisiune cerinta)
    {
        if (utilizator.UtilizatorId is not { } utilizatorId || firma.FirmaId is not { } firmaId)
        {
            return;
        }

        var http = context.Resource as HttpContext;
        Guid? proiectId = null;
        if (http is not null
            && http.Request.RouteValues.TryGetValue("proiectId", out var valoare)
            && Guid.TryParse(valoare?.ToString(), out var id))
        {
            proiectId = id;
        }

        var evaluator = await permisiuni.PentruUtilizatorAsync(utilizatorId, http?.RequestAborted ?? default);

        var permis = cerinta.DoarPeFirma
            ? evaluator.Are(cerinta.Permisiune, new Domeniu(firmaId))
            : proiectId is not null
                ? evaluator.Are(cerinta.Permisiune, new Domeniu(firmaId, proiectId))
                : evaluator.ProiecteAccesibile(cerinta.Permisiune, firmaId) is not { Count: 0 };

        if (permis)
        {
            context.Succeed(cerinta);
        }
    }
}
