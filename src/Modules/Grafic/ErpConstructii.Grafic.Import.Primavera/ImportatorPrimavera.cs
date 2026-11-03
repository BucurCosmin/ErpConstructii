using System.Text;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Grafic.Import.Primavera;

/// <summary>
/// Importator pentru graficele Primavera P6: XER (text, separat prin TAB) și PMXML.
///
/// STARE: detectarea este implementată; citirea este SCHELET.
///
/// Pași de implementare (pentru Claude Code):
///  1. Adaugă MPXJ (net.sf.mpxj-for-csharp) și citește fișierul cu UniversalProjectReader
///     (acoperă XER și PMXML). Alternativ, XER se poate parsa direct: tabelele TASK, TASKPRED,
///     PROJWBS, CALENDAR, ACTVCODE / TASKACTV.
///  2. Mapează: TASK → ActivitateCanonica (task_code, task_name, target_start/end, durata,
///     task_type = TT_Mile/TT_FinMile → jalon), TASKPRED → Dependenta (PR_FS/SS/FF/SF, lag),
///     PROJWBS → CodWbs, TASKACTV → CoduriActivitate.
///  3. Linia de bază (baseline) din P6 → DataReferinta / start-sfârșit planificat.
///  4. Exportul actualizat înapoi (REQ-GRF-05): MPXJ scrie XER și PMXML.
///  5. Teste cu fișiere .xer reale de la pilot (tests/samples-private/primavera/).
/// </summary>
public sealed class ImportatorPrimavera : IImportatorGrafic
{
    public string FormatId => "primavera";

    public string Denumire => "Primavera P6 (XER / PMXML)";

    public RezultatDetectare PoateCiti(SursaImport sursa)
    {
        // XER începe cu linia „ERMHDR” (antetul exportului P6).
        if (sursa.Extensie == ".xer")
        {
            var antet = Encoding.UTF8.GetString(sursa.Continut, 0, Math.Min(sursa.Continut.Length, 16));
            return antet.StartsWith("ERMHDR", StringComparison.Ordinal)
                ? new RezultatDetectare(0.95, "Antet XER Primavera.")
                : new RezultatDetectare(0.3, "Extensie .xer fără antetul ERMHDR.");
        }

        if (sursa.Extensie == ".xml")
        {
            var inceput = Encoding.UTF8.GetString(sursa.Continut, 0, Math.Min(sursa.Continut.Length, 2048));
            return inceput.Contains("APIBusinessObjects", StringComparison.Ordinal)
                ? new RezultatDetectare(0.9, "PMXML Primavera.")
                : RezultatDetectare.Nu("XML fără rădăcina APIBusinessObjects.");
        }

        return RezultatDetectare.Nu("Nu este un fișier Primavera.");
    }

    public Task<RezultatImport<GraficCanonic>> CitesteAsync(SursaImport sursa, CancellationToken ct = default) =>
        Task.FromResult(RezultatImport<GraficCanonic>.Esuat(
            "Citirea Primavera nu este încă implementată (vezi pașii din comentariul clasei)."));
}
