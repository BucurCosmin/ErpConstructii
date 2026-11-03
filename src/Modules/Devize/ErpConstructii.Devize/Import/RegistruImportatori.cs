using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Devize.Import;

/// <summary>
/// Alege importatorul de deviz potrivit pentru un fișier (REQ-IMP-03, pasul „Detectare”).
/// Importatorii se înregistrează în DI ca <see cref="IImportatorDeviz"/>.
/// </summary>
public sealed class RegistruImportatori(IEnumerable<IImportatorDeviz> importatori)
{
    public IReadOnlyList<IImportatorDeviz> Toti { get; } = importatori.ToList();

    public (IImportatorDeviz? Importator, IReadOnlyList<string> Motive) Alege(
        SursaImport sursa,
        IReadOnlySet<string>? formateActive = null) =>
        AlegereImportator.Alege<IImportatorDeviz, DevizCanonic>(Toti, sursa, formateActive);
}
