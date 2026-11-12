using System.Text.Json;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Devize.Import.Isdp.Excel;

/// <summary>
/// Exporturile ISDP (foile citite) → modelul canonic al modulului Devize (docs/import-isdp.md §7). Logică pură:
/// <list type="number">
/// <item>foile „Deviz ofertă cu resurse” (câte una pe stadiu fizic) → <see cref="ParserDevizResurse"/> + <see cref="VerificatorDeviz"/>;</item>
/// <item>lista F3 (opțională) → <see cref="ReconciliereF3"/>: cursul și prețurile de ofertă pe articol (verificate cu F3);
/// fără F3, prețul de ofertă se calculează cu <see cref="PretOferta"/> la cursul dat (lei = 1);</item>
/// <item>arborele: stadiul fizic → capitole → articole → resurse (cantitate totală, consum unitar, tip dedus); articolul
/// „resursă directă” primește resursa lui (consum 1); cotele ISDP (procente) devin fracții, rotunjirea „Fara”.</item>
/// </list>
/// </summary>
public static class ConversieIsdp
{
    public static RezultatImport<DevizCanonic> Converteste(IReadOnlyList<FoaieCalcul> foi, decimal? curs = null, string? moneda = null)
    {
        var probleme = new List<ProblemaImport>();
        var foiDeviz = foi.Where(f => DetectorIsdp.Detecteaza(f).Tip == TipRaportIsdp.DevizCuResurse).ToList();
        var foiF3 = foi.Where(f => DetectorIsdp.Detecteaza(f).Tip == TipRaportIsdp.ListaCantitatiF3).ToList();
        if (foiDeviz.Count == 0)
        {
            return RezultatImport<DevizCanonic>.Esuat(foiF3.Count > 0
                ? "Lista F3 nu se poate importa singură: nu are simbolurile normelor și nici resursele. Încarcă „Deviz ofertă cu resurse” (.xls) din ISDP, cu F3 ca al doilea fișier."
                : "Fișierul nu e un export ISDP „Deviz ofertă cu resurse” (lipsește antetul WBS / Simbol).");
        }

        // 1. Devizele (o foaie = un stadiu fizic).
        var devize = new List<DevizIsdp>();
        foreach (var foaie in foiDeviz)
        {
            var r = ParserDevizResurse.Parseaza(foaie);
            probleme.AddRange(r.Probleme.Select(p => Problema(p, foaie.Nume)));
            if (r.Valoare is null)
                return new RezultatImport<DevizCanonic>(null, probleme, new Dictionary<string, string>());
            var (_, verificare) = VerificatorDeviz.Verifica(r.Valoare);
            probleme.AddRange(verificare.Select(p => Problema(p, foaie.Nume)));
            devize.Add(r.Valoare);
        }

        if (devize.Any(d => d.Recapitulatie?.ProcentCam is null || d.Recapitulatie.ProcentIndirecte is null || d.Recapitulatie.ProcentProfit is null || d.Recapitulatie.CotaTva is null))
        {
            probleme.Add(new(Severitate.Eroare, "recapitulație", "Devizul nu conține procentele recapitulației (CAM, cheltuieli indirecte, profit, TVA)."));
            return new RezultatImport<DevizCanonic>(null, probleme, new Dictionary<string, string>());
        }

        var recap = devize[0].Recapitulatie!;
        if (devize.Skip(1).Any(d => d.Recapitulatie!.ProcentCam != recap.ProcentCam || d.Recapitulatie.ProcentIndirecte != recap.ProcentIndirecte
                                     || d.Recapitulatie.ProcentProfit != recap.ProcentProfit || d.Recapitulatie.CotaTva != recap.CotaTva))
        {
            probleme.Add(new(Severitate.Eroare, "recapitulație", "Foile devizului au procente diferite de CAM / indirecte / profit / TVA: importă-le separat."));
        }

        if (foiDeviz.Count > 1)
        {
            probleme.Add(new(Severitate.Informare, "fișier", $"Fișierul are {foiDeviz.Count} stadii fizice (foi); se importă într-o singură versiune, câte un capitol pe stadiu."));
        }

        // 2. Lista F3: cursul și prețurile de ofertă (pe stadiul cu același nume, altfel pe singurul stadiu).
        var preturi = new Dictionary<NodIsdp, PretOfertaArticol>();
        var monedaOferta = (moneda ?? "RON").Trim().ToUpperInvariant();
        var cursOferta = monedaOferta == "RON" ? 1m : curs;
        if (foiF3.Count > 0)
        {
            if (foiF3.Count > 1)
                probleme.Add(new(Severitate.Avertisment, "F3", $"Am găsit {foiF3.Count} liste F3; folosesc prima."));
            var f3 = ParserListaF3.Parseaza(foiF3[0]);
            probleme.AddRange(f3.Probleme.Select(p => Problema(p, foiF3[0].Nume)));
            if (f3.Valoare is { } lista)
            {
                var deviz = devize.FirstOrDefault(d => string.Equals(d.Antet.StadiuFizic, lista.Antet.StadiuFizic, StringComparison.OrdinalIgnoreCase))
                            ?? (devize.Count == 1 ? devize[0] : null);
                if (deviz is null)
                {
                    probleme.Add(new(Severitate.Eroare, "F3", $"Lista F3 e pentru stadiul „{lista.Antet.StadiuFizic}”, care nu e în deviz."));
                }
                else
                {
                    monedaOferta = lista.Moneda;
                    var reconciliere = ReconciliereF3.Reconciliaza(deviz, lista, curs);
                    probleme.AddRange(reconciliere.Probleme.Select(p => Problema(p, foiF3[0].Nume)));
                    cursOferta = reconciliere.Curs;
                    foreach (var p in reconciliere.Perechi.Where(p => p.PretCalculat is not null))
                        preturi[p.Articol] = p.PretCalculat!;
                    if (reconciliere.Curs is { } k)
                        probleme.Add(new(Severitate.Informare, "F3",
                            $"Curs {(curs is null ? "dedus" : "dat")}: {k} lei / {lista.Moneda}; prețuri de ofertă identice cu F3: {reconciliere.ComponenteIdentice} componente, diferite: {reconciliere.ComponenteDiferite}."));
                }
            }
        }

        if (cursOferta is not { } cursFolosit)
        {
            probleme.Add(new(Severitate.Avertisment, "ofertă", $"Fără curs pentru {monedaOferta}: articolele nu primesc preț de ofertă (îl poți calcula dând cursul)."));
        }
        else
        {
            var p = ParametriOferta.Din(recap, cursFolosit, monedaOferta);
            foreach (var a in devize.SelectMany(d => d.Articole()).Where(a => !preturi.ContainsKey(a)))
                preturi[a] = PretOferta.Calculeaza(a, p);
        }

        // 3. Arborele canonic.
        var capitole = devize.SelectMany(d => d.Radacini).Select(r => Capitol(r, preturi, radacina: true)).ToList();
        var declarat = Declarat(devize);
        var antet = devize[0].Antet;
        var parametri = new ParametriRecapitulatie(recap.ProcentCam!.Value / 100, recap.ProcentIndirecte!.Value / 100, recap.ProcentProfit!.Value / 100, recap.CotaTva!.Value / 100)
            .Cu(PoliticaRotunjire.Fara);
        var deviz0 = new DevizCanonic(Denumire(antet), capitole, parametri, declarat)
        {
            Antet = new AntetCanonic(Gol(antet.Obiectiv), Gol(antet.Obiect), devize.Count == 1 ? Gol(antet.StadiuFizic) : null),
            Moneda = monedaOferta,
            Curs = monedaOferta == "RON" ? null : cursOferta,
            PoliticaRotunjire = PoliticaRotunjire.Fara,
            TotaluriDeclarateJson = JsonSerializer.Serialize(devize.Select(d => new { foaie = d.NumeFoaie, recapitulatie = d.Recapitulatie })),
        };
        return new RezultatImport<DevizCanonic>(deviz0, probleme, new Dictionary<string, string> { ["moneda"] = monedaOferta });
    }

    private static CapitolCanonic Capitol(NodIsdp grup, IReadOnlyDictionary<NodIsdp, PretOfertaArticol> preturi, bool radacina = false) => new(
        grup.Wbs.Split('.')[^1],
        grup.Denumire,
        radacina ? TipCapitol.StadiuFizic : TipCapitol.Capitol,
        grup.Copii.Where(c => c.Tip == TipNodIsdp.Grup).Select(c => Capitol(c, preturi)).ToList(),
        grup.Copii.Where(c => c.Tip == TipNodIsdp.Articol).Select(a => Articol(a, preturi.GetValueOrDefault(a))).ToList(),
        grup.Wbs);

    private static ArticolCanonic Articol(NodIsdp a, PretOfertaArticol? oferta)
    {
        var consumuri = a.Copii.Where(c => c.Tip == TipNodIsdp.Resursa)
            .Select(r => new ConsumCanonic(r.Simbol, r.Denumire, Tip(r.TipResursa), r.UnitateMasura, r.ConsumUnitar, r.PretUnitar.Total, r.Cantitate, r.TipResursaDedus))
            .ToList();
        // Resursa pusă direct ca articol (ex. „Cherestea rășinoase”, 5 mc): devine resursa lui, cu consum 1.
        if (a.TipArticol == TipArticolIsdp.ResursaDirecta && consumuri.Count == 0)
        {
            var tip = ValoriIsdp.Tipuri.FirstOrDefault(t => a.PretUnitar[t] != 0);
            consumuri.Add(new ConsumCanonic(a.Simbol, a.Denumire, Tip(tip), a.UnitateMasura, 1, a.PretUnitar.Total, a.Cantitate));
        }

        return new ArticolCanonic(
            a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, Valori(a.PretUnitar), consumuri, a.Rand.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (TipArticol)(int)a.TipArticol, a.SimbolBaza, a.Wbs,
            oferta is null ? null : Componente(oferta, c => c.PretUnitar),
            oferta is null ? null : Componente(oferta, c => c.Total));
    }

    private static TotaluriDeclarate? Declarat(IReadOnlyList<DevizIsdp> devize)
    {
        var recap = devize.Select(d => d.Recapitulatie!).ToList();
        if (recap.Any(r => r.T0ValoareDirecta is null || r.T3CuProfit is null))
            return null;
        // „Valoare echipamente” nu există în fișier: echipamentul = total − celelalte.
        var t0 = recap.Select(r => r.T0ValoareDirecta!).Aggregate(ValoriResurse.Zero, (s, t) => s + new ValoriResurse(
            t.Material, t.Manopera, t.Utilaj, t.Transport, t.Total - t.Material - t.Manopera - t.Utilaj - t.Transport));
        return new TotaluriDeclarate(t0, recap.Sum(r => r.T3CuProfit!.Total), recap.All(r => r.ValoareCuTva is not null) ? recap.Sum(r => r.ValoareCuTva!.Value) : null);
    }

    private static ValoriResurse Componente(PretOfertaArticol p, Func<(decimal PretUnitar, decimal Total), decimal> ce) => new(
        ce(p.Componente[TipResursaIsdp.Material]), ce(p.Componente[TipResursaIsdp.Manopera]), ce(p.Componente[TipResursaIsdp.Utilaj]),
        ce(p.Componente[TipResursaIsdp.Transport]), ce(p.Componente[TipResursaIsdp.Echipament]));

    private static ValoriResurse Valori(ValoriIsdp v) => new(v.Material, v.Manopera, v.Utilaj, v.Transport, v.Echipament);

    private static TipResursa Tip(TipResursaIsdp t) => (TipResursa)(int)t;

    private static string Denumire(AntetIsdp antet)
    {
        var titlu = antet.Titlu.Trim();
        const string prefix = "Deviz Oferta - ";
        if (titlu.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            titlu = titlu[prefix.Length..];
        titlu = string.IsNullOrWhiteSpace(titlu) ? antet.StadiuFizic : titlu;
        return titlu.Length > 300 ? titlu[..300] : titlu;
    }

    private static string? Gol(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ProblemaImport Problema(ProblemaIsdp p, string foaie) => new(
        p.Severitate switch { SeveritateIsdp.Eroare => Severitate.Eroare, SeveritateIsdp.Avertisment => Severitate.Avertisment, _ => Severitate.Informare },
        string.IsNullOrWhiteSpace(foaie) ? p.Locatie : $"{foaie}: {p.Locatie}",
        p.Mesaj);
}
