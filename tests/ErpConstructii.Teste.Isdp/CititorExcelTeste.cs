using System.Globalization;
using ErpConstructii.Devize.Import.Isdp;
using ErpConstructii.Devize.Import.Isdp.Excel;

namespace ErpConstructii.Teste.Isdp;

/// <summary>
/// Test de acceptanță al adaptorului Excel: fișierele originale exportate din ISDP, citite cu <see cref="CititorExcel"/>,
/// dau exact același rezultat ca fixture-urile JSON (aceleași fișiere convertite celulă cu celulă). Fișierele reale nu
/// intră în git: se caută în tests/samples-private/isdp/ (extras_de_resurse.xls, sapare_chilleri.xlsx), apoi în
/// Sabloane/devize/ (documentele reale ținute local); fără ele, testele se sar.
/// </summary>
public class CititorExcelTeste
{
    [FactCuFisiereIsdp]
    public void Devizul_xls_citit_din_Excel_e_identic_cu_fixture_ul()
    {
        var excel = DevizDinExcel();
        var json = ParserDevizResurse.Parseaza(Fixturi.Incarca(Fixturi.DevizResurse)).Valoare!;

        Assert.Equal(15, excel.Articole().Count());
        Assert.Equal(521134.09m, Math.Round(excel.Recapitulatie!.T3CuProfit!.Total, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(json.Antet, excel.Antet);
        Assert.Equal(json.Moneda, excel.Moneda);
        Assert.Equal(json.Recapitulatie, excel.Recapitulatie);
        // Nod cu nod: aceeași structură, aceleași tipuri și aceleași valori (prețuri, cantități, consumuri).
        Assert.Equal(json.Toate().Select(Descriere).ToList(), excel.Toate().Select(Descriere).ToList());

        var (_, probleme) = VerificatorDeviz.Verifica(excel);
        Assert.DoesNotContain(probleme, p => p.Severitate == SeveritateIsdp.Eroare);
    }

    [FactCuFisiereIsdp]
    public void F3_xlsx_citit_din_Excel_e_identic_cu_fixture_ul_si_reproduce_preturile()
    {
        var f3 = F3DinExcel();
        var json = ParserListaF3.Parseaza(Fixturi.Incarca(Fixturi.ListaF3)).Valoare!;

        Assert.Equal(json.TotalGeneral, f3.TotalGeneral);
        Assert.Equal(json.Articole().Select(Descriere).ToList(), f3.Articole().Select(Descriere).ToList());

        var rezultat = ReconciliereF3.Reconciliaza(DevizDinExcel(), f3);
        Assert.Equal(5.23m, rezultat.Curs);
        Assert.Equal(60, rezultat.ComponenteIdentice); // 15 articole × 4 componente
        Assert.Equal(0, rezultat.ComponenteDiferite);
        Assert.Empty(rezultat.Probleme);
    }

    private static DevizIsdp DevizDinExcel()
    {
        var foi = CititorExcel.Citeste(File.ReadAllBytes(FisiereIsdp.Deviz!));
        var foaie = Assert.Single(foi, f => DetectorIsdp.Detecteaza(f).Tip == TipRaportIsdp.DevizCuResurse);
        var r = ParserDevizResurse.Parseaza(foaie);
        Assert.NotNull(r.Valoare);
        return r.Valoare!;
    }

    private static ListaF3 F3DinExcel()
    {
        var foi = CititorExcel.Citeste(File.ReadAllBytes(FisiereIsdp.F3!));
        var foaie = Assert.Single(foi, f => DetectorIsdp.Detecteaza(f).Tip == TipRaportIsdp.ListaCantitatiF3);
        var r = ParserListaF3.Parseaza(foaie);
        Assert.DoesNotContain(r.Probleme, p => p.Severitate == SeveritateIsdp.Eroare);
        return r.Valoare!;
    }

    private static string Descriere(NodIsdp n) => string.Join("|",
        n.Wbs, n.Tip, n.Simbol, n.Denumire, n.UnitateMasura, Nr(n.Cantitate), n.PretUnitar, n.Valoare, n.TotalDeclarat,
        n.TipArticol, n.SimbolBaza, n.TipResursa, n.TipResursaDedus, Nr(n.ConsumUnitar), n.Parinte?.Wbs);

    private static string Descriere(ArticolF3 a) => string.Join("|",
        a.Numar, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretUnitar, a.Total,
        string.Join(";", a.Componente.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value.PretUnitar}/{c.Value.Total}")));

    private static string Nr(decimal v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Fișierele ISDP originale (nu sunt în git); null dacă lipsesc.</summary>
internal static class FisiereIsdp
{
    public static string? Deviz { get; } = Cauta(["tests", "samples-private", "isdp", "extras_de_resurse.xls"], ["Sabloane", "devize", "extras de resurse.xls"]);

    public static string? F3 { get; } = Cauta(["tests", "samples-private", "isdp", "sapare_chilleri.xlsx"], ["Sabloane", "devize", "sapare chilleri.xlsx"]);

    private static string? Cauta(params string[][] cai)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var cale in cai)
            {
                var candidat = Path.Combine([dir.FullName, .. cale]);
                if (File.Exists(candidat))
                {
                    return candidat;
                }
            }
        }

        return null;
    }
}

/// <summary>Test sărit când fișierele ISDP originale lipsesc (ex. pe alt calculator sau în CI).</summary>
public sealed class FactCuFisiereIsdpAttribute : FactAttribute
{
    public FactCuFisiereIsdpAttribute()
    {
        if (FisiereIsdp.Deviz is null || FisiereIsdp.F3 is null)
        {
            Skip = "Fișierele ISDP originale lipsesc: pune-le în tests/samples-private/isdp/ (extras_de_resurse.xls, sapare_chilleri.xlsx).";
        }
    }
}
