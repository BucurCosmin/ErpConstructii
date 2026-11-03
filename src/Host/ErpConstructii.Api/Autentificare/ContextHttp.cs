using System.Security.Claims;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Api.Autentificare;

/// <summary>Numele claim-urilor proprii din token.</summary>
public static class ClaimuriErp
{
    /// <summary>Firma activă a sesiunii (un utilizator poate avea acces la mai multe firme).</summary>
    public const string Firma = "firma_id";

    public const string Utilizator = ClaimTypes.NameIdentifier;
}

public sealed class FirmaCurentaHttp(IHttpContextAccessor accesor) : IFirmaCurenta
{
    public Guid? FirmaId =>
        Guid.TryParse(accesor.HttpContext?.User.FindFirstValue(ClaimuriErp.Firma), out var id) ? id : null;
}

public sealed class UtilizatorCurentHttp(IHttpContextAccessor accesor) : IUtilizatorCurent
{
    public Guid? UtilizatorId =>
        Guid.TryParse(accesor.HttpContext?.User.FindFirstValue(ClaimuriErp.Utilizator), out var id) ? id : null;

    public bool EsteAutentificat => accesor.HttpContext?.User.Identity?.IsAuthenticated == true;
}
