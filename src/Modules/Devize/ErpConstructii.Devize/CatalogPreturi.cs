using ErpConstructii.Devize.Calcul;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Devize;

/// <summary>
/// Un preț unitar văzut într-un deviz: al unei resurse (material, manoperă, utilaj, transport, echipament) sau al unui
/// articol (costul direct unitar, cu <paramref name="Componente"/> pe resurse).
/// </summary>
/// <param name="Categorie">Tipul resursei, respectiv prefixul simbolului articolului (TS, AC, RP…), pentru filtrare.</param>
/// <param name="Cantitate">Cantitatea din deviz (ponderea în medie).</param>
public sealed record ObservatiePret(
    string Cod, string Denumire, string UnitateMasura, string Categorie, decimal Pret, decimal Cantitate,
    Guid VersiuneId, Guid ProiectId, DateTimeOffset Data, ValoriResurse? Componente = null);

/// <summary>Prețul unei resurse / al unui articol pe toate devizele firmei.</summary>
/// <param name="Medie">Media ponderată cu cantitățile (media simplă când cantitățile sunt zero).</param>
/// <param name="Abatere">(max − min) / min: cât diferă prețurile între devize (null când minimul e zero).</param>
public sealed record StatisticaPret(
    string Cod, string UnitateMasura, string Denumire, string Categorie, int Observatii, int Devize, int Proiecte,
    decimal Minim, decimal Maxim, decimal Medie, decimal Ultimul, DateTimeOffset DataUltimului, decimal Cantitate, decimal? Abatere);

/// <summary>Un rând al comparației: aceeași resursă / același articol în devizele alese.</summary>
/// <param name="Preturi">Prețul în fiecare versiune comparată (null = lipsește din ea), în ordinea versiunilor cerute.</param>
public sealed record RandComparatiePreturi(
    string Cod, string UnitateMasura, string Denumire, string Categorie, IReadOnlyList<decimal?> Preturi, decimal? Abatere, bool InToate);

/// <summary>Prețurile de achiziție ale unei resurse legate de un material din stoc (D-55), în UM-ul resursei.</summary>
/// <param name="MedieInterval">Media ponderată cu cantitățile pe ultimele luni cerute; null dacă nu sunt achiziții în interval.</param>
public sealed record StatisticaAchizitie(
    string Cod, string UnitateMasura, int Achizitii, decimal Minim, decimal Maxim, decimal Ultimul, DateOnly DataUltimului, string? FurnizorUltim,
    string DocumentUltim, decimal? MedieInterval, int AchizitiiInInterval, decimal? MinimInterval = null);

/// <summary>
/// Catalogul de prețuri din devizele importate (<c>editor-deviz.md</c> §5, sursa „DevizIsdp”) și compararea prețurilor între
/// devize (§7). Resursa se recunoaște după cod + UM, articolul după simbol + UM (codurile fără spații și majuscule, UM
/// fără majuscule), deci „mc” și „MC” sunt aceeași unitate, iar „to” și „kg” nu. Logică pură.
/// </summary>
public static class CatalogPreturi
{
    /// <summary>Prețurile unitare: 4 zecimale (prețurile din devize au de obicei 2–4).</summary>
    public static readonly Rotunjire RotunjirePret = new(4, MidpointRounding.AwayFromZero);

    public static (string Cod, string Um) Cheie(string cod, string unitateMasura) =>
        (Normalizeaza(cod).ToUpperInvariant(), Normalizeaza(unitateMasura).ToLowerInvariant());

    public static IReadOnlyList<StatisticaPret> Agrega(IEnumerable<ObservatiePret> observatii) =>
        observatii
            .GroupBy(o => Cheie(o.Cod, o.UnitateMasura))
            .Select(g =>
            {
                var ultima = g.OrderBy(o => o.Data).ThenBy(o => o.VersiuneId).Last();
                var min = g.Min(o => o.Pret);
                var max = g.Max(o => o.Pret);
                return new StatisticaPret(
                    Normalizeaza(ultima.Cod), Normalizeaza(ultima.UnitateMasura), Normalizeaza(ultima.Denumire), ultima.Categorie,
                    g.Count(), g.Select(o => o.VersiuneId).Distinct().Count(), g.Select(o => o.ProiectId).Distinct().Count(),
                    min, max, Medie(g.ToList()), ultima.Pret, ultima.Data, g.Sum(o => o.Cantitate), Abatere(min, max));
            })
            .OrderBy(s => s.Cod, StringComparer.Ordinal).ThenBy(s => s.UnitateMasura, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Prețurile resurselor / articolelor în versiunile date, una lângă alta. Când același cod apare de mai multe ori în
    /// aceeași versiune (ex. în capitole diferite), prețul versiunii e media lui ponderată.
    /// </summary>
    public static IReadOnlyList<RandComparatiePreturi> Compara(IReadOnlyList<Guid> versiuni, IEnumerable<ObservatiePret> observatii)
    {
        var pozitie = versiuni.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
        return observatii
            .Where(o => pozitie.ContainsKey(o.VersiuneId))
            .GroupBy(o => Cheie(o.Cod, o.UnitateMasura))
            .Select(g =>
            {
                var preturi = new decimal?[versiuni.Count];
                foreach (var pv in g.GroupBy(o => o.VersiuneId))
                    preturi[pozitie[pv.Key]] = Medie(pv.ToList());
                var prezente = preturi.Where(p => p is not null).Select(p => p!.Value).ToList();
                var model = g.OrderBy(o => pozitie[o.VersiuneId]).First();
                return new RandComparatiePreturi(
                    Normalizeaza(model.Cod), Normalizeaza(model.UnitateMasura), Normalizeaza(model.Denumire), model.Categorie, preturi,
                    prezente.Count > 1 ? Abatere(prezente.Min(), prezente.Max()) : null, prezente.Count == versiuni.Count);
            })
            .OrderBy(r => r.Cod, StringComparer.Ordinal).ThenBy(r => r.UnitateMasura, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Prețurile de achiziție ale materialului legat, aduse în UM-ul resursei: preț resursă = preț stoc × factor, cantitate
    /// resursă = cantitate stoc / factor (factorul: 1 UM resursă = factor × UM stoc). Null fără achiziții cu preț.
    /// </summary>
    public static StatisticaAchizitie? Achizitii(string cod, string unitateMasura, decimal factor, IEnumerable<PretAchizitie> preturi, DateOnly azi, int luni)
    {
        if (factor <= 0)
            return null;
        var lista = preturi.Where(p => p.PretUnitar > 0)
            .Select(p => (p.Data, Pret: p.PretUnitar * factor, Cantitate: p.Cantitate / factor, p.Furnizor, p.Document))
            .OrderBy(p => p.Data).ToList();
        if (lista.Count == 0)
            return null;
        var ultima = lista[^1];
        var interval = lista.Where(p => p.Data >= azi.AddMonths(-luni)).ToList();
        decimal? medie = null;
        if (interval.Count > 0)
        {
            var cantitate = interval.Sum(p => p.Cantitate);
            medie = RotunjirePret.Aplica(cantitate > 0 ? interval.Sum(p => p.Pret * p.Cantitate) / cantitate : interval.Average(p => p.Pret));
        }

        return new StatisticaAchizitie(Normalizeaza(cod), Normalizeaza(unitateMasura), lista.Count, RotunjirePret.Aplica(lista.Min(p => p.Pret)),
            RotunjirePret.Aplica(lista.Max(p => p.Pret)), RotunjirePret.Aplica(ultima.Pret), ultima.Data, ultima.Furnizor, ultima.Document, medie, interval.Count,
            interval.Count > 0 ? RotunjirePret.Aplica(interval.Min(p => p.Pret)) : null);
    }

    /// <summary>Prefixul de litere al simbolului articolului (TSA04G1 → TS): capitolul de indicator, informativ.</summary>
    public static string CapitolIndicator(string simbol)
    {
        var s = Normalizeaza(simbol).ToUpperInvariant();
        return s.Length >= 2 && char.IsLetter(s[0]) && char.IsLetter(s[1]) ? s[..2] : string.Empty;
    }

    private static decimal Medie(IReadOnlyList<ObservatiePret> o)
    {
        var cantitate = o.Sum(x => x.Cantitate);
        var medie = cantitate > 0 ? o.Sum(x => x.Pret * x.Cantitate) / cantitate : o.Average(x => x.Pret);
        return RotunjirePret.Aplica(medie);
    }

    private static decimal? Abatere(decimal min, decimal max) => min > 0 ? Math.Round((max - min) / min, 4, MidpointRounding.AwayFromZero) : null;

    private static string Normalizeaza(string? s) => string.Join(' ', (s ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
