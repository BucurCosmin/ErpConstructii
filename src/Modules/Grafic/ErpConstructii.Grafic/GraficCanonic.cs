using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Grafic;

/// <summary>Tipurile de dependență dintre activități (ca în Primavera / MS Project).</summary>
public enum TipDependenta
{
    /// <summary>Finish-to-Start: succesorul începe după ce predecesorul se termină.</summary>
    FS,
    /// <summary>Start-to-Start.</summary>
    SS,
    /// <summary>Finish-to-Finish.</summary>
    FF,
    /// <summary>Start-to-Finish (rar).</summary>
    SF,
}

public sealed record Dependenta(string CodPredecesor, TipDependenta Tip, int DecalajZile = 0);

/// <param name="Cod">Codul activității din programul sursă (Activity ID în Primavera).</param>
/// <param name="CodWbs">Nodul WBS, pentru afișare ierarhică și pentru propunerea legăturilor cu devizul.</param>
/// <param name="CoduriActivitate">Codurile de activitate (Primavera „activity codes”), utile la legarea automată.</param>
public sealed record ActivitateCanonica(
    string Cod,
    string Denumire,
    string? CodWbs,
    DateOnly StartPlanificat,
    DateOnly SfarsitPlanificat,
    int DurataZile,
    bool EsteJalon,
    IReadOnlyList<Dependenta> Dependente,
    IReadOnlyDictionary<string, string> CoduriActivitate);

/// <summary>
/// Modelul intern comun al unui grafic de execuție (REQ-GRF-01). Graficele Excel din documentațiile
/// de atribuire nu au de obicei dependențe → <see cref="AreDependente"/> = false, iar prognoza
/// rămâne liniară (fără drum critic).
/// </summary>
public sealed record GraficCanonic(
    string Denumire,
    DateOnly? DataReferinta,
    IReadOnlyList<ActivitateCanonica> Activitati)
{
    public bool AreDependente => Activitati.Any(a => a.Dependente.Count > 0);
}

/// <summary>Contractul importatorilor de grafice (Primavera XER/PMXML, Excel, ulterior MS Project).</summary>
public interface IImportatorGrafic : IImportator<GraficCanonic>;
