using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Aprovizionare.Import;

public sealed record LinieAvizCitita(int Rand, CampCitit<string> Denumire, CampCitit<string> UnitateMasura, CampCitit<decimal> Cantitate);

/// <param name="ReferintaComanda">Numărul comenzii pomenite pe aviz („CF COMANDA 244 / 02.06.2026” → „244”), pentru legarea de nota de comandă.</param>
/// <param name="Pagini">Paginile citite (avizul către firmă; avizele anexate, către alți clienți, se lasă deoparte).</param>
public sealed record AvizCitit(
    CampCitit<string> NumarExtern,
    CampCitit<DateOnly> Data,
    CampCitit<string> FurnizorDenumire,
    CampCitit<string> FurnizorCui,
    CampCitit<string> Lucrare,
    string? ReferintaComanda,
    IReadOnlyList<LinieAvizCitita> Linii,
    IReadOnlyList<int> Pagini,
    IReadOnlyList<ProblemaImport> Probleme,
    bool DinOcr);

/// <summary>
/// Interpretează avizul de însoțire a mărfii (D-40). Formatele diferă de la furnizor la furnizor („Aviz de însoțire a mărfii”,
/// „Aviz expediție”), dar au aceleași părți: seria / numărul și data sus, furnizorul și clientul pe două coloane, tabelul
/// Denumire | UM | Cantitate (fără prețuri, de obicei), apoi observațiile (lucrarea, comanda). Liniile se recunosc după
/// finalul lor: o unitate de măsură urmată de cantitate. Un PDF poate conține și avizele furnizorului furnizorului
/// (marfa „reavizată”), emise către alt client: se citește avizul în care clientul e firma activă. Funcție pură.
/// </summary>
public static partial class InterpretorAviz
{
    private static readonly HashSet<string> Unitati =
    [
        "KG", "KGR", "KGS", "TO", "T", "TONE", "TONA", "BUC", "BUCATI", "PCS", "MC", "M3", "MP", "M2", "ML", "M", "L", "LT", "LITRI", "LITRU",
        "SET", "SETURI", "SAC", "SACI", "PAL", "PALET", "PALETI", "PERECHE", "PER", "ROLA", "ROLE", "CUTIE", "CUT", "BAX", "KM", "MII", "BOB", "FOI", "COLET",
    ];

    /// <summary>Confuzii OCR frecvente ale unităților (litera „g” citită „a”).</summary>
    private static readonly Dictionary<string, string> UnitatiOcr = new() { ["KA"] = "KG", ["K9"] = "KG", ["KQ"] = "KG" };

    public static AvizCitit Interpreteaza(TextDocument document, string? cuiFirma)
    {
        var probleme = new List<ProblemaImport>();
        var firma = string.IsNullOrWhiteSpace(cuiFirma) ? null : ValidareFiscala.NormalizeazaCui(cuiFirma);
        var (randuri, pagini, catreFirma) = AvizulFirmei(document, firma);
        if (pagini.Count < document.Randuri.Select(r => r.Pagina).Distinct().Count())
        {
            var ignorate = document.Randuri.Select(r => r.Pagina).Distinct().Except(pagini).Order().ToList();
            probleme.Add(new ProblemaImport(Severitate.Informare, "pagini",
                $"Am citit avizul de pe {(pagini.Count == 1 ? $"pagina {pagini[0]}" : $"paginile {string.Join(", ", pagini)}")}; " +
                $"paginile {string.Join(", ", ignorate)} sunt alte avize (de ex. ale furnizorului furnizorului, către alt client) și au fost lăsate deoparte."));
        }

        if (firma is not null && !catreFirma)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "client",
                $"Nu găsesc CUI-ul firmei active ({firma}) pe aviz: verifică dacă avizul e emis către firmă."));
        }

        var nTitlu = Math.Max(12, randuri.Count / 3);
        var titlu = randuri.Take(nTitlu).ToList();
        var numar = Numar(titlu);
        var data = Data(titlu, randuri);
        var cui = randuri.SelectMany(r => PotrivireCui().Matches(r.Text))
            .Select(m => ValidareFiscala.NormalizeazaCui(m.Groups["v"].Value.Replace(" ", string.Empty, StringComparison.Ordinal)))
            .FirstOrDefault(c => ValidareFiscala.EsteCuiValid(c) && c != firma);
        var furnizor = Furnizor(randuri);
        var lucrare = Lucrare(randuri);
        var comanda = randuri.Select(r => PotrivireComanda().Match(r.Text)).FirstOrDefault(m => m.Success)?.Groups["n"].Value;
        var linii = Linii(randuri, document.LiniiOrizontale.Where(l => pagini.Contains(l.Pagina)).ToList(), document.DinOcr);

        if (linii.Count == 0)
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "materiale", "Nu am găsit materialele avizului. Adaugă-le manual."));
        if (furnizor is null)
            probleme.Add(new ProblemaImport(Severitate.Informare, "furnizor", "Nu am găsit furnizorul; completează-l."));
        if (numar.Certitudine == Certitudine.Lipsa)
            probleme.Add(new ProblemaImport(Severitate.Informare, "număr", "Nu am găsit seria și numărul avizului; completează-le."));

        var c = document.DinOcr ? Certitudine.Nesigur : Certitudine.Sigur;
        return new AvizCitit(
            numar,
            data,
            furnizor is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(furnizor, c, furnizor),
            cui is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(cui, Certitudine.Sigur),
            lucrare is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(lucrare, Certitudine.Nesigur, lucrare),
            comanda,
            linii,
            pagini,
            probleme,
            document.DinOcr);
    }

    // ------------------------------------------------------------------ paginile avizului

    /// <summary>
    /// Avizul către firmă: prima pagină pe care apare CUI-ul firmei (alături de al furnizorului), cu paginile de continuare
    /// (fără titlu de aviz) de după ea. Fără CUI-ul firmei: primul aviz din document.
    /// </summary>
    private static (IReadOnlyList<RandText> Randuri, IReadOnlyList<int> Pagini, bool CatreFirma) AvizulFirmei(TextDocument document, string? firma)
    {
        var pagini = document.Randuri.Select(r => r.Pagina).Distinct().Order().ToList();
        if (pagini.Count <= 1)
            return (document.Randuri, pagini, firma is null || ContineCui(document.Randuri, firma));

        var clasificare = ClasificarePagini.Clasifica(document).ToDictionary(p => p.Pagina, p => p.Tip);
        int? cuFirma = null;
        if (firma is not null && pagini.FirstOrDefault(p => ContineCui(document.Randuri.Where(r => r.Pagina == p), firma), -1) is var gasita and >= 0)
            cuFirma = gasita;
        var start = cuFirma ?? pagini[0];
        var alese = new List<int> { start };
        foreach (var p in pagini.Where(p => p > start))
        {
            if (clasificare.GetValueOrDefault(p) == TipPagina.Aviz)
                break;
            alese.Add(p);
        }

        return (document.Randuri.Where(r => alese.Contains(r.Pagina)).ToList(), alese, firma is null || cuFirma is not null);
    }

    private static bool ContineCui(IEnumerable<RandText> randuri, string cui) =>
        randuri.SelectMany(r => PotrivireCui().Matches(r.Text))
            .Any(m => ValidareFiscala.NormalizeazaCui(m.Groups["v"].Value.Replace(" ", string.Empty, StringComparison.Ordinal)) == cui);

    // ------------------------------------------------------------------ antet

    private static CampCitit<string> Numar(IReadOnlyList<RandText> titlu)
    {
        foreach (var r in titlu)
        {
            if (PotrivireSerieSlashNumar().Match(r.Text) is { Success: true } a)
                return new CampCitit<string>(a.Groups["n"].Value.Trim('.', ',', '-'), CertitudineOcr(r), a.Value.Trim());
            if (PotrivireSerieNumar().Match(r.Text) is { Success: true } b)
                return new CampCitit<string>($"{b.Groups["s"].Value.ToUpperInvariant()} {b.Groups["n"].Value}", CertitudineOcr(r), b.Value.Trim());
        }

        foreach (var r in titlu)
        {
            if (PotrivireNumar().Match(r.Text) is { Success: true } m)
                return new CampCitit<string>(m.Groups["n"].Value.Trim('.', ',', '-'), Certitudine.Nesigur, m.Value.Trim());
        }

        return CampCitit<string>.Lipsa();
    }

    /// <summary>Data cu etichetă din antet („Data: 14.08.2026”); altfel prima dată din antet sau din document (de verificat).</summary>
    private static CampCitit<DateOnly> Data(IReadOnlyList<RandText> titlu, IReadOnlyList<RandText> randuri)
    {
        foreach (var r in titlu)
        {
            if (PotrivireDataEticheta().Match(r.Text) is { Success: true } m && ParseazaData(m.Groups["d"].Value) is { } d)
                return new CampCitit<DateOnly>(d, CertitudineOcr(r), m.Value.Trim());
        }

        foreach (var r in titlu.Concat(randuri))
        {
            if (PotrivireData().Match(r.Text) is { Success: true } m && ParseazaData(m.Value) is { } d)
                return new CampCitit<DateOnly>(d, Certitudine.Nesigur, m.Value);
        }

        return CampCitit<DateOnly>.Lipsa();
    }

    /// <summary>
    /// Furnizorul: textul de după eticheta „Furnizor”, pe același rând sau pe rândurile următoare, doar în coloana lui (la
    /// stânga etichetei „Client”, care stă de obicei în dreapta, pe același rând sau pe unul vecin).
    /// </summary>
    private static string? Furnizor(IReadOnlyList<RandText> randuri)
    {
        for (var i = 0; i < randuri.Count; i++)
        {
            var eticheta = randuri[i].Cuvinte.FirstOrDefault(c => Normalizeaza(c.Text).StartsWith("FURNIZOR", StringComparison.Ordinal));
            if (eticheta is null)
                continue;

            var arePozitii = randuri[i].Cuvinte.Any(c => c.X1 > 0);
            if (!arePozitii)
            {
                var m = PotrivireFurnizorText().Match(randuri[i].Text);
                if (m.Success && Curata(m.Groups["v"].Value) is { Length: >= 3 } v)
                    return v;
                if (i + 1 < randuri.Count && Curata(randuri[i + 1].Text) is { Length: >= 3 } urm && !EsteEticheta(urm))
                    return urm;
                continue;
            }

            var client = Enumerable.Range(Math.Max(0, i - 3), Math.Min(randuri.Count, i + 4) - Math.Max(0, i - 3))
                .SelectMany(j => randuri[j].Cuvinte)
                .Where(c => c.X0 > eticheta.X1 && EsteEtichetaClient(Normalizeaza(c.Text)))
                .Select(c => c.X0).DefaultIfEmpty(int.MaxValue).Min();
            var stanga = eticheta.X0 - 40;

            string? Coloana(IEnumerable<CuvantText> cuvinte) =>
                Curata(string.Join(' ', cuvinte.Where(c => c.X0 >= stanga && c.X0 < client - 5 && c.Incredere >= 40).Select(c => c.Text)));

            var dupaEticheta = randuri[i].Cuvinte.SkipWhile(c => !ReferenceEquals(c, eticheta)).Skip(1);
            if (Coloana(dupaEticheta) is { Length: >= 3 } peRand && !EsteEticheta(peRand))
                return peRand;
            for (var j = i + 1; j < randuri.Count && j <= i + 3; j++)
            {
                if (Coloana(randuri[j].Cuvinte) is { Length: >= 3 } t && !EsteEticheta(t) && t.Any(char.IsLetter))
                    return t;
            }
        }

        return null;
    }

    /// <summary>Lucrarea (pentru propunerea proiectului): „LUCRARE: …” sau observațiile („Santier …”, „Nr. proiect …”).</summary>
    private static string? Lucrare(IReadOnlyList<RandText> randuri)
    {
        foreach (var r in randuri)
        {
            if (PotrivireLucrare().Match(r.Text) is { Success: true } m && Curata(m.Groups["v"].Value) is { Length: >= 3 } v)
                return v;
        }

        for (var i = 0; i < randuri.Count; i++)
        {
            var t = Normalizeaza(randuri[i].Text);
            if (!t.StartsWith("OBSERVATI", StringComparison.Ordinal))
                continue;
            var dupa = Curata(PotrivireObservatii().Replace(randuri[i].Text, string.Empty));
            var parti = new List<string>();
            if (dupa.Length >= 3)
                parti.Add(dupa);
            for (var j = i + 1; j < randuri.Count && j <= i + 2; j++)
                parti.Add(randuri[j].Text.Trim());
            var text = string.Join(' ', parti).Trim();
            return text.Length < 3 ? null : text[..Math.Min(text.Length, 300)];
        }

        return null;
    }

    // ------------------------------------------------------------------ materiale

    /// <summary>
    /// Liniile tabelului, pe rânduri fizice (ca la necesarele de materiale): rândul de text care se termină cu „UM cantitate”
    /// (eventual urmat de preț / valoare) ține locul materialului, iar celelalte rânduri de text ale tabelului (denumirea lungă,
    /// scrisă pe mai multe rânduri, deasupra și / sau dedesubtul cantității) se lipesc de materialul lor: după banda dintre
    /// liniile orizontale ale tabelului, dacă s-au găsit, altfel de rândul cu cantitate cel mai apropiat pe verticală. Așa,
    /// continuarea unei denumiri nu ajunge la materialul următor, iar un rând fără cantitate nu devine material separat.
    /// Denumirea e textul din coloana ei (la stânga UM-ului), fără numărul curent și fără rândurile de detaliu
    /// („REAVIZARE AVIZ …”, „Cod NC: …”).
    /// </summary>
    private static List<LinieAvizCitita> Linii(IReadOnlyList<RandText> randuri, IReadOnlyList<LinieOrizontala> liniiOrizontale, bool dinOcr)
    {
        var start = 0;
        for (var i = 0; i < randuri.Count; i++)
        {
            var t = Normalizeaza(randuri[i].Text);
            if ((t.Contains("CANT", StringComparison.Ordinal) && (Regex.IsMatch(t, @"(?<![A-Z])U\.?M\.?(?![A-Z])") || t.Contains("DENUMIRE", StringComparison.Ordinal))) ||
                (t.Contains("DENUMIRE", StringComparison.Ordinal) && (t.Contains("PRODUS", StringComparison.Ordinal) || t.Contains("SERVICII", StringComparison.Ordinal))))
            {
                start = i + 1;
            }
        }

        // Rândurile tabelului, până la total / observații / datele expediției (după primul material găsit).
        var tabel = new List<RandAviz>();
        for (var i = start; i < randuri.Count; i++)
        {
            var t = Normalizeaza(randuri[i].Text).TrimStart('[', '|', ' ');
            if (EsteSfarsitTabel(t))
            {
                if (tabel.Any(r => r.EsteMaterial))
                    break;
                continue;
            }

            if (EsteAntetTabel(t))
                continue; // a doua parte a antetului („crt.”, „sau servicii”, „unitar (21%)”)
            tabel.Add(RandAviz.Din(randuri[i]));
        }

        var materiale = tabel.Where(r => r.EsteMaterial).ToList();
        if (materiale.Count == 0)
            return [];

        // Celula fiecărui material: rândurile de text lipite de rândul cu cantitate, deasupra și dedesubt.
        var grupuri = materiale.ToDictionary(m => m, m => new List<RandAviz> { m });
        if (tabel.Any(r => r.Rand.ArePozitieVerticala))
        {
            var benzi = BenziAviz.Din(liniiOrizontale, materiale);
            var inaltime = materiale.Where(m => m.Rand.ArePozitieVerticala).Select(m => m.Rand.Y1 - m.Rand.Y0).Order().ToList();
            var distantaMaxima = inaltime.Count == 0 ? 40 : Math.Max(15, (int)(inaltime[inaltime.Count / 2] * 1.6));
            // Un rând aflat între două materiale ține de cel mai apropiat dintre ele.
            var alMaterialului = tabel.Where(r => !r.EsteMaterial && r.Rand.ArePozitieVerticala)
                .ToDictionary(r => r, r => CelMaiApropiat(r, materiale, benzi));
            foreach (var m in materiale.Where(m => m.Rand.ArePozitieVerticala))
            {
                var candidati = alMaterialului.Where(x => x.Value == m).Select(x => x.Key).ToList();
                var coloana = m.ColoanaDenumire() ?? candidati.Where(r => r.Califica(null, m.XUm))
                    .OrderBy(r => Distanta(r.Rand, m.Rand)).Select(r => r.PrimulCuvantCuLitere()?.X0).FirstOrDefault();
                foreach (var directie in new[] { -1, 1 })
                {
                    var capat = m.Rand;
                    var sir = candidati.Where(r => directie < 0 ? r.Rand.Y0 < m.Rand.Y0 : r.Rand.Y0 > m.Rand.Y0)
                        .OrderBy(r => directie < 0 ? -r.Rand.Y0 : r.Rand.Y0);
                    foreach (var r in sir)
                    {
                        var aceeasiBanda = benzi is not null && benzi.Banda(r.Rand) is { } b && b == benzi.Banda(m.Rand);
                        if (!r.Califica(coloana, m.XUm) || (!aceeasiBanda && Distanta(r.Rand, capat) > distantaMaxima))
                            break; // celula se termină la primul rând străin sau la un spațiu mare
                        grupuri[m].Add(r);
                        capat = r.Rand;
                    }
                }
            }
        }
        else
        {
            // Fără poziții verticale (Word, unele PDF-uri): rândurile de text de imediat dinaintea cantității țin de ea, cât
            // sunt în coloana denumirii și nu sunt etichete (urcarea se oprește la primul rând străin sau la alt material).
            for (var i = 0; i < tabel.Count; i++)
            {
                var m = tabel[i];
                if (!m.EsteMaterial)
                    continue;
                int? coloana = m.ColoanaDenumire();
                for (var j = i - 1; j >= 0 && !tabel[j].EsteMaterial; j--)
                {
                    var r = tabel[j];
                    coloana ??= r.Califica(null, m.XUm) ? r.PrimulCuvantCuLitere()?.X0 : null;
                    if (!r.Califica(coloana, m.XUm))
                        break;
                    grupuri[m].Add(r);
                }
            }
        }

        var linii = new List<LinieAvizCitita>();
        foreach (var m in materiale)
        {
            var randuriMaterial = grupuri[m].OrderBy(r => r.Rand.Pagina).ThenBy(r => r.Rand.Y0).ThenBy(r => tabel.IndexOf(r)).ToList();
            var parti = randuriMaterial
                .Select(r => r.Denumire(m.XUm))
                .Where(d => d.Length > 0 && !EsteDetaliu(d))
                .ToList();
            var textDenumire = string.Join(' ', parti);
            if (textDenumire.Length > 300)
                textDenumire = textDenumire[..300].TrimEnd();

            var umCitita = m.Cuvinte[m.K].Text;
            var um = UnitateMasura(umCitita)!;
            var corectata = UnitatiOcr.ContainsKey(Normalizeaza(umCitita).TrimEnd('.'));
            var incredere = m.Rand.Cuvinte.Count == 0 ? 100 : m.Rand.Cuvinte.Min(c => c.Incredere);
            var cert = dinOcr && incredere < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;
            var textCantitate = m.Cuvinte[m.K + 1].Text;
            var cantitate = ParseazaCantitate(textCantitate);
            // Denumirea adunată din mai multe rânduri e de verificat (s-ar putea să fi prins un rând străin).
            var certDenumire = parti.Count > 1 ? Certitudine.Nesigur : cert;
            linii.Add(new LinieAvizCitita(
                linii.Count + 1,
                textDenumire.Any(char.IsLetter) ? new CampCitit<string>(textDenumire, certDenumire, textDenumire) : CampCitit<string>.Lipsa(),
                new CampCitit<string>(um, corectata ? Certitudine.Nesigur : cert, umCitita),
                cantitate is { } q and > 0 ? new CampCitit<decimal>(q, cert, textCantitate) : CampCitit<decimal>.Lipsa(textCantitate)));
        }

        return linii;
    }

    /// <summary>Materialul cel mai apropiat de rând, pe aceeași pagină: cel din aceeași bandă a tabelului, altfel după distanța verticală.</summary>
    private static RandAviz? CelMaiApropiat(RandAviz r, IReadOnlyList<RandAviz> materiale, BenziAviz? benzi)
    {
        if (benzi?.Banda(r.Rand) is { } banda && materiale.Where(m => benzi.Banda(m.Rand) == banda).ToList() is { Count: 1 } dinBanda)
            return dinBanda[0];
        return materiale
            .Where(m => m.Rand.Pagina == r.Rand.Pagina && m.Rand.ArePozitieVerticala)
            .OrderBy(m => Distanta(r.Rand, m.Rand))
            .FirstOrDefault();
    }

    /// <summary>Spațiul vertical dintre două rânduri (0 dacă se suprapun).</summary>
    private static int Distanta(RandText a, RandText b) =>
        a.Y0 >= b.Y1 ? a.Y0 - b.Y1 : a.Y1 <= b.Y0 ? b.Y0 - a.Y1 : 0;

    private static readonly HashSet<string> CuvinteAntet =
    [
        "NR", "CRT", "DENUMIRE", "DENUMIREA", "PRODUSE", "PRODUSELOR", "SERVICII", "SERVICIILOR", "SAU", "SI", "PRODUSE/SERVICII", "UM", "U.M", "CANT",
        "CANTITATE", "PRET", "UNITAR", "VALOARE", "VALOAREA", "TVA", "LEI", "RON", "FARA", "-", "|", "_",
    ];

    /// <summary>Rând care conține doar cuvinte de antet de tabel (a doua parte a antetului, pe un rând separat).</summary>
    private static bool EsteAntetTabel(string normalizat)
    {
        var cuvinte = normalizat.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim('.', ',', ':', '(', ')', '[', ']')).Where(c => c.Length > 0).ToList();
        return cuvinte.Any(c => c.Any(char.IsLetter)) &&
               cuvinte.All(c => CuvinteAntet.Contains(c) || (c.EndsWith('%') && c[..^1].All(char.IsDigit)));
    }

    /// <summary>Rândurile de detaliu din celula denumirii, care nu fac parte din denumirea materialului.</summary>
    private static bool EsteDetaliu(string denumire)
    {
        var t = Normalizeaza(denumire);
        return t.StartsWith("REAVIZARE", StringComparison.Ordinal) || t.StartsWith("COD NC", StringComparison.Ordinal) ||
               t.StartsWith("NC:", StringComparison.Ordinal) || t.StartsWith("COD VAMAL", StringComparison.Ordinal);
    }

    private sealed record CuvantAviz(string Text, int X0, int X1);

    /// <summary>Un rând al tabelului: cuvintele (cu poziții, dacă se știu) și, la rândul cu cantitate, poziția UM-ului.</summary>
    /// <param name="K">Indexul cuvântului UM (urmat de cantitate); -1 = rând fără cantitate.</param>
    private sealed record RandAviz(RandText Rand, IReadOnlyList<CuvantAviz> Cuvinte, int K)
    {
        public bool EsteMaterial => K >= 0;
        public bool AreText => Cuvinte.Any(c => c.Text.Count(char.IsLetter) >= 2);

        /// <summary>X-ul UM-ului (granița coloanei denumirii); 0 fără poziții.</summary>
        public int XUm => K >= 0 ? Cuvinte[K].X0 : 0;

        /// <summary>Primul cuvânt cu litere (începutul textului din coloana denumirii).</summary>
        public CuvantAviz? PrimulCuvantCuLitere() => Cuvinte.FirstOrDefault(c => c.Text.Count(char.IsLetter) >= 1);

        /// <summary>Începutul coloanei denumirii pe rândul cu cantitate (null dacă rândul are doar nr., UM și cantitate).</summary>
        public int? ColoanaDenumire() =>
            Cuvinte.Take(Math.Max(K, 0)).FirstOrDefault(c => c.Text.Any(char.IsLetter) && c.X1 > 0 && c.X1 < XUm - 5)?.X0;

        /// <summary>
        /// Rândul poate fi parte din celula denumirii: text citit sigur (nu zgomot OCR), fără etichete („Numele delegatului:”),
        /// început în coloana denumirii (±45 px) și terminat înaintea coloanei UM.
        /// </summary>
        public bool Califica(int? coloana, int xUm)
        {
            if (!AreText || Cuvinte.Any(c => c.Text.EndsWith(':')))
                return false;
            if (Rand.Cuvinte.Count > 0 && Rand.Cuvinte.Average(c => c.Incredere) < 60)
                return false;
            var primul = PrimulCuvantCuLitere();
            if (primul is null || primul.Text.Count(char.IsLetter) < 2 || (xUm > 0 && primul.X0 >= xUm - 5))
                return false;
            return coloana is null || Math.Abs(primul.X0 - coloana.Value) <= 45;
        }

        public static RandAviz Din(RandText rand)
        {
            var cuvinte = (rand.Cuvinte.Count > 0 && rand.Cuvinte.Any(c => c.X1 > 0)
                    ? rand.Cuvinte.Select(c => new CuvantAviz(c.Text.Trim('|', '[', ']'), c.X0, c.X1))
                    : rand.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(c => new CuvantAviz(c.Trim('|', '[', ']'), 0, 0)))
                .Where(c => c.Text.Length > 0)
                .ToList();
            var k = -1;
            for (var j = cuvinte.Count - 2; j >= 1; j--)
            {
                if (UnitateMasura(cuvinte[j].Text) is not null && EsteNumar(cuvinte[j + 1].Text) && cuvinte.Skip(j + 2).All(c => EsteValoareSauSemn(c.Text)))
                {
                    k = j;
                    break;
                }
            }

            return new RandAviz(rand, cuvinte, k);
        }

        /// <summary>
        /// Textul din coloana denumirii: cuvintele de la stânga UM-ului materialului (toate, fără poziții), fără numărul curent
        /// din față („1”, „2.”).
        /// </summary>
        public string Denumire(int xUm)
        {
            IEnumerable<CuvantAviz> cuvinte = K >= 0 ? Cuvinte.Take(K) : Cuvinte;
            if (xUm > 0)
                cuvinte = cuvinte.Where(c => c.X1 == 0 || c.X1 < xUm - 5);
            var lista = cuvinte.ToList();
            if (lista.Count > 0 && int.TryParse(lista[0].Text.TrimEnd('.', ')'), out var nr) && nr is > 0 and < 1000 &&
                (lista.Count > 1 || K >= 0))
                lista.RemoveAt(0);
            return Curata(string.Join(' ', lista.Select(c => c.Text)));
        }
    }

    /// <summary>Benzile tabelului: spațiile dintre liniile orizontale care traversează coloanele denumire → cantitate.</summary>
    private sealed class BenziAviz(Dictionary<int, List<LinieOrizontala>> pePagini)
    {
        public static BenziAviz? Din(IReadOnlyList<LinieOrizontala> linii, IReadOnlyList<RandAviz> materiale)
        {
            var cuPozitii = materiale.Where(m => m.XUm > 0).ToList();
            if (linii.Count == 0 || cuPozitii.Count == 0)
                return null;
            var stanga = cuPozitii.Min(m => m.Cuvinte[0].X0);
            var um = cuPozitii.Max(m => m.XUm);
            var pePagini = linii.Where(l => l.X0 <= stanga + 30 && l.X1 >= um)
                .GroupBy(l => l.Pagina).Where(g => g.Count() >= 2).ToDictionary(g => g.Key, g => g.ToList());
            return pePagini.Count == 0 ? null : new BenziAviz(pePagini);
        }

        public int? Banda(RandText rand)
        {
            if (!rand.ArePozitieVerticala || !pePagini.TryGetValue(rand.Pagina, out var pagina) || rand.Cuvinte.Count == 0)
                return null;
            var x = (rand.Cuvinte.Min(c => c.X0) + rand.Cuvinte.Max(c => c.X1)) / 2.0;
            var y = (rand.Y0 + rand.Y1) / 2.0;
            var deasupra = pagina.Count(l => l.YLa(x) < y);
            return deasupra == 0 || deasupra == pagina.Count ? null : rand.Pagina * 1000 + deasupra;
        }
    }

    private static bool EsteSfarsitTabel(string t) =>
        t.StartsWith("TOTAL", StringComparison.Ordinal) || t.StartsWith("OBSERVATI", StringComparison.Ordinal) || t.StartsWith("COD UIT", StringComparison.Ordinal) ||
        t.StartsWith("DATE PRIVIND", StringComparison.Ordinal) || t.StartsWith("EXPEDITIA", StringComparison.Ordinal) || t.StartsWith("LUCRARE", StringComparison.Ordinal) ||
        t.StartsWith("EMIS DE", StringComparison.Ordinal) || t.StartsWith("INTOCMIT", StringComparison.Ordinal) || t.StartsWith("SEMNATURA", StringComparison.Ordinal);

    /// <summary>UM-ul normalizat (minuscule, ca în nomenclator) sau null dacă textul nu e o unitate de măsură.</summary>
    private static string? UnitateMasura(string text)
    {
        var t = Normalizeaza(text).TrimEnd('.');
        if (UnitatiOcr.TryGetValue(t, out var corect))
            t = corect;
        return Unitati.Contains(t) ? t.ToLowerInvariant() : null;
    }

    private static bool EsteNumar(string text) => ParseazaCantitate(text) is not null;

    private static bool EsteValoareSauSemn(string text) => EsteNumar(text) || text is "-" or "—" || text.EndsWith('%');

    /// <summary>„11.002,34”, „101.7”, „24.920”; OCR-ul pune uneori punct și în locul virgulei („1.001.66” = 1001,66).</summary>
    private static decimal? ParseazaCantitate(string text)
    {
        var t = text.Trim().TrimEnd('.', ',');
        if (!t.Contains(',') && t.Count(ch => ch == '.') >= 2 && t.Length - t.LastIndexOf('.') - 1 == 2)
        {
            var ultim = t.LastIndexOf('.');
            var reparat = t[..ultim].Replace(".", string.Empty, StringComparison.Ordinal) + "." + t[(ultim + 1)..];
            return decimal.TryParse(reparat, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var r) ? r : null;
        }

        return TextNumeric.ParseazaNumar(t, out var v) ? v : null;
    }

    // ------------------------------------------------------------------ utilitare

    private static bool EsteEtichetaClient(string t) =>
        t.StartsWith("CLIENT", StringComparison.Ordinal) || t.StartsWith("CUMPARATOR", StringComparison.Ordinal) || t.StartsWith("BENEFICIAR", StringComparison.Ordinal) ||
        t.StartsWith("DESTINATAR", StringComparison.Ordinal);

    private static bool EsteEticheta(string text)
    {
        var t = Normalizeaza(text);
        return EsteEtichetaClient(t) || t.StartsWith("CIF", StringComparison.Ordinal) || t.StartsWith("CUI", StringComparison.Ordinal) ||
               t.StartsWith("NRC", StringComparison.Ordinal) || t.StartsWith("REG", StringComparison.Ordinal);
    }

    private static Certitudine CertitudineOcr(RandText rand) =>
        rand.Cuvinte.Count > 0 && rand.Cuvinte.Min(c => c.Incredere) < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;

    private static string Curata(string text) => SpatiiMultiple().Replace(text, " ").Trim(' ', ':', '-', ',', '|', '_', '.').Trim();

    private static DateOnly? ParseazaData(string text)
    {
        var p = text.Split('.', '/', '-');
        return p.Length == 3 && int.TryParse(p[0], out var z) && int.TryParse(p[1], out var l) && int.TryParse(p[2], out var a) &&
               a is >= 2000 and <= 2100 && l is >= 1 and <= 12 && z >= 1 && z <= DateTime.DaysInMonth(a, l)
            ? new DateOnly(a, l, z)
            : null;
    }

    private static string Normalizeaza(string text)
    {
        var fd = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(fd.Length);
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToUpperInvariant(ch));
        }

        return SpatiiMultiple().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>„Serie/Numar: INTFSN/20488”.</summary>
    [GeneratedRegex(@"\bSeri[ea]\s*/\s*Num[aă]r\s*[:.]?\s*(?<n>[A-Z0-9][A-Z0-9/\-.]*\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireSerieSlashNumar();

    /// <summary>„Serie AVSOV Numar 6786”, „Seria ABC nr. 123” (OCR: „Serle”).</summary>
    [GeneratedRegex(@"\bSer(?:i|l)[ea]\s*[:.]?\s*(?<s>[A-Z]{1,10})\s*,?\s*(?:Num[aă]r(?:ul)?|Nr\.?)\s*[:.]?\s*(?<n>\d[\d\-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireSerieNumar();

    /// <summary>„Număr OTL-220”, „Nr. 220” (în antet; nu „Nr. crt.”).</summary>
    [GeneratedRegex(@"\b(?:Num[aă]r|Nr)\.?\s*[:.]?\s*(?<n>[A-Z]{0,8}-?\d[\d/\-]*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireNumar();

    [GeneratedRegex(@"\bDat[aă](?:\s+(?:emiterii|emitere|facturare|avizului))?\s*[:.]?\s*(?<d>\d{1,2}[./-]\d{1,2}[./-]\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireDataEticheta();

    [GeneratedRegex(@"(?<!\d)\d{1,2}[./]\d{1,2}[./]\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireData();

    /// <summary>CUI / CIF / Cod fiscal (OCR: „CUL”, „CU1”).</summary>
    [GeneratedRegex(@"(?:\bC\.?U\.?[IL1]\.?|\bC\.?I\.?F\.?|\bCod\s+fiscal)\s*[:.]?\s*(?<v>(?:RO)?\s?\d{2,10})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireCui();

    [GeneratedRegex(@"Furnizor(?:ul)?\s*:?\s*(?<v>.+?)(?=\s+(?:Client|Cump[aă]r[aă]tor|Beneficiar|CIF|CUI|NRC|Reg)\b|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireFurnizorText();

    [GeneratedRegex(@"\bLucrare[a]?\s*[:.]\s*(?<v>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireLucrare();

    [GeneratedRegex(@"^\s*Observa[tț]i[i]?\s*[:.]?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireObservatii();

    /// <summary>„CF COMANDA 244 / 02.06.2026”, „Comanda nr. 12”.</summary>
    [GeneratedRegex(@"\bComand[aă]\s*(?:nr\.?|n[or]\.?)?\s*[:.]?\s*(?<n>[A-Z]{0,5}[\-/]?\d{1,7})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireComanda();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpatiiMultiple();
}
