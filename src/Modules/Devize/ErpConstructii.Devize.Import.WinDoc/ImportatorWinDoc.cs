using ErpConstructii.Devize.Import;
using ErpConstructii.SharedKernel.Import;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Devize.Import.WinDoc;

/// <summary>
/// Importator pentru devizele exportate din WinDoc.
///
/// STARE: SCHELET. Formatul exact al exportului WinDoc NU este încă cunoscut (întrebare deschisă
/// în documentul de proiect: fișier nativ, XML sau Excel?). Implementarea se face DUPĂ ce avem
/// fișiere reale de la clientul pilot, împreună cu totalurile din devizul original.
///
/// Pași de implementare (pentru Claude Code):
///  1. Pune fișierele reale în tests/samples-private/windoc/ (NU în git) și 1–2 fișiere
///     anonimizate în tests/samples/windoc/, cu un .json alăturat care conține totalurile așteptate.
///  2. Implementează <see cref="PoateCiti"/> pe semnătura reală (extensie + antet/structură).
///  3. Implementează <see cref="CitesteAsync"/>: mapare la <see cref="DevizCanonic"/>, inclusiv
///     consumurile pe resurse și cotele recapitulației, dacă exportul le conține.
///  4. Testul de regresie: pentru fiecare fișier de test, ValidatorDevizCanonic nu raportează
///     erori de totaluri (recalculul iese la ban). Dacă nu iese, ajustează ParametriRecapitulatie
///     (rotunjiri), NU adăuga cazuri speciale în CalculatorDeviz.
/// </summary>
public sealed class ImportatorWinDoc : IImportatorDeviz
{
    public string FormatId => "windoc";

    public string Denumire => "WinDoc";

    public RezultatDetectare PoateCiti(SursaImport sursa)
    {
        // TODO: înlocuiește cu detectarea pe structura reală a fișierului.
        return sursa.Extensie switch
        {
            ".xml" or ".xlsx" or ".mdb" => new RezultatDetectare(0.1, "Extensie posibilă, dar formatul WinDoc nu este încă implementat."),
            _ => RezultatDetectare.Nu("Extensie necunoscută pentru WinDoc."),
        };
    }

    public Task<RezultatImport<DevizCanonic>> CitesteAsync(SursaImport sursa, CancellationToken ct = default) =>
        Task.FromResult(RezultatImport<DevizCanonic>.Esuat(
            "Importatorul WinDoc nu este încă implementat: lipsesc fișiere reale de test."));
}

public static class InregistrareWinDoc
{
    public static IServiceCollection AdaugaImportatorWinDoc(this IServiceCollection servicii) =>
        servicii.AddSingleton<IImportatorDeviz, ImportatorWinDoc>();
}
