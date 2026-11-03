using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Infrastructure;

/// <summary>
/// Contractul fiecărui modul al monolitului modular.
///
/// Reguli de modularitate (verificate la code review, ulterior cu teste de arhitectură):
/// 1. Un modul nu referă proiectele de infrastructură ale altui modul.
/// 2. Comunicarea între module se face prin interfețe publice din proiectul „core”
///    al modulului (ex. ErpConstructii.Proiecte) sau prin evenimente.
/// 3. Fiecare modul are propria schemă PostgreSQL și propriul DbContext.
/// </summary>
public interface IModul
{
    /// <summary>Nume scurt, folosit ca schemă PostgreSQL și prefix de rută (ex. "devize").</summary>
    string Nume { get; }

    void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare);

    void MapeazaEndpointuri(IEndpointRouteBuilder rute);
}
