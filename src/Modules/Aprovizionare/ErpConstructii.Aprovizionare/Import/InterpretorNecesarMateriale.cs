using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Aprovizionare.Import;

/// <summary>
/// Interpretează formularul „Necesar materiale” / notă de comandă emis în afara sistemului (REQ-FRM-08, REQ-FRM-10):
/// antetul (Nr., Data, Compartiment/Șantier, Lucrare) și tabelul de materiale (Nr. crt., Denumire, UM, Cantitate, Observații).
///
/// Funcție pură peste <see cref="TextDocument"/>: aceeași logică pentru Word (tabele exacte) și pentru scanări
/// (rânduri OCR, coloanele deduse din poziția cuvintelor din antetul tabelului). Fiecare câmp primește o
/// <see cref="Certitudine"/>; ce nu e sigur se arată utilizatorului pentru verificare.
/// </summary>
public static partial class InterpretorNecesarMateriale
{
    /// <summary>Unitățile de măsură uzuale, cu forma normalizată.</summary>
    private static readonly Dictionary<string, string> UnitatiCunoscute = new(StringComparer.OrdinalIgnoreCase)
    {
        ["buc"] = "buc", ["bucata"] = "buc", ["bucati"] = "buc", ["buc."] = "buc",
        ["kg"] = "kg", ["t"] = "t", ["to"] = "t", ["tona"] = "t", ["tone"] = "t",
        ["m"] = "m", ["ml"] = "ml", ["mp"] = "mp", ["m2"] = "mp", ["mc"] = "mc", ["m3"] = "mc",
        ["l"] = "l", ["litri"] = "l", ["litru"] = "l",
        ["set"] = "set", ["seturi"] = "set", ["bax"] = "bax", ["baxuri"] = "bax",
        ["cut"] = "cut", ["cutie"] = "cut", ["cutii"] = "cut", ["rola"] = "rola", ["role"] = "rola",
        ["sac"] = "sac", ["saci"] = "sac", ["per"] = "per", ["perechi"] = "per", ["pereche"] = "per",
        ["pac"] = "pac", ["pachet"] = "pac", ["kit"] = "kit", ["palet"] = "palet", ["paleti"] = "palet",
    };

    /// <summary>Confuziile tipice OCR în numere (litere citite în loc de cifre).</summary>
    private static readonly Dictionary<char, char> ConfuziiCifre = new()
    {
        ['O'] = '0', ['o'] = '0', ['D'] = '0', ['l'] = '1', ['I'] = '1', ['|'] = '1', ['i'] = '1',
        ['S'] = '5', ['s'] = '5', ['B'] = '8', ['Z'] = '2', ['z'] = '2',
    };

    private enum Coloana { Nr, Denumire, Um, Cantitate, Observatii }

    public static NotaComandaCitita Interpreteaza(TextDocument document)
    {
        var probleme = new List<ProblemaImport>();
        var randuri = document.Randuri;

        var numar = CitesteNumar(randuri);
        var data = CitesteData(randuri);
        var compartiment = CitesteCompartiment(randuri);
        var lucrare = CitesteLucrare(randuri);

        var linii = document.Tabele.Count > 0 && GasesteTabelMateriale(document.Tabele) is { } tabel
            ? LiniiDinTabel(tabel, probleme)
            : LiniiDinRanduri(randuri, document.DinOcr, probleme);

        if (linii.Count == 0)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "tabel",
                "Nu am găsit materiale în document. Adaugă-le manual."));
        }

        foreach (var (camp, eticheta) in new (Certitudine, string)[]
                 {
                     (numar.Certitudine, "numărul documentului"), (data.Certitudine, "data"),
                     (compartiment.Certitudine, "compartimentul / șantierul"), (lucrare.Certitudine, "lucrarea"),
                 })
        {
            if (camp == Certitudine.Lipsa)
            {
                probleme.Add(new ProblemaImport(Severitate.Informare, "antet", $"Nu am găsit {eticheta}; completează-l."));
            }
        }

        return new NotaComandaCitita(numar, data, compartiment, lucrare, linii, probleme, document.DinOcr);
    }

    // ------------------------------------------------------------------ antet

    private static CampCitit<string> CitesteNumar(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            foreach (Match m in PotrivireNumar().Matches(rand.Text))
            {
                var valoare = m.Groups["v"].Value.Trim().TrimEnd('.', ',', ';');
                if (EstePlaceholder(valoare) || valoare.Length == 0)
                {
                    return CampCitit<string>.Lipsa(m.Value);
                }

                if (!valoare.Any(char.IsAsciiDigit))
                {
                    continue; // „Nr. Crt.” etc.
                }

                return new CampCitit<string>(valoare, CertitudineOcr(rand, m.Groups["v"].Value), m.Value.Trim());
            }
        }

        return CampCitit<string>.Lipsa();
    }

    private static CampCitit<DateOnly> CitesteData(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            var m = PotrivireData().Match(rand.Text);
            if (!m.Success)
            {
                continue;
            }

            if (EstePlaceholder(m.Groups["v"].Value))
            {
                return CampCitit<DateOnly>.Lipsa(m.Value);
            }

            var corectat = CorecteazaCifre(m.Groups["v"].Value, out var aFostCorectat);
            var d = PotrivireValoareData().Match(corectat);
            if (d.Success && ParseazaData(d, out var data))
            {
                // Virgula în loc de punct e o greșeală tipică de recunoaștere: data e probabil corectă, dar de verificat.
                var certitudine = aFostCorectat || d.Value.Contains(',')
                    ? Certitudine.Nesigur
                    : CertitudineOcr(rand, m.Groups["v"].Value);
                return new CampCitit<DateOnly>(data, certitudine, m.Value.Trim());
            }

            return new CampCitit<DateOnly>(default, Certitudine.Lipsa, m.Value.Trim());
        }

        return CampCitit<DateOnly>.Lipsa();
    }

    private static CampCitit<string> CitesteCompartiment(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            var m = PotrivireCompartiment().Match(rand.Text);
            if (!m.Success)
            {
                continue;
            }

            // Substituentul din șablon („{{Compartiment/Santier}}”) conține chiar eticheta: câmpul e necompletat.
            if (m.Index >= 2 && rand.Text.AsSpan(m.Index - 2, 2) is "{{")
            {
                return CampCitit<string>.Lipsa(rand.Text.Trim());
            }

            // Valoarea se oprește la următoarea celulă/etichetă de pe același rând (ex. „| Data: …”).
            var valoare = TaieLaEticheta(TextFiabil(rand, m.Groups["v"].Index));
            valoare = CurataCompletare(valoare);
            return valoare.Length == 0 || EstePlaceholder(valoare)
                ? CampCitit<string>.Lipsa(m.Value.Trim())
                : new CampCitit<string>(valoare, CertitudineOcr(rand, valoare), m.Value.Trim());
        }

        // Șablonul Word poate avea doar substituentul „{{Compartiment/Santier}}”, fără etichetă.
        return CampCitit<string>.Lipsa();
    }

    private static CampCitit<string> CitesteLucrare(IReadOnlyList<RandText> randuri)
    {
        for (var i = 0; i < randuri.Count; i++)
        {
            var m = PotrivireLucrare().Match(randuri[i].Text);
            if (!m.Success)
            {
                continue;
            }

            var parti = new List<string> { TextFiabil(randuri[i], m.Groups["v"].Index) };
            var incredereMinima = CertitudineOcr(randuri[i], m.Groups["v"].Value);

            // Textul lucrării continuă pe rândurile următoare, până la antetul tabelului.
            for (var j = i + 1; j < randuri.Count && j <= i + 3; j++)
            {
                var urmator = Normalizeaza(randuri[j].Text);
                if (EsteAntetTabel(randuri[j].Text) || (urmator.StartsWith("NR", StringComparison.Ordinal) && urmator.Length <= 8))
                {
                    break;
                }

                parti.Add(randuri[j].Text);
                if (CertitudineOcr(randuri[j], randuri[j].Text) == Certitudine.Nesigur)
                {
                    incredereMinima = Certitudine.Nesigur;
                }
            }

            var valoare = CurataCompletare(string.Join(' ', parti.Select(p => p.Trim())));
            if (valoare.Length == 0 || EstePlaceholder(valoare))
            {
                return CampCitit<string>.Lipsa(m.Value.Trim());
            }

            return new CampCitit<string>(Scurteaza(valoare, 500), incredereMinima, m.Value.Trim());
        }

        return CampCitit<string>.Lipsa();
    }

    // ------------------------------------------------------------------ tabel din Word

    private static TabelText? GasesteTabelMateriale(IReadOnlyList<TabelText> tabele) =>
        tabele.FirstOrDefault(t => t.Randuri.Any(r => EsteAntetTabel(string.Join(" ", r))));

    private static List<LinieCitita> LiniiDinTabel(TabelText tabel, List<ProblemaImport> probleme)
    {
        var indexAntet = tabel.Randuri.ToList().FindIndex(r => EsteAntetTabel(string.Join(" ", r)));
        var antet = tabel.Randuri[indexAntet].Select(Normalizeaza).ToList();
        int Col(Func<string, bool> criteriu) => antet.FindIndex(c => criteriu(c));

        var coloane = new Dictionary<Coloana, int>
        {
            [Coloana.Nr] = Col(c => c.StartsWith("NR", StringComparison.Ordinal)),
            [Coloana.Denumire] = Col(c => c.Contains("DENUMIRE", StringComparison.Ordinal) || c.Contains("MATERIAL", StringComparison.Ordinal)),
            [Coloana.Um] = Col(c => c is "UM" or "U.M" or "U.M." or "U M"),
            [Coloana.Cantitate] = Col(c => c.Contains("CANTIT", StringComparison.Ordinal)),
            [Coloana.Observatii] = Col(c => c.Contains("OBSERV", StringComparison.Ordinal)),
        };

        var linii = new List<LinieCitita>();
        for (var i = indexAntet + 1; i < tabel.Randuri.Count; i++)
        {
            var celule = tabel.Randuri[i];
            string Celula(Coloana c) => coloane[c] >= 0 && coloane[c] < celule.Count ? celule[coloane[c]].Trim() : string.Empty;

            var denumire = Celula(Coloana.Denumire);
            var cantitate = Celula(Coloana.Cantitate);
            if ((denumire.Length == 0 || EstePlaceholder(denumire)) && cantitate.Length == 0)
            {
                continue; // rând gol al formularului
            }

            var rand = int.TryParse(Celula(Coloana.Nr).TrimEnd('.'), out var nr) ? nr : linii.Count + 1;
            linii.Add(ConstruiesteLinie(rand, denumire, Celula(Coloana.Um), cantitate, Celula(Coloana.Observatii), null, probleme));
        }

        return linii;
    }

    // ------------------------------------------------------------------ tabel din scanare (OCR)

    private static List<LinieCitita> LiniiDinRanduri(IReadOnlyList<RandText> randuri, bool dinOcr, List<ProblemaImport> probleme)
    {
        var indexAntet = randuri.ToList().FindIndex(r => EsteAntetTabel(r.Text));
        if (indexAntet < 0)
        {
            return [];
        }

        var limite = LimiteColoane(randuri[indexAntet]);
        var linii = new List<RandTabel>();

        for (var i = indexAntet + 1; i < randuri.Count; i++)
        {
            var rand = randuri[i];
            if (EsteSubsol(rand.Text))
            {
                break;
            }

            // „Crt.” din antetul „Nr. / Crt.” scris pe două rânduri.
            if (Normalizeaza(rand.Text).TrimEnd('.') == "CRT")
            {
                continue;
            }

            var cuvinte = rand.Cuvinte.Count > 0
                ? rand.Cuvinte.Where(c => !EsteDoarPunctuatie(c.Text)).ToList()
                : rand.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => new CuvantText(t, 0, 0, 100)).ToList();
            if (cuvinte.Count == 0)
            {
                continue;
            }

            int? nr = null;
            if (limite is not null && int.TryParse(CorecteazaCifre(cuvinte[0].Text.TrimEnd('.'), out _), out var n) && n is > 0 and < 1000
                && cuvinte[0].X1 <= limite.Value.InceputDenumire)
            {
                nr = n;
                cuvinte = cuvinte.Skip(1).ToList();
            }

            var curent = new RandTabel(nr ?? 0, rand);
            foreach (var c in cuvinte)
            {
                curent.Lista(limite is null ? Coloana.Denumire : ColoanaCuvant(c, limite.Value)).Add(c);
            }

            var anterior = linii.Count > 0 ? linii[^1] : null;
            if (nr is null && anterior is not null && DeUnitCuAnterior(curent, anterior))
            {
                anterior.Adauga(curent);
                continue;
            }

            if (nr is null)
            {
                curent.Nr = (anterior?.Nr ?? 0) + 1; // numărul de rând nu s-a citit
            }

            linii.Add(curent);
        }

        var rezultat = new List<LinieCitita>();
        foreach (var (nr, den, um, cant, obs) in linii.Select(l => (l.Nr, l.Den, l.Um, l.Cant, l.Obs)))
        {
            var denumire = string.Join(' ', den.Select(c => c.Text));
            if (denumire.Count(char.IsLetter) < 3 && cant.Count == 0)
            {
                if (denumire.Length > 0 || um.Count > 0)
                {
                    probleme.Add(new ProblemaImport(Severitate.Informare, $"rândul {nr}",
                        "Rândul pare gol sau barat; l-am ignorat."));
                }

                continue;
            }

            var incredere = new Dictionary<Coloana, float>
            {
                [Coloana.Denumire] = Minim(den), [Coloana.Um] = Minim(um), [Coloana.Cantitate] = Minim(cant), [Coloana.Observatii] = Minim(obs),
            };
            rezultat.Add(ConstruiesteLinie(
                nr, denumire, string.Join(' ', um.Select(c => c.Text)), string.Join(string.Empty, cant.Select(c => c.Text)),
                string.Join(' ', obs.Select(c => c.Text)), dinOcr ? incredere : null, probleme));
        }

        return rezultat;
    }

    /// <summary>Un rând al tabelului în construire (poate aduna mai multe rânduri OCR din aceeași celulă).</summary>
    private sealed class RandTabel(int nr, RandText primul)
    {
        public int Nr { get; set; } = nr;
        public List<CuvantText> Den { get; } = [];
        public List<CuvantText> Um { get; } = [];
        public List<CuvantText> Cant { get; } = [];
        public List<CuvantText> Obs { get; } = [];
        public RandText Primul { get; } = primul;
        public RandText Ultimul { get; private set; } = primul;

        public List<CuvantText> Lista(Coloana c) => c switch
        {
            Coloana.Um => Um,
            Coloana.Cantitate => Cant,
            Coloana.Observatii => Obs,
            _ => Den,
        };

        public void Adauga(RandTabel alt)
        {
            Den.AddRange(alt.Den);
            Um.AddRange(alt.Um);
            Cant.AddRange(alt.Cant);
            Obs.AddRange(alt.Obs);
            Ultimul = alt.Ultimul;
        }
    }

    /// <summary>
    /// Un rând OCR fără număr aparține rândului de tabel de deasupra când:
    ///  - are doar UM / cantitate / observații, iar rândul de deasupra nu le are încă (în celulă sunt scrise mai jos); sau
    ///  - are doar text de denumire, foarte aproape de rândul de deasupra (denumire pe două rânduri).
    /// Fără poziții verticale (Word), un rând doar cu denumire continuă denumirea de deasupra.
    /// </summary>
    private static bool DeUnitCuAnterior(RandTabel curent, RandTabel anterior)
    {
        var sus = anterior.Ultimul;
        var jos = curent.Primul;
        var arePozitii = sus.ArePozitieVerticala && jos.ArePozitieVerticala;
        var inaltime = arePozitii ? sus.Y1 - sus.Y0 : 0;
        var spatiu = arePozitii ? jos.Y0 - sus.Y1 : 0;

        var doarValori = curent.Den.Count == 0 && (curent.Um.Count > 0 || curent.Cant.Count > 0);
        if (doarValori)
        {
            return anterior.Um.Count == 0 && anterior.Cant.Count == 0 && (!arePozitii || spatiu < inaltime * 2);
        }

        var doarDenumire = curent.Den.Count > 0 && curent.Um.Count == 0 && curent.Cant.Count == 0;
        return doarDenumire && (!arePozitii || spatiu < inaltime * 0.8);
    }

    private readonly record struct Limite(int InceputDenumire, int DenumireUm, int UmCantitate, int CantitateObservatii);

    /// <summary>
    /// Limitele coloanelor, din pozițiile cuvintelor din antet: granița dintre două coloane e la mijlocul
    /// spațiului dintre cuvintele lor de antet. Coloana „Nr.” e îngustă: doar un număr la începutul rândului intră în ea.
    /// </summary>
    private static Limite? LimiteColoane(RandText antet)
    {
        CuvantText? Cuvant(Func<string, bool> criteriu) => antet.Cuvinte.FirstOrDefault(c => criteriu(Normalizeaza(c.Text)));

        var denumire = Cuvant(c => c.StartsWith("DENUMIRE", StringComparison.Ordinal));
        var material = Cuvant(c => c.StartsWith("MATERIAL", StringComparison.Ordinal)) ?? denumire;
        var um = Cuvant(c => c is "UM" or "U.M" or "U.M." or "UM.");
        var cantitate = Cuvant(c => c.StartsWith("CANTIT", StringComparison.Ordinal));
        var observatii = Cuvant(c => c.StartsWith("OBSERV", StringComparison.Ordinal));
        if (denumire is null || cantitate is null)
        {
            return null;
        }

        var sfarsitDenumire = material!.X1;
        var umX0 = um?.X0 ?? (sfarsitDenumire + cantitate.X0) / 2;
        var umX1 = um?.X1 ?? umX0;
        var obsX0 = observatii?.X0 ?? cantitate.X1 + (cantitate.X1 - cantitate.X0) * 2;

        return new Limite(
            InceputDenumire: denumire.X0,
            DenumireUm: (sfarsitDenumire + umX0) / 2,
            UmCantitate: (umX1 + cantitate.X0) / 2,
            CantitateObservatii: (cantitate.X1 + obsX0) / 2);
    }

    private static Coloana ColoanaCuvant(CuvantText c, Limite l)
    {
        var centru = (c.X0 + c.X1) / 2;
        if (centru < l.DenumireUm)
        {
            return Coloana.Denumire;
        }

        if (centru < l.UmCantitate)
        {
            return Coloana.Um;
        }

        return centru < l.CantitateObservatii ? Coloana.Cantitate : Coloana.Observatii;
    }

    // ------------------------------------------------------------------ o linie

    private static LinieCitita ConstruiesteLinie(
        int rand, string denumire, string um, string cantitate, string observatii,
        IReadOnlyDictionary<Coloana, float>? incredereOcr, List<ProblemaImport> probleme)
    {
        Certitudine Ocr(Coloana c) =>
            incredereOcr is not null && incredereOcr[c] < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;

        // Denumirea
        denumire = denumire.Trim();
        var campDenumire = denumire.Length == 0 || EstePlaceholder(denumire)
            ? CampCitit<string>.Lipsa()
            : new CampCitit<string>(Scurteaza(denumire, 300), Ocr(Coloana.Denumire), denumire);

        // Unitatea de măsură
        um = um.Trim();
        CampCitit<string> campUm;
        if (um.Length == 0 || EstePlaceholder(um))
        {
            campUm = CampCitit<string>.Lipsa();
        }
        else if (UnitatiCunoscute.TryGetValue(um.TrimEnd('.'), out var umNormalizat) || UnitatiCunoscute.TryGetValue(um, out umNormalizat))
        {
            campUm = new CampCitit<string>(umNormalizat, Ocr(Coloana.Um), um);
        }
        else
        {
            campUm = new CampCitit<string>(Scurteaza(um.ToLowerInvariant(), 30), Certitudine.Nesigur, um);
            probleme.Add(new ProblemaImport(Severitate.Avertisment, $"rândul {rand}", $"Unitate de măsură necunoscută: „{um}”. Verific-o."));
        }

        // Cantitatea
        cantitate = cantitate.Trim();
        CampCitit<decimal> campCantitate;
        if (cantitate.Length == 0 || EstePlaceholder(cantitate))
        {
            campCantitate = CampCitit<decimal>.Lipsa();
        }
        else
        {
            var corectat = CorecteazaCifre(cantitate.Replace(" ", string.Empty, StringComparison.Ordinal), out var aFostCorectat);
            if (ParseazaCantitate(corectat, out var valoare) && valoare > 0)
            {
                var certitudine = aFostCorectat ? Certitudine.Nesigur : Ocr(Coloana.Cantitate);
                campCantitate = new CampCitit<decimal>(valoare, certitudine, cantitate);
            }
            else
            {
                campCantitate = new CampCitit<decimal>(default, Certitudine.Lipsa, cantitate);
                probleme.Add(new ProblemaImport(Severitate.Avertisment, $"rândul {rand}", $"Cantitatea „{cantitate}” nu e un număr valid."));
            }
        }

        observatii = observatii.Trim();
        var campObservatii = observatii.Length == 0 || EstePlaceholder(observatii)
            ? new CampCitit<string>(string.Empty, Certitudine.Sigur)
            : new CampCitit<string>(Scurteaza(observatii, 500), Ocr(Coloana.Observatii), observatii);

        return new LinieCitita(rand, campDenumire, campUm, campCantitate, campObservatii);
    }

    // ------------------------------------------------------------------ utilitare

    private static bool EsteAntetTabel(string text)
    {
        var t = Normalizeaza(text);
        return t.Contains("DENUMIRE", StringComparison.Ordinal) && t.Contains("CANTIT", StringComparison.Ordinal);
    }

    private static bool EsteSubsol(string text)
    {
        var t = Normalizeaza(text);
        return t.Contains("COMPARTIMENT APROVIZIONARE", StringComparison.Ordinal) ||
               t.StartsWith("INTOCMIT", StringComparison.Ordinal) ||
               t.Contains("FORMULAR COD", StringComparison.Ordinal);
    }

    private static Certitudine CertitudineOcr(RandText rand, string fragment)
    {
        if (rand.Cuvinte.Count == 0)
        {
            return Certitudine.Sigur;
        }

        var tokeni = fragment.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var cuvinte = rand.Cuvinte.Where(c => tokeni.Contains(c.Text) || tokeni.Any(t => t.Contains(c.Text, StringComparison.Ordinal))).ToList();
        return cuvinte.Count > 0 && cuvinte.Min(c => c.Incredere) < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;
    }

    /// <summary>Pragul sub care un cuvânt OCR e considerat zgomot (puncte de completare, linii, ștampile).</summary>
    private const float PragZgomot = 50f;

    /// <summary>
    /// Textul rândului de la poziția dată încolo; pentru OCR, fără cuvintele recunoscute cu încredere foarte mică
    /// (punctele de completare „.......” sunt citite des ca „III EEE oioi”).
    /// </summary>
    private static string TextFiabil(RandText rand, int deLa)
    {
        if (rand.Cuvinte.Count == 0)
        {
            return rand.Text[Math.Min(deLa, rand.Text.Length)..];
        }

        var pozitie = 0;
        var pastrate = new List<string>();
        foreach (var c in rand.Cuvinte)
        {
            var sfarsit = pozitie + c.Text.Length;
            if (sfarsit > deLa && c.Incredere >= PragZgomot)
            {
                // Primul cuvânt poate începe înaintea valorii (ex. „Santier:valoare” lipit).
                pastrate.Add(pozitie < deLa ? c.Text[(deLa - pozitie)..] : c.Text);
            }

            pozitie = sfarsit + 1;
        }

        return string.Join(' ', pastrate.Where(t => t.Length > 0));
    }

    private static float Minim(List<CuvantText> cuvinte) => cuvinte.Count == 0 ? 100 : cuvinte.Min(c => c.Incredere);

    private static string CorecteazaCifre(string text, out bool aFostCorectat)
    {
        aFostCorectat = false;
        if (!text.Any(char.IsAsciiDigit) && text.Length > 2)
        {
            return text; // un cuvânt, nu un număr
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ConfuziiCifre.TryGetValue(ch, out var cifra))
            {
                sb.Append(cifra);
                aFostCorectat = true;
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static bool ParseazaCantitate(string text, out decimal valoare)
    {
        // „1.250,5” (ro) sau „1250.5”; separatorul zecimal e ultimul dintre „,” și „.”.
        var t = text.Trim();
        var ultimaVirgula = t.LastIndexOf(',');
        var ultimulPunct = t.LastIndexOf('.');
        if (ultimaVirgula > ultimulPunct)
        {
            t = t.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else if (ultimulPunct > ultimaVirgula && ultimaVirgula >= 0)
        {
            t = t.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valoare);
    }

    private static bool ParseazaData(Match m, out DateOnly data)
    {
        data = default;
        var an = int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
        if (an < 100)
        {
            an += 2000;
        }

        if (!int.TryParse(m.Groups["z"].Value, out var zi) || !int.TryParse(m.Groups["l"].Value, out var luna) ||
            an is < 2000 or > 2100 || luna is < 1 or > 12 || zi < 1 || zi > DateTime.DaysInMonth(an, luna))
        {
            return false;
        }

        data = new DateOnly(an, luna, zi);
        return true;
    }

    private static bool EstePlaceholder(string text) => text.Contains("{{", StringComparison.Ordinal);

    private static bool EsteDoarPunctuatie(string text) => text.All(ch => !char.IsLetterOrDigit(ch));

    /// <summary>Scoate punctele de completare („.......”, „……”, „____”) și spațiile de la capete.</summary>
    private static string CurataCompletare(string text) =>
        PotrivireCompletare().Replace(text, " ").Trim().Trim(':', '-', ' ');

    private static string TaieLaEticheta(string text)
    {
        var m = PotrivireEtichetaUrmatoare().Match(text);
        return m.Success ? text[..m.Index] : text;
    }

    private static string Scurteaza(string text, int maxim) => text.Length <= maxim ? text : text[..maxim];

    /// <summary>Majuscule, fără diacritice, spații simple: pentru recunoașterea etichetelor.</summary>
    internal static string Normalizeaza(string text)
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

        return SpatiiMultiple().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\bNr\.?\s*(?:crt\.?)?\s*[:.]?\s*(?<v>\{\{[^}]*\}\}|[A-Za-z0-9][A-Za-z0-9/\-.]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireNumar();

    [GeneratedRegex(@"\bData\s*[:.]?\s*(?<v>\{\{[^}]*\}\}|[0-9OoIl|]{1,2}\s*[./,\-]\s*[0-9OoIl|]{1,2}\s*[./,\-]\s*[0-9OoIl|]{2,4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireData();

    [GeneratedRegex(@"(?<z>\d{1,2})\s*[./,\-]\s*(?<l>\d{1,2})\s*[./,\-]\s*(?<a>\d{2,4})", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireValoareData();

    [GeneratedRegex(@"Compartiment\s*/\s*[SȘŞsșş]antier\s*:?(?<v>.*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireCompartiment();

    [GeneratedRegex(@"Lucrare(?:a)?\s*:\s*(?<v>.*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireLucrare();

    [GeneratedRegex(@"\s*\||\bData\s*:|\bNr\.\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireEtichetaUrmatoare();

    [GeneratedRegex(@"[.…_]{3,}|…+")]
    private static partial Regex PotrivireCompletare();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpatiiMultiple();
}
