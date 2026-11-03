namespace ErpConstructii.Formulare;

/// <summary>O versiune imuabilă de șablon disponibilă pentru generarea unui formular.</summary>
public sealed record ContinutSablonFormular(Guid Id, string CodTip, int Versiune, string NumeFisier, byte[] Continut);

/// <summary>Contract public pentru accesarea șabloanelor din modulele care generează documente.</summary>
public interface IInterogareSabloaneFormulare
{
    Task<ContinutSablonFormular?> SablonActivAsync(string codTip, CancellationToken ct = default);
    Task<ContinutSablonFormular?> SablonAsync(Guid id, int versiune, CancellationToken ct = default);
}
