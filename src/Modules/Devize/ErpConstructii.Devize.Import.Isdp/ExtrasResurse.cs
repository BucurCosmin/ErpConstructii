namespace ErpConstructii.Devize.Import.Isdp;

/// <summary>Unde se consumă o resursă: articolul și cantitatea (totală) pe acel articol.</summary>
public sealed record UtilizareResursa(string WbsArticol, string SimbolArticol, decimal Cantitate, decimal ConsumUnitar);

/// <summary>O linie din extrasul agregat de resurse (necesarul total pe deviz).</summary>
public sealed record LinieExtras(
    string Cod,
    string Denumire,
    string UnitateMasura,
    TipResursaIsdp Tip,
    bool TipDedus,
    decimal PretUnitar,
    decimal CantitateTotala,
    decimal ValoareTotala,
    IReadOnlyList<UtilizareResursa> Utilizari);

/// <summary>
/// Extrasul de resurse: necesarul total pe resursă pentru tot devizul (materiale, ore pe meserii,
/// ore utilaj, transport). Este punctul de plecare pentru APROVIZIONARE (necesar de materiale) și pentru
/// planificarea MANOPEREI și a UTILAJELOR; fiecare linie păstrează articolele din care provine, ca un
/// referat / o comandă să poată fi legată de articolul de deviz (REQ-GEN-02).
///
/// Articolele „resursă directă” (fără descompunere) intră ca resursă cu codul = simbolul articolului.
/// Cheia de agregare este codul resursei ISDP (trim); prețul unitar diferit pentru același cod este semnalat.
/// </summary>
public static class ExtrasResurse
{
    public static (IReadOnlyList<LinieExtras> Linii, IReadOnlyList<ProblemaIsdp> Probleme) Agrega(DevizIsdp deviz)
    {
        var probleme = new List<ProblemaIsdp>();
        var grupuri = new Dictionary<string, List<(NodIsdp Resursa, NodIsdp Articol, TipResursaIsdp Tip, bool Dedus, decimal Pret, decimal Valoare, decimal Consum)>>(StringComparer.Ordinal);

        foreach (var a in deviz.Articole())
        {
            if (a.Copii.Count == 0)
            {
                // Resursă introdusă direct: tipul = coloana cu valoare.
                var tip = ValoriIsdp.Tipuri.FirstOrDefault(t => a.PretUnitar[t] != 0, TipResursaIsdp.Material);
                Adauga(grupuri, a.Simbol, (a, a, tip, false, a.PretUnitar[tip], a.Valoare.Total, 1m));
                continue;
            }

            foreach (var r in a.Copii)
            {
                Adauga(grupuri, r.Simbol, (r, a, r.TipResursa, r.TipResursaDedus, r.PretUnitar[r.TipResursa], r.Valoare.Total, r.ConsumUnitar));
            }
        }

        var linii = new List<LinieExtras>();
        foreach (var (cod, lista) in grupuri)
        {
            var prima = lista[0];
            if (lista.Select(x => x.Pret).Distinct().Count() > 1)
            {
                probleme.Add(new(SeveritateIsdp.Avertisment, $"resursa {cod}", "Aceeași resursă are prețuri unitare diferite în articole diferite."));
            }

            if (lista.Select(x => TextIsdp.UnitateMasura(x.Resursa.UnitateMasura)).Distinct().Count() > 1)
            {
                probleme.Add(new(SeveritateIsdp.Eroare, $"resursa {cod}", "Aceeași resursă apare cu unități de măsură diferite."));
            }

            linii.Add(new LinieExtras(
                cod,
                prima.Resursa.Denumire,
                prima.Resursa.UnitateMasura,
                prima.Tip,
                lista.All(x => x.Dedus),
                prima.Pret,
                lista.Sum(x => x.Resursa.Cantitate),
                lista.Sum(x => x.Valoare),
                lista.Select(x => new UtilizareResursa(x.Articol.Wbs, x.Articol.Simbol, x.Resursa.Cantitate, x.Consum)).ToList()));
        }

        return (linii.OrderBy(l => l.Tip).ThenByDescending(l => l.ValoareTotala).ThenBy(l => l.Cod, StringComparer.Ordinal).ToList(), probleme);
    }

    private static void Adauga<T>(Dictionary<string, List<T>> grupuri, string cod, T element)
    {
        if (!grupuri.TryGetValue(cod, out var lista))
        {
            grupuri[cod] = lista = [];
        }

        lista.Add(element);
    }
}
