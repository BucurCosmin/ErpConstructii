using System.Globalization;
using ErpConstructii.SharedKernel.Import;
using static ErpConstructii.Grafic.Import.Primavera.FisierXer;

namespace ErpConstructii.Grafic.Import.Primavera;

/// <summary>
/// Conversia tabelelor XER în <see cref="GraficCanonic"/>: PROJECT (data actualizării), PROJWBS (structura), TASK (activitățile),
/// TASKPRED (legăturile), TASKRSRC + RSRC și PROJCOST (resursele și cheltuielile, cu costurile), TASKACTV + ACTVTYPE + ACTVCODE
/// (codurile de activitate), TASKPROC (pașii), CALENDAR (orele pe zi, pentru durate). Duratele XER sunt în ore: se împart la
/// orele pe zi ale calendarului activității. Procentul urmează tipul de procent al activității (fizic / durată / unități).
/// Logică pură: nu aruncă excepții pentru fișiere greșite, ci întoarce probleme.
/// </summary>
public static class ConversieXer
{
    private const decimal OrePeZiImplicit = 8m;

    public static RezultatImport<GraficCanonic> Converteste(FisierXer xer)
    {
        var probleme = new List<ProblemaImport>();
        var proiecte = xer.Randuri("PROJECT");
        var activitatiToate = xer.Randuri("TASK");
        if (proiecte.Count == 0 || activitatiToate.Count == 0)
            return RezultatImport<GraficCanonic>.Esuat("Fișierul XER nu conține un proiect cu activități (tabelele PROJECT / TASK).");

        // Un XER poate conține mai multe proiecte: se ia cel cu cele mai multe activități.
        var peProiect = activitatiToate.GroupBy(t => Text(t, "proj_id")).ToDictionary(g => g.Key, g => g.ToList());
        var proiect = proiecte.OrderByDescending(p => peProiect.GetValueOrDefault(Text(p, "proj_id"))?.Count ?? 0).First();
        var proiectId = Text(proiect, "proj_id");
        if (proiecte.Count > 1)
            probleme.Add(new(Severitate.Avertisment, "fișier",
                $"Fișierul conține {proiecte.Count} proiecte; se importă „{Text(proiect, "proj_short_name")}” (cele mai multe activități)."));
        var taskuri = peProiect.GetValueOrDefault(proiectId) ?? [];

        // Calendarele: orele pe zi, pentru transformarea duratelor.
        var calendare = xer.Randuri("CALENDAR").ToDictionary(c => Text(c, "clndr_id"), c => (
            Nume: Text(c, "clndr_name"),
            Ore: Numar(c, "day_hr_cnt") is > 0 and var o ? o : OrePeZiImplicit));
        var orePeZiProiect = calendare.TryGetValue(Text(proiect, "clndr_id"), out var cp) ? cp.Ore : OrePeZiImplicit;
        decimal OrePeZi(IReadOnlyDictionary<string, string> t) => calendare.TryGetValue(Text(t, "clndr_id"), out var c) ? c.Ore : orePeZiProiect;

        // Structura WBS: nodul proiectului (proj_node_flag = Y) e rădăcina; cheia = calea codurilor de sub ea.
        var wbsProiect = xer.Randuri("PROJWBS").Where(w => Text(w, "proj_id") == proiectId).ToList();
        var radacina = wbsProiect.FirstOrDefault(w => Text(w, "proj_node_flag") == "Y");
        var radacinaId = radacina is null ? null : Text(radacina, "wbs_id");
        var copii = wbsProiect.Where(w => Text(w, "wbs_id") != radacinaId).GroupBy(w => Text(w, "parent_wbs_id"))
            .ToDictionary(g => g.Key, g => g.OrderBy(w => Numar(w, "seq_num")).ThenBy(w => Text(w, "wbs_short_name"), StringComparer.Ordinal).ToList());
        var noduri = new List<NodWbsCanonic>();
        var cheiWbs = new Dictionary<string, string>(StringComparer.Ordinal);
        var folosite = new HashSet<string>(StringComparer.Ordinal);
        void Parcurge(string parinteId, string? cheieParinte)
        {
            foreach (var w in copii.GetValueOrDefault(parinteId) ?? [])
            {
                var cod = Text(w, "wbs_short_name") is { Length: > 0 } c ? c : Text(w, "wbs_id");
                var cheie = cheieParinte is null ? cod : $"{cheieParinte}.{cod}";
                for (var i = 2; !folosite.Add(cheie); i++)
                    cheie = (cheieParinte is null ? cod : $"{cheieParinte}.{cod}") + $"~{i}";
                cheiWbs[Text(w, "wbs_id")] = cheie;
                noduri.Add(new NodWbsCanonic(cheie, cod, Text(w, "wbs_name"), cheieParinte, noduri.Count));
                Parcurge(Text(w, "wbs_id"), cheie);
            }
        }

        if (radacinaId is not null)
            Parcurge(radacinaId, null);
        else
        {
            // Fără nodul proiectului: rădăcinile sunt nodurile al căror părinte nu e în proiect.
            var ids = wbsProiect.Select(w => Text(w, "wbs_id")).ToHashSet();
            foreach (var p in wbsProiect.Select(w => Text(w, "parent_wbs_id")).Where(p => !ids.Contains(p)).Distinct())
                Parcurge(p, null);
        }

        // Codurile activităților (cheia stabilă la reimport).
        var coduriTask = new Dictionary<string, string>(StringComparer.Ordinal);
        var coduriFolosite = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in taskuri)
        {
            var cod = Text(t, "task_code") is { Length: > 0 } c ? c : Text(t, "task_id");
            if (!coduriFolosite.Add(cod))
            {
                probleme.Add(new(Severitate.Avertisment, $"activitatea {cod}", "Cod de activitate repetat: a primit un sufix."));
                var baza = cod;
                for (var i = 2; !coduriFolosite.Add(cod); i++)
                    cod = $"{baza}~{i}";
            }

            coduriTask[Text(t, "task_id")] = cod;
        }

        // Codurile de activitate (tip → cod).
        var tipuriCod = xer.Randuri("ACTVTYPE").ToDictionary(r => Text(r, "actv_code_type_id"), r => Text(r, "actv_code_type"));
        var valoriCod = xer.Randuri("ACTVCODE").ToDictionary(r => Text(r, "actv_code_id"), r => (Tip: Text(r, "actv_code_type_id"), Cod: Text(r, "short_name"), Denumire: Text(r, "actv_code_name")));
        var coduriPeTask = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var coduriUtilizate = new Dictionary<(string, string), CodActivitateCanonic>();
        foreach (var r in xer.Randuri("TASKACTV"))
        {
            if (!coduriTask.ContainsKey(Text(r, "task_id")) || !valoriCod.TryGetValue(Text(r, "actv_code_id"), out var v)
                || !tipuriCod.TryGetValue(v.Tip, out var tip))
                continue;
            if (!coduriPeTask.TryGetValue(Text(r, "task_id"), out var d))
                coduriPeTask[Text(r, "task_id")] = d = new Dictionary<string, string>(StringComparer.Ordinal);
            d[tip] = v.Cod;
            coduriUtilizate.TryAdd((tip, v.Cod), new CodActivitateCanonic(tip, v.Cod, v.Denumire));
        }

        // Legăturile: TASKPRED pe succesor.
        var taskDupaId = taskuri.ToDictionary(t => Text(t, "task_id"), StringComparer.Ordinal);
        var dependente = new Dictionary<string, List<Dependenta>>(StringComparer.Ordinal);
        var externe = 0;
        foreach (var r in xer.Randuri("TASKPRED"))
        {
            if (!taskDupaId.TryGetValue(Text(r, "task_id"), out var succesor))
                continue;
            if (!coduriTask.TryGetValue(Text(r, "pred_task_id"), out var codPred))
            {
                externe++;
                continue;
            }

            var tip = Text(r, "pred_type") switch
            {
                var s when s.StartsWith("PR_SS", StringComparison.Ordinal) => TipDependenta.SS,
                var s when s.StartsWith("PR_FF", StringComparison.Ordinal) => TipDependenta.FF,
                var s when s.StartsWith("PR_SF", StringComparison.Ordinal) => TipDependenta.SF,
                _ => TipDependenta.FS,
            };
            var decalaj = (int)Math.Round(Numar(r, "lag_hr_cnt") / OrePeZi(succesor), MidpointRounding.AwayFromZero);
            if (!dependente.TryGetValue(Text(r, "task_id"), out var l))
                dependente[Text(r, "task_id")] = l = [];
            l.Add(new Dependenta(codPred, tip, decalaj));
        }

        if (externe > 0)
            probleme.Add(new(Severitate.Informare, "legături", $"{externe} legături cu activități din alte proiecte au fost ignorate."));

        // Resursele și cheltuielile pe activitate.
        var unitati = xer.Randuri("UMEASURE").ToDictionary(u => Text(u, "unit_id"), u => Text(u, "unit_abbrev"));
        var resurseNomenclator = xer.Randuri("RSRC").ToDictionary(r => Text(r, "rsrc_id"));
        var resurse = new Dictionary<string, List<ResursaActivitate>>(StringComparer.Ordinal);
        void AdaugaResursa(string taskId, ResursaActivitate r)
        {
            if (!resurse.TryGetValue(taskId, out var l))
                resurse[taskId] = l = [];
            l.Add(r);
        }

        foreach (var r in xer.Randuri("TASKRSRC"))
        {
            if (!taskDupaId.ContainsKey(Text(r, "task_id")))
                continue;
            resurseNomenclator.TryGetValue(Text(r, "rsrc_id"), out var rs);
            var tipText = rs is null ? Text(r, "rsrc_type") : Text(rs, "rsrc_type");
            var tip = tipText switch { "RT_Mat" => TipResursaGrafic.Material, "RT_Equip" => TipResursaGrafic.Utilaj, _ => TipResursaGrafic.Manopera };
            var um = rs is not null && unitati.TryGetValue(Text(rs, "unit_id"), out var u) && u.Length > 0 ? u : tip == TipResursaGrafic.Material ? null : "h";
            AdaugaResursa(Text(r, "task_id"), new ResursaActivitate(
                rs is null ? Text(r, "rsrc_id") : Text(rs, "rsrc_short_name"),
                rs is null ? string.Empty : Text(rs, "rsrc_name"),
                tip, um,
                Numar(r, "target_qty"), Numar(r, "act_reg_qty") + Numar(r, "act_ot_qty"), Numar(r, "remain_qty"),
                Numar(r, "cost_per_qty"),
                Numar(r, "target_cost"), Numar(r, "act_reg_cost") + Numar(r, "act_ot_cost"), Numar(r, "remain_cost")));
        }

        foreach (var r in xer.Randuri("PROJCOST"))
        {
            if (!taskDupaId.ContainsKey(Text(r, "task_id")))
                continue;
            var realizat = Numar(r, "act_cost");
            var ramas = Numar(r, "remain_cost");
            var cantitate = Numar(r, "target_qty");
            // Cheltuiala nu are cantitate realizată în XER: se împarte proporțional cu costul.
            var cantRealizata = realizat + ramas > 0 ? Math.Round(cantitate * realizat / (realizat + ramas), 4) : 0;
            AdaugaResursa(Text(r, "task_id"), new ResursaActivitate(
                Text(r, "cost_name"),
                Text(r, "cost_descr") is { Length: > 0 } d ? d : Text(r, "cost_name"),
                TipResursaGrafic.Cheltuiala, Text(r, "qty_name") is { Length: > 0 } q ? q : null,
                cantitate, cantRealizata, cantitate - cantRealizata, Numar(r, "cost_per_qty"),
                Numar(r, "target_cost"), realizat, ramas));
        }

        var pasi = xer.Randuri("TASKPROC").Where(p => taskDupaId.ContainsKey(Text(p, "task_id")))
            .GroupBy(p => Text(p, "task_id"))
            .ToDictionary(g => g.Key, g => g.OrderBy(p => Numar(p, "seq_num"))
                .Select(p => new PasActivitate(Text(p, "proc_name"), Text(p, "complete_flag") == "Y")).ToList());

        // Pragul drumului critic: rezerva totală ≤ pragul proiectului (implicit 0).
        var pragCritic = Numar(proiect, "critical_drtn_hr_cnt");
        var activitati = new List<ActivitateCanonica>(taskuri.Count);
        foreach (var t in taskuri)
        {
            var id = Text(t, "task_id");
            var cod = coduriTask[id];
            var ore = OrePeZi(t);
            var tip = Text(t, "task_type") switch
            {
                "TT_Mile" => TipActivitate.JalonInceput,
                "TT_FinMile" => TipActivitate.JalonSfarsit,
                "TT_WBS" => TipActivitate.Rezumat,
                "TT_LOE" => TipActivitate.Nivel,
                _ => TipActivitate.Activitate,
            };
            var stadiu = Text(t, "status_code") switch
            {
                "TK_Complete" => StadiuActivitate.Finalizata,
                "TK_Active" => StadiuActivitate.InCurs,
                _ => StadiuActivitate.Neinceputa,
            };
            var startReal = Data(t, "act_start_date");
            var sfarsitReal = stadiu == StadiuActivitate.Finalizata ? Data(t, "act_end_date") : null;
            var startPlan = Data(t, "target_start_date") ?? Data(t, "early_start_date") ?? startReal;
            var sfarsitPlan = Data(t, "target_end_date") ?? Data(t, "early_end_date") ?? sfarsitReal;
            var start = startReal ?? Data(t, "early_start_date") ?? startPlan;
            var sfarsit = sfarsitReal ?? Data(t, "early_end_date") ?? sfarsitPlan;
            if (startPlan is null || sfarsitPlan is null || start is null || sfarsit is null)
            {
                probleme.Add(new(Severitate.Avertisment, $"activitatea {cod}", "Activitate fără date: nu a fost importată."));
                continue;
            }

            var rezerva = NumarOptional(t, "total_float_hr_cnt");
            var durata = Zile(Numar(t, "target_drtn_hr_cnt"), ore);
            activitati.Add(new ActivitateCanonica(
                cod, Text(t, "task_name"),
                cheiWbs.GetValueOrDefault(Text(t, "wbs_id")),
                startPlan.Value, sfarsitPlan.Value, durata,
                tip is TipActivitate.JalonInceput or TipActivitate.JalonSfarsit,
                dependente.GetValueOrDefault(id) ?? [],
                coduriPeTask.GetValueOrDefault(id) ?? new Dictionary<string, string>(),
                tip, stadiu, Procent(t, stadiu),
                start, sfarsit, startReal, sfarsitReal,
                Data(t, "late_start_date"), Data(t, "late_end_date"),
                stadiu == StadiuActivitate.Finalizata ? 0 : Zile(Numar(t, "remain_drtn_hr_cnt"), ore),
                stadiu == StadiuActivitate.Finalizata || rezerva is null ? null : Zile(rezerva.Value, ore),
                stadiu != StadiuActivitate.Finalizata && rezerva is not null && rezerva.Value <= pragCritic,
                calendare.TryGetValue(Text(t, "clndr_id"), out var cal) ? cal.Nume : null,
                Constrangere(t),
                resurse.GetValueOrDefault(id),
                pasi.GetValueOrDefault(id)));
        }

        var denumire = radacina is not null && Text(radacina, "wbs_name") is { Length: > 0 } n ? n : Text(proiect, "proj_short_name");
        var grafic = new GraficCanonic(
            denumire,
            Data(proiect, "last_recalc_date") ?? Data(proiect, "next_data_date"),
            activitati,
            Text(proiect, "proj_short_name"),
            xer.Moneda,
            noduri,
            coduriUtilizate.Values.OrderBy(c => c.Tip, StringComparer.Ordinal).ThenBy(c => c.Cod, StringComparer.Ordinal).ToList());
        probleme.Add(new(Severitate.Informare, "fișier",
            $"Primavera {xer.Versiune}: {noduri.Count} noduri WBS, {activitati.Count} activități, {activitati.Sum(a => a.Dependente.Count)} legături."));
        return RezultatImport<GraficCanonic>.Ok(grafic, probleme);
    }

    private static decimal Zile(decimal ore, decimal orePeZi) => Math.Round(ore / orePeZi, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Procentul după tipul activității: fizic (introdus), durată ((originală − rămasă) / originală) sau unități
    /// (realizate / totale). Terminată = 100.
    /// </summary>
    internal static decimal Procent(IReadOnlyDictionary<string, string> t, StadiuActivitate stadiu)
    {
        if (stadiu == StadiuActivitate.Finalizata)
            return 100m;
        decimal? p = Text(t, "complete_pct_type") switch
        {
            "CP_Phys" => Numar(t, "phys_complete_pct"),
            "CP_Units" => (Numar(t, "act_work_qty") + Numar(t, "act_equip_qty")) is var r
                && r + Numar(t, "remain_work_qty") + Numar(t, "remain_equip_qty") is > 0 and var total ? r / total * 100 : null,
            _ => null,
        };
        if (p is null && stadiu == StadiuActivitate.InCurs && Numar(t, "target_drtn_hr_cnt") is > 0 and var original)
            p = (original - Numar(t, "remain_drtn_hr_cnt")) / original * 100;
        return Math.Round(Math.Clamp(p ?? 0, 0, 100), 2, MidpointRounding.AwayFromZero);
    }

    private static string? Constrangere(IReadOnlyDictionary<string, string> t)
    {
        var text = Text(t, "cstr_type") switch
        {
            "CS_MSO" => "Start la",
            "CS_MSOA" => "Start nu mai devreme de",
            "CS_MSOB" => "Start nu mai târziu de",
            "CS_MEO" => "Sfârșit la",
            "CS_MEOA" => "Sfârșit nu mai devreme de",
            "CS_MEOB" => "Sfârșit nu mai târziu de",
            "CS_MANDSTART" => "Start obligatoriu",
            "CS_MANDFIN" => "Sfârșit obligatoriu",
            "CS_ALAP" => "Cât mai târziu posibil",
            "" => null,
            var alt => alt,
        };
        if (text is null)
            return null;
        return Data(t, "cstr_date") is { } d ? $"{text} {d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}" : text;
    }
}
