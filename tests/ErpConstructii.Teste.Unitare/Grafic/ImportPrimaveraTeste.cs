using System.Text;
using ErpConstructii.Grafic;
using ErpConstructii.Grafic.Import.Primavera;
using ErpConstructii.SharedKernel.Import;
using Xunit.Abstractions;

namespace ErpConstructii.Teste.Unitare.Grafic;

/// <summary>Un XER mic, scris de mână: structura, activitățile, legăturile, resursele, codurile și pașii.</summary>
internal static class XerTest
{
    private static string T(params string[] campuri) => string.Join('\t', campuri);

    public static string Text => string.Join("\r\n",
        T("ERMHDR", "15.2", "2026-11-16", "Project", "ADMIN", "test", "db", "Project Management", "EUR"),
        T("%T", "PROJECT"),
        T("%F", "proj_id", "proj_short_name", "clndr_id", "last_recalc_date", "critical_drtn_hr_cnt"),
        T("%R", "1", "P1-A", "100", "2026-11-13 00:00", "0"),
        T("%T", "CALENDAR"),
        T("%F", "clndr_id", "clndr_name", "day_hr_cnt"),
        T("%R", "100", "Standard 5 zile", "8"),
        T("%T", "PROJWBS"),
        T("%F", "wbs_id", "proj_id", "parent_wbs_id", "seq_num", "wbs_short_name", "wbs_name", "proj_node_flag"),
        T("%R", "1", "1", "99", "0", "P1-A", "Pachet A", "Y"),
        T("%R", "2", "1", "1", "2", "B5", "Clădirea 5", "N"),
        T("%R", "3", "1", "2", "1", "FUND", "Fundații", "N"),
        T("%R", "4", "1", "1", "1", "M", "Jaloane", "N"),
        T("%T", "TASK"),
        T("%F", "task_id", "proj_id", "wbs_id", "clndr_id", "task_code", "task_name", "task_type", "status_code", "complete_pct_type", "phys_complete_pct",
            "target_drtn_hr_cnt", "remain_drtn_hr_cnt", "total_float_hr_cnt", "target_start_date", "target_end_date", "act_start_date", "act_end_date",
            "early_start_date", "early_end_date", "act_work_qty", "remain_work_qty", "act_equip_qty", "remain_equip_qty", "cstr_type", "cstr_date"),
        T("%R", "10", "1", "3", "100", "A100", "Săpătură generală", "TT_Task", "TK_Complete", "CP_Drtn", "0", "80", "0", "", "2026-03-02 08:00",
            "2026-03-13 17:00", "2026-03-03 08:00", "2026-03-16 17:00", "2026-11-13 00:00", "2026-11-13 00:00", "0", "0", "0", "0", "", ""),
        T("%R", "11", "1", "3", "100", "A110", "Turnare beton", "TT_Task", "TK_Active", "CP_Drtn", "0", "80", "20", "0", "2026-03-16 08:00",
            "2026-03-27 17:00", "2026-03-17 08:00", "", "2026-11-13 08:00", "2026-11-16 17:00", "0", "0", "0", "0", "", ""),
        T("%R", "12", "1", "4", "100", "M1", "Finalizare", "TT_FinMile", "TK_NotStart", "CP_Phys", "0", "0", "0", "16", "2026-04-01 17:00",
            "2026-04-01 17:00", "", "", "2026-12-01 17:00", "2026-12-01 17:00", "0", "0", "0", "0", "CS_MEOB", "2026-12-15 17:00"),
        T("%R", "13", "1", "3", "100", "A120", "Armare", "TT_Task", "TK_Active", "CP_Units", "0", "40", "10", "40", "2026-03-20 08:00",
            "2026-03-26 17:00", "2026-04-01 08:00", "", "2026-11-13 08:00", "2026-11-20 17:00", "30", "10", "0", "0", "", ""),
        T("%T", "TASKPRED"),
        T("%F", "task_id", "pred_task_id", "pred_type", "lag_hr_cnt"),
        T("%R", "11", "10", "PR_FS", "8"),
        T("%R", "12", "11", "PR_FF", "0"),
        T("%R", "11", "999", "PR_FS", "0"),
        T("%T", "UMEASURE"),
        T("%F", "unit_id", "unit_abbrev"),
        T("%R", "7", "mc"),
        T("%T", "RSRC"),
        T("%F", "rsrc_id", "rsrc_short_name", "rsrc_name", "rsrc_type", "unit_id"),
        T("%R", "50", "BET", "Beton C25/30", "RT_Mat", "7"),
        T("%T", "TASKRSRC"),
        T("%F", "task_id", "rsrc_id", "target_qty", "act_reg_qty", "act_ot_qty", "remain_qty", "cost_per_qty", "target_cost", "act_reg_cost", "act_ot_cost", "remain_cost"),
        T("%R", "11", "50", "100", "60", "0", "40", "50", "5000", "3000", "0", "2000"),
        T("%T", "PROJCOST"),
        T("%F", "task_id", "cost_name", "cost_descr", "qty_name", "target_qty", "cost_per_qty", "target_cost", "act_cost", "remain_cost"),
        T("%R", "10", "TRANSP", "Transport pământ", "", "10", "100", "1000", "1000", "0"),
        T("%T", "ACTVTYPE"),
        T("%F", "actv_code_type_id", "actv_code_type"),
        T("%R", "5", "Area"),
        T("%T", "ACTVCODE"),
        T("%F", "actv_code_id", "actv_code_type_id", "short_name", "actv_code_name"),
        T("%R", "7", "5", "B5", "Clădirea 5"),
        T("%T", "TASKACTV"),
        T("%F", "task_id", "actv_code_type_id", "actv_code_id"),
        T("%R", "10", "5", "7"),
        T("%T", "TASKPROC"),
        T("%F", "task_id", "seq_num", "proc_name", "complete_flag"),
        T("%R", "11", "2", "Turnare", "N"),
        T("%R", "11", "1", "Cofrare", "Y"),
        "%E");

    public static byte[] Cp1250()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250).GetBytes(Text);
    }

    public static GraficCanonic Citeste(byte[]? continut = null)
    {
        var r = new ImportatorPrimavera().CitesteAsync(new SursaImport("test.xer", continut ?? Cp1250())).Result;
        Assert.False(r.AreErori, string.Join("; ", r.Probleme.Select(p => p.Mesaj)));
        return r.Valoare!;
    }
}

public class ImportPrimaveraTeste
{
    [Fact]
    public void Proiectul_da_denumirea_codul_moneda_si_data_actualizarii()
    {
        var g = XerTest.Citeste();

        Assert.Equal("Pachet A", g.Denumire);          // numele nodului WBS al proiectului
        Assert.Equal("P1-A", g.CodSursa);
        Assert.Equal("EUR", g.Moneda);                 // ultimul câmp din ERMHDR
        Assert.Equal(new DateOnly(2026, 11, 13), g.DataReferinta);
    }

    [Fact]
    public void Fisierul_in_windows_1250_si_cel_in_utf8_dau_aceleasi_diacritice()
    {
        Assert.Equal("Săpătură generală", XerTest.Citeste().Activitati.Single(a => a.Cod == "A100").Denumire);
        Assert.Equal("Săpătură generală", XerTest.Citeste(Encoding.UTF8.GetBytes(XerTest.Text)).Activitati.Single(a => a.Cod == "A100").Denumire);
    }

    [Fact]
    public void Structura_wbs_are_cheile_caii_si_ordinea_din_primavera()
    {
        var g = XerTest.Citeste();

        // Nodul proiectului nu apare; „M” are seq 1, „B5” seq 2; copiii după părinte.
        Assert.Equal(["M", "B5", "B5.FUND"], g.Wbs!.Select(n => n.Cheie));
        Assert.Equal("B5", g.Wbs!.Single(n => n.Cheie == "B5.FUND").CheieParinte);
        Assert.Equal("B5.FUND", g.Activitati.Single(a => a.Cod == "A110").CodWbs);
    }

    [Fact]
    public void Activitatea_terminata_are_datele_reale_si_100_procent()
    {
        var a = XerTest.Citeste().Activitati.Single(a => a.Cod == "A100");

        Assert.Equal(StadiuActivitate.Finalizata, a.Stadiu);
        Assert.Equal(100m, a.Procent);
        Assert.Equal(new DateOnly(2026, 3, 3), a.Start);       // start real, nu cel calculat (= data actualizării)
        Assert.Equal(new DateOnly(2026, 3, 16), a.Sfarsit);
        Assert.Equal(new DateOnly(2026, 3, 2), a.StartPlanificat);
        Assert.Equal(10m, a.DurataZile);                       // 80 h / 8 h pe zi
        Assert.Null(a.RezervaTotalaZile);
        Assert.False(a.Critica);
        Assert.Equal("B5", a.CoduriActivitate["Area"]);
    }

    [Fact]
    public void Activitatea_in_curs_are_procentul_dupa_durata_si_e_critica_fara_rezerva()
    {
        var a = XerTest.Citeste().Activitati.Single(a => a.Cod == "A110");

        Assert.Equal(75m, a.Procent);                          // (80 − 20) / 80
        Assert.True(a.Critica);                                // rezerva 0 ≤ pragul 0
        Assert.Equal(new DateOnly(2026, 3, 17), a.Start);
        Assert.Equal(new DateOnly(2026, 11, 16), a.Sfarsit);   // sfârșitul prognozat
        Assert.Equal(2.5m, a.DurataRamasaZile);                // 20 h / 8
        var d = Assert.Single(a.Dependente);                   // legătura cu alt proiect e ignorată
        Assert.Equal(new Dependenta("A100", TipDependenta.FS, 1), d);   // decalaj 8 h = 1 zi
        Assert.Equal(["Cofrare", "Turnare"], a.Pasi!.Select(p => p.Denumire));
        Assert.True(a.Pasi![0].Finalizat);
    }

    [Fact]
    public void Procentul_dupa_unitati_vine_din_cantitatile_realizate()
    {
        Assert.Equal(75m, XerTest.Citeste().Activitati.Single(a => a.Cod == "A120").Procent);   // 30 / (30 + 10)
    }

    [Fact]
    public void Jalonul_are_constrangerea_si_rezerva_in_zile()
    {
        var m = XerTest.Citeste().Activitati.Single(a => a.Cod == "M1");

        Assert.True(m.EsteJalon);
        Assert.Equal(TipActivitate.JalonSfarsit, m.Tip);
        Assert.Equal(2m, m.RezervaTotalaZile);                 // 16 h / 8
        Assert.False(m.Critica);
        Assert.Equal("Sfârșit nu mai târziu de 15.12.2026", m.Constrangere);
        Assert.Equal(TipDependenta.FF, Assert.Single(m.Dependente).Tip);
    }

    [Fact]
    public void Resursele_si_cheltuielile_dau_costurile_activitatii()
    {
        var g = XerTest.Citeste();
        var beton = Assert.Single(g.Activitati.Single(a => a.Cod == "A110").Resurse!);
        Assert.Equal(("BET", "Beton C25/30", TipResursaGrafic.Material, "mc"), (beton.Cod, beton.Denumire, beton.Tip, beton.UnitateMasura));
        Assert.Equal((100m, 60m, 40m), (beton.CantitatePlanificata, beton.CantitateRealizata, beton.CantitateRamasa));

        var sapatura = g.Activitati.Single(a => a.Cod == "A100");
        Assert.Equal(TipResursaGrafic.Cheltuiala, Assert.Single(sapatura.Resurse!).Tip);
        Assert.Equal((1000m, 1000m, 0m), (sapatura.CostPlanificat, sapatura.CostRealizat, sapatura.CostRamas));
        Assert.Equal(10m, sapatura.Resurse![0].CantitateRealizata);    // cheltuiala realizată integral: 10 × 1000 / 1000
    }

    [Fact]
    public async Task Fisierul_care_nu_e_xer_da_eroare_fara_exceptie()
    {
        var r = await new ImportatorPrimavera().CitesteAsync(new SursaImport("x.xer", "nu e XER"u8.ToArray()));
        Assert.True(r.AreErori);
    }
}

public class AnalizaGraficTeste
{
    [Fact]
    public void Nodul_wbs_aduna_costurile_si_pondereaza_procentul_cu_costul()
    {
        var g = XerTest.Citeste();
        var t = AnalizaGrafic.Totalizeaza(g.Wbs!, g.Activitati);

        var b5 = t["B5"];                                      // A100, A110, A120 (sub B5.FUND)
        Assert.Equal(6000m, b5.CostPlanificat);                // 1.000 + 5.000 + 0
        Assert.Equal(4000m, b5.CostRealizat);                  // 1.000 + 3.000
        // (1.000 × 100 + 5.000 × 75 + 0 × 75) / 6.000 = 475.000 / 6.000 = 79,1666…
        Assert.Equal(79.17m, b5.Procent);
        Assert.Equal((3, 1, 2, 1), (b5.Activitati, b5.Finalizate, b5.InCurs, b5.Critice));
        Assert.Equal(new DateOnly(2026, 3, 3), b5.Start);
        Assert.Equal(new DateOnly(2026, 11, 20), b5.Sfarsit);  // A120 se termină ultima
        Assert.Equal(t["B5"], t["B5.FUND"]);
    }

    [Fact]
    public void Fara_costuri_procentul_se_pondereaza_cu_durata()
    {
        ActivitateCanonica[] a =
        [
            Activitate("X", 30m, 100m),
            Activitate("Y", 10m, 0m),
        ];
        Assert.Equal(75m, AnalizaGrafic.Total(a).Procent);     // (30 × 100 + 10 × 0) / 40
    }

    internal static ActivitateCanonica Activitate(string cod, decimal durata, decimal procent, DateOnly? sfarsit = null,
        StadiuActivitate stadiu = StadiuActivitate.InCurs) =>
        new(cod, $"Activitatea {cod}", null, new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 5), durata, false, [], new Dictionary<string, string>(),
            Stadiu: stadiu, Procent: procent, Start: new DateOnly(2026, 1, 5), Sfarsit: sfarsit ?? new DateOnly(2026, 2, 5));
}

public class ComparareGraficeTeste
{
    private static readonly DateOnly Mai10 = new(2026, 5, 10);

    [Fact]
    public void Comparatia_gaseste_activitatile_noi_sterse_si_intarziate()
    {
        ActivitateCanonica[] vechi =
        [
            AnalizaGraficTeste.Activitate("A", 10, 50, Mai10),
            AnalizaGraficTeste.Activitate("B", 10, 0, Mai10.AddDays(20)),
            AnalizaGraficTeste.Activitate("C", 10, 0, Mai10),
        ];
        ActivitateCanonica[] nou =
        [
            AnalizaGraficTeste.Activitate("A", 10, 100, Mai10.AddDays(5), StadiuActivitate.Finalizata),
            AnalizaGraficTeste.Activitate("B", 10, 0, Mai10.AddDays(20)),
            AnalizaGraficTeste.Activitate("D", 10, 0, Mai10.AddDays(30)),
        ];

        var c = ComparareGrafice.Compara(vechi, nou);

        Assert.Equal("D", Assert.Single(c.Noi).Cod);
        Assert.Equal("C", Assert.Single(c.Sterse).Cod);
        var a = Assert.Single(c.Modificate);                   // B e neschimbată
        Assert.Equal(("A", 5, 50m, 100m), (a.Cod, a.DecalajSfarsitZile!.Value, a.ProcentAnterior, a.Procent));
        Assert.Null(a.DecalajStartZile);
        Assert.Equal((1, 1), (c.Finalizate, c.Intarziate));
        Assert.Equal(10, c.DecalajSfarsitProiectZile);         // 30 mai (B) → 9 iunie (D)
    }
}

/// <summary>Exportul real din Sabloane/grafic executie (nu intră în git): testul se sare fără el.</summary>
public sealed class FactCuXerRealAttribute : FactAttribute
{
    public FactCuXerRealAttribute()
    {
        if (XerReal.Fisier() is null)
            Skip = "Exportul real (Sabloane/grafic executie/*.xer) lipsește: testul rulează doar local.";
    }
}

internal static class XerReal
{
    public static string? Fisier()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "Sabloane", "grafic executie");
            if (Directory.Exists(c) && Directory.GetFiles(c, "*.xer").FirstOrDefault() is { } f)
                return f;
        }

        return null;
    }
}

public class ImportPrimaveraRealTeste(ITestOutputHelper iesire)
{
    [FactCuXerReal]
    public async Task Exportul_real_se_citeste_complet()
    {
        var r = await new ImportatorPrimavera().CitesteAsync(new SursaImport("real.xer", await File.ReadAllBytesAsync(XerReal.Fisier()!)));
        foreach (var p in r.Probleme)
            iesire.WriteLine($"{p.Severitate} {p.Locatie}: {p.Mesaj}");
        Assert.False(r.AreErori);
        var g = r.Valoare!;
        var total = AnalizaGrafic.Total(g.Activitati);
        iesire.WriteLine($"{g.Denumire} | {g.CodSursa} | {g.Moneda} | {g.DataReferinta} | WBS {g.Wbs!.Count} | activități {g.Activitati.Count} | " +
                         $"plan {total.CostPlanificat:N2} realizat {total.CostRealizat:N2} rămas {total.CostRamas:N2} | {total.Procent}% | {total.Start}–{total.Sfarsit} | critice {total.Critice}");
        Assert.Equal(1189, g.Activitati.Count);
        Assert.Equal(423, g.Wbs.Count);                        // 424 cu nodul proiectului
        Assert.All(g.Activitati, a => Assert.True(a.CodWbs is null || g.Wbs.Any(n => n.Cheie == a.CodWbs)));
        // TASKRSRC 25.812.339,9323 + PROJCOST 4.641.702,4128 = 30.454.042,3451; sfârșitul = scd_end_date din PROJECT.
        Assert.Equal(30454042.35m, Math.Round(total.CostPlanificat, 2));
        Assert.Equal(new DateOnly(2027, 8, 26), total.Sfarsit);
    }
}
