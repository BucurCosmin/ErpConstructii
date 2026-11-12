namespace ErpConstructii.Devize.Import.Isdp;

/// <summary>O pereche articol F3 ↔ articol din deviz, cu prețul de ofertă recalculat.</summary>
public sealed record PerecheF3(ArticolF3 F3, NodIsdp Articol, PretOfertaArticol? PretCalculat);

public sealed record RezultatReconciliere(
    decimal? Curs,
    IReadOnlyList<PerecheF3> Perechi,
    int ComponenteIdentice,
    int ComponenteDiferite,
    IReadOnlyList<ProblemaIsdp> Probleme);

/// <summary>
/// Leagă lista F3 de devizul cu resurse și verifică prețurile de ofertă.
///
/// Potrivirea se face POZIȚIONAL: articolul „c.a” din F3 = al a-lea articol din al c-lea capitol
/// (grupurile de sub stadiul fizic, în ordinea WBS). Pe fiecare pereche se verifică UM, cantitatea și
/// denumirea, ca o nepotrivire de ordine să fie prinsă imediat. F3 nu conține simbolurile normelor,
/// deci NU poate fi importat singur ca deviz complet — doar ca listă de cantități (vezi docs/import-isdp.md).
/// </summary>
public static class ReconciliereF3
{
    public static RezultatReconciliere Reconciliaza(DevizIsdp deviz, ListaF3 lista, decimal? curs = null)
    {
        var probleme = new List<ProblemaIsdp>();
        var capitoleDeviz = CapitoleDeviz(deviz);
        var perechi = new List<PerecheF3>();

        if (capitoleDeviz.Count != lista.Capitole.Count)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, "F3", $"Devizul are {capitoleDeviz.Count} capitole, lista F3 are {lista.Capitole.Count}."));
        }

        for (var c = 0; c < Math.Min(capitoleDeviz.Count, lista.Capitole.Count); c++)
        {
            var artDeviz = capitoleDeviz[c];
            var artF3 = lista.Capitole[c].Articole;
            if (artDeviz.Count != artF3.Count)
            {
                probleme.Add(new(SeveritateIsdp.Eroare, $"F3 capitol {lista.Capitole[c].Numar}",
                    $"Devizul are {artDeviz.Count} articole în capitol, F3 are {artF3.Count}."));
            }

            for (var i = 0; i < Math.Min(artDeviz.Count, artF3.Count); i++)
            {
                var (f, a) = (artF3[i], artDeviz[i]);
                var loc = $"F3 {f.Numar} ↔ {a.Wbs} {a.Simbol}";
                if (TextIsdp.UnitateMasura(f.UnitateMasura) != TextIsdp.UnitateMasura(a.UnitateMasura))
                {
                    probleme.Add(new(SeveritateIsdp.Eroare, loc, $"UM diferită: „{f.UnitateMasura}” vs „{a.UnitateMasura}”."));
                }

                if (f.Cantitate is { } q && Math.Abs(q - a.Cantitate) > 0.0001m)
                {
                    probleme.Add(new(SeveritateIsdp.Eroare, loc, $"Cantitate diferită: {q} vs {a.Cantitate}."));
                }

                var dF3 = TextIsdp.Normalizeaza(f.Denumire);
                var dDev = TextIsdp.Normalizeaza(a.Denumire);
                if (!dF3.StartsWith(dDev, StringComparison.Ordinal) && !dDev.StartsWith(dF3, StringComparison.Ordinal))
                {
                    probleme.Add(new(SeveritateIsdp.Avertisment, loc, $"Denumiri diferite: „{f.Denumire}” vs „{a.Denumire}”."));
                }

                perechi.Add(new PerecheF3(f, a, null));
            }
        }

        if (!lista.ArePreturi || deviz.Recapitulatie is not { } recap)
        {
            return new RezultatReconciliere(curs, perechi, 0, 0, probleme);
        }

        var faraCurs = ParametriOferta.Din(recap, 1m, lista.Moneda);
        var cursFolosit = curs ?? (lista.Moneda == "RON" ? 1m : PretOferta.DeduceCurs(deviz, lista, faraCurs));
        if (cursFolosit is not { } k)
        {
            probleme.Add(new(SeveritateIsdp.Avertisment, "F3", "Nu s-a putut determina cursul; prețurile nu au fost verificate."));
            return new RezultatReconciliere(null, perechi, 0, 0, probleme);
        }

        var p = faraCurs with { Curs = k };
        int identice = 0, diferite = 0;
        var cuPret = new List<PerecheF3>(perechi.Count);
        foreach (var per in perechi)
        {
            var calc = PretOferta.Calculeaza(per.Articol, p);
            foreach (var (tip, valF3) in per.F3.Componente)
            {
                var (pu, tot) = calc.Componente[tip];
                if (pu == valF3.PretUnitar && tot == valF3.Total)
                {
                    identice++;
                    continue;
                }

                diferite++;
                var sev = Math.Abs(pu - valF3.PretUnitar) <= 0.01m && Math.Abs(tot - valF3.Total) <= 0.01m
                    ? SeveritateIsdp.Avertisment
                    : SeveritateIsdp.Eroare;
                probleme.Add(new(sev, $"F3 {per.F3.Numar} {tip}",
                    $"Calculat {pu} / {tot}, în F3 {valF3.PretUnitar} / {valF3.Total} (curs {k})."));
            }

            cuPret.Add(per with { PretCalculat = calc });
        }

        return new RezultatReconciliere(k, cuPret, identice, diferite, probleme);
    }

    /// <summary>Articolele grupate pe capitole: copiii de tip grup ai rădăcinii (stadiul fizic), în ordine.</summary>
    private static List<List<NodIsdp>> CapitoleDeviz(DevizIsdp deviz)
    {
        var capitole = new List<List<NodIsdp>>();
        foreach (var radacina in deviz.Radacini)
        {
            var grupuri = radacina.Copii.Where(c => c.Tip == TipNodIsdp.Grup).ToList();
            if (grupuri.Count == 0)
            {
                capitole.Add(radacina.Copii.Where(c => c.Tip == TipNodIsdp.Articol).ToList());
                continue;
            }

            capitole.AddRange(grupuri.Select(g => g.Descendenti().Where(n => n.Tip == TipNodIsdp.Articol).ToList()));
        }

        return capitole;
    }
}
