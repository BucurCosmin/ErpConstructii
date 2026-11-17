using System.Text.RegularExpressions;
using ErpConstructii.Devize.Import;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Devize.Import.Boq;

/// <summary>Prețul unitar și valoarea unei componente pe un articol (în moneda BOQ-ului).</summary>
public sealed record ValoareComponenta(decimal PretUnitar, decimal Valoare);

/// <param name="Cod">Codul din „Work Items”, cu spațiile normalizate (ex. „B5 C 20000 1 A”); null când rândul nu are cod.</param>
/// <param name="Componente">Cheia componentei profilului → preț / valoare.</param>
public sealed record ArticolBoq(
    string? Numar, string? Cod, string Denumire, string? Specificatie, string UnitateMasura, decimal Cantitate,
    IReadOnlyDictionary<string, ValoareComponenta> Componente, string? Observatii, int Rand)
{
    public decimal Valoare => Componente.Values.Sum(c => c.Valoare);
}

/// <summary>Grupul de articole dintr-o secțiune (rândul fără cantitate, ex. „1 EXTERIOR WALLS AND FINISHINGS”).</summary>
public sealed record GrupBoq(string Cod, string Denumire, IReadOnlyList<ArticolBoq> Articole);

/// <param name="Foaie">Foaia Excel a secțiunii.</param>
/// <param name="Articole">Articolele de dinaintea primului grup.</param>
/// <param name="TotalDeclarat">Valoarea de pe rândul TOTAL al foii.</param>
/// <param name="TotalRezumat">Valoarea din foaia de rezumat, pentru secțiunea potrivită.</param>
public sealed record SectiuneBoq(
    string Cod, string Denumire, string Foaie, IReadOnlyList<ArticolBoq> Articole, IReadOnlyList<GrupBoq> Grupuri, decimal? TotalDeclarat,
    decimal? TotalRezumat)
{
    public IEnumerable<ArticolBoq> ArticoleToate() => Articole.Concat(Grupuri.SelectMany(g => g.Articole));

    public decimal Valoare => ArticoleToate().Sum(a => a.Valoare);
}

/// <summary>O linie a foii de rezumat (secțiune → total).</summary>
public sealed record LinieRezumatBoq(string Cod, string Denumire, decimal? Total);

/// <param name="Obiect">Codul obiectului, dedus din codurile articolelor (ex. „B5”, „PCA16A”).</param>
/// <param name="DenumireObiect">Din titlul rezumatului (ex. „Building 5”).</param>
/// <param name="Componente">Componentele de preț găsite (cheia profilului → antetul din fișier).</param>
public sealed record BoqCitit(
    string Obiect, string DenumireObiect, IReadOnlyList<SectiuneBoq> Sectiuni, IReadOnlyList<LinieRezumatBoq> Rezumat, decimal? TotalGeneral,
    IReadOnlyDictionary<string, string> Componente, IReadOnlyList<ParteIgnorata> Ignorate, IReadOnlyList<ProblemaImport> Probleme);

/// <summary>
/// Citirea unui BOQ de ofertă (D-58) după un <see cref="ProfilBoq"/>: foaia de rezumat (secțiunile cu totalurile lor) și
/// foile de secțiune (antetul recunoscut după profil, oriunde ar fi coloanele; componentele fără antet se iau după perechile
/// „Rate / Amount”, în ordinea profilului). Rândurile: articol (cantitate sau valori), grup (cu număr / cod, fără valori),
/// TOTAL (sfârșitul secțiunii); restul sunt note. Foile în altă limbă decât româna / engleza (ex. coreeană), cele fără antet
/// de BOQ și cele care nu apar în rezumat se lasă deoparte, cu motivul. Logică pură, fără excepții pentru fișiere greșite.
/// </summary>
public static partial class CititorBoq
{
    /// <summary>Diferența tolerată între totalurile declarate și cele calculate (rotunjirile din Excel).</summary>
    public const decimal Toleranta = 1m;

    private sealed record Antet(int Rand, IReadOnlyDictionary<CampBoq, int> Coloane, IReadOnlyDictionary<string, (int Pret, int Valoare)> Componente,
        IReadOnlyDictionary<string, string> Etichete, int? ColoanaTotalValoare);

    /// <summary>Foaia are un antet de BOQ după profil (pentru detectarea formatului).</summary>
    public static bool AreAntet(FoaieTabel foaie, ProfilBoq profil) => !AltaLimba(foaie.Nume) && GasesteAntet(foaie, profil) is not null;

    public static BoqCitit Citeste(IReadOnlyList<FoaieTabel> foi, ProfilBoq profil, string numeFisier)
    {
        var probleme = new List<ProblemaImport>();
        var ignorate = new List<ParteIgnorata>();
        var componente = new Dictionary<string, string>();
        List<LinieRezumatBoq> rezumat = [];
        decimal? totalGeneral = null;
        string? titluRezumat = null;
        var sectiuni = new List<SectiuneBoq>();

        foreach (var foaie in foi)
        {
            if (AltaLimba(foaie.Nume))
            {
                ignorate.Add(new ParteIgnorata(foaie.Nume, "foaie în altă limbă decât româna / engleza"));
                continue;
            }

            var antet = GasesteAntet(foaie, profil);
            if (antet is null)
            {
                if (foaie.Randuri.Any(r => r.Any(c => !string.IsNullOrWhiteSpace(c))))
                    ignorate.Add(new ParteIgnorata(foaie.Nume, AltaLimba(string.Join(' ', foaie.Randuri.Take(12).SelectMany(r => r).Where(c => c is not null)))
                        ? "foaie în altă limbă decât româna / engleza"
                        : "fără antet de BOQ"));
                continue;
            }

            foreach (var (cheie, eticheta) in antet.Etichete)
                componente.TryAdd(cheie, eticheta);
            if (profil.FoiRezumat.Contains(ValoriFoaie.Cheie(foaie.Nume)))
            {
                (rezumat, totalGeneral) = CitesteRezumat(foaie, antet, profil);
                titluRezumat = Titlu(foaie, antet);
                continue;
            }

            sectiuni.Add(CitesteSectiune(foaie, antet, profil, probleme));
        }

        // Potrivirea foilor cu rezumatul: după cod, apoi după total. Foile fără linie în rezumat nu intră în deviz.
        if (rezumat.Count > 0)
        {
            var potrivite = new List<SectiuneBoq>();
            var libere = sectiuni.ToList();
            foreach (var linie in rezumat)
            {
                var s = libere.FirstOrDefault(x => string.Equals(x.Cod, linie.Cod, StringComparison.OrdinalIgnoreCase)
                                                   && (linie.Total is null || x.TotalDeclarat is null || Math.Abs(x.TotalDeclarat.Value - linie.Total.Value) <= Toleranta))
                        ?? (linie.Total is { } t ? Unic(libere.Where(x => Math.Abs((x.TotalDeclarat ?? x.Valoare) - t) <= Toleranta)) : null);
                if (s is null)
                {
                    probleme.Add(new(Severitate.Avertisment, $"rezumat: {linie.Cod}",
                        $"Secțiunea „{linie.Cod} {linie.Denumire}” din rezumat ({Suma(linie.Total)}) nu are o foaie cu același cod sau total: lipsește din deviz."));
                    continue;
                }

                libere.Remove(s);
                if (!string.Equals(s.Cod, linie.Cod, StringComparison.OrdinalIgnoreCase))
                    probleme.Add(new(Severitate.Informare, $"foaia {s.Foaie}",
                        $"Rezumatul o numește „{linie.Cod} {linie.Denumire}”; potrivită după total, rămâne cu codul foii ({s.Cod})."));
                potrivite.Add(s with { Denumire = string.IsNullOrWhiteSpace(linie.Denumire) ? s.Denumire : linie.Denumire, TotalRezumat = linie.Total });
            }

            foreach (var s in libere)
                ignorate.Add(new ParteIgnorata(s.Foaie, s.ArticoleToate().Any() && s.Valoare != 0
                    ? $"nu apare în rezumatul BOQ ({s.ArticoleToate().Count()} articole, {Suma(s.Valoare)})"
                    : "nu apare în rezumatul BOQ (goală)"));
            sectiuni = potrivite;
        }
        else
        {
            probleme.Add(new(Severitate.Avertisment, "fișier", "Nu am găsit foaia de rezumat (BOQ): se importă toate foile cu antet de BOQ."));
            foreach (var s in sectiuni.Where(s => !s.ArticoleToate().Any()).ToList())
            {
                sectiuni.Remove(s);
                ignorate.Add(new ParteIgnorata(s.Foaie, "fără articole"));
            }
        }

        // Verificarea totalurilor: foaia (calculat vs TOTAL), rezumatul, totalul general.
        foreach (var s in sectiuni)
        {
            if (s.TotalDeclarat is { } d && Math.Abs(d - s.Valoare) > Toleranta)
                probleme.Add(new(Severitate.Avertisment, $"foaia {s.Foaie}", $"Articolele dau {Suma(s.Valoare)}, rândul TOTAL al foii {Suma(d)}."));
        }

        var total = sectiuni.Sum(s => s.Valoare);
        if (totalGeneral is { } g && Math.Abs(g - total) > Toleranta * Math.Max(1, sectiuni.Count))
            probleme.Add(new(Severitate.Avertisment, "rezumat", $"Totalul general din rezumat e {Suma(g)}, articolele importate dau {Suma(total)}."));

        var obiect = Obiect(sectiuni.SelectMany(s => s.ArticoleToate()).Select(a => a.Cod)) ?? Path.GetFileNameWithoutExtension(numeFisier);
        VerificaCoduri(sectiuni, obiect, probleme);
        var denumireObiect = titluRezumat is { Length: > 0 } tr ? tr : obiect;
        if (componente.Count == 0)
            probleme.Add(new(Severitate.Eroare, "fișier", "Nu am găsit componentele de preț (ex. Material / Labour / Expense)."));
        return new BoqCitit(obiect, denumireObiect, sectiuni, rezumat, totalGeneral, componente, ignorate, probleme);
    }

    // ---------------------------------------------------------------- antetul

    private static Antet? GasesteAntet(FoaieTabel foaie, ProfilBoq profil)
    {
        for (var r = 0; r < Math.Min(foaie.Randuri.Count, 15); r++)
        {
            var coloane = new Dictionary<CampBoq, int>();
            var componente = new Dictionary<string, int>();
            var etichete = new Dictionary<string, string>();
            var rand = foaie.Randuri[r];
            for (var c = 0; c < rand.Count; c++)
            {
                if (foaie.Celula(r, c) is not { } text)
                    continue;
                var k = ValoriFoaie.Cheie(text);
                if (profil.Componente.FirstOrDefault(p => p.Prefixe.Any(k.StartsWith)) is { } comp && !componente.ContainsKey(comp.Cheie))
                {
                    componente[comp.Cheie] = c;
                    etichete[comp.Cheie] = text.ReplaceLineEndings(" ").Trim();
                    continue;
                }

                foreach (var col in profil.Coloane)
                {
                    if (coloane.ContainsKey(col.Camp))
                        continue;
                    if ((col.Exact?.Contains(k) ?? false) || col.Prefixe.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
                    {
                        coloane[col.Camp] = c;
                        break;
                    }
                }
            }

            if (!coloane.ContainsKey(CampBoq.Cantitate) || !coloane.ContainsKey(CampBoq.Denumire) && !coloane.ContainsKey(CampBoq.Cod))
                continue;

            // Perechile „Rate / Amount” de sub antet: componentele fără antet primesc perechile rămase, în ordinea profilului.
            var perechi = new List<int>();
            if (r + 1 < foaie.Randuri.Count)
            {
                for (var c = 0; c < foaie.Randuri[r + 1].Count; c++)
                {
                    if (foaie.Celula(r + 1, c) is { } t && ValoriFoaie.Cheie(t) is "rate" or "unit price" or "pret unitar" or "pu")
                        perechi.Add(c);
                }
            }

            var totalCol = coloane.TryGetValue(CampBoq.Total, out var tc) ? tc : (int?)null;
            var faraAntet = profil.Componente.Where(p => !componente.ContainsKey(p.Cheie)).ToList();
            var libere = perechi.Where(p => !componente.ContainsValue(p) && p != totalCol).ToList();
            foreach (var comp in faraAntet)
            {
                if (libere.Count == 0)
                    break;
                componente[comp.Cheie] = libere[0];
                etichete[comp.Cheie] = comp.Eticheta;
                libere.RemoveAt(0);
            }

            if (componente.Count == 0)
                continue;
            var dePret = componente.ToDictionary(x => x.Key, x => (x.Value, x.Value + 1));
            return new Antet(r, coloane, dePret, etichete, totalCol is { } t2 ? t2 + 1 : null);
        }

        return null;
    }

    private static string? Titlu(FoaieTabel foaie, Antet antet)
    {
        for (var r = 0; r < antet.Rand; r++)
        {
            foreach (var c in foaie.Randuri[r])
            {
                if (c is not null && ValoriFoaie.Cheie(c).StartsWith("bill of quantities", StringComparison.Ordinal))
                {
                    var parti = c.Split(" - ", 2, StringSplitOptions.TrimEntries);
                    return parti.Length > 1 ? parti[1] : null;
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- rezumatul și secțiunile

    private static (List<LinieRezumatBoq>, decimal?) CitesteRezumat(FoaieTabel foaie, Antet antet, ProfilBoq profil)
    {
        var linii = new List<LinieRezumatBoq>();
        decimal? general = null;
        // În rezumat, „Work Items” e codul secțiunii, iar coloana de după el (sau „Specification”) e denumirea.
        var colCod = antet.Coloane.GetValueOrDefault(CampBoq.Cod, 0);
        var colDen = antet.Coloane.TryGetValue(CampBoq.Specificatie, out var sp) && sp == colCod + 1 ? sp : colCod + 1;
        for (var r = antet.Rand + 1; r < foaie.Randuri.Count; r++)
        {
            var cod = foaie.Celula(r, colCod);
            var total = antet.ColoanaTotalValoare is { } tv ? ValoriFoaie.Numar(foaie.Celula(r, tv)) : null;
            if (cod is null)
                continue;
            if (profil.EticheteTotalGeneral.Contains(ValoriFoaie.Cheie(cod)))
            {
                general = total;
                continue;
            }

            if (ValoriFoaie.Cheie(cod) is "rate" or "amount")
                continue;
            linii.Add(new LinieRezumatBoq(cod.Trim(), (foaie.Celula(r, colDen) ?? string.Empty).Trim(), total));
        }

        return (linii, general);
    }

    private static SectiuneBoq CitesteSectiune(FoaieTabel foaie, Antet antet, ProfilBoq profil, List<ProblemaImport> probleme)
    {
        string? Camp(int r, CampBoq camp) => antet.Coloane.TryGetValue(camp, out var c) ? foaie.Celula(r, c) : null;
        var articole = new List<ArticolBoq>();
        var grupuri = new List<GrupBoq>();
        List<ArticolBoq>? curent = null;
        decimal? total = null;
        var cantitateZero = 0;
        for (var r = antet.Rand + 1; r < foaie.Randuri.Count; r++)
        {
            var primele = Enumerable.Range(0, 5).Select(c => foaie.Celula(r, c)).Where(x => x is not null).Select(x => ValoriFoaie.Cheie(x)).ToList();
            // Rândul TOTAL cu valoare încheie secțiunea; unul fără valoare (subtotal gol, ex. la mijlocul foii) se sare.
            if (primele.Any(profil.EticheteTotal.Contains))
            {
                total = antet.ColoanaTotalValoare is { } tv ? ValoriFoaie.Numar(foaie.Celula(r, tv)) : null;
                if (total is not null)
                    break;
                continue;
            }

            var numar = Camp(r, CampBoq.Numar);
            var cod = Camp(r, CampBoq.Cod) is { } cd ? Spatii().Replace(cd, " ").Trim() : null;
            var denumire = Text(Camp(r, CampBoq.Denumire));
            var specificatie = Text(Camp(r, CampBoq.Specificatie));
            var cantitate = ValoriFoaie.Numar(Camp(r, CampBoq.Cantitate));
            var valori = antet.Componente.ToDictionary(x => x.Key, x => (Pret: ValoriFoaie.Numar(foaie.Celula(r, x.Value.Pret)), Valoare: ValoriFoaie.Numar(foaie.Celula(r, x.Value.Valoare))));
            var areValori = valori.Values.Any(v => v.Valoare is { } x && x != 0);
            if (cantitate is { } q && q != 0 || areValori)
            {
                var q2 = cantitate ?? 0;
                if (q2 == 0)
                {
                    cantitateZero++;
                    continue;
                }

                // Cu codul nedescriptiv (ex. „Site Office”), codul devine parte din denumire.
                var esteCod = cod is not null && EsteCod(cod);
                var den = denumire ?? specificatie ?? cod ?? $"Rândul {r + 1}";
                if (cod is not null && !esteCod)
                    den = $"{cod} — {den}";
                var componente = valori.ToDictionary(x => x.Key, x =>
                {
                    var val = x.Value.Valoare ?? (x.Value.Pret ?? 0) * q2;
                    var pret = x.Value.Pret ?? (q2 == 0 ? 0 : val / q2);
                    return new ValoareComponenta(pret, val);
                });
                var articol = new ArticolBoq(numar, esteCod ? cod : null, den, denumire is null ? null : specificatie,
                    Text(Camp(r, CampBoq.UnitateMasura)) ?? "buc", q2, componente, Text(Camp(r, CampBoq.Observatii)), r + 1);
                (curent ?? articole).Add(articol);
                continue;
            }

            // Grup: are număr sau cod și o denumire, fără cantitate și fără valori. Rândurile doar cu text sunt note.
            if ((numar is not null || cod is not null) && (denumire ?? specificatie) is { } titlu)
            {
                var lista = new List<ArticolBoq>();
                grupuri.Add(new GrupBoq((numar ?? cod)!.Trim(), titlu, lista));
                curent = lista;
            }
        }

        if (cantitateZero > 0)
            probleme.Add(new(Severitate.Informare, $"foaia {foaie.Nume}", $"{cantitateZero} rânduri cu cantitatea 0 au fost lăsate deoparte."));
        var codFoaie = CodFoaie().Match(foaie.Nume) is { Success: true } m ? m.Value : foaie.Nume.Trim();
        var denumireFoaie = Titlu(foaie, antet) is { } t && t.Split(" - ", StringSplitOptions.TrimEntries) is var p && p.Length > 0 ? p[^1] : foaie.Nume.Trim();
        // Grupurile goale (titluri fără articole sub ele) nu au rost în deviz.
        return new SectiuneBoq(codFoaie, denumireFoaie, foaie.Nume.Trim(), articole,
            grupuri.Where(g => g.Articole.Count > 0).ToList(), total, null);
    }

    // ---------------------------------------------------------------- codurile

    /// <summary>Codul de articol: are cifre și litere, fără cuvinte obișnuite (ex. „B5 C 20000 1 A”, nu „Site Office”).</summary>
    public static bool EsteCod(string text) => text.Any(char.IsAsciiDigit) && !CuvantMic().IsMatch(text);

    /// <summary>Cheia codului pentru potriviri (cu graficul Primavera): fără spații, majuscule („B5C200001A”).</summary>
    public static string CheieCod(string cod) => new(cod.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Obiectul: cel mai frecvent prim segment cu cifre al codurilor (în „E B5 50000 1”, „B5”).</summary>
    private static string? Obiect(IEnumerable<string?> coduri) =>
        coduri.Where(c => c is not null).Select(c => c!.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(s => s.Any(char.IsAsciiDigit) && s.Any(char.IsAsciiLetter)))
            .Where(s => s is not null).GroupBy(s => s!).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    private static void VerificaCoduri(IReadOnlyList<SectiuneBoq> sectiuni, string obiect, List<ProblemaImport> probleme)
    {
        foreach (var s in sectiuni)
        {
            var straine = s.ArticoleToate().Where(a => a.Cod is not null && Obiect([a.Cod]) is { } o && o != obiect).Select(a => a.Cod!).ToList();
            if (straine.Count > 0)
                probleme.Add(new(Severitate.Avertisment, $"foaia {s.Foaie}",
                    $"{straine.Count} coduri nu sunt ale obiectului {obiect} (ex. {string.Join(", ", straine.Take(3))}): verifică-le, legătura cu graficul se face după cod."));
        }

        var dubluri = sectiuni.SelectMany(s => s.ArticoleToate()).Where(a => a.Cod is not null).GroupBy(a => CheieCod(a.Cod!)).Where(g => g.Count() > 1).ToList();
        if (dubluri.Count > 0)
            probleme.Add(new(Severitate.Avertisment, "coduri",
                $"{dubluri.Count} coduri apar de mai multe ori (ex. {string.Join(", ", dubluri.Take(3).Select(g => g.First().Cod))}): primesc un sufix „~2”, „~3”."));
    }

    // ---------------------------------------------------------------- ajutătoare

    /// <summary>Litere coreene, chinezești sau japoneze: foaia nu e în română / engleză.</summary>
    private static bool AltaLimba(string text) => AsiatI().IsMatch(text);

    private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : Spatii().Replace(s.ReplaceLineEndings(" "), " ").Trim();

    private static T? Unic<T>(IEnumerable<T> x) where T : class => x.Take(2).ToList() is { Count: 1 } l ? l[0] : null;

    private static string Suma(decimal? v) => v?.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("ro-RO")) ?? "—";

    [GeneratedRegex(@"[가-힣ᄀ-ᇿ㄰-㆏一-鿿぀-ヿ]")]
    private static partial Regex AsiatI();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spatii();

    [GeneratedRegex(@"\b[a-z]{3,}\b")]
    private static partial Regex CuvantMic();

    [GeneratedRegex(@"^\s*\d{4,6}")]
    private static partial Regex CodFoaie();
}
