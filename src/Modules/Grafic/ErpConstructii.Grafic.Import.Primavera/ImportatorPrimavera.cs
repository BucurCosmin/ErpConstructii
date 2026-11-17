using System.Text;
using ErpConstructii.SharedKernel.Import;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Grafic.Import.Primavera;

/// <summary>
/// Importator pentru graficele Primavera P6 exportate ca XER (text separat prin TAB, <see cref="FisierXer"/>), convertite în
/// modelul canonic de <see cref="ConversieXer"/>. PMXML e recunoscut, dar nu încă citit: utilizatorul e rugat să exporte XER.
/// </summary>
public sealed class ImportatorPrimavera : IImportatorGrafic
{
    public string FormatId => "primavera";

    public string Denumire => "Primavera P6 (XER)";

    public RezultatDetectare PoateCiti(SursaImport sursa)
    {
        // XER începe cu linia „ERMHDR” (antetul exportului P6).
        if (FisierXer.EsteXer(sursa.Continut))
            return new RezultatDetectare(0.95, "Antet XER Primavera.");
        if (sursa.Extensie == ".xer")
            return new RezultatDetectare(0.3, "Extensie .xer fără antetul ERMHDR.");

        if (sursa.Extensie == ".xml")
        {
            var inceput = Encoding.UTF8.GetString(sursa.Continut, 0, Math.Min(sursa.Continut.Length, 2048));
            return inceput.Contains("APIBusinessObjects", StringComparison.Ordinal)
                ? new RezultatDetectare(0.9, "PMXML Primavera.")
                : RezultatDetectare.Nu("XML fără rădăcina APIBusinessObjects.");
        }

        return RezultatDetectare.Nu("Nu este un fișier Primavera.");
    }

    public Task<RezultatImport<GraficCanonic>> CitesteAsync(SursaImport sursa, CancellationToken ct = default)
    {
        if (FisierXer.Citeste(sursa.Continut) is { } xer)
            return Task.FromResult(ConversieXer.Converteste(xer));
        return Task.FromResult(RezultatImport<GraficCanonic>.Esuat(sursa.Extensie == ".xml"
            ? "Fișierele PMXML nu sunt încă citite: exportă graficul din Primavera ca XER (File → Export → Primavera PM – (XER))."
            : "Fișierul nu este un export Primavera XER (lipsește antetul ERMHDR)."));
    }
}

public static class ExtensiiImportatorPrimavera
{
    /// <summary>Înregistrează importatorul Primavera (REQ-GRF-01), ca la ceilalți importatori.</summary>
    public static IServiceCollection AdaugaImportatorPrimavera(this IServiceCollection servicii) =>
        servicii.AddSingleton<IImportatorGrafic, ImportatorPrimavera>();
}
