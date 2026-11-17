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

/// <summary>Felul activității (Primavera „Activity Type”).</summary>
public enum TipActivitate
{
    /// <summary>Activitate cu durată (Task Dependent / Resource Dependent).</summary>
    Activitate,
    /// <summary>Jalon de început (Start Milestone).</summary>
    JalonInceput,
    /// <summary>Jalon de sfârșit (Finish Milestone).</summary>
    JalonSfarsit,
    /// <summary>Activitate rezumat pe WBS (WBS Summary).</summary>
    Rezumat,
    /// <summary>Activitate de nivel (Level of Effort): ține cât activitățile de care depinde.</summary>
    Nivel,
}

/// <summary>Stadiul activității la data actualizării.</summary>
public enum StadiuActivitate
{
    Neinceputa,
    InCurs,
    Finalizata,
}

/// <summary>Felul resursei alocate pe activitate.</summary>
public enum TipResursaGrafic
{
    Manopera,
    Material,
    Utilaj,
    /// <summary>Cost direct pe activitate (Primavera „Expense”), fără cantitate de resursă.</summary>
    Cheltuiala,
}

/// <summary>Nodul din structura de lucrări (WBS). <paramref name="Cheie"/> e calea codurilor („2INFRA.4.2”), stabilă la reimport.</summary>
public sealed record NodWbsCanonic(string Cheie, string Cod, string Denumire, string? CheieParinte, int Ordine);

/// <summary>Resursa sau cheltuiala alocată pe activitate, cu valorile în moneda graficului.</summary>
public sealed record ResursaActivitate(
    string Cod,
    string Denumire,
    TipResursaGrafic Tip,
    string? UnitateMasura,
    decimal CantitatePlanificata,
    decimal CantitateRealizata,
    decimal CantitateRamasa,
    decimal PretUnitar,
    decimal CostPlanificat,
    decimal CostRealizat,
    decimal CostRamas);

/// <summary>Pasul de execuție al activității (Primavera „Steps”).</summary>
public sealed record PasActivitate(string Denumire, bool Finalizat);

/// <summary>Valoarea unui cod de activitate (ex. tipul „Area”, codul „B5”, „Reactor Components Warehouse”).</summary>
public sealed record CodActivitateCanonic(string Tip, string Cod, string Denumire);

/// <param name="Cod">Codul activității din programul sursă (Activity ID în Primavera): cheia stabilă la reimport.</param>
/// <param name="CodWbs">Cheia nodului WBS (<see cref="NodWbsCanonic.Cheie"/>).</param>
/// <param name="StartPlanificat">Începutul planificat (Primavera „Planned Start”).</param>
/// <param name="DurataZile">Durata planificată, în zile lucrătoare ale calendarului activității.</param>
/// <param name="CoduriActivitate">Codurile de activitate: tipul → codul (ex. „Area” → „B5”).</param>
/// <param name="Procent">Procentul de execuție, 0–100, după tipul de procent al activității.</param>
/// <param name="Start">Începutul curent: real dacă a început, altfel cel calculat (Primavera „Start”).</param>
/// <param name="Sfarsit">Sfârșitul curent: real dacă s-a terminat, altfel cel prognozat (Primavera „Finish”).</param>
/// <param name="RezervaTotalaZile">Rezerva totală (total float) în zile; null la activitățile terminate.</param>
/// <param name="Critica">Pe drumul critic: neterminată, cu rezerva sub pragul proiectului.</param>
public sealed record ActivitateCanonica(
    string Cod,
    string Denumire,
    string? CodWbs,
    DateOnly StartPlanificat,
    DateOnly SfarsitPlanificat,
    decimal DurataZile,
    bool EsteJalon,
    IReadOnlyList<Dependenta> Dependente,
    IReadOnlyDictionary<string, string> CoduriActivitate,
    TipActivitate Tip = TipActivitate.Activitate,
    StadiuActivitate Stadiu = StadiuActivitate.Neinceputa,
    decimal Procent = 0,
    DateOnly? Start = null,
    DateOnly? Sfarsit = null,
    DateOnly? StartReal = null,
    DateOnly? SfarsitReal = null,
    DateOnly? StartTarziu = null,
    DateOnly? SfarsitTarziu = null,
    decimal DurataRamasaZile = 0,
    decimal? RezervaTotalaZile = null,
    bool Critica = false,
    string? Calendar = null,
    string? Constrangere = null,
    IReadOnlyList<ResursaActivitate>? Resurse = null,
    IReadOnlyList<PasActivitate>? Pasi = null)
{
    public decimal CostPlanificat => Resurse?.Sum(r => r.CostPlanificat) ?? 0;
    public decimal CostRealizat => Resurse?.Sum(r => r.CostRealizat) ?? 0;
    public decimal CostRamas => Resurse?.Sum(r => r.CostRamas) ?? 0;
}

/// <summary>
/// Modelul intern comun al unui grafic de execuție (REQ-GRF-01). Graficele Excel din documentațiile
/// de atribuire nu au de obicei dependențe → <see cref="AreDependente"/> = false, iar prognoza
/// rămâne liniară (fără drum critic).
/// </summary>
/// <param name="DataReferinta">Data actualizării (Primavera „Data Date”).</param>
/// <param name="CodSursa">Codul proiectului în programul sursă (Primavera „Project ID”): același cod = aceeași planificare, versiune nouă.</param>
/// <param name="Wbs">Structura de lucrări, părinții înaintea copiilor.</param>
/// <param name="Coduri">Valorile codurilor de activitate folosite, cu denumirile lor.</param>
public sealed record GraficCanonic(
    string Denumire,
    DateOnly? DataReferinta,
    IReadOnlyList<ActivitateCanonica> Activitati,
    string? CodSursa = null,
    string? Moneda = null,
    IReadOnlyList<NodWbsCanonic>? Wbs = null,
    IReadOnlyList<CodActivitateCanonic>? Coduri = null)
{
    public bool AreDependente => Activitati.Any(a => a.Dependente.Count > 0);
}

/// <summary>Contractul importatorilor de grafice (Primavera XER/PMXML, Excel, ulterior MS Project).</summary>
public interface IImportatorGrafic : IImportator<GraficCanonic>;
