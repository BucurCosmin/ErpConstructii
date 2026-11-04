using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Aprovizionare.Import;

public sealed record LinieNotaComandaCitita(int Rand, CampCitit<string> Denumire, CampCitit<string> UnitateMasura, CampCitit<decimal> Cantitate, string? Termen);

/// <param name="CuiBeneficiar">CUI-ul beneficiarului (al firmei care comandă) găsit în document, pentru verificare.</param>
public sealed record NotaComandaCitita(
    CampCitit<string> NumarExtern,
    CampCitit<DateOnly> Data,
    CampCitit<string> FurnizorDenumire,
    CampCitit<string> FurnizorCui,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    CampCitit<string> Lucrare,
    IReadOnlyList<LinieNotaComandaCitita> Linii,
    string? CuiBeneficiar,
    IReadOnlyList<ProblemaImport> Probleme,
    bool DinOcr);

/// <summary>
/// Interpretează nota de comandă către furnizor (formularul GC 007): antet pe trei coloane (Beneficiar | COMANDA nr./dată,
/// Obiect / Lucrare | Furnizor, adresă, telefon) și tabelul de materiale (Nr. crt. | Material / produs | U.M | Cantitate | Termen).
/// Word: celulele tabelelor, exact. PDF / scanare: coloanele după poziția etichetelor. Funcție pură.
/// </summary>
public static partial class InterpretorNotaComanda
{
    private enum Col { Denumire, Um, Cantitate, Termen }

    public static NotaComandaCitita Interpreteaza(TextDocument document, string? cuiFirma)
    {
        var randuri = document.Randuri;
        var probleme = new List<ProblemaImport>();
        var firma = string.IsNullOrWhiteSpace(cuiFirma) ? null : ValidareFiscala.NormalizeazaCui(cuiFirma);

        var (numar, data) = CitesteNumarSiData(randuri);
        var textFurnizor = TextColoana(randuri, EsteEtichetaFurnizor, document.Tabele);
        var textLucrare = TextColoana(randuri, EsteEtichetaLucrare, document.Tabele);

        var furnizor = Denumire(textFurnizor);
        var (adresa, telefon) = (Potrivire(PotrivireAdresa(), textFurnizor), Potrivire(PotrivireTelefon(), textFurnizor));
        var cuiFurnizor = textFurnizor is null
            ? null
            : PotrivireCui().Matches(textFurnizor).Select(m => ValidareFiscala.NormalizeazaCui(m.Groups["v"].Value))
                .FirstOrDefault(c => ValidareFiscala.EsteCuiValid(c) && c != firma);
        var lucrare = Lucrare(textLucrare);

        // Beneficiarul (firma care comandă): primul CUI valid din document care nu e al furnizorului.
        var cuiBeneficiar = randuri.SelectMany(r => PotrivireCui().Matches(r.Text))
            .Select(m => ValidareFiscala.NormalizeazaCui(m.Groups["v"].Value))
            .FirstOrDefault(c => ValidareFiscala.EsteCuiValid(c) && c != cuiFurnizor);
        if (firma is not null && cuiBeneficiar is not null && cuiBeneficiar != firma)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "beneficiar",
                $"Nota de comandă pare emisă de altă firmă (CUI {cuiBeneficiar}), nu de firma activă (CUI {firma}). Verifică firma activă."));
        }

        var linii = document.Tabele.Count > 0 && LiniiDinTabeleWord(document.Tabele) is { Count: > 0 } dinWord
            ? dinWord
            : LiniiDinRanduri(randuri, document.DinOcr);
        if (linii.Count == 0)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "materiale", "Nu am găsit tabelul de materiale. Adaugă materialele manual."));
        }

        if (furnizor is null)
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "furnizor", "Nu am găsit furnizorul; completează-l."));
        }

        return new NotaComandaCitita(
            numar, data,
            furnizor is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(furnizor, document.DinOcr ? Certitudine.Nesigur : Certitudine.Sigur, $"Furnizor: {furnizor}"),
            cuiFurnizor is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(cuiFurnizor, Certitudine.Sigur),
            adresa, telefon,
            lucrare is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(lucrare, document.DinOcr ? Certitudine.Nesigur : Certitudine.Sigur),
            linii, cuiBeneficiar, probleme, document.DinOcr);
    }

    // ------------------------------------------------------------------ antet

    private static (CampCitit<string> Numar, CampCitit<DateOnly> Data) CitesteNumarSiData(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            var m = PotrivireNumarCuData().Match(rand.Text);
            if (m.Success && ParseazaData(m.Groups["d"].Value) is { } d)
            {
                var c = CertitudineOcr(rand);
                return (new CampCitit<string>(m.Groups["n"].Value, c, m.Value.Trim()), new CampCitit<DateOnly>(d, c, m.Value.Trim()));
            }
        }

        CampCitit<string> numar = CampCitit<string>.Lipsa();
        CampCitit<DateOnly> data = CampCitit<DateOnly>.Lipsa();
        foreach (var rand in randuri)
        {
            if (numar.Certitudine == Certitudine.Lipsa && PotrivireNumar().Match(rand.Text) is { Success: true } n)
            {
                numar = new CampCitit<string>(n.Groups["n"].Value, CertitudineOcr(rand), n.Value.Trim());
            }

            if (data.Certitudine == Certitudine.Lipsa && PotrivireData().Match(rand.Text) is { Success: true } dm && ParseazaData(dm.Groups["d"].Value) is { } d)
            {
                data = new CampCitit<DateOnly>(d, CertitudineOcr(rand), dm.Value.Trim());
            }
        }

        return (numar, data);
    }

    /// <summary>
    /// Textul coloanei unei etichete din antet: Word → celula care conține eticheta; PDF / scanare → cuvintele de sub
    /// etichetă, între poziția ei și următoarea etichetă din dreapta, până la textul de dinaintea tabelului („Vă rugăm…”).
    /// </summary>
    private static string? TextColoana(IReadOnlyList<RandText> randuri, Func<string, bool> esteEticheta, IReadOnlyList<TabelText> tabele)
    {
        foreach (var celula in tabele.SelectMany(t => t.Randuri).SelectMany(r => r))
        {
            if (Normalizeaza(celula).Split(' ').Any(esteEticheta))
            {
                return celula;
            }
        }

        for (var i = 0; i < randuri.Count; i++)
        {
            var eticheta = randuri[i].Cuvinte.FirstOrDefault(c => esteEticheta(Normalizeaza(c.Text)));
            if (eticheta is null)
            {
                continue;
            }

            var dreapta = randuri[i].Cuvinte
                .Where(c => c.X0 > eticheta.X1 + 40 && (EsteEtichetaFurnizor(Normalizeaza(c.Text)) || EsteEtichetaLucrare(Normalizeaza(c.Text)) || EsteEtichetaBeneficiar(Normalizeaza(c.Text))))
                .Select(c => c.X0).DefaultIfEmpty(int.MaxValue).Min();
            var limitaDreapta = Math.Min(dreapta, LimitaDreaptaDinRanduriVecine(randuri, i, eticheta));
            var stanga = eticheta.X0 - 30;

            var parti = new List<string>();
            for (var j = i; j < randuri.Count && j <= i + 12; j++)
            {
                var t = Normalizeaza(randuri[j].Text);
                if (j > i && (t.StartsWith("VA RUGAM", StringComparison.Ordinal) || EsteAntetTabel(t)))
                {
                    break;
                }

                // La scanare, liniile punctate de completare ies ca „cuvinte” cu încredere foarte mică: le sar.
                var cuvinte = randuri[j].Cuvinte.Where(c => c.X0 >= stanga && c.X0 < limitaDreapta && c.Incredere >= PragZgomotOcr).Select(c => c.Text);
                parti.Add(string.Join(' ', cuvinte));
            }

            return LitereIzolate().Replace(string.Join(' ', parti.Where(p => p.Length > 0)), " ");
        }

        // Fără poziții și fără tabel (text simplu): rândul etichetei.
        return randuri.FirstOrDefault(r => Normalizeaza(r.Text).Split(' ').Any(esteEticheta))?.Text;
    }

    /// <summary>Eticheta din coloana din dreapta poate fi pe alt rând (ex. „Furnizor:” mai sus decât „Obiect / Lucrare”): caut în tot antetul.</summary>
    private static int LimitaDreaptaDinRanduriVecine(IReadOnlyList<RandText> randuri, int index, CuvantText eticheta)
    {
        var limita = int.MaxValue;
        for (var j = 0; j < randuri.Count && j <= index + 12; j++)
        {
            foreach (var c in randuri[j].Cuvinte.Where(c => c.X0 > eticheta.X1 + 40))
            {
                var t = Normalizeaza(c.Text);
                if (EsteEtichetaFurnizor(t) || EsteEtichetaLucrare(t) || EsteEtichetaBeneficiar(t))
                {
                    limita = Math.Min(limita, c.X0 - 10);
                }
            }
        }

        return limita;
    }

    private static string? Denumire(string? textFurnizor)
    {
        if (textFurnizor is null)
        {
            return null;
        }

        var m = PotrivireDenumireFurnizor().Match(textFurnizor);
        var valoare = m.Success ? Curata(m.Groups["v"].Value) : null;
        return string.IsNullOrWhiteSpace(valoare) || valoare.Length < 2 ? null : valoare;
    }

    private static string? Lucrare(string? textLucrare)
    {
        if (textLucrare is null)
        {
            return null;
        }

        var m = PotrivireLucrare().Match(textLucrare);
        var valoare = m.Success ? Curata(m.Groups["v"].Value) : null;
        return string.IsNullOrWhiteSpace(valoare) ? null : valoare;
    }

    private static string? Potrivire(Regex regex, string? text)
    {
        if (text is null)
        {
            return null;
        }

        var m = regex.Match(text);
        var v = m.Success ? Curata(m.Groups["v"].Value) : null;
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    // ------------------------------------------------------------------ materiale

    private static List<LinieNotaComandaCitita> LiniiDinTabeleWord(IReadOnlyList<TabelText> tabele)
    {
        foreach (var tabel in tabele)
        {
            var indexAntet = tabel.Randuri.ToList().FindIndex(r => EsteAntetTabel(Normalizeaza(string.Join(' ', r))));
            if (indexAntet < 0)
            {
                continue;
            }

            var antet = tabel.Randuri[indexAntet].Select(Normalizeaza).ToList();
            int Col(params string[] chei) => antet.FindIndex(c => chei.Any(k => c.Contains(k, StringComparison.Ordinal)));
            var (iNr, iDen, iUm, iCant, iTermen) = (Col("NR"), Col("MATERIAL", "DENUMIRE", "PRODUS"), antet.FindIndex(c => c is "UM" or "U.M" or "U.M."), Col("CANT"), Col("TERMEN"));

            var linii = new List<LinieNotaComandaCitita>();
            foreach (var r in tabel.Randuri.Skip(indexAntet + 1))
            {
                string Celula(int i) => i >= 0 && i < r.Count ? r[i].Trim() : string.Empty;
                var denumire = Celula(iDen);
                if (denumire.Length == 0)
                {
                    continue;
                }

                var nr = int.TryParse(Celula(iNr).TrimEnd('.'), out var n) ? n : linii.Count + 1;
                linii.Add(Linie(nr, denumire, Celula(iUm), Celula(iCant), Celula(iTermen), Certitudine.Sigur));
            }

            return linii;
        }

        return [];
    }

    private static List<LinieNotaComandaCitita> LiniiDinRanduri(IReadOnlyList<RandText> randuri, bool dinOcr)
    {
        var indexAntet = randuri.ToList().FindIndex(r => EsteAntetTabel(Normalizeaza(r.Text)));
        if (indexAntet < 0)
        {
            return [];
        }

        var antet = randuri[indexAntet].Cuvinte;
        CuvantText? Cuvant(Func<string, bool> f) => antet.FirstOrDefault(c => f(Normalizeaza(c.Text)));
        var den = Cuvant(t => t.StartsWith("MATERIAL", StringComparison.Ordinal) || t.StartsWith("DENUMIRE", StringComparison.Ordinal) || t.StartsWith("PRODUS", StringComparison.Ordinal));
        var um = Cuvant(t => t is "UM" or "U.M" or "U.M.");
        var cant = Cuvant(t => t.StartsWith("CANT", StringComparison.Ordinal));
        var termen = Cuvant(t => t.StartsWith("TERMEN", StringComparison.Ordinal));
        if (den is null || cant is null)
        {
            return [];
        }

        var coloane = new List<(Col Col, int Centru)> { (Col.Cantitate, (cant.X0 + cant.X1) / 2) };
        if (um is not null) coloane.Add((Col.Um, (um.X0 + um.X1) / 2));
        if (termen is not null) coloane.Add((Col.Termen, (termen.X0 + termen.X1) / 2));
        var inceputValori = Math.Min(um?.X0 ?? int.MaxValue, cant.X0) - 5;

        var linii = new List<(int Nr, List<string> Den, Dictionary<Col, string> Valori, float Incredere)>();
        for (var i = indexAntet + 1; i < randuri.Count; i++)
        {
            var rand = randuri[i];
            var t = Normalizeaza(rand.Text);
            if (t.StartsWith("APROBAT", StringComparison.Ordinal) || t.Contains("INTOCMIT", StringComparison.Ordinal) || t.StartsWith("FORMULAR", StringComparison.Ordinal))
            {
                break;
            }

            if (t.TrimEnd('.') == "CRT")
            {
                continue; // „Nr. / Crt.” pe două rânduri
            }

            var cuvinte = rand.Cuvinte.Where(c => c.Text.Any(char.IsLetterOrDigit)).ToList();
            if (cuvinte.Count == 0)
            {
                continue;
            }

            int? nr = null;
            if (int.TryParse(cuvinte[0].Text.TrimEnd('.'), out var n) && n is > 0 and < 1000 && cuvinte[0].X1 < den.X0)
            {
                nr = n;
                cuvinte = cuvinte.Skip(1).ToList();
            }

            var denumire = new List<string>();
            var valori = new Dictionary<Col, string>();
            foreach (var c in cuvinte)
            {
                if (c.X1 < inceputValori)
                {
                    denumire.Add(c.Text);
                    continue;
                }

                var centru = (c.X0 + c.X1) / 2;
                var col = coloane.OrderBy(x => Math.Abs(x.Centru - centru)).First().Col;
                valori[col] = valori.TryGetValue(col, out var v) ? v + " " + c.Text : c.Text;
            }

            var incredere = rand.Cuvinte.Count == 0 ? 100 : rand.Cuvinte.Min(c => c.Incredere);
            if (nr is null && valori.Count == 0)
            {
                if (linii.Count > 0)
                {
                    linii[^1].Den.AddRange(denumire); // denumirea continuă pe rândul următor
                }

                continue;
            }

            // Cu UM sau cantitate e o linie nouă, chiar dacă OCR-ul a pierdut numărul (cantitatea lipsă se completează manual).

            linii.Add((nr ?? linii.Count + 1, denumire, valori, incredere));
        }

        return linii
            .Where(l => l.Den.Count > 0)
            .Select(l => Linie(l.Nr, string.Join(' ', l.Den), l.Valori.GetValueOrDefault(Col.Um) ?? string.Empty,
                l.Valori.GetValueOrDefault(Col.Cantitate) ?? string.Empty, l.Valori.GetValueOrDefault(Col.Termen),
                dinOcr && l.Incredere < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur))
            .ToList();
    }

    private static LinieNotaComandaCitita Linie(int nr, string denumire, string um, string cantitate, string? termen, Certitudine certitudine)
    {
        var umCurat = um.Trim().TrimEnd('.').ToLowerInvariant();
        return new LinieNotaComandaCitita(
            nr,
            new CampCitit<string>(denumire.Trim(), certitudine, denumire),
            umCurat.Length == 0 ? CampCitit<string>.Lipsa() : new CampCitit<string>(umCurat, certitudine, um),
            TextNumeric.ParseazaNumar(cantitate, out var c) && c > 0 ? new CampCitit<decimal>(c, certitudine, cantitate) : CampCitit<decimal>.Lipsa(cantitate),
            string.IsNullOrWhiteSpace(termen) ? null : termen.Trim());
    }

    // ------------------------------------------------------------------ utilitare

    /// <summary>Sub acest prag, un cuvânt OCR din antet e zgomot (linii punctate, ștampile).</summary>
    private const float PragZgomotOcr = 50;

    private static bool EsteEtichetaFurnizor(string t) => t.StartsWith("FURNIZOR", StringComparison.Ordinal);

    private static bool EsteEtichetaLucrare(string t) => t.StartsWith("OBIECT", StringComparison.Ordinal);

    private static bool EsteEtichetaBeneficiar(string t) => t.StartsWith("BENEFICIAR", StringComparison.Ordinal);

    private static bool EsteAntetTabel(string t) =>
        (t.Contains("MATERIAL", StringComparison.Ordinal) || t.Contains("DENUMIRE", StringComparison.Ordinal) || t.Contains("PRODUS", StringComparison.Ordinal)) &&
        t.Contains("CANT", StringComparison.Ordinal);

    private static Certitudine CertitudineOcr(RandText rand) =>
        rand.Cuvinte.Count > 0 && rand.Cuvinte.Min(c => c.Incredere) < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;

    /// <summary>Fără punctele de completare, două puncte și spațiile de la capete.</summary>
    private static string Curata(string text) =>
        SpatiiMultiple().Replace(PotrivireCompletare().Replace(text, " "), " ").Trim(' ', ':', '-', ',', '.');

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
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return SpatiiMultiple().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\bNr\.?\s*(?<n>[A-Z0-9][A-Z0-9\-]*)\s*/\s*(?<d>\d{1,2}\.\d{1,2}\.\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireNumarCuData();

    [GeneratedRegex(@"\bNr\.?\s*(?<n>\d[\dA-Z/\-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireNumar();

    [GeneratedRegex(@"\bData\s*[:.]?\s*(?<d>\d{1,2}\.\d{1,2}\.\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireData();

    [GeneratedRegex(@"Furnizor(?:ul)?\s*:?\s*(?<v>.*?)(?=\s*(?:\bAdresa\b|\bADRESA\b|\bTel\b|\bTel\.|\bE\s*:|\bE-mail\b|\bCUI\b|\bCIF\b|[.…]{3,})|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireDenumireFurnizor();

    [GeneratedRegex(@"\bAdresa\s*:?\s*(?<v>.*?)(?=\s*(?:\bTel\b|\bTel\.|\bE\s*:|\bE-mail\b|[.…]{3,})|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireAdresa();

    [GeneratedRegex(@"\bTel\.?\s*:?\s*(?<v>[\d\s./\-+()]{6,}\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireTelefon();

    [GeneratedRegex(@"Obiect\s*/?\s*Lucrare\s*:?\s*(?<v>.*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireLucrare();

    [GeneratedRegex(@"(?:C\.?U\.?I\.?|C\.?I\.?F\.?)\s*[:.]?\s*(?<v>(?:RO)?\s?\d{2,10})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireCui();

    [GeneratedRegex(@"[.…_]{3,}|…+")]
    private static partial Regex PotrivireCompletare();

    /// <summary>Cel puțin 4 litere izolate la rând („n n n n”): zgomot OCR din liniile punctate.</summary>
    [GeneratedRegex(@"(?:(?<=\s)|^)(?:\p{L}\s+){3,}\p{L}(?=\s|$)")]
    private static partial Regex LitereIzolate();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpatiiMultiple();
}
