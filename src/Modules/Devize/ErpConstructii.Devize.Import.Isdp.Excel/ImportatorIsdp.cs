using ErpConstructii.Devize.Import;
using ErpConstructii.SharedKernel.Import;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Devize.Import.Isdp.Excel;

/// <summary>
/// Importatorul devizelor ISDP (Intersoft), D-50: fișierul principal „Deviz ofertă cu resurse” (.xls) și, opțional, lista F3
/// (.xlsx) ca anexă. Citește foile cu <see cref="CititorExcel"/> și le convertește cu <see cref="ConversieIsdp"/>. Nu atinge
/// baza de date și nu aruncă excepții pentru fișiere greșite: întoarce probleme (regula 7).
/// </summary>
public sealed class ImportatorIsdp : IImportatorDevizCuAnexe
{
    public string FormatId => "isdp";

    public string Denumire => "ISDP (Intersoft)";

    public RezultatDetectare PoateCiti(SursaImport sursa)
    {
        if (sursa.Extensie is not (".xls" or ".xlsx"))
            return RezultatDetectare.Nu("ISDP exportă .xls / .xlsx.");
        var foi = Citeste(sursa, out var eroare);
        if (foi is null)
            return RezultatDetectare.Nu(eroare!);
        var tipuri = foi.Select(f => DetectorIsdp.Detecteaza(f)).ToList();
        if (tipuri.Any(t => t.Tip == TipRaportIsdp.DevizCuResurse))
            return new RezultatDetectare(0.95, "„Deviz ofertă cu resurse” din ISDP.");
        return tipuri.Any(t => t.Tip == TipRaportIsdp.ListaCantitatiF3)
            ? new RezultatDetectare(0.6, "Lista F3 din ISDP (singură nu e un deviz complet).")
            : RezultatDetectare.Nu("Nu e un export ISDP cunoscut.");
    }

    public Task<RezultatImport<DevizCanonic>> CitesteAsync(SursaImport sursa, CancellationToken ct = default) =>
        CitesteAsync(sursa, new OptiuniImportDeviz([]), ct);

    public Task<RezultatImport<DevizCanonic>> CitesteAsync(SursaImport principal, OptiuniImportDeviz optiuni, CancellationToken ct = default)
    {
        var foi = new List<FoaieCalcul>();
        foreach (var sursa in optiuni.Anexe.Prepend(principal))
        {
            var citite = Citeste(sursa, out var eroare);
            if (citite is null)
                return Task.FromResult(RezultatImport<DevizCanonic>.Esuat($"{sursa.NumeFisier}: {eroare}"));
            foi.AddRange(citite);
        }

        return Task.FromResult(ConversieIsdp.Converteste(foi, optiuni.Curs, optiuni.Moneda));
    }

    private static IReadOnlyList<FoaieCalcul>? Citeste(SursaImport sursa, out string? eroare)
    {
        try
        {
            eroare = null;
            return CititorExcel.Citeste(sursa.Continut);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            eroare = "Fișierul Excel nu poate fi citit (deteriorat sau protejat cu parolă).";
            return null;
        }
    }
}

public static class InregistrareIsdp
{
    public static IServiceCollection AdaugaImportatorIsdp(this IServiceCollection servicii) =>
        servicii.AddSingleton<IImportatorDeviz, ImportatorIsdp>();
}
