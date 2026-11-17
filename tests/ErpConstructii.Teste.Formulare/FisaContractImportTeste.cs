using ErpConstructii.Contracte;
using ErpConstructii.Contracte.Import;
using ErpConstructii.Import.Xlsx;
using ErpConstructii.SharedKernel;
using Xunit;
using Xunit.Abstractions;

namespace ErpConstructii.Teste.Formulare;

/// <summary>Fișele reale de urmărire a contractelor (Sabloane/contracte, nu intră în git): testele se sar fără ele.</summary>
public sealed class FactCuFiseContractAttribute : FactAttribute
{
    public FactCuFiseContractAttribute()
    {
        if (FiseContract.Folder() is null)
            Skip = "Fișele reale (Sabloane/contracte) lipsesc: testul rulează doar local.";
    }
}

internal static class FiseContract
{
    public static string? Folder()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "Sabloane", "contracte");
            if (Directory.Exists(c) && Directory.GetFiles(c, "*.xlsx").Length > 0)
                return c;
        }

        return null;
    }

    public static FisaCitita Citeste(string fisier)
    {
        using var s = File.OpenRead(Path.Combine(Folder()!, fisier));
        var foi = CititorFoiXlsx.Citeste(s) ?? throw new InvalidOperationException("Fișierul nu e .xlsx");
        return InterpretorFisaContract.Citeste(foi, "GENERAL CONCRETE CERNAVODĂ SRL", "RO123");
    }
}

/// <summary>Importul fișelor de urmărire (D-56, etapa 3), verificat pe cele trei fișe reale: Züblin, HT Clima, AGER-ROM.</summary>
public class FisaContractImportTeste(ITestOutputHelper iesire)
{
    private void Arata(FisaCitita f)
    {
        iesire.WriteLine($"nr={f.NumarExtern.Valoare} data={f.DataSemnarii.Valoare} partener={f.PartenerDenumire.Valoare} cui={f.PartenerCui.Valoare} moneda={f.Moneda.Valoare} valoare={f.Valoare.Valoare}");
        iesire.WriteLine($"perioada={f.DataInceput}-{f.DataSfarsit} termenPlata={f.TermenPlataZile} garantieBE={f.ProcentGarantie}");
        iesire.WriteLine($"membri: {string.Join(" | ", f.Conditii.Membri.Select(m => $"{m.Denumire} {m.Cota} {(m.EsteFirmaActiva ? "*" : "")}"))}");
        iesire.WriteLine($"transe: {string.Join(" + ", f.Conditii.TranseAvans.Select(t => t.Procent))}; gar avans {f.Conditii.ProcentGarantieAvans} tehnica {f.Conditii.ProcentGarantieTehnica}/{f.Conditii.LuniGarantieTehnica} pen {f.Conditii.PenalitateExecutieZi}/{f.Conditii.PlafonPenalitateExecutie} plata {f.Conditii.PenalitatePlataZi}/{f.Conditii.PlafonPenalitatePlata}");
        iesire.WriteLine($"acte: {string.Join(" | ", f.Acte.Select(a => $"{a.NumarExtern} {a.Data} {a.Diferenta} semnat={a.Semnat}"))}");
        iesire.WriteLine($"obligatii {f.Obligatii.Count}, actiuni {f.Actiuni.Count}, faze {f.Faze.Count}, riscuri {f.Riscuri.Count}, instrumente {f.Instrumente.Count}, surse {f.Surse.Count}");
        foreach (var x in f.Facturi)
            iesire.WriteLine($"  F {x.Membru[..Math.Min(12, x.Membru.Length)]} {x.Numar} {x.Data} {x.Tip} T{x.TransaAvans} val={x.Valoare} storno={x.StornoAvans} curs={x.Curs} net={x.NetRon} tva={x.TvaRon} tot={x.TotalRon} scad={x.Scadenta}");
        foreach (var p in f.Plati)
            iesire.WriteLine($"  P {p.NumarFactura} {p.Plata.Data} {p.Plata.SumaRon} {p.Plata.Instrument} conf={p.Plata.Confirmata}");
        foreach (var i in f.Instrumente)
            iesire.WriteLine($"  I {i.Tip} {i.Numar} {i.Emitent} {i.Beneficiar} {i.Valoare} {i.Moneda} {i.DataEmitere} {i.DataExpirare}");
        foreach (var n in f.Note)
            iesire.WriteLine($"  NOTA {n}");
        iesire.WriteLine($"obs: {f.Observatii}");
    }

    [FactCuFiseContract]
    public void Fisa_Zublin()
    {
        var f = FiseContract.Citeste("Fisa Contract Zublin.xlsx");
        Arata(f);

        Assert.Equal(("247", new DateOnly(2025, 10, 23), "ZÜBLIN ROMANIA SRL", "15646620", "EUR", 2_864_813.83m),
            (f.NumarExtern.Valoare, f.DataSemnarii.Valoare, f.PartenerDenumire.Valoare, f.PartenerCui.Valoare, f.Moneda.Valoare, f.Valoare.Valoare));
        // 9 facturi GCC, lucrări 1.552.199,16 EUR, storno avans 144.487,79 EUR, total RON 8.759.547,57 (ca rândul TOTAL din fișă).
        Assert.Equal(9, f.Facturi.Count);
        Assert.Equal((1_552_199.16m, 144_487.79m, 8_759_547.57m), (f.Facturi.Sum(x => x.Valoare), f.Facturi.Sum(x => x.StornoAvans), f.Facturi.Sum(x => x.TotalRon)));
        Assert.All(f.Facturi, x => Assert.Equal("GENERAL CONCRETE CERNAVODĂ SRL", x.Membru));
        Assert.Equal([0.10m], f.Conditii.TranseAvans.Select(t => t.Procent));
        Assert.Equal(45, f.TermenPlataZile);
        Assert.Equal(13, f.Obligatii.Count);
        Assert.Equal((0.001m, 0.05m), (f.Conditii.PenalitateExecutieZi, f.Conditii.PlafonPenalitateExecutie));
    }

    [FactCuFiseContract]
    public void Fisa_HT_Clima()
    {
        var f = FiseContract.Citeste("Fisa Contract HT Clima SA.xlsx");
        Arata(f);

        Assert.Equal(("87", new DateOnly(2026, 4, 30), "HIGH-TECH CLIMA SA", "RO16645925", 3_501_265.27m),
            (f.NumarExtern.Valoare, f.DataSemnarii.Valoare, f.PartenerDenumire.Valoare, f.PartenerCui.Valoare, f.Valoare.Valoare));
        Assert.Equal(2, f.Conditii.Membri.Count);
        Assert.True(f.Conditii.Membri.Single(m => m.Denumire.StartsWith("GENERAL")).EsteFirmaActiva);
        Assert.Equal([0.05m, 0.10m, 0.05m], f.Conditii.TranseAvans.Select(t => t.Procent));
        // AA1 nesemnat: +26.439,26 EUR, propus.
        var aa = Assert.Single(f.Acte);
        Assert.Equal((26_439.26m, false), (aa.Diferenta, aa.Semnat));
        Assert.Equal(2, f.Facturi.Count);
        Assert.All(f.Facturi, x => Assert.Equal((TipFacturaContract.Avans, 175_063.26m), (x.Tip, x.Valoare)));
        Assert.Equal(2, f.Instrumente.Count);
        Assert.All(f.Instrumente, i => Assert.Equal((TipInstrument.GarantieAvans, new DateOnly(2027, 4, 30)), (i.Tip, i.DataExpirare!.Value)));
        Assert.Equal((0.05m, 60), (f.ProcentGarantie, f.Conditii.LuniGarantieTehnica!.Value));
        Assert.Equal((0.0013m, 0.10m, 0.0013m, 0.10m), (f.Conditii.PenalitateExecutieZi, f.Conditii.PlafonPenalitateExecutie, f.Conditii.PenalitatePlataZi, f.Conditii.PlafonPenalitatePlata));
        Assert.Equal(35, f.TermenPlataZile);
        Assert.Equal((6, 6, 12), (f.Actiuni.Count, f.Surse.Count, f.Obligatii.Count));
    }

    [FactCuFiseContract]
    public void Fisa_AGER_ROM()
    {
        var f = FiseContract.Citeste("Fisa Contract AGER ROM.xlsx");
        Arata(f);

        Assert.Equal(("95", new DateOnly(2026, 4, 4), "RO21909144", 2_392_036m), (f.NumarExtern.Valoare, f.DataSemnarii.Valoare, f.PartenerCui.Valoare, f.Valoare.Valoare));
        Assert.Equal((new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 7)), (f.DataInceput!.Value, f.DataSfarsit!.Value));
        Assert.Equal([0.25m, 0.25m], f.Conditii.TranseAvans.Select(t => t.Procent));
        // 7 facturi cu număr și sume (ARM0011843 nu are sume: factura lipsește din dosar).
        Assert.Contains(f.Facturi, x => x.Numar == "ARM0011844" && x.Tip == TipFacturaContract.Avans && x.TotalRon == 1_860_243.64m);
        // Plățile: ARM0011844 achitată (confirmată), cele două bilete la ordin emise (neconfirmate).
        Assert.Single(f.Plati, p => p.Plata.Confirmata && p.NumarFactura == "ARM0011844");
        Assert.Equal(2, f.Plati.Count(p => !p.Plata.Confirmata && p.Plata.Instrument == InstrumentPlata.BiletLaOrdin));
        Assert.Equal(4, f.Instrumente.Count);
        Assert.Equal(6, f.Faze.Count);
    }

    [Fact]
    public void Numerele_si_datele_din_foi_se_citesc_in_ambele_scrieri()
    {
        Assert.Equal(175_063.26m, ValoriFoaie.Numar("175.063,26"));
        Assert.Equal(1_860_243.64m, ValoriFoaie.Numar("1.860.243,64"));
        Assert.Equal(2_864_813.83m, ValoriFoaie.Numar("2864813.83"));
        Assert.Equal(350_126.52m, ValoriFoaie.Numar("350.126,52 €"));
        Assert.Equal(new DateOnly(2026, 4, 30), ValoriFoaie.Data("87/30.04.2026"));
        Assert.Equal([0.05m, 0.10m, 0.05m], ValoriFoaie.Procente("TCS: 5% + 10% + 5%"));
        Assert.Equal([0.0013m, 0.10m], ValoriFoaie.Procente("0,13%/zi din valoarea contractului; plafon 10%"));
    }
}
