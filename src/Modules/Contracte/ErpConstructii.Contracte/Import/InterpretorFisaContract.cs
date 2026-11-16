using System.Text.RegularExpressions;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Contracte.Import;

/// <summary>Un act adițional citit din fișă (semnat sau „nesemnat — propunere”).</summary>
public sealed record ActCitit(string? NumarExtern, DateOnly? Data, string Obiect, decimal Diferenta, bool Semnat);

/// <summary>O plată citită din fișă, legată de factura cu numărul dat.</summary>
public sealed record PlataCitita(string NumarFactura, DatePlata Plata);

/// <summary>
/// Fișa de urmărire citită din Excel (D-56): datele contractului (cu certitudinea lor), condițiile, actele adiționale,
/// registrele, facturile și plățile, plus notele (ce nu s-a putut citi sigur). Se verifică în ecran înainte de confirmare.
/// </summary>
public sealed record FisaCitita(
    CampCitit<string> NumarExtern,
    CampCitit<DateOnly> DataSemnarii,
    CampCitit<string> PartenerDenumire,
    CampCitit<string> PartenerCui,
    CampCitit<string> Proiect,
    CampCitit<string> Obiect,
    CampCitit<string> Moneda,
    CampCitit<decimal> Valoare,
    DateOnly? DataInceput,
    DateOnly? DataSfarsit,
    int? TermenPlataZile,
    decimal ProcentGarantie,
    ConditiiContract Conditii,
    string? Observatii,
    IReadOnlyList<ActCitit> Acte,
    IReadOnlyList<DateObligatie> Obligatii,
    IReadOnlyList<DateActiune> Actiuni,
    IReadOnlyList<DateFaza> Faze,
    IReadOnlyList<DateRisc> Riscuri,
    IReadOnlyList<DateInstrument> Instrumente,
    IReadOnlyList<DateSursa> Surse,
    IReadOnlyList<DateFacturaContract> Facturi,
    IReadOnlyList<PlataCitita> Plati,
    IReadOnlyList<string> Note);

/// <summary>
/// Citirea fișei de urmărire a contractului din foile Excel (D-56, docs/contracte.md §4). Foile se recunosc după nume
/// („Date Generale”, „Valoare contract”, „Obligații”, „Situații de lucrări” / „Facturi Plăți”, „Garanții”, „Acțiuni”,
/// „Surse”, „Termene execuție”), câmpurile după eticheta din coloana A (variantele din fișele reale), tabelele după antet
/// (coloanele diferă între fișe). Textele de tipul „De completat” / „De verificat” nu devin valori. Logică pură.
/// </summary>
public static partial class InterpretorFisaContract
{
    public static FisaCitita Citeste(IReadOnlyList<FoaieTabel> foi, string? firmaDenumire, string? firmaCui)
    {
        var note = new List<string>();
        var obs = new List<string>();
        FoaieTabel? Foaie(params string[] chei) => foi.FirstOrDefault(f => chei.Any(k => ValoriFoaie.Cheie(f.Nume).Contains(k)));

        // ---- 1. Date generale: etichetă (A) → valoare (B), observație (C)
        var generale = Foaie("date generale") is { } fg ? Perechi(fg) : [];
        (string? Valoare, string? Obs)? Gaseste(params string[] chei) =>
            generale.FirstOrDefault(p => chei.Any(k => p.Cheie == k || p.Cheie.StartsWith(k + " ", StringComparison.Ordinal))) is { Eticheta.Length: > 0 } x ? (x.Valoare, x.Obs) : null;
        var folosite = new HashSet<string>();
        (string? Valoare, string? Obs)? Ia(params string[] chei)
        {
            var c = Gaseste(chei);
            if (c is not null)
                folosite.UnionWith(generale.Where(p => chei.Any(k => p.Cheie == k || p.Cheie.StartsWith(k + " ", StringComparison.Ordinal))).Select(p => p.Cheie));
            return c;
        }

        var contract = Ia("contract");
        var numar = contract?.Valoare is { } cv ? Util(cv.Split('/')[0]) : null;
        var data = ValoriFoaie.Data(contract?.Valoare);
        var sub = Ia("subantreprenor");
        var cui = Cui(sub?.Obs) ?? Cui(sub?.Valoare);
        var proiect = Ia("proiect");
        var obiect = Ia("obiect");
        var cladiri = Ia("cladiri");
        var tipPret = Ia("tip pret", "tip contract");
        var moneda = Ia("moneda");
        var valoareCamp = Ia("valoare contract eur fara tva", "valoare contract", "valoare initiala", "valoare contractuala");
        var valoare = ValoriFoaie.Numar(valoareCamp?.Valoare);
        var durata = Ia("durata de executie", "durata");
        var perioada = Ia("perioada de executie");
        var predare = Ia("data predarii amplasamentului");
        var finalizare = Ia("data finalizarii contractuale");
        var stadiu = Ia("status", "stadiu executie");
        var antreprenori = Ia("antreprenori", "antreprenor urmarit");
        var avans = generale.Where(p => p.Cheie.StartsWith("avans contractual", StringComparison.Ordinal)).ToList();
        folosite.UnionWith(avans.Select(a => a.Cheie));
        folosite.UnionWith(generale.Where(p => p.Cheie.StartsWith("pondere", StringComparison.Ordinal) || p.Cheie.StartsWith("data fisei", StringComparison.Ordinal)
                                               || p.Cheie.StartsWith("valoare propusa", StringComparison.Ordinal) || p.Cheie.StartsWith("diferenta", StringComparison.Ordinal)
                                               || p.Cheie.StartsWith("valoare lucrari facturate", StringComparison.Ordinal) || p.Cheie.StartsWith("avans aferent", StringComparison.Ordinal)
                                               || p.Cheie.StartsWith("act aditional", StringComparison.Ordinal) || p.Cheie.StartsWith("situatie aa", StringComparison.Ordinal))
            .Select(p => p.Cheie));
        var monedaCod = moneda?.Valoare is { } m ? (ValoriFoaie.Cheie(m).Contains("eur") ? "EUR" : ValoriFoaie.Cheie(m).Contains("ron") || ValoriFoaie.Cheie(m).Contains("lei") ? "RON" : null) : null;
        monedaCod ??= valoareCamp is { } vc && ValoriFoaie.Cheie(vc.Valoare + " " + vc.Obs + " " + string.Join(' ', generale.Select(g => g.Eticheta))).Contains("eur") ? "EUR" : null;

        var dateExecutie = ValoriFoaie.Date(perioada?.Valoare);
        DateOnly? inceput = dateExecutie.Count >= 2 ? dateExecutie[0] : null;
        DateOnly? sfarsit = dateExecutie.Count >= 2 ? dateExecutie[1] : ValoriFoaie.Data(finalizare?.Valoare);

        // Restul rândurilor din „Date generale” rămân ca observații (nimic din fișă nu se pierde).
        foreach (var p in generale.Where(p => !folosite.Contains(p.Cheie) && p.Eticheta.Length > 0 && !p.Cheie.StartsWith("fisa", StringComparison.Ordinal)
                                              && p.Cheie != "indicator" && !p.Eticheta.StartsWith('…')))
            obs.Add($"{p.Eticheta}: {p.Valoare}{(p.Obs is null ? string.Empty : $" ({p.Obs})")}");

        // ---- membrii asocierii
        var membri = Membri(antreprenori?.Valoare, firmaDenumire, firmaCui, note);

        // ---- 2. Valoare contract: actele adiționale (semnate / propuse), tranșele de avans
        var acte = new List<ActCitit>();
        var transe = new List<TransaAvans>();
        if (Foaie("valoare contract") is { } fv && Tabel(fv, "document") is { } tv)
        {
            var colModificare = tv.Coloana("modificare") ?? tv.Coloana("total eur") ?? 1;
            var colStatut = tv.Coloana("statut") ?? tv.Coloana("observatii");
            foreach (var r in tv.Randuri)
            {
                var doc = r.Text(0);
                if (doc is null) continue;
                var k = ValoriFoaie.Cheie(doc);
                if (k.StartsWith("transa", StringComparison.Ordinal) && ValoriFoaie.Procente(doc) is [var pr, ..])
                    transe.Add(new TransaAvans(transe.Count + 1, pr, Util(doc)));
                else if (k.StartsWith("aa", StringComparison.Ordinal) || k.Contains("act aditional"))
                {
                    var statut = colStatut is { } cs ? r.Text(cs) : null;
                    var dif = ValoriFoaie.Numar(r.Text(colModificare)) ?? 0;
                    if (dif == 0 && !k.Contains("aa")) continue;
                    // „AA1 / 12.10.2026 — Înlocuire chillere” (exportul aplicației): obiectul e textul de după liniuță.
                    var obiectAct = PrefixAct().Match(doc) is { Success: true } pa ? pa.Groups[1].Value.Trim() : doc;
                    acte.Add(new ActCitit(NumarAct(doc), ValoriFoaie.Data(doc), obiectAct, dif, !ValoriFoaie.Cheie(statut).Contains("nesemnat")));
                }
            }
        }

        // Fără foaia de valoare: actele din „Date generale” („Act adițional nr. 1 | 26.439,26 | 12.10.2026 – nesemnat; …”).
        if (acte.Count == 0)
        {
            foreach (var p in generale.Where(p => p.Cheie.StartsWith("act aditional", StringComparison.Ordinal) && ValoriFoaie.Numar(p.Valoare) is not null))
                acte.Add(new ActCitit(NumarAct(p.Eticheta), ValoriFoaie.Data(p.Obs), p.Obs ?? p.Eticheta, ValoriFoaie.Numar(p.Valoare)!.Value,
                    !ValoriFoaie.Cheie(p.Obs).Contains("nesemnat")));
        }

        // Tranșele din textul avansului: „TCS: 5% + 10% + 5%”, „Două tranșe a câte 25%”, „Avans contractual 10% EUR”, 0,5.
        if (transe.Count == 0)
            transe.AddRange(TranseDin(avans.SelectMany(a => new[] { a.Eticheta, a.Valoare, a.Obs }).Where(t => t is not null)!, note));

        // ---- 3. Obligații și termene
        var obligatii = new List<DateObligatie>();
        int? termenPlata = null;
        if (Foaie("obligatii") is { } fo && Tabel(fo, "obligatie") is { } to)
        {
            var cTemei = to.Coloana("temei");
            var cTermen = to.Coloana("termen");
            var cStare = to.Coloana("status");
            var cObs = to.Coloana("situatie") ?? to.Coloana("responsabil") ?? to.Coloana("observatii");
            foreach (var r in to.Randuri)
            {
                if (r.Text(0) is not { } den) continue;
                var termen = cTermen is { } ct ? r.Text(ct) : null;
                obligatii.Add(new DateObligatie(den, cTemei is { } ctm ? r.Text(ctm) : null, termen, ValoriFoaie.Data(termen),
                    cStare is { } cst ? r.Text(cst) : null, null, cObs is { } co ? r.Text(co) : null));
                if (termenPlata is null && ValoriFoaie.Cheie(den).StartsWith("plat", StringComparison.Ordinal) && Zile().Match(termen ?? string.Empty) is { Success: true } z)
                    termenPlata = int.Parse(z.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // ---- 4. Facturi / situații / plăți
        var (facturi, plati) = Facturi(Foaie("situatii de lucrari", "facturi"), membri, note);

        // ---- 5. Garanții și riscuri (procentele, aspectele de clarificat, instrumentele)
        decimal garantieBe = 0, garantieAvans = 0, garantieTehnica = 0, penExec = 0, plafonExec = 0, penPlata = 0, plafonPlata = 0;
        int? luniTehnica = null;
        var riscuri = new List<DateRisc>();
        var instrumente = new List<DateInstrument>();
        if (Foaie("garantii") is { } fgar)
        {
            foreach (var t in Tabele(fgar))
            {
                var antet = string.Join(' ', t.Antet.Select(ValoriFoaie.Cheie));
                if (antet.Contains("emitent") || antet.StartsWith("instrument", StringComparison.Ordinal))
                    instrumente.AddRange(Instrumente(t));
                else if (antet.StartsWith("element", StringComparison.Ordinal) || antet.StartsWith("garantie / aspect", StringComparison.Ordinal) || antet.StartsWith("garantie aspect", StringComparison.Ordinal))
                {
                    foreach (var r in t.Randuri)
                    {
                        if (r.Text(0) is not { } el) continue;
                        var prev = r.Text(1);
                        var stare = r.Text(2);
                        riscuri.Add(new DateRisc(el, prev is not null && ValoriFoaie.Numar(prev) is { } nv && Numeric().IsMatch(prev) ? nv.ToString("N2", Ro) : prev, stare));
                        var k = ValoriFoaie.Cheie(el);
                        var text = $"{prev} {stare}";
                        var procente = ValoriFoaie.Procente(text);
                        if (k.Contains("buna executie") && garantieBe == 0 && procente.Count > 0)
                            garantieBe = Ori(text, procente[0]);
                        else if ((k.Contains("tehnic") || k.Contains("garantie lucrari")) && !k.Contains("expirare"))
                        {
                            if (garantieTehnica == 0 && procente.Count > 0) garantieTehnica = Ori(text, procente[0]);
                            if (luniTehnica is null && Luni().Match(text) is { Success: true } l) luniTehnica = int.Parse(l.Groups[1].Value, Ro);
                        }
                        else if (k.Contains("avans") && k.Contains("sgb") && garantieAvans == 0 && procente.Count > 0)
                            garantieAvans = Ori(text, procente[0]);
                        else if (k.Contains("penalitate") && procente.Count > 0)
                        {
                            var plata = k.Contains("plata");
                            var pz = procente[0];
                            var plafon = procente.Count > 1 ? procente[^1] : 0;
                            if (plata) { penPlata = pz; plafonPlata = plafon; }
                            else { penExec = pz; plafonExec = plafon; }
                        }
                    }
                }
            }
        }

        // ---- 6. Acțiuni, 7. Surse, 8. Termene de execuție
        var actiuni = new List<DateActiune>();
        if (Foaie("actiuni") is { } fa && Tabel(fa, "actiune") is { } ta)
        {
            var cA = ta.Coloana("actiune") ?? 1;
            var cP = ta.Coloana("prioritate");
            var cS = ta.Coloana("status");
            var cO = ta.Coloana("observatii");
            var cR = ta.Coloana("responsabil");
            var cT = ta.Coloana("termen");
            foreach (var r in ta.Randuri)
            {
                if (r.Text(cA) is not { } a) continue;
                actiuni.Add(new DateActiune(a, Prioritate(cP is { } cp ? r.Text(cp) : null), StareActiuneDin(cS is { } cs ? r.Text(cs) : null), null,
                    cR is { } cr ? r.Text(cr) : null, cT is { } ct ? ValoriFoaie.Data(r.Text(ct)) : null, cO is { } co ? r.Text(co) : null));
            }
        }

        var surse = new List<DateSursa>();
        if (Foaie("surse") is { } fs && Tabel(fs, "document") is { } ts)
        {
            foreach (var r in ts.Randuri)
                if (r.Text(0) is { } d)
                    surse.Add(new DateSursa(d, ValoriFoaie.Data(r.Text(1)), r.Text(2)));
        }

        var faze = new List<DateFaza>();
        if (Foaie("termene executie", "termene de executie") is { } ft && Tabel(ft, "faza") is { } tf)
        {
            var cStart = tf.Coloana("start") ?? 1;
            var cFinal = tf.Coloana("final") ?? 2;
            var cDur = tf.Coloana("durata");
            var cStare = tf.Coloana("status");
            var cDoc = tf.Coloana("document") ?? tf.Coloana("observatie");
            foreach (var r in tf.Randuri)
            {
                if (r.Text(0) is not { } den) continue;
                var start = ValoriFoaie.Data(r.Text(cStart));
                var final = ValoriFoaie.Data(r.Text(cFinal));
                var cond = cDur is { } cd ? r.Text(cd) : null;
                faze.Add(new DateFaza(den, start, final is { } f2 && start is { } s2 && f2 < s2 ? null : final, cond, cStare is { } cs ? r.Text(cs) : null,
                    cDoc is { } cdo ? r.Text(cdo) : null));
                if (sfarsit is null && ValoriFoaie.Cheie(den).StartsWith("executie", StringComparison.Ordinal) && final is not null)
                {
                    inceput ??= start;
                    sfarsit = final;
                }
            }
        }

        if (valoare is null) note.Add("Valoarea contractului nu a fost găsită: completeaz-o.");
        if (numar is null || data is null) note.Add("Numărul / data contractului nu au fost găsite sigur.");
        if (sub?.Valoare is null) note.Add("Subantreprenorul nu a fost găsit: alege-l.");
        if (facturi.Any(f => f.Valoare == 0 && f.Tip != TipFacturaContract.Altele))
            note.Add("Unele facturi nu au valoarea în moneda contractului (fișa nu dă cursul): completeaz-o în fișă după import.");

        var conditii = new ConditiiContract(Util(tipPret?.Valoare), Util(durata?.Valoare), Util(cladiri?.Valoare), Util(stadiu?.Valoare),
            ValoriFoaie.Data(predare?.Valoare), null, membri, transe, garantieAvans, garantieTehnica, luniTehnica, penExec, plafonExec, penPlata, plafonPlata);

        return new FisaCitita(
            Camp(numar), CampData(data), Camp(sub?.Valoare), cui is null ? new CampCitit<string>(null, Certitudine.Lipsa) : new CampCitit<string>(cui, Certitudine.Sigur),
            Camp(proiect?.Valoare), Camp(obiect?.Valoare),
            monedaCod is null ? new CampCitit<string>("RON", Certitudine.Nesigur) : new CampCitit<string>(monedaCod, Certitudine.Sigur),
            valoare is { } v ? new CampCitit<decimal>(v, Certitudine.Sigur, valoareCamp?.Valoare) : new CampCitit<decimal>(0, Certitudine.Lipsa),
            inceput, sfarsit, termenPlata, garantieBe, conditii, obs.Count == 0 ? null : string.Join("\n", obs),
            acte, obligatii, actiuni, faze, riscuri, instrumente, surse, facturi, plati, note);
    }

    private static readonly System.Globalization.CultureInfo Ro = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

    private static CampCitit<string> Camp(string? s) =>
        string.IsNullOrWhiteSpace(s) || EsteGol(s) ? new CampCitit<string>(null, Certitudine.Lipsa) : new CampCitit<string>(s.Trim(), Certitudine.Sigur);

    private static CampCitit<DateOnly> CampData(DateOnly? d) => d is { } x ? new CampCitit<DateOnly>(x, Certitudine.Sigur) : new CampCitit<DateOnly>(default, Certitudine.Lipsa);

    /// <summary>Texte de lucru care nu sunt valori („De completat”, „De verificat”, „—”).</summary>
    private static bool EsteGol(string s)
    {
        var k = ValoriFoaie.Cheie(s);
        return k.Length == 0 || k.StartsWith("de completat", StringComparison.Ordinal) || k.StartsWith("de determinat", StringComparison.Ordinal)
               || k.StartsWith("de confirmat", StringComparison.Ordinal) || k.StartsWith("de calculat", StringComparison.Ordinal) || s.Trim() is "—" or "-";
    }

    private static string? Util(string? s) => string.IsNullOrWhiteSpace(s) || EsteGol(s) ? null : s.Trim();

    /// <summary>„Două SGB a câte 5%” = 10% (procentul înmulțit cu numărul de instrumente / antreprenori).</summary>
    private static decimal Ori(string text, decimal procent)
    {
        var k = ValoriFoaie.Cheie(text);
        return (k.Contains("doua") || k.Contains("2 sgb")) && k.Contains("cate") ? procent * 2 : procent;
    }

    private static string? Cui(string? text) =>
        text is not null && CuiRegex().Match(text) is { Success: true } m ? m.Groups[1].Value.Replace(" ", string.Empty).ToUpperInvariant() : null;

    private static string? NumarAct(string text) => NrAct().Match(text) is { Success: true } m ? m.Groups[1].Value : null;

    // ---- membrii asocierii

    private static IReadOnlyList<MembruAsociere> Membri(string? text, string? firmaDenumire, string? firmaCui, List<string> note)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var nume = Separatori().Split(text).Select(n => Paranteze().Replace(n, string.Empty).Trim()).Where(n => n.Length > 2).ToList();
        if (nume.Count == 0)
            return [];
        if (nume.Count == 1)
            note.Add($"Un singur antreprenor găsit ({nume[0]}): dacă contractul e semnat de o asociere, adaugă celălalt membru și cotele.");
        var cota = Math.Round(1m / nume.Count, 4);
        var lista = nume.Select((n, i) => new MembruAsociere(n, null, i == nume.Count - 1 ? 1m - (cota * (nume.Count - 1)) : cota, false)).ToList();
        var firma = ValoriFoaie.Cheie(firmaDenumire);
        var activ = lista.FindIndex(m => firma.Length > 0 && (ValoriFoaie.Cheie(m.Denumire).Contains(firma) || firma.Contains(ValoriFoaie.Cheie(m.Denumire))));
        if (activ >= 0)
            lista[activ] = lista[activ] with { EsteFirmaActiva = true, Cui = string.IsNullOrWhiteSpace(firmaCui) ? null : firmaCui };
        else if (firma.Length > 0)
            note.Add("Firma din aplicație nu apare printre antreprenori: alege membrul care e firma.");
        return lista;
    }

    /// <summary>Membrul din denumirea / prescurtarea de pe factură („GCC” = GENERAL CONCRETE CERNAVODĂ SRL, „MELSAM”).</summary>
    private static string MembruDin(string? text, IReadOnlyList<MembruAsociere> membri)
    {
        if (string.IsNullOrWhiteSpace(text))
            return membri.FirstOrDefault(m => m.EsteFirmaActiva)?.Denumire ?? membri.FirstOrDefault()?.Denumire ?? "Firma";
        var k = ValoriFoaie.Cheie(text);
        var gasit = membri.FirstOrDefault(m => ValoriFoaie.Cheie(m.Denumire) == k)
                    ?? membri.FirstOrDefault(m => ValoriFoaie.Cheie(m.Denumire).StartsWith(k, StringComparison.Ordinal) || k.StartsWith(ValoriFoaie.Cheie(m.Denumire), StringComparison.Ordinal))
                    ?? membri.FirstOrDefault(m => Initiale(m.Denumire) == k.Replace(" ", string.Empty));
        return gasit?.Denumire ?? text.Trim();
    }

    private static string Initiale(string denumire) =>
        string.Concat(ValoriFoaie.Cheie(denumire).Split(' ').Where(c => c.Length > 0 && c is not ("srl" or "sa" or "sc" or "s.r.l." or "s.a.")).Select(c => c[0]));

    // ---- avansul

    private static IEnumerable<TransaAvans> TranseDin(IEnumerable<string> texte, List<string> note)
    {
        var lista = texte.ToList();
        foreach (var t in lista)
        {
            var m = SumaProcente().Match(t);
            if (m.Success)
                return ValoriFoaie.Procente(m.Value).Select((p, i) => new TransaAvans(i + 1, p, null));
        }

        foreach (var t in lista)
        {
            var m = TranseLaFel().Match(ValoriFoaie.Cheie(t));
            if (m.Success)
            {
                var n = m.Groups[1].Value switch { "doua" or "2" => 2, "trei" or "3" => 3, "patru" or "4" => 4, _ => 1 };
                var p = decimal.Parse(m.Groups[2].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) / 100;
                return Enumerable.Range(1, n).Select(i => new TransaAvans(i, p, null));
            }
        }

        // Un procent în etichetă / text („Avans contractual 10% EUR”, „20% în tranșe”) sau o fracție (0,5).
        foreach (var t in lista)
            if (ValoriFoaie.Procente(t) is [var p] && p > 0)
                return [new TransaAvans(1, p, null)];
        foreach (var t in lista)
            if (ValoriFoaie.Numar(t) is { } f && f is > 0 and <= 1 && Numeric().IsMatch(t.Trim()))
                return [new TransaAvans(1, f, null)];
        if (lista.Count > 0)
            note.Add("Avansul contractual nu a putut fi împărțit pe tranșe: verifică-l.");
        return [];
    }

    // ---- facturile și plățile

    private static (List<DateFacturaContract>, List<PlataCitita>) Facturi(FoaieTabel? foaie, IReadOnlyList<MembruAsociere> membri, List<string> note)
    {
        var facturi = new List<DateFacturaContract>();
        var plati = new List<PlataCitita>();
        if (foaie is null || Tabel(foaie, "nr. factur", "nr factur") is not { } t)
            return (facturi, plati);
        int? C(params string[] k) => k.Select(x => t.Coloana(x)).FirstOrDefault(x => x is not null);
        var cLuna = C("luna");
        var cMembru = C("partener facturat", "beneficiar");
        var cNr = C("nr. factur", "nr factur");
        var cData = t.Antet.Select((a, i) => (Cheie: ValoriFoaie.Cheie(a), i)).FirstOrDefault(x => x.Cheie is "data" or "data facturii").i is var cd && t.Antet.Count > 0 && ValoriFoaie.Cheie(t.Antet.ElementAtOrDefault(cd)).StartsWith("data", StringComparison.Ordinal) ? cd : (int?)null;
        var cSit = C("situatie de plata");
        var cTip = t.Antet.Select((a, i) => (Cheie: ValoriFoaie.Cheie(a), i)).Where(x => x.Cheie == "tip").Select(x => (int?)x.i).FirstOrDefault();
        var cEur = C("lucrari eur", "eur fara tva", "lucrari ron", "valoare lucrari");
        var cStorno = C("storno");
        var cCurs = t.Antet.Select((a, i) => (Cheie: ValoriFoaie.Cheie(a), i)).Where(x => x.Cheie.StartsWith("curs", StringComparison.Ordinal)).Select(x => (int?)x.i).FirstOrDefault();
        var cNet = C("net", "ron fara tva");
        var cTva = t.Antet.Select((a, i) => (Cheie: ValoriFoaie.Cheie(a), i)).Where(x => x.Cheie.StartsWith("tva", StringComparison.Ordinal)).Select(x => (int?)x.i).FirstOrDefault();
        var cTotal = C("total factura", "total ron", "ron cu tva");
        var cPlata = C("plata ron");
        var cStatus = t.Antet.Select((a, i) => (Cheie: ValoriFoaie.Cheie(a), i)).Where(x => x.Cheie.StartsWith("status", StringComparison.Ordinal)).Select(x => (int?)x.i).FirstOrDefault();
        var cObs = C("observatii");
        var cBrut = C("brut");
        var cBaza = C("baza cursului");
        string? T(Rand r, int? c) => c is { } x ? r.Text(x) : null;
        decimal? N(Rand r, int? c) => c is { } x ? ValoriFoaie.Numar(r.Text(x)) : null;

        foreach (var r in t.Randuri)
        {
            var nr = T(r, cNr);
            var prima = ValoriFoaie.Cheie(r.Text(0));
            if (prima.StartsWith("total", StringComparison.Ordinal) || prima.StartsWith("control", StringComparison.Ordinal))
                break;
            if (nr is null || EsteGol(nr))
                continue;
            var tipText = ValoriFoaie.Cheie(T(r, cTip));
            var tip = tipText.Contains("avans") && !tipText.Contains("storn") ? TipFacturaContract.Avans
                : tipText.Contains("livrare") ? TipFacturaContract.Livrare : TipFacturaContract.Lucrari;
            int? transa = TransaNr().Match(tipText) is { Success: true } tm ? int.Parse(tm.Groups[1].Value, Ro) : null;
            var curs = N(r, cCurs);
            var net = N(r, cNet) ?? 0;
            var tva = N(r, cTva) ?? 0;
            var total = N(r, cTotal) ?? 0;
            var brut = N(r, cBrut);
            var eur = N(r, cEur);
            var storno = N(r, cStorno);
            // Fără coloana în EUR: din RON, la cursul de pe factură (avansul = netul; lucrările = brutul; stornarea = brut − net).
            if (eur is null && curs is > 0)
            {
                eur = tip == TipFacturaContract.Avans ? Math.Round(net / curs.Value, 2) : brut is { } b ? Math.Round(b / curs.Value, 2) : null;
                if (storno is null && brut is { } b2 && tip != TipFacturaContract.Avans)
                    storno = Math.Round((b2 - net) / curs.Value, 2);
            }

            var status = T(r, cStatus);
            var observatii = string.Join("; ", new[] { status, T(r, cObs), T(r, cBaza) is { } bz ? $"curs: {bz}" : null, brut is { } br && eur is null ? $"brut {br.ToString("N2", Ro)} RON" : null }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            var data = ValoriFoaie.Data(T(r, cData));
            if (data is null)
            {
                note.Add($"Factura {nr}: data lipsește.");
                continue;
            }

            var ks = ValoriFoaie.Cheie(status);
            DateOnly? scadenta = ks.Contains("scadent") ? ValoriFoaie.Data(status) : null;
            facturi.Add(new DateFacturaContract(MembruDin(T(r, cMembru), membri), null, nr, data.Value, tip, transa, T(r, cSit), T(r, cLuna), eur ?? 0,
                tip == TipFacturaContract.Avans ? 0 : storno ?? 0, curs, net, tva, total, scadenta, observatii.Length == 0 ? null : observatii));

            // Plățile: „Achitat 19.05.2026” = confirmată; „BO emis” = bilet la ordin neconfirmat; coloana „Plată RON”.
            var suma = N(r, cPlata);
            if (ks.Contains("achitat"))
                plati.Add(new PlataCitita(nr, new DatePlata(null, ValoriFoaie.Data(status) ?? data.Value, suma is > 0 ? suma.Value : total, InstrumentPlata.OrdinDePlata, null, null, true, status)));
            else if (ks.Contains("bo emis") || ks.Contains("bilet"))
                plati.Add(new PlataCitita(nr, new DatePlata(null, data.Value, suma is > 0 ? suma.Value : total, InstrumentPlata.BiletLaOrdin,
                    BoNr().Match(T(r, cObs) ?? string.Empty) is { Success: true } bo ? bo.Groups[1].Value : null, null, false, status)));
            else if (suma is > 0)
                plati.Add(new PlataCitita(nr, new DatePlata(null, data.Value, suma.Value, InstrumentPlata.OrdinDePlata, null, null, true, "Plată din fișă (data neprecizată)")));
        }

        return (facturi, plati);
    }

    // ---- instrumentele de garanție

    private static IEnumerable<DateInstrument> Instrumente(TabelCitit t)
    {
        var cDesc = t.Coloana("sursa") ?? t.Coloana("instrument") ?? 0;
        var cEmitent = t.Coloana("emitent");
        var cBenef = t.Coloana("beneficiar");
        var cNr = t.Coloana("nr. document") ?? t.Coloana("nr document");
        var cData = t.Coloana("data emiterii");
        var cVal = t.Coloana("valoare");
        var cExp = t.Coloana("valabilitate") ?? t.Coloana("scadenta");
        var cMoneda = t.Coloana("moneda");
        var cStare = t.Coloana("stare");
        var moneda = ValoriFoaie.Cheie(cVal is { } cv ? t.Antet.ElementAtOrDefault(cv) : null) is var m && m.Contains("eur") ? "EUR" : m.Contains("ron") ? "RON" : null;
        foreach (var r in t.Randuri)
        {
            var desc = r.Text(cDesc);
            if (desc is null || ValoriFoaie.Cheie(desc) == "total" || ValoriFoaie.Cheie(r.Text(0)) == "total") continue;
            var k = ValoriFoaie.Cheie(desc);
            var tip = k.Contains("avans") && !k.StartsWith("bo", StringComparison.Ordinal) && !k.Contains("bilet") ? TipInstrument.GarantieAvans
                : k.Contains("buna executie") ? TipInstrument.GarantieBunaExecutie
                : k.Contains("tehnic") ? TipInstrument.GarantieTehnica
                : k.StartsWith("bo", StringComparison.Ordinal) || k.Contains("bilet") ? TipInstrument.BiletLaOrdin : TipInstrument.Altul;
            var numar = cNr is { } cn ? r.Text(cn) : NrInstrument().Match(desc) is { Success: true } nm ? nm.Groups[1].Value : null;
            // „SGB nr. … – Garanti Bank”: emitentul după liniuță; la biletul la ordin textul de după liniuță e o descriere.
            var emitent = cEmitent is { } ce ? r.Text(ce) : tip != TipInstrument.BiletLaOrdin && desc.Contains('–') ? desc[(desc.IndexOf('–') + 1)..].Trim() : null;
            yield return new DateInstrument(tip, desc, emitent, numar, cBenef is { } cb ? r.Text(cb) : null, cVal is { } cvv ? ValoriFoaie.Numar(r.Text(cvv)) : null,
                cMoneda is { } cm && r.Text(cm) is { Length: 3 } mon ? mon.ToUpperInvariant() : moneda, cData is { } cdt ? ValoriFoaie.Data(r.Text(cdt)) : null,
                cExp is { } cx ? ValoriFoaie.Data(r.Text(cx)) : null, cStare is { } cst ? r.Text(cst) : null, null);
        }
    }

    private static PrioritateActiune Prioritate(string? text)
    {
        var k = ValoriFoaie.Cheie(text);
        return k.StartsWith("critic", StringComparison.Ordinal) ? PrioritateActiune.Critica
            : k.StartsWith("ridicat", StringComparison.Ordinal) ? PrioritateActiune.Ridicata
            : k.StartsWith("medi", StringComparison.Ordinal) ? PrioritateActiune.Medie : PrioritateActiune.Normala;
    }

    private static StareActiune StareActiuneDin(string? text)
    {
        var k = ValoriFoaie.Cheie(text);
        return k.StartsWith("inchis", StringComparison.Ordinal) || k.StartsWith("closed", StringComparison.Ordinal) || k.StartsWith("rezolvat", StringComparison.Ordinal) ? StareActiune.Inchisa
            : k.Contains("progres") || k.Contains("lucru") ? StareActiune.InLucru : StareActiune.Deschisa;
    }

    // ---- citirea foilor

    private sealed record Pereche(string Eticheta, string Cheie, string? Valoare, string? Obs);

    /// <summary>Rândurile „etichetă | valoare | observație” (foaia de date generale).</summary>
    private static List<Pereche> Perechi(FoaieTabel f) =>
        Enumerable.Range(0, f.Randuri.Count)
            .Where(i => f.Celula(i, 0) is not null && f.Celula(i, 1) is not null)
            .Select(i => new Pereche(f.Celula(i, 0)!, ValoriFoaie.Cheie(f.Celula(i, 0)), f.Celula(i, 1), f.Celula(i, 2)))
            .Where(p => p.Cheie is not ("indicator" or "informatii"))
            .ToList();

    private sealed record Rand(IReadOnlyList<string?> Celule)
    {
        public string? Text(int c) => c >= 0 && c < Celule.Count && !string.IsNullOrWhiteSpace(Celule[c]) ? Celule[c]!.Trim() : null;
    }

    private sealed record TabelCitit(IReadOnlyList<string> Antet, IReadOnlyList<Rand> Randuri)
    {
        /// <summary>Coloana al cărei antet începe cu (sau conține) cheia.</summary>
        public int? Coloana(string cheie)
        {
            for (var i = 0; i < Antet.Count; i++)
                if (ValoriFoaie.Cheie(Antet[i]).StartsWith(cheie, StringComparison.Ordinal))
                    return i;
            for (var i = 0; i < Antet.Count; i++)
                if (ValoriFoaie.Cheie(Antet[i]).Contains(cheie))
                    return i;
            return null;
        }
    }

    /// <summary>Primul tabel al cărui antet (un rând cu cel puțin două celule) conține una din chei; rândurile până la primul gol.</summary>
    private static TabelCitit? Tabel(FoaieTabel f, params string[] chei) =>
        Tabele(f).FirstOrDefault(t => t.Antet.Any(a => chei.Any(k => ValoriFoaie.Cheie(a).Contains(k))));

    /// <summary>Tabelele foii: un rând cu cel puțin două celule text urmat de rânduri, până la primul rând gol.</summary>
    private static IEnumerable<TabelCitit> Tabele(FoaieTabel f)
    {
        var i = 0;
        while (i < f.Randuri.Count)
        {
            var antet = f.Randuri[i];
            var pline = antet.Count(c => !string.IsNullOrWhiteSpace(c));
            if (pline >= 2 && antet.Where(c => !string.IsNullOrWhiteSpace(c)).All(c => ValoriFoaie.Numar(c) is null || !Numeric().IsMatch(c!.Trim())))
            {
                var randuri = new List<Rand>();
                var j = i + 1;
                while (j < f.Randuri.Count && f.Randuri[j].Any(c => !string.IsNullOrWhiteSpace(c)))
                    randuri.Add(new Rand(f.Randuri[j++]));
                yield return new TabelCitit(antet.Select(c => c ?? string.Empty).ToList(), randuri);
                i = j;
            }
            else
                i++;
        }
    }

    [GeneratedRegex(@"^AA\s*\d+[^—]*—\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixAct();

    [GeneratedRegex(@"CUI[:\s]*((?:RO)?\s?\d{2,10})", RegexOptions.IgnoreCase)]
    private static partial Regex CuiRegex();

    [GeneratedRegex(@"(?:AA|act adi\w+(?: nr\.?)?)\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NrAct();

    [GeneratedRegex(@"\s+(?:și|si|\+|,)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Separatori();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Paranteze();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?\s*%(?:\s*\+\s*\d+(?:[.,]\d+)?\s*%)+")]
    private static partial Regex SumaProcente();

    [GeneratedRegex(@"(doua|trei|patru|2|3|4) transe a cate (\d+(?:[.,]\d+)?)%")]
    private static partial Regex TranseLaFel();

    [GeneratedRegex(@"(\d{1,3}) (?:de )?zile")]
    private static partial Regex Zile();

    [GeneratedRegex(@"(\d{1,3}) (?:de )?luni")]
    private static partial Regex Luni();

    [GeneratedRegex(@"transa (\d+)")]
    private static partial Regex TransaNr();

    [GeneratedRegex(@"BO nr\.?\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex BoNr();

    [GeneratedRegex(@"nr\.?\s*(\d{4,})", RegexOptions.IgnoreCase)]
    private static partial Regex NrInstrument();

    [GeneratedRegex(@"^-?\d+(\.\d+)?([eE]-?\d+)?$")]
    private static partial Regex Numeric();
}
