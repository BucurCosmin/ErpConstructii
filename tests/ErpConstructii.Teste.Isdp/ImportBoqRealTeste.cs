using System.Globalization;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import;
using ErpConstructii.Devize.Import.Boq;
using ErpConstructii.SharedKernel.Import;
using Xunit.Abstractions;

namespace ErpConstructii.Teste.Isdp;

/// <summary>Ofertele BOQ reale (Sabloane/devize/*_OFFER_*.xlsx, nu sunt în git): testele se sar fără ele.</summary>
public sealed class FactCuBoqRealAttribute : FactAttribute
{
    public FactCuBoqRealAttribute()
    {
        if (BoqReal.Fisiere().Count == 0)
            Skip = "Ofertele BOQ reale (Sabloane/devize/*_OFFER_*.xlsx) lipsesc: testul rulează doar local.";
    }
}

internal static class BoqReal
{
    public static IReadOnlyList<string> Fisiere()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "Sabloane", "devize");
            if (Directory.Exists(c) && Directory.GetFiles(c, "*_OFFER_*.xlsx") is { Length: > 0 } f)
                return f.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        return [];
    }
}

/// <summary>Importul ofertelor BOQ (D-58) pe cele patru fișiere reale ale pachetului: B5, B6, B10, PCA16A.</summary>
public class ImportBoqRealTeste(ITestOutputHelper iesire)
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    [FactCuBoqReal]
    public async Task Ofertele_reale_se_citesc_cu_totalurile_rezumatului()
    {
        var alegere = new Dictionary<string, TipResursa> { ["C"] = TipResursa.Utilaj };
        foreach (var f in BoqReal.Fisiere())
        {
            var sursa = new SursaImport(Path.GetFileName(f), await File.ReadAllBytesAsync(f));
            var importator = new ImportatorBoq();
            Assert.True(importator.PoateCiti(sursa).Incredere >= AlegereImportator.PragIncredere);
            var r = await importator.CitesteAsync(sursa, new OptiuniImportDeviz([], Componente: alegere));
            var d = r.Valoare!;
            iesire.WriteLine($"#### {sursa.NumeFisier}: {d.Denumire} | {d.Moneda} | articole {d.Articole().Count()} | oferta {d.Articole().Sum(a => a.ValoareOferta?.Total ?? 0).ToString("N2", Ro)}");
            foreach (var s in d.Sectiuni!)
                iesire.WriteLine($"   {s.Cod,-22} {s.Denumire,-28} [{s.Sursa}] art {s.Articole,4}  calc {s.Calculat,16:N2}  foaie {s.Declarat,16:N2}  rezumat {s.DeclaratRezumat,16:N2}");
            foreach (var i in d.Ignorate!)
                iesire.WriteLine($"   ignorat {i.Sursa}: {i.Motiv}");
            foreach (var p in r.Probleme.Concat(ValidatorDevizCanonic.Valideaza(d)).GroupBy(p => (p.Severitate, p.Mesaj.Length > 60 ? p.Mesaj[..60] : p.Mesaj)).Take(25))
                iesire.WriteLine($"   {p.Key.Severitate} x{p.Count()} {p.First().Locatie}: {p.First().Mesaj}");
            Assert.False(r.AreErori, string.Join("; ", r.Probleme.Where(p => p.Severitate == Severitate.Eroare).Select(p => p.Mesaj)));
        }
    }
}
