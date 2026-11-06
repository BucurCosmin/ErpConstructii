using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Stocuri.Import;

/// <param name="Cod">Codul materialului din bon, dacă e completat (ex. „R20681”).</param>
public sealed record LinieBonCitita(
    int Rand, CampCitit<string> Denumire, string? Cod, CampCitit<string> UnitateMasura, CampCitit<decimal> Cantitate,
    CampCitit<decimal> PretUnitar, CampCitit<decimal> Valoare);

/// <summary>Un bon de consum citit dintr-un formular pe hârtie (GC-087). Prețul și valoarea din bon sunt informative:
/// în aplicație consumul se evaluează la CMP-ul din stoc.</summary>
public sealed record BonConsumCitit(
    CampCitit<string> Numar,
    CampCitit<DateOnly> Data,
    CampCitit<string> Lucrare,
    CampCitit<string> Gestiune,
    string? Primitor,
    string? Gestionar,
    string? SefCompartiment,
    IReadOnlyList<LinieBonCitita> Linii,
    CampCitit<decimal> Total,
    IReadOnlyList<ProblemaImport> Probleme)
{
    /// <summary>Bon necompletat (formularul tipărit, fără materiale): nu se importă.</summary>
    public bool Gol => Linii.Count == 0;
}

/// <summary>
/// Interpretează bonurile de consum (formularul GC-087, „BON DE CONSUM (colectiv)”), câte două pe pagină:
/// <list type="bullet">
/// <item>bonurile se separă după rândul „UNITATEA” (<see cref="ZoneDocumente"/>);</item>
/// <item>numărul (coloana „NUMĂR DOCUMENT”), data (celulele Ziua / Luna / Anul), lucrarea, gestiunea, semnatarii;</item>
/// <item>liniile: coloanele deduse din antetul tabelului (cantitate necesară / cod / UM / cantitate eliberată / preț /
/// valoare); un rând cu denumire dar fără cantitate și valoare continuă denumirea liniei de deasupra;</item>
/// <item>verificări: cantitate × preț = valoare, suma liniilor = TOTAL.</item>
/// </list>
/// Funcție pură.
/// </summary>
public static partial class InterpretorBonConsum
{
    private enum Col { Nr, Denumire, CantitateNecesara, Cod, Um, Cantitate, Pret, Valoare, Obs }

    public static IReadOnlyList<BonConsumCitit> Interpreteaza(TextDocument document) =>
        ZoneDocumente.Bonuri(document.Randuri).Select(z => Bon(z, document.DinOcr)).ToList();

    private static BonConsumCitit Bon(IReadOnlyList<RandText> randuri, bool dinOcr)
    {
        var probleme = new List<ProblemaImport>();
        var numar = ZoneDocumente.NumarBon(randuri) is { } n
            ? new CampCitit<string>(n, CertitudineOcr(randuri, n), $"Număr document: {n}")
            : CampCitit<string>.Lipsa();
        var data = CitesteData(randuri);
        var lucrare = DupaEticheta(randuri, PotrivireLucrare());
        var gestiune = DupaEticheta(randuri, PotrivireGestiune());
        var (primitor, gestionar, sef) = Semnatari(randuri);
        var (linii, total) = CitesteLinii(randuri, dinOcr, probleme);

        if (linii.Count > 0)
        {
            var suma = linii.Sum(l => l.Valoare.Valoare);
            if (total.Certitudine != Certitudine.Lipsa && Math.Abs(suma - total.Valoare) > 0.01m)
            {
                probleme.Add(new ProblemaImport(Severitate.Avertisment, "total",
                    $"Suma liniilor ({suma:N2}) diferă de totalul bonului ({total.Valoare:N2}). Verifică liniile."));
                total = total with { Certitudine = Certitudine.Nesigur };
            }

            if (numar.Certitudine == Certitudine.Lipsa)
            {
                probleme.Add(new ProblemaImport(Severitate.Avertisment, "număr", "Nu am găsit numărul bonului. Completează-l din document."));
            }
        }

        return new BonConsumCitit(numar, data, lucrare, gestiune, primitor, gestionar, sef, linii, total, probleme);
    }

    // ------------------------------------------------------------------ antet și subsol

    private static CampCitit<DateOnly> CitesteData(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            var m = PotrivireDataCelule().Match(rand.Text);
            if (m.Success && int.TryParse(m.Groups["z"].Value, CultureInfo.InvariantCulture, out var z) &&
                int.TryParse(m.Groups["l"].Value, CultureInfo.InvariantCulture, out var l) &&
                int.TryParse(m.Groups["a"].Value, CultureInfo.InvariantCulture, out var a) &&
                l is >= 1 and <= 12 && z >= 1 && z <= DateTime.DaysInMonth(a, l))
            {
                return new CampCitit<DateOnly>(new DateOnly(a, l, z), CertitudineOcr(randuri, m.Groups["a"].Value), m.Value.Trim());
            }
        }

        return CampCitit<DateOnly>.Lipsa();
    }

    /// <summary>Valoarea de după etichetă, pe același rând („Lucrare:POMPA APA”); resturile OCR scurte nu contează.</summary>
    private static CampCitit<string> DupaEticheta(IReadOnlyList<RandText> randuri, Regex eticheta)
    {
        foreach (var rand in randuri)
        {
            var m = eticheta.Match(rand.Text);
            if (m.Success)
            {
                var v = m.Groups["v"].Value.Trim(' ', ':', '.', '-', '_', '|');
                return v.Length >= 3 ? new CampCitit<string>(v, Certitudine.Nesigur, rand.Text.Trim()) : CampCitit<string>.Lipsa();
            }
        }

        return CampCitit<string>.Lipsa();
    }

    private static (string? Primitor, string? Gestionar, string? Sef) Semnatari(IReadOnlyList<RandText> randuri)
    {
        var rand = randuri.FirstOrDefault(r => Normalizeaza(r.Text).Contains("PRIMITOR", StringComparison.Ordinal) &&
                                               Normalizeaza(r.Text).Contains("SEMNATUR", StringComparison.Ordinal));
        if (rand is null)
        {
            return (null, null, null);
        }

        // Numele din subsol (cel mult trei cuvinte; restul sunt de obicei linii punctate citite de OCR).
        string? Valoare(Regex r) => r.Match(rand.Text) is { Success: true } m && m.Groups["v"].Value.Trim(' ', '-', '_', '|') is { Length: >= 3 } v
            ? string.Join(' ', v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3))
            : null;
        return (Valoare(PotrivirePrimitor()), Valoare(PotrivireGestionar()), Valoare(PotrivireSef()));
    }

    // ------------------------------------------------------------------ linii

    private static (List<LinieBonCitita> Linii, CampCitit<decimal> Total) CitesteLinii(IReadOnlyList<RandText> randuri, bool dinOcr, List<ProblemaImport> probleme)
    {
        var iAntet = -1;
        for (var i = 0; i < randuri.Count; i++)
        {
            var t = Normalizeaza(randuri[i].Text);
            if (t.Contains("PRET", StringComparison.Ordinal) && t.Contains("VALOARE", StringComparison.Ordinal))
            {
                iAntet = i;
                break;
            }
        }

        if (iAntet < 0)
        {
            return ([], CampCitit<decimal>.Lipsa());
        }

        // Antetul tabelului e pe 2–3 rânduri: cuvintele din jurul rândului „Preț unitar / Valoarea”.
        var antet = randuri.Skip(Math.Max(0, iAntet - 2)).Take(5).SelectMany(r => r.Cuvinte).ToList();
        var coloane = Coloane(antet);
        if (coloane is null)
        {
            return ([], CampCitit<decimal>.Lipsa());
        }

        var linii = new List<(int Nr, List<string> Den, Dictionary<Col, string> Val, float Incredere)>();
        var total = CampCitit<decimal>.Lipsa();
        foreach (var rand in randuri.Skip(iAntet + 1))
        {
            var t = Normalizeaza(rand.Text);
            if (t.StartsWith("DATA SI SEMNATUR", StringComparison.Ordinal) || t.StartsWith("DATA ȘI", StringComparison.Ordinal))
            {
                break;
            }

            var cuvinte = rand.Cuvinte.Where(c => c.Text.Any(char.IsLetterOrDigit)).ToList();
            if (cuvinte.Count == 0 || EsteAntet(t))
            {
                continue;
            }

            if (Regex.IsMatch(t, @"(?<![A-Z])TOTAL(?![A-Z])", RegexOptions.CultureInvariant))
            {
                var suma = cuvinte.Where(c => ColoanaCuvant(c, coloane) == Col.Valoare).Select(c => Numar(c.Text)).LastOrDefault(v => v is not null);
                if (suma is { } s)
                {
                    total = new CampCitit<decimal>(s, CertitudineOcr([rand], cuvinte.Last().Text), rand.Text.Trim());
                }

                break;
            }

            int? nr = null;
            if (EsteNumarCurent(cuvinte[0], coloane) && int.TryParse(cuvinte[0].Text.TrimEnd('.'), CultureInfo.InvariantCulture, out var nn))
            {
                nr = nn;
                cuvinte = cuvinte.Skip(1).ToList();
            }

            var den = new List<string>();
            var val = new Dictionary<Col, string>();
            foreach (var c in cuvinte)
            {
                var col = ColoanaCuvant(c, coloane);
                if (col == Col.Denumire)
                {
                    den.Add(c.Text);
                }
                else if (col is not (Col.Obs or Col.Nr))
                {
                    val[col] = val.TryGetValue(col, out var v) ? v + c.Text : c.Text;
                }
            }

            var cantitate = Numar(val.GetValueOrDefault(Col.Cantitate));
            var valoare = Numar(val.GetValueOrDefault(Col.Valoare));
            if (den.Count == 0)
            {
                continue; // rând gol al formularului („0,00” în coloana valorii)
            }

            // Denumire fără cantitate și fără valoare: continuarea denumirii de deasupra (rândul următor al tabelului).
            if (cantitate is null && valoare is null or 0 && linii.Count > 0)
            {
                linii[^1].Den.AddRange(den);
                continue;
            }

            linii.Add((nr ?? linii.Count + 1, den, val, cuvinte.Count == 0 ? 100 : cuvinte.Min(c => c.Incredere)));
        }

        var rezultat = new List<LinieBonCitita>();
        foreach (var (nr, den, val, incredere) in linii)
        {
            var cOcr = dinOcr && incredere < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;
            var cantitate = Numar(val.GetValueOrDefault(Col.Cantitate));
            var pret = Numar(val.GetValueOrDefault(Col.Pret));
            var valoare = Numar(val.GetValueOrDefault(Col.Valoare));
            var consistent = cantitate is { } q && pret is { } p && valoare is { } v && Math.Abs(q * p - v) <= Math.Max(0.01m, Math.Abs(v) * 0.0005m);
            var cValori = consistent ? cOcr : Certitudine.Nesigur;
            if (!consistent && cantitate is { } q2 && pret is { } p2 && valoare is { } v2)
            {
                probleme.Add(new ProblemaImport(Severitate.Avertisment, $"linia {nr}",
                    $"Cantitate × preț ({q2 * p2:N2}) diferă de valoare ({v2:N2}). Verifică linia."));
            }

            if (cantitate is null)
            {
                probleme.Add(new ProblemaImport(Severitate.Avertisment, $"linia {nr}", "Cantitatea eliberată nu a putut fi citită. Completeaz-o din document."));
            }

            var denumire = string.Join(' ', den).Trim();
            var um = val.GetValueOrDefault(Col.Um)?.Trim('.', ' ').ToLowerInvariant();
            rezultat.Add(new LinieBonCitita(
                nr,
                new CampCitit<string>(denumire, cOcr, denumire),
                val.GetValueOrDefault(Col.Cod) is { Length: >= 3 } cod && cod.Any(char.IsAsciiDigit) ? cod : null,
                string.IsNullOrEmpty(um) ? CampCitit<string>.Lipsa() : new CampCitit<string>(um, cOcr, val[Col.Um]),
                cantitate is { } cq ? new CampCitit<decimal>(cq, cValori, val[Col.Cantitate]) : CampCitit<decimal>.Lipsa(),
                pret is { } cp ? new CampCitit<decimal>(cp, cValori, val[Col.Pret]) : CampCitit<decimal>.Lipsa(),
                valoare is { } cv ? new CampCitit<decimal>(cv, cValori, val[Col.Valoare]) : CampCitit<decimal>.Lipsa()));
        }

        return (rezultat, total);
    }

    /// <summary>
    /// Coloanele tabelului: unde se termină „Nr. crt.” (0 dacă antetul nu-l are) și denumirea, centrul antetului coloanelor
    /// de text și capătul din dreapta al antetului coloanelor numerice (valorile sunt aliniate la dreapta).
    /// </summary>
    private sealed record ColoaneBon(int SfarsitNr, int SfarsitDenumire, Dictionary<Col, int> Centre, Dictionary<Col, int> Dreapta);

    /// <summary>
    /// Coloanele din antet. Antetele sunt centrate în celule, iar valorile aliniate la dreapta, deci granițele dintre celule
    /// nu se pot deduce sigur; fiecare valoare merge la coloana cu centrul antetului cel mai apropiat (numerele doar în
    /// coloanele numerice, textul în cod / UM). Denumirea se termină la începutul antetului „Cantitatea necesară”.
    /// </summary>
    private static ColoaneBon? Coloane(List<CuvantText> antet)
    {
        int? X(Func<string, bool> potrivire, bool ultimul = false)
        {
            var c = antet.Where(w => potrivire(Normalizeaza(w.Text).Trim('.', ':', ','))).OrderBy(w => w.X0).ToList();
            return c.Count == 0 ? null : ultimul ? Centru(c[^1]) : Centru(c[0]);
        }

        int? Dr(params string[] inceputuri) => antet.Where(w => inceputuri.Any(i => Normalizeaza(w.Text).StartsWith(i, StringComparison.Ordinal)))
            .Select(w => (int?)w.X1).Max();

        var elib = X(t => t.StartsWith("ELIBERAT", StringComparison.Ordinal));
        var pret = X(t => t.StartsWith("PRET", StringComparison.Ordinal));
        var val = X(t => t.StartsWith("VALOARE", StringComparison.Ordinal));
        if (elib is null || pret is null || val is null)
        {
            return null;
        }

        var um = X(t => t is "UM" or "U/M" or "U" || t.StartsWith("U/M", StringComparison.Ordinal)) ?? (elib.Value - 180);
        var cod = X(t => t == "COD", ultimul: true) ?? (um - 170);
        var nec = X(t => t.StartsWith("NECESAR", StringComparison.Ordinal)) ?? (cod - 250);
        var obs = X(t => t.StartsWith("OBS", StringComparison.Ordinal)) ?? (val.Value + 260);
        var sfarsitNr = antet.Where(w => Normalizeaza(w.Text).StartsWith("NR", StringComparison.Ordinal) || Normalizeaza(w.Text).StartsWith("CRT", StringComparison.Ordinal))
            .Select(w => w.X1).DefaultIfEmpty(0).Min();
        var sfarsitDenumire = antet.Where(w => Normalizeaza(w.Text).StartsWith("CANTITATE", StringComparison.Ordinal)).Select(w => w.X0).DefaultIfEmpty(nec - 60).Min() - 30;

        var centre = new Dictionary<Col, int>
        {
            [Col.CantitateNecesara] = nec,
            [Col.Cod] = cod,
            [Col.Um] = um,
            [Col.Cantitate] = elib.Value,
            [Col.Pret] = pret.Value,
            [Col.Valoare] = val.Value,
            [Col.Obs] = obs,
        };
        var dreapta = new Dictionary<Col, int>
        {
            [Col.CantitateNecesara] = Dr("NECESAR") ?? nec + 70,
            [Col.Cantitate] = Dr("ELIBERAT") ?? elib.Value + 70,
            [Col.Pret] = Dr("UNITAR", "PRET") ?? pret.Value + 90,
            [Col.Valoare] = Dr("VALOARE") ?? val.Value + 70,
        };
        return new ColoaneBon(sfarsitNr, sfarsitDenumire, centre, dreapta);
    }

    private static readonly Col[] ColoaneNumerice = [Col.CantitateNecesara, Col.Cantitate, Col.Pret, Col.Valoare];
    private static readonly Col[] ColoaneText = [Col.Cod, Col.Um, Col.Obs];

    /// <summary>Numărul curent: cifre la începutul rândului, în coloana „Nr. crt.” (unele formulare au „1” în loc de antet).</summary>
    private static bool EsteNumarCurent(CuvantText c, ColoaneBon coloane) =>
        c.Text.TrimEnd('.').Length is >= 1 and <= 3 && c.Text.TrimEnd('.').All(char.IsAsciiDigit) &&
        (coloane.SfarsitNr > 0 ? c.X0 < coloane.SfarsitNr + 40 : c.X0 < coloane.SfarsitDenumire / 3);

    private static Col ColoanaCuvant(CuvantText c, ColoaneBon coloane)
    {
        if (c.X0 < coloane.SfarsitDenumire)
        {
            return coloane.SfarsitNr > 0 && c.X1 <= coloane.SfarsitNr + 15 ? Col.Nr : Col.Denumire;
        }

        if (Numar(c.Text) is not null)
        {
            // Aliniate la dreapta: capătul din dreapta al numărului, față de capătul din dreapta al antetului coloanei.
            var x = c.X1 > c.X0 ? c.X1 : c.X0;
            return ColoaneNumerice.OrderBy(k => Math.Abs(coloane.Dreapta[k] - x)).First();
        }

        var centru = Centru(c);
        return ColoaneText.OrderBy(k => Math.Abs(coloane.Centre[k] - centru)).First();
    }

    private static int Centru(CuvantText c) => c.X1 > c.X0 ? (c.X0 + c.X1) / 2 : c.X0;

    private static bool EsteAntet(string t) =>
        t.Contains("DENUMIREA", StringComparison.Ordinal) || t.Contains("ELIBERAT", StringComparison.Ordinal) || t.Contains("INCLUSIV", StringComparison.Ordinal);

    /// <summary>Formularul folosește virgula zecimală fără separator de mii („19,969”, „2400,00”).</summary>
    private static decimal? Numar(string? text) =>
        TextNumeric.ParseazaNumar(text?.Trim('|', '/', '\'', '"', '~'), out var v, miiImplicit: false) ? v : null;

    private static Certitudine CertitudineOcr(IEnumerable<RandText> randuri, string fragment)
    {
        var c = randuri.SelectMany(r => r.Cuvinte).FirstOrDefault(w => w.Text.Contains(fragment, StringComparison.Ordinal));
        return c is not null && c.Incredere < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;
    }

    private static string Normalizeaza(string text)
    {
        var fd = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(fd.Length);
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    [GeneratedRegex(@"(?<!\d)(?<z>\d{1,2})\s+(?<l>\d{1,2})\s+(?<a>20\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireDataCelule();

    [GeneratedRegex(@"\bLucrare\s*:?\s*(?<v>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireLucrare();

    [GeneratedRegex(@"\bGESTIUNE\s*:?\s*(?<v>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireGestiune();

    [GeneratedRegex(@"PRIMITOR\s*:\s*(?<v>[^:]+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivirePrimitor();

    [GeneratedRegex(@"GESTIONAR\s*:\s*(?<v>.+?)\s*(?=PRIMITOR|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireGestionar();

    [GeneratedRegex(@"COMPARTIMENT\s*:\s*(?<v>.+?)\s*(?=\S*GESTIONAR|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireSef();
}
