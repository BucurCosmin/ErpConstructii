using ErpConstructii.Devize.Calcul;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Devize.Import;

/// <summary>
/// Reguli de validare comune tuturor formatelor (REQ-IMP-03, pasul „Validare”).
/// Rulează după importator și înainte de previzualizare.
/// </summary>
public static class ValidatorDevizCanonic
{
    /// <summary>Diferența maximă acceptată între totalul recalculat și cel declarat.</summary>
    public const decimal TolerantaLei = 0.05m;

    public static IReadOnlyList<ProblemaImport> Valideaza(DevizCanonic deviz)
    {
        var probleme = new List<ProblemaImport>();

        if (deviz.Obiecte.Count == 0)
        {
            probleme.Add(new(Severitate.Eroare, "deviz", "Devizul nu conține niciun obiect."));
            return probleme;
        }

        foreach (var obiect in deviz.Obiecte)
        {
            foreach (var categorie in obiect.Categorii)
            {
                var simboluriVazute = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var articol in categorie.Articole)
                {
                    var loc = $"{obiect.Cod} / {categorie.Cod} / {articol.Simbol}{(articol.Rand is null ? "" : $" (rând {articol.Rand})")}";
                    ValideazaArticol(articol, loc, simboluriVazute, probleme);
                }
            }
        }

        if (deviz.Parametri is not null && deviz.Totaluri is not null)
        {
            ComparaTotaluri(deviz, probleme);
        }
        else if (deviz.Parametri is null)
        {
            probleme.Add(new(Severitate.Avertisment, "recapitulație",
                "Fișierul nu conține cotele recapitulației; se vor folosi setările firmei."));
        }

        return probleme;
    }

    private static void ValideazaArticol(ArticolCanonic a, string loc, HashSet<string> simboluri, List<ProblemaImport> probleme)
    {
        if (string.IsNullOrWhiteSpace(a.UnitateMasura))
        {
            probleme.Add(new(Severitate.Eroare, loc, "Lipsește unitatea de măsură."));
        }

        if (a.Cantitate <= 0)
        {
            probleme.Add(new(Severitate.Eroare, loc, $"Cantitate invalidă: {a.Cantitate}."));
        }

        if (a.PretUnitar.Total == 0)
        {
            probleme.Add(new(Severitate.Avertisment, loc, "Articol fără preț."));
        }

        if (!simboluri.Add(a.Simbol))
        {
            probleme.Add(new(Severitate.Avertisment, loc, "Simbolul apare de mai multe ori în aceeași categorie."));
        }

        foreach (var c in a.Consumuri.Where(c => c.PretUnitar == 0 && c.ConsumUnitar != 0))
        {
            probleme.Add(new(Severitate.Avertisment, loc, $"Resursa {c.CodResursa} ({c.Denumire}) nu are preț."));
        }

        // Prețul unitar pe resursă trebuie să fie suma consumurilor × prețurilor resurselor de acel tip.
        if (a.Consumuri.Count > 0)
        {
            foreach (var tip in Enum.GetValues<TipResursa>())
            {
                var dinConsumuri = Rotunjire.Lei.Aplica(a.Consumuri.Where(c => c.Tip == tip).Sum(c => c.ConsumUnitar * c.PretUnitar));
                var declarat = a.PretUnitar[tip];
                if (Math.Abs(dinConsumuri - declarat) > 0.01m)
                {
                    probleme.Add(new(Severitate.Avertisment, loc,
                        $"Prețul unitar la {tip} ({declarat}) diferă de suma consumurilor ({dinConsumuri})."));
                }
            }
        }
    }

    private static void ComparaTotaluri(DevizCanonic deviz, List<ProblemaImport> probleme)
    {
        var linii = deviz.Obiecte
            .SelectMany(o => o.Categorii)
            .SelectMany(c => c.Articole)
            .Select(a => new LinieDeviz(a.Cantitate, a.PretUnitar));
        var recalculat = CalculatorDeviz.Calculeaza(linii, deviz.Parametri!);
        var declarat = deviz.Totaluri!;

        void Compara(string ce, decimal calculat, decimal? dinFisier)
        {
            if (dinFisier is { } v && Math.Abs(calculat - v) > TolerantaLei)
            {
                probleme.Add(new(Severitate.Eroare, "recapitulație",
                    $"{ce}: recalculat {calculat:N2} lei, în fișier {v:N2} lei (diferență {calculat - v:N2})."));
            }
        }

        if (declarat.CheltuieliDirecteResurse is { } d)
        {
            Compara("Material", recalculat.CheltuieliDirecteResurse.Material, d.Material);
            Compara("Manoperă", recalculat.CheltuieliDirecteResurse.Manopera, d.Manopera);
            Compara("Utilaj", recalculat.CheltuieliDirecteResurse.Utilaj, d.Utilaj);
            Compara("Transport", recalculat.CheltuieliDirecteResurse.Transport, d.Transport);
        }

        Compara("Total fără TVA", recalculat.TotalFaraTva, declarat.TotalFaraTva);
        Compara("Total cu TVA", recalculat.TotalCuTva, declarat.TotalCuTva);
    }
}
