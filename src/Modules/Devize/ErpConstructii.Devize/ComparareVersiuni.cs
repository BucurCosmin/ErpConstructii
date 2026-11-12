using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Devize;

public enum TipDiferentaVersiune
{
    Adaugat,
    Modificat,
    Sters,
}

/// <summary>Datele unui articol într-una din cele două versiuni comparate (valoarea = cost direct, cu rotunjirea versiunii).</summary>
public sealed record ArticolComparat(
    Guid ArticolId, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, ValoriResurse PretUnitar, decimal Valoare, string Capitol);

/// <param name="Campuri">Ce s-a schimbat la un articol modificat: „simbol”, „denumire”, „um”, „cantitate”, „pret”.</param>
public sealed record DiferentaVersiune(
    Guid CheieStabila, TipDiferentaVersiune Tip, ArticolComparat? Vechi, ArticolComparat? Nou, IReadOnlyList<string> Campuri)
{
    /// <summary>Cu cât s-a schimbat costul direct al articolului (nou − vechi).</summary>
    public decimal DiferentaValoare => (Nou?.Valoare ?? 0) - (Vechi?.Valoare ?? 0);
}

public sealed record ComparatieVersiuni(
    IReadOnlyList<DiferentaVersiune> Diferente, RecapitulatieDeviz RecapitulatieVeche, RecapitulatieDeviz RecapitulatieNoua);

/// <summary>
/// Ce s-a schimbat între două versiuni ale aceluiași deviz (REQ-DEV-03): articolele se potrivesc după cheia stabilă
/// (REQ-DEV-05), deci și cele cu simbolul sau denumirea schimbate se recunosc. Logică pură.
/// </summary>
public static class ComparareVersiuni
{
    public static ComparatieVersiuni Compara(VersiuneDeviz vechi, VersiuneDeviz noua)
    {
        var dinVechi = Articole(vechi);
        var dinNoua = Articole(noua);
        var diferente = new List<DiferentaVersiune>();

        // În ordinea versiunii noi (cum le vede utilizatorul), apoi cele scoase, în ordinea lor veche.
        foreach (var (cheie, n) in dinNoua)
        {
            if (!dinVechi.TryGetValue(cheie, out var v))
            {
                diferente.Add(new DiferentaVersiune(cheie, TipDiferentaVersiune.Adaugat, null, n, []));
                continue;
            }

            var campuri = new List<string>();
            if (Text(v.Simbol) != Text(n.Simbol))
                campuri.Add("simbol");
            if (Text(v.Denumire) != Text(n.Denumire))
                campuri.Add("denumire");
            if (Text(v.UnitateMasura) != Text(n.UnitateMasura))
                campuri.Add("um");
            if (v.Cantitate != n.Cantitate)
                campuri.Add("cantitate");
            if (v.PretUnitar != n.PretUnitar)
                campuri.Add("pret");
            if (campuri.Count > 0)
                diferente.Add(new DiferentaVersiune(cheie, TipDiferentaVersiune.Modificat, v, n, campuri));
        }

        diferente.AddRange(dinVechi.Where(x => !dinNoua.ContainsKey(x.Key))
            .Select(x => new DiferentaVersiune(x.Key, TipDiferentaVersiune.Sters, x.Value, null, [])));

        return new ComparatieVersiuni(diferente, vechi.Recapitulatie(), noua.Recapitulatie());
    }

    /// <summary>Textul fără diferențele de spații (salvarea curăță spațiile de la capete; importul le poate păstra).</summary>
    private static string Text(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<Guid, ArticolComparat> Articole(VersiuneDeviz v)
    {
        var p = v.Parametri();
        var rezultat = new Dictionary<Guid, ArticolComparat>();
        foreach (var c in v.Capitole)
        {
            var capitol = string.Join(" / ", v.Cale(c).Select(x => x.Denumire));
            foreach (var a in c.Articole.OrderBy(x => x.Ordine))
            {
                rezultat.TryAdd(a.CheieStabila, new ArticolComparat(a.Id, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretUnitar,
                    CalculatorDeviz.ValoareLinie(a.CaLinie(), p.RotunjireLinie).Total, capitol));
            }
        }

        // Ordinea arborelui (capitolele în ordinea lor, cu subcapitolele), nu ordinea listei plate.
        var ordine = v.Articole().Select(a => a.CheieStabila).ToList();
        return ordine.Where(rezultat.ContainsKey).Distinct().ToDictionary(k => k, k => rezultat[k]);
    }
}
