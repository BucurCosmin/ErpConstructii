using System.Globalization;
using System.Text.RegularExpressions;

namespace ErpConstructii.Stocuri.Import;

/// <summary>Un material din fișier, într-o gestiune.</summary>
/// <param name="Rand">Rândul din foaia de calcul (de la 1), pentru mesaje.</param>
/// <param name="UnitateMasura">Gol în „Raport rapid stoc” (exportul nu are U.M.).</param>
/// <param name="PretUnitar">Doar în „Raport rapid stoc”; balanța are doar cantități.</param>
/// <param name="Valoare">Valoarea stocului final (din „Raport rapid stoc”); null = fișier fără valori.</param>
public sealed record LinieBalantaStoc(
    int Rand, string Denumire, string? CodExtern, string UnitateMasura,
    decimal StocInitial, decimal Intrari, decimal Iesiri, decimal StocFinal, string? Cont,
    decimal? PretUnitar = null, decimal? Valoare = null);

/// <param name="Cod">Codul gestiunii din programul de gestiune (ex. „01-G5”).</param>
public sealed record GestiuneBalantaStoc(string Cod, IReadOnlyList<LinieBalantaStoc> Linii);

public sealed record ProblemaBalanta(bool Eroare, int? Rand, string Mesaj);

/// <param name="Categorie">Din titlu („Balanta stoc: Materiale consumabile” → „Materiale consumabile”).</param>
/// <param name="CuValori">„Raport rapid stoc”: fiecare rând are preț și valoare, dar nu are U.M.</param>
public sealed record BalantaStoc(
    string? Firma, string? Cif, string? Categorie, DateOnly? PerioadaDe, DateOnly? PerioadaPanaLa,
    IReadOnlyList<GestiuneBalantaStoc> Gestiuni, IReadOnlyList<ProblemaBalanta> Probleme, bool CuValori = false)
{
    public bool AreErori => Probleme.Any(p => p.Eroare);
}

/// <summary>
/// Citește stocul exportat în Excel din programul de gestiune anterior (Nexus ERP, câte un fișier pe categorie), în două
/// formate: „Balanta stoc” (blocuri „Gestiune: 01-G5”, cantități fără valori, cu U.M.) și „Raport rapid stoc” (un rând pe
/// lot de preț, cu gestiunea, prețul și valoarea pe rând, fără U.M.). Coloanele se găsesc după antet (pozițiile diferă
/// între exporturi), iar o valoare poate sta în oricare celulă dintre coloana antetului ei și următoarea (celule unite).
/// Funcție pură: primește celulele ca text (numerele în formă invariantă), nu aruncă excepții pentru fișiere greșite, ci
/// întoarce probleme.
/// </summary>
public static partial class InterpretorBalantaStoc
{
    private enum Coloana { NrCrt, Denumire, CodExtern, Um, StocInitial, Intrari, Iesiri, StocFinal, Cont, Cantitate, PretUnitar, Valoare, Gestiune }

    public static BalantaStoc Interpreteaza(IReadOnlyList<IReadOnlyList<string?>> randuri)
    {
        if (AntetRaportRapid(randuri) is { } antet)
            return InterpreteazaRaportRapid(randuri, antet.Index, antet.Coloane);

        string? firma = null, cif = null, categorie = null;
        DateOnly? de = null, panaLa = null;
        Dictionary<Coloana, int>? coloane = null;
        var probleme = new List<ProblemaBalanta>();
        var gestiuni = new List<(string Cod, List<LinieBalantaStoc> Linii)>();

        for (var i = 0; i < randuri.Count; i++)
        {
            var rand = randuri[i];
            var nr = i + 1;
            var primul = Celula(rand, 0);
            var text = string.Join(" ", rand.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()));
            if (text.Length == 0)
                continue;

            if (firma is null && cif is null && CifRegex().Match(text) is { Success: true } mc)
            {
                firma = text.Split('\n', 2)[0].Trim();
                cif = mc.Groups[1].Value.ToUpperInvariant();
                continue;
            }

            if (categorie is null && TitluRegex().Match(text) is { Success: true } mt)
            {
                categorie = NumeCategorie(mt.Groups[1].Value);
                continue;
            }

            if (de is null && PerioadaRegex().Match(text) is { Success: true } mp)
            {
                de = Data(mp.Groups[1].Value);
                panaLa = Data(mp.Groups[2].Value);
                continue;
            }

            if (primul is not null && primul.StartsWith("Nr", StringComparison.OrdinalIgnoreCase) && text.Contains("Denumire", StringComparison.OrdinalIgnoreCase))
            {
                coloane = Antet(rand);
                continue;
            }

            if (GestiuneRegex().Match(text) is { Success: true } mg)
            {
                gestiuni.Add((mg.Groups[1].Value.Trim(), []));
                continue;
            }

            // Rând de material: numărul curent în prima coloană și denumirea.
            if (coloane is null || !int.TryParse(primul, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;
            if (gestiuni.Count == 0)
            {
                probleme.Add(new ProblemaBalanta(true, nr, "Material înaintea primului rând „Gestiune: …”."));
                continue;
            }

            var linie = Linie(rand, nr, coloane, probleme);
            if (linie is not null)
                gestiuni[^1].Linii.Add(linie);
        }

        if (coloane is null)
            probleme.Add(new ProblemaBalanta(true, null, "Nu am găsit antetul tabelului („Nr. crt.”, „Denumire produs”, „Stoc final cantitate”)."));
        else if (!coloane.ContainsKey(Coloana.StocFinal) || !coloane.ContainsKey(Coloana.Denumire) || !coloane.ContainsKey(Coloana.Um))
            probleme.Add(new ProblemaBalanta(true, null, "Antetul nu are coloanele „Denumire produs”, „U.M.” și „Stoc final cantitate”."));
        if (gestiuni.Count == 0)
            probleme.Add(new ProblemaBalanta(true, null, "Fișierul nu are niciun bloc „Gestiune: …”."));
        if (panaLa is null)
            probleme.Add(new ProblemaBalanta(false, null, "Nu am găsit perioada („Perioada: 01.09.2026 - 30.09.2026”); data soldului se alege la import."));

        return new BalantaStoc(firma, cif, categorie, de, panaLa,
            gestiuni.Select(g => new GestiuneBalantaStoc(g.Cod, g.Linii)).ToList(), probleme);
    }

    private static Dictionary<Coloana, int> Antet(IReadOnlyList<string?> rand)
    {
        var coloane = new Dictionary<Coloana, int>();
        for (var j = 0; j < rand.Count; j++)
        {
            var t = Cheie(rand[j]);
            Coloana? c = t switch
            {
                _ when t.StartsWith("NR", StringComparison.Ordinal) => Coloana.NrCrt,
                _ when t.Contains("CONT", StringComparison.Ordinal) => Coloana.Cont,
                _ when t.StartsWith("DENUMIRE", StringComparison.Ordinal) => Coloana.Denumire,
                _ when t.StartsWith("COD", StringComparison.Ordinal) => Coloana.CodExtern,
                "UM" or "U M" => Coloana.Um,
                _ when t.StartsWith("STOC INI", StringComparison.Ordinal) => Coloana.StocInitial,
                _ when t.StartsWith("INTRARI", StringComparison.Ordinal) => Coloana.Intrari,
                _ when t.StartsWith("IESIRI", StringComparison.Ordinal) => Coloana.Iesiri,
                _ when t.StartsWith("STOC FIN", StringComparison.Ordinal) => Coloana.StocFinal,
                _ => null,
            };
            if (c is { } col)
                coloane.TryAdd(col, j);
        }

        return coloane;
    }

    private static LinieBalantaStoc? Linie(IReadOnlyList<string?> rand, int nr, Dictionary<Coloana, int> coloane, List<ProblemaBalanta> probleme)
    {
        string? Valoare(Coloana c) => ValoareColoana(rand, coloane, c);

        var denumire = Valoare(Coloana.Denumire);
        var um = Valoare(Coloana.Um);
        if (denumire is null)
        {
            probleme.Add(new ProblemaBalanta(true, nr, "Rând fără denumire."));
            return null;
        }

        var codExtern = Valoare(Coloana.CodExtern);
        denumire = FaraCod(denumire, codExtern);

        decimal Numar(Coloana c, string camp) => NumarColoana(rand, coloane, c, camp, denumire, nr, probleme) ?? 0;

        var si = Numar(Coloana.StocInitial, "stocul inițial");
        var intrari = Numar(Coloana.Intrari, "intrările");
        var iesiri = Numar(Coloana.Iesiri, "ieșirile");
        var sf = Numar(Coloana.StocFinal, "stocul final");
        if (um is null)
            probleme.Add(new ProblemaBalanta(true, nr, $"„{denumire}”: lipsește unitatea de măsură."));
        if (sf < 0)
            // Programul anterior permitea stoc negativ; în ERP stocul nu poate fi negativ, deci linia nu se preia.
            probleme.Add(new ProblemaBalanta(false, nr, $"„{denumire}”: stoc final negativ ({sf.ToString(CultureInfo.InvariantCulture)}); nu se preia."));
        else if (si + intrari - iesiri != sf)
            // Ex. 1200 + 0 − 0 ≠ 1100: balanța nu se închide pe rând; se ia stocul final.
            probleme.Add(new ProblemaBalanta(false, nr,
                $"„{denumire}”: stoc inițial + intrări − ieșiri = {(si + intrari - iesiri).ToString(CultureInfo.InvariantCulture)}, dar stocul final e {sf.ToString(CultureInfo.InvariantCulture)}; se preia stocul final."));

        return new LinieBalantaStoc(nr, denumire, codExtern, um ?? string.Empty, si, intrari, iesiri, sf, Valoare(Coloana.Cont));
    }

    /// <summary>Antetul „Raport rapid stoc”: „Cod extern | Denumire | Cantitate | Pret unitar | Valoare | Gestiune”.</summary>
    private static (int Index, Dictionary<Coloana, int> Coloane)? AntetRaportRapid(IReadOnlyList<IReadOnlyList<string?>> randuri)
    {
        for (var i = 0; i < randuri.Count; i++)
        {
            var coloane = new Dictionary<Coloana, int>();
            for (var j = 0; j < randuri[i].Count; j++)
            {
                Coloana? c = Cheie(randuri[i][j]) switch
                {
                    "COD EXTERN" => Coloana.CodExtern,
                    "DENUMIRE" => Coloana.Denumire,
                    "CANTITATE" => Coloana.Cantitate,
                    "PRET UNITAR" => Coloana.PretUnitar,
                    "VALOARE" => Coloana.Valoare,
                    "GESTIUNE" => Coloana.Gestiune,
                    "UM" or "U M" => Coloana.Um,
                    _ => null,
                };
                if (c is { } col)
                    coloane.TryAdd(col, j);
            }

            if (coloane.ContainsKey(Coloana.Denumire) && coloane.ContainsKey(Coloana.Cantitate) && coloane.ContainsKey(Coloana.Gestiune))
                return (i, coloane);
        }

        return null;
    }

    /// <summary>
    /// „Raport rapid stoc” (Nexus): stocul la sfârșitul lunii, câte un rând pe lot de preț (același material poate apărea de
    /// mai multe ori, cu prețuri diferite), cu gestiunea pe fiecare rând. Valoarea se ia din coloana „Valoare”; cantitate ×
    /// preț diferit de valoare dă avertisment.
    /// </summary>
    private static BalantaStoc InterpreteazaRaportRapid(IReadOnlyList<IReadOnlyList<string?>> randuri, int indexAntet, Dictionary<Coloana, int> coloane)
    {
        string? firma = null, cif = null, categorie = null;
        DateOnly? de = null, panaLa = null;
        var probleme = new List<ProblemaBalanta>();
        var gestiuni = new List<(string Cod, List<LinieBalantaStoc> Linii)>();

        // Antetul firmei: numele în primul rând, CIF-ul pe rândul lui; titlul „Materii prime - Sep.2026” lângă nume.
        for (var i = 0; i < indexAntet; i++)
        {
            foreach (var celula in randuri[i].Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()))
            {
                if (cif is null && CifRegex().Match(celula) is { Success: true } mc)
                {
                    cif = mc.Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
                }
                else if (categorie is null && TitluLunaRegex().Match(celula) is { Success: true } mt && Luna(mt.Groups[2].Value) is { } luna)
                {
                    categorie = NumeCategorie(mt.Groups[1].Value);
                    de = new DateOnly(int.Parse(mt.Groups[3].Value, CultureInfo.InvariantCulture), luna, 1);
                    panaLa = de.Value.AddMonths(1).AddDays(-1);
                }
                else if (firma is null && i == 0)
                {
                    firma = celula.Split('\n', 2)[0].Trim();
                }
            }
        }

        for (var i = indexAntet + 1; i < randuri.Count; i++)
        {
            var rand = randuri[i];
            var nr = i + 1;
            var gestiune = ValoareColoana(rand, coloane, Coloana.Gestiune);
            var denumire = ValoareColoana(rand, coloane, Coloana.Denumire);
            // Subsolul („Data: 09.11.2026 12:20 …”, „Nexus ERP v…”) și rândurile goale nu au gestiune.
            if (gestiune is null)
            {
                if (denumire is not null && ValoareColoana(rand, coloane, Coloana.Cantitate) is not null)
                    probleme.Add(new ProblemaBalanta(true, nr, $"„{denumire}”: lipsește gestiunea."));
                continue;
            }

            if (denumire is null)
            {
                probleme.Add(new ProblemaBalanta(true, nr, "Rând fără denumire."));
                continue;
            }

            var codExtern = ValoareColoana(rand, coloane, Coloana.CodExtern);
            denumire = FaraCod(denumire, codExtern);
            var cantitate = NumarColoana(rand, coloane, Coloana.Cantitate, "cantitatea", denumire, nr, probleme) ?? 0;
            var pret = NumarColoana(rand, coloane, Coloana.PretUnitar, "prețul unitar", denumire, nr, probleme);
            var valoareFisier = NumarColoana(rand, coloane, Coloana.Valoare, "valoarea", denumire, nr, probleme);
            var valoare = Math.Round(valoareFisier ?? (pret ?? 0) * cantitate, 2, MidpointRounding.AwayFromZero);
            var calculat = pret is { } p ? Math.Round(p * cantitate, 2, MidpointRounding.AwayFromZero) : valoare;

            if (cantitate < 0)
                probleme.Add(new ProblemaBalanta(false, nr, $"„{denumire}”: cantitate negativă ({cantitate.ToString(CultureInfo.InvariantCulture)}); nu se preia."));
            else if (valoare < 0)
                probleme.Add(new ProblemaBalanta(true, nr, $"„{denumire}”: valoare negativă ({valoare.ToString(CultureInfo.InvariantCulture)})."));
            else if (Math.Abs(calculat - valoare) > 0.01m)
                // Ex. 2 × 71,305 = 142,61: se închide; 3 × 10 ≠ 25: se ia valoarea din fișier.
                probleme.Add(new ProblemaBalanta(false, nr,
                    $"„{denumire}”: cantitate × preț = {calculat.ToString(CultureInfo.InvariantCulture)}, dar valoarea e {valoare.ToString(CultureInfo.InvariantCulture)}; se preia valoarea."));

            var g = gestiuni.FindIndex(x => x.Cod == gestiune);
            if (g < 0)
            {
                gestiuni.Add((gestiune, []));
                g = gestiuni.Count - 1;
            }

            gestiuni[g].Linii.Add(new LinieBalantaStoc(nr, denumire, codExtern, ValoareColoana(rand, coloane, Coloana.Um) ?? string.Empty,
                0, 0, 0, cantitate, null, pret, valoare));
        }

        if (gestiuni.Count == 0)
            probleme.Add(new ProblemaBalanta(true, null, "Fișierul nu are niciun material cu gestiune."));
        if (panaLa is null)
            probleme.Add(new ProblemaBalanta(false, null, "Nu am găsit luna raportului („Materii prime - Sep.2026”); data soldului se alege la import."));

        return new BalantaStoc(firma, cif, categorie, de, panaLa,
            gestiuni.Select(g => new GestiuneBalantaStoc(g.Cod, g.Linii)).ToList(), probleme, CuValori: true);
    }

    /// <summary>Valoarea unei coloane: prima celulă nevidă de la coloana antetului până la coloana următoare din antet.</summary>
    private static string? ValoareColoana(IReadOnlyList<string?> rand, Dictionary<Coloana, int> coloane, Coloana c)
    {
        if (!coloane.TryGetValue(c, out var start))
            return null;
        var sfarsit = coloane.Values.Where(p => p > start).DefaultIfEmpty(Math.Max(rand.Count, start + 1)).Min();
        for (var j = start; j < sfarsit; j++)
        {
            if (Celula(rand, j) is { } v)
                return v;
        }

        return null;
    }

    /// <returns>Null dacă celula e goală sau nu e număr (atunci se adaugă problema).</returns>
    private static decimal? NumarColoana(
        IReadOnlyList<string?> rand, Dictionary<Coloana, int> coloane, Coloana c, string camp, string denumire, int nr, List<ProblemaBalanta> probleme)
    {
        var v = ValoareColoana(rand, coloane, c);
        if (v is null)
            return null;
        if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            || decimal.TryParse(v, NumberStyles.Number, CultureInfo.GetCultureInfo("ro-RO"), out d))
            return Math.Round(d, 4, MidpointRounding.AwayFromZero); // Excel salvează uneori 25.499999999999996
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var e))
            return Math.Round((decimal)e, 4, MidpointRounding.AwayFromZero); // notație științifică (1E-3)
        probleme.Add(new ProblemaBalanta(true, nr, $"„{denumire}”: {camp} „{v}” nu este un număr."));
        return null;
    }

    /// <summary>Nexus lipește codul la sfârșitul denumirii („ALICE SFERICE  1035215”): codul se păstrează separat.</summary>
    private static string FaraCod(string denumire, string? codExtern) =>
        codExtern is not null && denumire.Length > codExtern.Length && denumire.EndsWith(codExtern, StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(denumire[^(codExtern.Length + 1)])
            ? denumire[..^codExtern.Length].TrimEnd()
            : denumire;

    private static int? Luna(string text) => text.Length < 3 ? null : text[..3].ToUpperInvariant() switch
    {
        "IAN" or "JAN" => 1, "FEB" => 2, "MAR" => 3, "APR" => 4, "MAI" or "MAY" => 5, "IUN" or "JUN" => 6,
        "IUL" or "JUL" => 7, "AUG" => 8, "SEP" => 9, "OCT" => 10, "NOI" or "NOV" => 11, "DEC" => 12, _ => null,
    };

    /// <summary>„Ob.inventar in magazie” → „Obiecte de inventar în magazie”; restul rămâne cum e scris.</summary>
    private static string NumeCategorie(string text)
    {
        var t = text.Trim().TrimEnd('*').Trim();
        return t.StartsWith("Ob.inventar", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Ob. inventar", StringComparison.OrdinalIgnoreCase)
            ? "Obiecte de inventar" + (t.Contains("magazie", StringComparison.OrdinalIgnoreCase) ? " în magazie" : string.Empty)
            : t;
    }

    private static string? Celula(IReadOnlyList<string?> rand, int j) =>
        j < rand.Count && !string.IsNullOrWhiteSpace(rand[j]) ? rand[j]!.Trim() : null;

    private static string Cheie(string? text) => text is null ? string.Empty : PotrivireArticol.Cheie(text);

    private static DateOnly? Data(string text) =>
        DateOnly.TryParseExact(text, ["dd.MM.yyyy", "d.M.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [GeneratedRegex(@"C\.?\s*I\.?\s*F\.?\s*:?\s*((?:RO)?\s*\d{2,10})", RegexOptions.IgnoreCase)]
    private static partial Regex CifRegex();

    // „Balanta stoc: Materiale consumabile”, „Balanta stoc - GCC: Materii prime” (nu rândul de subsol „FIRMA * Balanta stoc *”).
    [GeneratedRegex(@"^Balan[tț]a\s+stoc[^:*]*:\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TitluRegex();

    // „Materii prime  - Sep.2026”, „Ob.inventar in magazie - Sep.2026”.
    [GeneratedRegex(@"^(.+?)\s+-\s*([A-Za-z]{3})[a-z]*\.?\s*(\d{4})$")]
    private static partial Regex TitluLunaRegex();

    [GeneratedRegex(@"Perioada\s*:?\s*(\d{1,2}\.\d{1,2}\.\d{4})\s*-\s*(\d{1,2}\.\d{1,2}\.\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PerioadaRegex();

    [GeneratedRegex(@"^Gestiune\s*:\s*(\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex GestiuneRegex();
}
