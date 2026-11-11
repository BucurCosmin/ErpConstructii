namespace ErpConstructii.Identitate;

public sealed record UtilizatorFirma(Guid Id, string Nume, string Email);

/// <summary>
/// Contract public: utilizatorii activi cu acces valabil la firma activă (ex. gestionarul unei gestiuni de stoc), fără
/// dependență de infrastructura Identitate și fără detalii de administrare (roluri, atribuiri).
/// </summary>
public interface IInterogareUtilizatori
{
    Task<IReadOnlyList<UtilizatorFirma>> UtilizatoriFirmaAsync(CancellationToken ct = default);
}
