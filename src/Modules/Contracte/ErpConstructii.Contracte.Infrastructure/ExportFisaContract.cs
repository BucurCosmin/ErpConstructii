using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte.Infrastructure;

/// <summary>
/// Fișa de urmărire a contractului ca .xlsx (D-56, etapa 4), în formatul fișelor folosite azi: foile 1–8 (date generale,
/// valoare, obligații, facturi / plăți, garanții și riscuri, acțiuni, surse, termene de execuție) plus reconcilierea pe
/// antreprenor și istoricul corecturilor; titlul bleumarin, antetele albastru deschis, rândurile nesemnate / propuse pe
/// galben, sumele în moneda contractului, totalurile ca formule SUM (cu valoarea salvată). Fișa exportată se reimportă
/// cu <see cref="Import.InterpretorFisaContract"/> și dă același contract.
/// </summary>
internal static class ExportFisaContract
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    // Stilurile (indicii din Stiluri()).
    private const uint Normal = 0, Titlu = 1, Antet = 2, Propus = 3, Aldin = 4, Bani = 5, BaniPropus = 6, BaniAldin = 7, Lei = 8, LeiAldin = 9, Curs = 10;

    public static byte[] Genereaza(ContractDto c, DateOnly azi)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var carte = doc.AddWorkbookPart();
            carte.Workbook = new Workbook(new Sheets());
            carte.AddNewPart<WorkbookStylesPart>().Stylesheet = Stiluri(c.Moneda);
            uint id = 1;
            void Adauga(Foaie f)
            {
                var parte = carte.AddNewPart<WorksheetPart>();
                parte.Worksheet = f.Construieste();
                carte.Workbook.Sheets!.AppendChild(new Sheet { Id = carte.GetIdOfPart(parte), SheetId = id++, Name = f.Nume });
            }

            Adauga(DateGenerale(c, azi));
            Adauga(Valoare(c));
            Adauga(Obligatii(c));
            Adauga(Facturi(c));
            Adauga(Garantii(c));
            Adauga(Actiuni(c));
            Adauga(Surse(c));
            Adauga(Termene(c));
            Adauga(Reconciliere(c));
            if (c.Fisa?.Corecturi.Count > 0)
                Adauga(Corecturi(c));
            carte.Workbook.Save();
        }

        return ms.ToArray();
    }

    // ---------------------------------------------------------------- foile

    private static Foaie DateGenerale(ContractDto c, DateOnly azi)
    {
        var f = new Foaie("1. Date Generale", 31, 68, 76);
        f.PuneTitlu($"FIȘĂ DE URMĂRIRE CONTRACT — {c.PartenerDenumire.ToUpper(Ro)}", 3);
        f.Gol();
        f.PuneAntet("Indicator", "Informații", "Observații");
        var k = c.Conditii;
        var membri = k?.Membri ?? [];
        f.Rand(T("Contract"), T($"{c.NumarExtern ?? c.Numar} / {c.DataSemnarii:dd.MM.yyyy}"), T(c.Stare == "Ciorna" ? "Ciornă în aplicație (de verificat)" : $"Contract {c.Stare.ToLower(Ro)}; nr. intern {c.Numar}"));
        f.Rand(T("Subantreprenor"), T(c.PartenerDenumire), T(c.PartenerCui is null ? null : $"CUI {c.PartenerCui}"));
        if (membri.Count > 0)
            f.Rand(T("Antreprenori"), T(string.Join(" și ", membri.Select(m => m.Denumire))),
                T((membri.Count > 1 ? "Asociere; " : string.Empty) + string.Join(", ", membri.Select(m => $"{m.Denumire} {Procent(m.Cota)}"))));
        f.Rand(T("Proiect"), T(c.ProiectCod), T(null));
        f.Rand(T("Obiect"), T(c.Obiect), T(null));
        if (k?.Amplasament is { } a) f.Rand(T("Clădiri"), T(a), T(null));
        f.Rand(T("Tip contract"), T(k?.TipPret ?? (c.Linii.Count > 0 ? "Prețuri unitare (pe poziții)" : "Preț global")), T(null));
        f.Rand(T("Monedă"), T($"{c.Moneda} fără TVA"), T(c.Moneda != "RON" ? "Facturare în RON la cursul BNR de pe factură" : null));
        f.Rand(T("Data fișei"), T(azi.ToString("dd.MM.yyyy")), T("Generată din aplicație"));
        f.Rand(T("Valoare inițială"), N(c.Valoare), T("Fără TVA"));
        if (c.Situatie.ValoarePropusa != c.Situatie.ValoareActualizata)
            f.Rand(T("Valoare propusă (cu acte nesemnate)", Propus), N(c.Situatie.ValoarePropusa, BaniPropus), T("Nu e valoare contractuală până la semnarea actelor", Propus));
        if (k?.TranseAvans.Count > 0)
            f.Rand(T("Avans contractual"), T($"{Procent(k.TranseAvans.Sum(t => t.Procent))} în tranșe"), T($"Tranșe: {string.Join(" + ", k.TranseAvans.Select(t => Procent(t.Procent)))}"));
        if (c.DataInceput is { } di && c.Situatie.Termen is { } dt)
            f.Rand(T("Perioadă de execuție"), T($"{di:dd.MM.yyyy} – {dt:dd.MM.yyyy}"), T(dt != c.DataSfarsit ? "Termen prelungit prin act adițional" : null));
        else if (c.Situatie.Termen is { } t)
            f.Rand(T("Data finalizării contractuale"), T(t.ToString("dd.MM.yyyy")), T(null));
        if (k?.Durata is { } d) f.Rand(T("Durată de execuție"), T(d), T(null));
        if (k?.DataPredareAmplasament is { } pa) f.Rand(T("Data predării amplasamentului"), T(pa.ToString("dd.MM.yyyy")), T(null));
        if (k?.StareText is { } st) f.Rand(T("Stadiu execuție"), T(st), T(null));
        // Observațiile („Etichetă: valoare (observație)”, cum le-a preluat importul) redevin rânduri.
        foreach (var linie in (c.Observatii ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = linie.IndexOf(": ", StringComparison.Ordinal);
            if (i <= 0) { f.Rand(T("Observații"), T(linie), T(null)); continue; }
            var rest = linie[(i + 2)..];
            var p = rest.LastIndexOf(" (", StringComparison.Ordinal);
            f.Rand(T(linie[..i]), T(p > 0 && rest.EndsWith(')') ? rest[..p] : rest), T(p > 0 && rest.EndsWith(')') ? rest[(p + 2)..^1] : null));
        }

        return f;
    }

    private static Foaie Valoare(ContractDto c)
    {
        var membri = c.Conditii?.Membri ?? [];
        var f = new Foaie("2. Valoare contract", [42, 20, 24, .. membri.Select(_ => 20d), 40]);
        f.PuneTitlu("MONITORIZAREA VALORII CONTRACTULUI", 4 + membri.Count);
        f.Gol();
        f.PuneAntet(["Document", $"Modificare {c.Moneda}", $"Valoare cumulată {c.Moneda}", .. membri.Select(m => $"{Scurt(m.Denumire)} {c.Moneda}"), "Statut"]);
        var cumulat = c.Valoare;
        f.Rand([T($"Contract {c.NumarExtern ?? c.Numar} / {c.DataSemnarii:dd.MM.yyyy}"), N(c.Valoare), N(c.Valoare), .. membri.Select(m => N(Math.Round(c.Valoare * m.Cota, 2))), T("Semnat")]);
        foreach (var a in c.Acte ?? [])
        {
            cumulat += a.DiferentaValoare;
            var s = a.Semnat ? Normal : Propus;
            // „AA1 / 12.10.2026 — obiect”; obiectul care începe deja cu „AA1” (preluat așa din fișă) rămâne cum e.
            var document = ValoriFoaie.Cheie(a.Obiect).StartsWith($"aa{a.Numar}", StringComparison.Ordinal) || ValoriFoaie.Cheie(a.Obiect).StartsWith($"aa {a.Numar}", StringComparison.Ordinal)
                ? a.Obiect
                : $"AA{a.Numar}{(a.NumarExtern is null || a.NumarExtern == a.Numar.ToString(CultureInfo.InvariantCulture) ? string.Empty : $" (nr. {a.NumarExtern})")} / {a.Data:dd.MM.yyyy} — {a.Obiect}";
            f.Rand([T(document, s), N(a.DiferentaValoare, a.Semnat ? Bani : BaniPropus),
                N(cumulat, a.Semnat ? Bani : BaniPropus), .. membri.Select(m => N(Math.Round(a.DiferentaValoare * m.Cota, 2), a.Semnat ? Bani : BaniPropus)),
                T(a.Semnat ? "Semnat" : "nesemnat — propunere", s)]);
        }

        f.Rand([T("VALOARE CONTRACTUALĂ ACTUALĂ (semnată)", Aldin), N(c.Situatie.ValoareActualizata, BaniAldin), N(c.Situatie.ValoareActualizata, BaniAldin),
            .. membri.Select(m => N(Math.Round(c.Situatie.ValoareActualizata * m.Cota, 2), BaniAldin)), T(c.Situatie.ValoarePropusa != c.Situatie.ValoareActualizata ? $"Propusă: {c.Situatie.ValoarePropusa.ToString("N2", Ro)}" : null)]);
        foreach (var t in c.Conditii?.TranseAvans ?? [])
            f.Rand([T($"Tranșa {t.Numar} avans – {Procent(t.Procent)}"), N(Math.Round(c.Valoare * t.Procent, 2)), T(null),
                .. membri.Select(m => N(Math.Round(c.Valoare * t.Procent * m.Cota, 2))), T(t.Descriere)]);

        return f;
    }

    private static Foaie Obligatii(ContractDto c)
    {
        var f = new Foaie("3. Obligatii Termene", 48, 25, 58, 21, 49);
        f.PuneTitlu("REGISTRUL OBLIGAȚIILOR ȘI TERMENELOR", 5);
        f.Gol();
        f.PuneAntet("Obligație / control", "Temei contractual", "Termen / condiție", "Status", "Responsabil / observații");
        foreach (var o in c.Fisa?.Obligatii ?? [])
            f.Rand(T(o.Denumire), T(o.Temei), T(o.Termen ?? o.DataTermen?.ToString("dd.MM.yyyy")), T(o.Stare),
                T(string.Join(" — ", new[] { o.Responsabil, o.Observatii }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        return f;
    }

    private static Foaie Facturi(ContractDto c)
    {
        var f = new Foaie("4. Facturi Plati", 16, 34, 16, 12, 18, 18, 18, 16, 10, 18, 16, 18, 18, 24, 40);
        f.PuneTitlu("URMĂRIREA FACTURILOR ȘI PLĂȚILOR", 15);
        f.Gol();
        f.PuneAntet("Luna execuției", "Partener facturat", "Nr. factură", "Data facturii", "Tip", "Situație de plată", $"Lucrări {c.Moneda} fără TVA", $"Storno avans {c.Moneda}",
            "Curs BNR", "Net RON fără TVA", "TVA RON", "Total RON", "Plată RON", "Status plată", "Observații");
        var plati = c.Plati ?? [];
        var primul = f.UrmatorulRand;
        foreach (var x in c.FacturiFisa ?? [])
        {
            var aleFacturii = plati.Where(p => p.FacturaContractId == x.Id).ToList();
            var confirmat = aleFacturii.Where(p => p.Confirmata).Sum(p => p.SumaRon);
            var bo = aleFacturii.FirstOrDefault(p => !p.Confirmata);
            // Starea plății, ca în fișe: „Achitat zz.ll.aaaa”, „BO emis”, „Scadent zz.ll.aaaa”, „De verificat”.
            var status = x.Sold <= 0.01m && aleFacturii.Any(p => p.Confirmata) ? $"Achitat {aleFacturii.Where(p => p.Confirmata).Max(p => p.Data):dd.MM.yyyy}"
                : bo is not null ? "BO emis"
                : x.Scadenta is { } sc ? $"Scadent {sc:dd.MM.yyyy}" : "De verificat";
            var obs = string.Join("; ", new[] { bo?.Numar is { } nb ? $"BO nr. {nb}" : null, x.Observatii }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var tip = x.Tip switch { "Avans" => $"Avans{(x.TransaAvans is { } ta ? $" tranșa {ta}" : string.Empty)}", "Livrare" => "Livrare", "Altele" => "Altele", _ => "Lucrări" };
            if (x.StornoAvans > 0) tip += " + stornare avans";
            f.Rand(T(x.LunaExecutie), T(x.Membru), T(x.Numar), T(x.Data.ToString("dd.MM.yyyy")), T(tip), T(x.SituatiePlata), N(x.Valoare), N(x.StornoAvans),
                x.Curs is { } cr ? N(cr, Curs) : T(null), N(x.NetRon, Lei), N(x.TvaRon, Lei), N(x.TotalRon, Lei), confirmat > 0 ? N(confirmat, Lei) : T(null), T(status), T(obs));
        }

        var ultimul = f.UrmatorulRand - 1;
        if (ultimul >= primul)
        {
            string Sum(string col) => $"SUM({col}{primul}:{col}{ultimul})";
            var fz = c.FacturiFisa!;
            f.Rand(T("TOTAL", Aldin), T(null), T(null), T(null), T(null), T(null), F(Sum("G"), fz.Sum(x => x.Valoare), BaniAldin), F(Sum("H"), fz.Sum(x => x.StornoAvans), BaniAldin),
                T(null), F(Sum("J"), fz.Sum(x => x.NetRon), LeiAldin), F(Sum("K"), fz.Sum(x => x.TvaRon), LeiAldin), F(Sum("L"), fz.Sum(x => x.TotalRon), LeiAldin),
                F(Sum("M"), plati.Where(p => p.Confirmata && p.FacturaContractId is not null).Sum(p => p.SumaRon), LeiAldin));
        }

        // Plățile, în detaliu (inclusiv cele neatribuite și biletele la ordin emise).
        if (plati.Count > 0)
        {
            f.Gol();
            f.PuneAntet("Data plății", "Factura", "Instrument", "Nr.", "Sumă RON", "Scadență", "Stare", "Observații");
            foreach (var p in plati)
                f.Rand(T(p.Data.ToString("dd.MM.yyyy")), T((c.FacturiFisa ?? []).FirstOrDefault(x => x.Id == p.FacturaContractId)?.Numar ?? "neatribuită"),
                    T(p.Instrument switch { "OrdinDePlata" => "Ordin de plată", "BiletLaOrdin" => "Bilet la ordin", "Cec" => "CEC", "Compensare" => "Compensare", _ => "Altul" }),
                    T(p.Numar), N(p.SumaRon, Lei), T(p.Scadenta?.ToString("dd.MM.yyyy")), T(p.Confirmata ? "confirmată" : "emisă, neconfirmată", p.Confirmata ? Normal : Propus), T(p.Observatii));
        }

        return f;
    }

    private static Foaie Garantii(ContractDto c)
    {
        var membri = c.Conditii?.Membri ?? [];
        var f = new Foaie("5. Garantii Riscuri", 38, 52, 40, 22, 22, 18, 22, 16);
        f.PuneTitlu("GARANȚII, PENALITĂȚI ȘI ASPECTE DE CLARIFICAT", 3);
        f.Gol();
        f.PuneAntet("Element", "Prevedere contractuală / verificare", "Status / risc");
        var riscuri = c.Fisa?.Riscuri ?? [];
        var k = c.Conditii;
        var v = c.Situatie.ValoareActualizata;
        bool Acoperit(string cheie) => riscuri.Any(r => ValoriFoaie.Cheie(r.Element).Contains(cheie));
        // Garanțiile și penalitățile din condiții, dacă nu sunt deja printre aspectele preluate din fișă.
        if (c.ProcentGarantie > 0 && !Acoperit("buna executie"))
            f.Rand(T("Garanție bună execuție"), T($"{Procent(c.ProcentGarantie)} din valoarea contractului — {(v * c.ProcentGarantie).ToString("N2", Ro)} {c.Moneda}"), T(null));
        if (k?.ProcentGarantieAvans > 0 && !Acoperit("avans"))
            f.Rand(T("SGB avans total"), T($"{Procent(k.ProcentGarantieAvans)} din valoarea contractului — {(v * k.ProcentGarantieAvans).ToString("N2", Ro)} {c.Moneda}"), T(null));
        if (k?.ProcentGarantieTehnica > 0 && !Acoperit("tehnic") && !Acoperit("garantie lucrari"))
            f.Rand(T("Garanție tehnică"), T($"{Procent(k.ProcentGarantieTehnica)}{(k.LuniGarantieTehnica is { } l ? $"; {l} de luni de la PVRTL" : string.Empty)}"),
                T(k.DataPvrtl is { } pv && k.LuniGarantieTehnica is { } l2 ? $"PVRTL {pv:dd.MM.yyyy}; expiră {pv.AddMonths(l2):dd.MM.yyyy}" : null));
        if (k?.PenalitateExecutieZi > 0 && !Acoperit("penalitate intarziere executie") && !(Acoperit("penalitate") && k.PenalitatePlataZi == 0))
            f.Rand(T("Penalitate întârziere execuție"), T($"{Procent(k.PenalitateExecutieZi)}/zi din valoarea contractului{(k.PlafonPenalitateExecutie > 0 ? $"; plafon {Procent(k.PlafonPenalitateExecutie)}" : string.Empty)}"), T(null));
        if (k?.PenalitatePlataZi > 0 && !Acoperit("penalitate intarziere plata"))
            f.Rand(T("Penalitate întârziere plată"), T($"{Procent(k.PenalitatePlataZi)}/zi{(k.PlafonPenalitatePlata > 0 ? $"; plafon {Procent(k.PlafonPenalitatePlata)}" : string.Empty)} din valoarea contractului"), T(null));
        foreach (var r in riscuri)
            f.Rand(T(r.Element), T(r.Prevedere), T(r.Stare));

        // Recuperarea avansului, pe tranșe și antreprenori (calculată din facturile din fișă).
        if (c.SituatieFisa is { } s && k?.TranseAvans.Count > 0)
        {
            f.Gol();
            f.Rand(T("Tabel recuperare avans", Aldin));
            var randuri = membri.Count > 1 ? s.Membri : [s.Total];
            f.PuneAntet(["Indicator", .. randuri.Select(m => Scurt(m.Membru)), .. membri.Count > 1 ? new[] { "Total" } : []]);
            var cuTotal = membri.Count > 1 ? randuri.Append(s.Total).ToList() : randuri.ToList();
            foreach (var t in k.TranseAvans)
                f.Rand([T($"Avans tranșa {t.Numar} ({Procent(t.Procent)})"), .. cuTotal.Select(m => N(m.Transe.FirstOrDefault(x => x.Numar == t.Numar)?.Contractual ?? 0))]);
            f.Rand([T("Total avans contractual"), .. cuTotal.Select(m => N(m.AvansContractual))]);
            f.Rand([T("Avans facturat"), .. cuTotal.Select(m => N(m.AvansFacturat))]);
            f.Rand([T("Avans recuperat prin storno"), .. cuTotal.Select(m => N(m.AvansRecuperat))]);
            f.Rand([T("Sold avans de recuperat", Aldin), .. cuTotal.Select(m => N(m.AvansRamas, BaniAldin))]);
        }

        // Instrumentele de garanție și plată (SGB, bilete la ordin).
        f.Gol();
        f.PuneAntet("Nr. crt.", "Sursa / Document justificativ", "Emitent", "Beneficiar", "Nr. document", "Data emiterii", "Valoare garanție", "Monedă", "Valabilitate", "Stare");
        var nr = 0;
        foreach (var i in c.Fisa?.Instrumente ?? [])
        {
            var eticheta = i.Tip switch
            {
                "GarantieAvans" => "SGB restituire avans", "GarantieBunaExecutie" => "SGB bună execuție", "GarantieTehnica" => "SGB garanție tehnică",
                "BiletLaOrdin" => "Bilet la ordin", _ => "Instrument",
            };
            var desc = i.Descriere is { } d && ValoriFoaie.Cheie(d).Contains(ValoriFoaie.Cheie(eticheta).Split(' ')[^1]) ? d
                : i.Descriere is null ? eticheta : $"{eticheta} — {i.Descriere}";
            f.Rand(N(++nr, Normal), T(desc), T(i.Emitent), T(i.Beneficiar), T(i.Numar), T(i.DataEmitere?.ToString("dd.MM.yyyy")), i.Valoare is { } vi ? N(vi) : T(null),
                T(i.Moneda), T(i.DataExpirare?.ToString("dd.MM.yyyy")), T(i.Stare));
        }

        return f;
    }

    private static Foaie Actiuni(ContractDto c)
    {
        var f = new Foaie("6. Actiuni", 8, 60, 14, 14, 24, 14, 50);
        f.PuneTitlu("REGISTRU DE ACȚIUNI", 7);
        f.Gol();
        f.PuneAntet("Nr.", "Acțiune", "Prioritate", "Status", "Responsabil", "Termen", "Observații");
        foreach (var a in c.Fisa?.Actiuni ?? [])
            f.Rand(N(a.Ordine, Normal), T(a.Descriere), T(a.Prioritate switch { "Critica" => "Critică", "Ridicata" => "Ridicată", "Medie" => "Medie", _ => "Normală" }),
                T(a.Stare switch { "Inchisa" => "Închis", "InLucru" => "În lucru", _ => "Deschis" }), T(a.ResponsabilNume), T(a.Termen?.ToString("dd.MM.yyyy")), T(a.Observatii));
        return f;
    }

    private static Foaie Surse(ContractDto c)
    {
        var f = new Foaie("7. Surse", 60, 14, 70);
        f.PuneTitlu("BAZA DOCUMENTARĂ", 3);
        f.Gol();
        f.PuneAntet("Document", "Dată", "Observații");
        foreach (var s in c.Fisa?.Surse ?? [])
            f.Rand(T(s.Document), T(s.Data?.ToString("dd.MM.yyyy")), T(s.Observatii));
        if (c.Documente?.Count > 0)
        {
            f.Gol();
            f.PuneAntet("Fișier atașat în aplicație", "Încărcat", "De");
            foreach (var d in c.Documente)
                f.Rand(T(d.NumeFisier), T(d.IncarcatLa.ToString("dd.MM.yyyy")), T(d.IncarcatDe));
        }

        return f;
    }

    private static Foaie Termene(ContractDto c)
    {
        var f = new Foaie("8. Termene executie", 36, 16, 16, 36, 26, 44);
        f.PuneTitlu($"TERMENE DE EXECUȚIE — CONTRACT {c.PartenerDenumire.ToUpper(Ro)}", 6);
        f.Gol();
        f.PuneAntet("Fază / reper", "Start contractual", "Final contractual", "Durată / condiții", "Status", "Document / observație");
        foreach (var x in c.Fisa?.Faze ?? [])
            f.Rand(T(x.Denumire), T(x.Start?.ToString("dd.MM.yyyy") ?? "De confirmat"), T(x.Final?.ToString("dd.MM.yyyy") ?? "De confirmat"), T(x.Conditii), T(x.Stare), T(x.Observatii));
        return f;
    }

    private static Foaie Reconciliere(ContractDto c)
    {
        var f = new Foaie("9. Reconciliere", 34, 18, 18, 18, 18, 18, 18, 18, 18);
        f.PuneTitlu("SITUAȚIA PE ANTREPRENOR", 9);
        f.Gol();
        f.PuneAntet("Antreprenor", $"Valoare {c.Moneda}", $"Lucrări facturate {c.Moneda}", $"Rest de executat {c.Moneda}", $"Avans rămas {c.Moneda}", "Facturat fără TVA RON",
            "Facturat cu TVA RON", "Plătit confirmat RON", "Sold RON");
        if (c.SituatieFisa is { } s)
        {
            foreach (var m in s.Membri.Count > 1 ? s.Membri.Append(s.Total) : [s.Total])
            {
                var a = m.Membru == "Total" ? BaniAldin : Bani;
                var l = m.Membru == "Total" ? LeiAldin : Lei;
                f.Rand(T(m.Membru, m.Membru == "Total" ? Aldin : Normal), N(m.ValoareActuala, a), N(m.LucrariFacturate, a), N(m.RestDeExecutat, a), N(m.AvansRamas, a),
                    N(m.FacturatNetRon, l), N(m.FacturatTotalRon, l), N(m.PlatitConfirmat, l), N(m.Sold, l));
            }

            if (s.Alerte.Count > 0)
            {
                f.Gol();
                f.Rand(T("De urmărit", Aldin));
                foreach (var al in s.Alerte)
                    f.Rand(T(al.Mesaj, Propus));
            }
        }

        f.Gol();
        f.Rand(T("NU INTRODUCE ZERO", Aldin), T("Câmpurile goale la plăți nu confirmă neplata; biletul la ordin emis nu e plată."));
        return f;
    }

    private static Foaie Corecturi(ContractDto c)
    {
        var f = new Foaie("10. Corecturi", 16, 26, 50, 90);
        f.PuneTitlu("ISTORICUL CORECTURILOR CONTRACTULUI", 4);
        f.Gol();
        f.PuneAntet("Data", "Corectat de", "Motiv", "Modificări");
        foreach (var k in c.Fisa!.Corecturi)
            f.Rand(T(k.La.ToString("dd.MM.yyyy")), T(k.CorectatDe), T(k.Motiv), T(string.Join("\n", k.Modificari)));
        return f;
    }

    // ---------------------------------------------------------------- utilitare

    private static string Procent(decimal f) => $"{(f * 100).ToString("0.####", Ro)}%";

    /// <summary>Numele scurt al membrului în antete („GENERAL CONCRETE CERNAVODĂ SRL” → „GENERAL CONCRETE CERNAVODĂ”).</summary>
    private static string Scurt(string denumire) => denumire.Replace(" SRL", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" S.R.L.", string.Empty).Replace(" SA", string.Empty).Trim();

    private abstract record Celula(uint Stil);

    private sealed record CelulaText(string? Valoare, uint Stil) : Celula(Stil);

    private sealed record CelulaNumar(decimal Valoare, uint Stil) : Celula(Stil);

    private sealed record CelulaFormula(string Formula, decimal Valoare, uint Stil) : Celula(Stil);

    private static CelulaText T(string? v, uint stil = Normal) => new(v, stil);

    private static CelulaNumar N(decimal v, uint stil = Bani) => new(v, stil);

    private static CelulaFormula F(string formula, decimal v, uint stil) => new(formula, v, stil);

    /// <summary>O foaie construită pe rânduri: titlu (îmbinat), antete, rânduri de celule; lățimile coloanelor.</summary>
    private sealed class Foaie(string nume, params double[] latimi)
    {
        private readonly List<Row> _randuri = [];
        private readonly List<string> _imbinari = [];

        public string Nume { get; } = nume;
        public int UrmatorulRand => _randuri.Count + 1;

        public void PuneTitlu(string text, int coloane)
        {
            Rand(T(text, Titlu));
            if (coloane > 1)
                _imbinari.Add($"A{_randuri.Count}:{Litera(coloane - 1)}{_randuri.Count}");
        }

        public void Gol() => _randuri.Add(new Row { RowIndex = (uint)(_randuri.Count + 1) });

        public void PuneAntet(params string[] titluri) => Rand(titluri.Select(t => (Celula)T(t, Antet)).ToArray());

        public void Rand(params Celula[] celule)
        {
            var nr = (uint)(_randuri.Count + 1);
            var rand = new Row { RowIndex = nr };
            for (var i = 0; i < celule.Length; i++)
            {
                var referinta = $"{Litera(i)}{nr}";
                rand.AppendChild(celule[i] switch
                {
                    CelulaText t when string.IsNullOrEmpty(t.Valoare) => new Cell { CellReference = referinta, StyleIndex = t.Stil },
                    CelulaText t => new Cell { CellReference = referinta, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(t.Valoare!) { Space = SpaceProcessingModeValues.Preserve }), StyleIndex = t.Stil },
                    CelulaNumar n => new Cell { CellReference = referinta, CellValue = new CellValue(n.Valoare.ToString(CultureInfo.InvariantCulture)), DataType = CellValues.Number, StyleIndex = n.Stil },
                    CelulaFormula f => new Cell { CellReference = referinta, CellFormula = new CellFormula(f.Formula), CellValue = new CellValue(f.Valoare.ToString(CultureInfo.InvariantCulture)), DataType = CellValues.Number, StyleIndex = f.Stil },
                    _ => new Cell(),
                });
            }

            _randuri.Add(rand);
        }

        public Worksheet Construieste()
        {
            var foaie = new Worksheet(
                new SheetViews(new SheetView { WorkbookViewId = 0, ShowGridLines = true }),
                new Columns(latimi.Select((l, i) => new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = l, CustomWidth = true })),
                new SheetData(_randuri));
            if (_imbinari.Count > 0)
                foaie.AppendChild(new MergeCells(_imbinari.Select(r => new MergeCell { Reference = r })));
            return foaie;
        }

        private static string Litera(int i) => i < 26 ? ((char)('A' + i)).ToString() : $"{(char)('A' + (i / 26) - 1)}{(char)('A' + (i % 26))}";
    }

    /// <summary>Stilurile fișelor: titlu bleumarin (#183153), antet albastru deschis (#DCEAF5), propus galben (#FFF1D6), bani în moneda contractului.</summary>
    private static Stylesheet Stiluri(string moneda)
    {
        var aliniere = () => new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top };
        CellFormat Format(uint font, uint fill, uint numar = 0) => new(aliniere())
        {
            FontId = font, FillId = fill, NumberFormatId = numar, ApplyFont = font != 0, ApplyFill = fill != 0, ApplyNumberFormat = numar != 0, ApplyAlignment = true,
        };
        return new Stylesheet(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164, FormatCode = $"#,##0.00\\ \"{moneda}\"" },
                new NumberingFormat { NumberFormatId = 165, FormatCode = "#,##0.00" },
                new NumberingFormat { NumberFormatId = 166, FormatCode = "0.0000" }) { Count = 3 },
            new Fonts(
                new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 15 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FF183153" }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 4 },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FF183153" }) { PatternType = PatternValues.Solid }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFDCEAF5" }) { PatternType = PatternValues.Solid }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFFFF1D6" }) { PatternType = PatternValues.Solid })) { Count = 5 },
            new Borders(new Border()) { Count = 1 },
            new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            new CellFormats(
                Format(0, 0),           // 0 normal
                Format(1, 2),           // 1 titlu
                Format(2, 3),           // 2 antet
                Format(0, 4),           // 3 propus (galben)
                Format(3, 0),           // 4 aldin
                Format(0, 0, 164),      // 5 bani (moneda contractului)
                Format(0, 4, 164),      // 6 bani propus
                Format(3, 0, 164),      // 7 bani aldin
                Format(0, 0, 165),      // 8 lei
                Format(3, 0, 165),      // 9 lei aldin
                Format(0, 0, 166)) { Count = 11 },   // 10 curs
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
    }
}
