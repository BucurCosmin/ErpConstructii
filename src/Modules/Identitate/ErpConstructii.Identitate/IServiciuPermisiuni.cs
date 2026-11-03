namespace ErpConstructii.Identitate;

/// <summary>
/// Contractul public al modulului Identitate pentru celelalte module: permisiunile efective
/// ale unui utilizator. Implementarea (cu cache) este în ErpConstructii.Identitate.Infrastructure.
/// Cache-ul se invalidează la orice schimbare de atribuire sau rol.
/// </summary>
public interface IServiciuPermisiuni
{
    Task<EvaluatorPermisiuni> PentruUtilizatorAsync(Guid utilizatorId, CancellationToken ct = default);

    void Invalideaza(Guid utilizatorId);
}
