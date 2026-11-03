using System.Security.Claims;
using ErpConstructii.Identitate;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Api.Autentificare;

/// <summary>Firma activă, din claim-ul <c>firma_id</c> al token-ului.</summary>
public sealed class FirmaCurentaHttp(IHttpContextAccessor accesor) : IFirmaCurenta
{
    public Guid? FirmaId =>
        Guid.TryParse(accesor.HttpContext?.User.FindFirstValue(ClaimuriErp.Firma), out var id) ? id : null;
}

/// <summary>Utilizatorul curent, din claim-ul <c>sub</c> al token-ului.</summary>
public sealed class UtilizatorCurentHttp(IHttpContextAccessor accesor) : IUtilizatorCurent
{
    public Guid? UtilizatorId =>
        Guid.TryParse(accesor.HttpContext?.User.FindFirstValue(ClaimuriErp.Utilizator), out var id) ? id : null;

    public bool EsteAutentificat => accesor.HttpContext?.User.Identity?.IsAuthenticated == true;
}
