using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Grafic.Import.Excel;

/// <summary>
/// Importator pentru graficele de execuție în Excel.
///
/// STARE: SCHELET. Șablonul se definește DUPĂ ce strângem grafice Excel reale din documentații
/// de atribuire (întrebare deschisă în documentul de proiect).
///
/// Direcția propusă pentru șablon (de validat pe exemple reale):
///  - foaie „Grafic”: rânduri = activități / categorii de lucrări, coloane fixe
///    (Cod, Denumire, Cod categorie deviz, Start, Sfârșit) + o coloană per lună cu procent sau valoare;
///  - fără dependențe → GraficCanonic.AreDependente = false → prognoza este liniară, fără drum critic;
///  - lunile cu valori dau graficul valoric planificat (curba S planificată).
///
/// Implementare: ClosedXML; detectare pe numele foii și pe antetul coloanelor fixe.
/// </summary>
public sealed class ImportatorGraficExcel : IImportatorGrafic
{
    public string FormatId => "excel-grafic";

    public string Denumire => "Grafic Excel (șablon)";

    public RezultatDetectare PoateCiti(SursaImport sursa) =>
        sursa.Extensie == ".xlsx"
            ? new RezultatDetectare(0.2, "Fișier Excel; șablonul de grafic nu este încă definit.")
            : RezultatDetectare.Nu("Nu este un fișier .xlsx.");

    public Task<RezultatImport<GraficCanonic>> CitesteAsync(SursaImport sursa, CancellationToken ct = default) =>
        Task.FromResult(RezultatImport<GraficCanonic>.Esuat("Importul graficelor Excel nu este încă implementat."));
}
