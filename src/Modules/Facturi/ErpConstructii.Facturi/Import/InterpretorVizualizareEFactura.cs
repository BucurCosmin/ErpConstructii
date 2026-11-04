using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Facturi.Import;

/// <summary>
/// Interpretează vizualizarea facturii din SPV („RO eFactura”, generată de ANAF din XML), tipărită și scanată. Formatul e
/// același pentru toți furnizorii: VÂNZĂTOR (stânga) | număr, tip, date, monedă (mijloc) | CUMPĂRĂTOR (dreapta), apoi
/// totalurile pe coloane, detalierea TVA și liniile (Linia | Nume articol | Țara | Preț net | Monedă | Cantitate de bază |
/// Cantitate facturată | UM | Cota TVA | Valoare netă). Coloanele se reconstruiesc din pozițiile cuvintelor din antet.
/// Funcție pură; folosită când dosarul scanat conține pagina e-Factura (de preferat facturii tipărite de furnizor).
/// </summary>
public static partial class InterpretorVizualizareEFactura
{
    private enum Col { Linie, Denumire, Tara, Pret, Moneda, CantitateBaza, Cantitate, Um, Cota, Valoare }

    /// <summary>Ghilimele și apostrofuri pe care OCR-ul le vede des la marginea denumirilor.</summary>
    private static readonly char[] GhilimeleOcr = ['"', '“', '”', '„', '\'', ' '];

    /// <summary>Recunoaște pagina după antetul „VANZATOR … CUMPARATOR” și sigla „RO eFactura” (OCR-ul strică uneori unul dintre ele).</summary>
    public static bool EsteVizualizare(TextDocument document)
    {
        var sus = Normalizeaza(string.Join(' ', document.Randuri.Take(15).Select(r => r.Text)));
        return sus.Contains("VANZATOR", StringComparison.Ordinal) &&
               (sus.Contains("CUMPARATOR", StringComparison.Ordinal) || sus.Contains("EFACTURA", StringComparison.Ordinal));
    }

    public static FacturaCitita Interpreteaza(TextDocument document, string? cuiFirma)
    {
        var randuri = document.Randuri;
        var probleme = new List<ProblemaImport>();
        var firma = string.IsNullOrWhiteSpace(cuiFirma) ? null : ValidareFiscala.NormalizeazaCui(cuiFirma);
        var c = document.DinOcr ? Certitudine.Nesigur : Certitudine.Sigur;

        // Limitele celor trei coloane din antet: „CUMPARATOR” dă începutul coloanei din dreapta, eticheta „Nr. factura” pe a celei din mijloc.
        // Dacă OCR-ul strică „CUMPARATOR” (ștampile, rotire), coloana din dreapta începe la al doilea „Nume” din rândul numelor.
        var antet = randuri.FirstOrDefault(r => r.Cuvinte.Any(w => Normalizeaza(w.Text) == "CUMPARATOR"));
        var xCumparator = antet?.Cuvinte.First(w => Normalizeaza(w.Text) == "CUMPARATOR").X0
                          ?? randuri.Select(r => r.Cuvinte.Where(w => Normalizeaza(w.Text) == "NUME").Skip(1).FirstOrDefault()?.X0).FirstOrDefault(x => x is not null)
                          ?? int.MaxValue;
        var xMijloc = randuri.SelectMany(r => r.Cuvinte.Zip(r.Cuvinte.Skip(1)))
            .Where(p => Normalizeaza(p.First.Text).StartsWith("NR", StringComparison.Ordinal) && Normalizeaza(p.Second.Text) == "FACTURA")
            .Select(p => p.First.X0).DefaultIfEmpty(xCumparator).First() - 20;

        var numar = ValoareDupaEticheta(randuri, ["NR", "FACTURA"], xMijloc, xCumparator);
        var tip = ValoareDupaEticheta(randuri, ["CODUL", "TIPULUI"], xMijloc, xCumparator);
        var emitere = Data(ValoareDupaEticheta(randuri, ["DATA", "EMITERE"], xMijloc, xCumparator));
        var scadenta = Data(ValoareDupaEticheta(randuri, ["DATA", "SCADENTA"], xMijloc, xCumparator));
        var moneda = ValoareDupaEticheta(randuri, ["MONEDA", "FACTURII"], xMijloc, xCumparator);

        var furnizor = ValoareDupaEticheta(randuri, ["NUME"], 0, xMijloc);
        var cuiFurnizor = Cui(ValoareDupaEticheta(randuri, ["IDENTIFICATORUL", "TVA"], 0, xMijloc)) ?? Cui(ValoareDupaEticheta(randuri, ["IDENTIFICATOR"], 0, xMijloc));
        var cuiCumparator = randuri.SelectMany(r => r.Cuvinte.Where(w => w.X0 >= xCumparator - 20))
            .Select(w => Cui(w.Text)).FirstOrDefault(x => x is not null && ValidareFiscala.EsteCuiValid(x));
        if (cuiFurnizor is not null && cuiFurnizor == firma)
        {
            cuiFurnizor = null; // OCR-ul a amestecat coloanele: CUI-ul firmei nu e niciodată al furnizorului
        }

        if (firma is not null && cuiCumparator is not null && cuiCumparator != firma)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "cumpărător",
                $"Factura pare emisă către CUI {cuiCumparator}, nu către firma activă (CUI {firma}). Verifică."));
        }

        var semn = tip?.Trim() == "381" ? -1m : 1m; // 381 = notă de credit
        var (fara, cuTva, totalTva, plata) = Totaluri(randuri);
        if (plata is { } tp && cuTva is { } tc && tp != tc && fara is { } tf && totalTva is { } tt && tf + tt == tc)
        {
            // „TOTAL PLATA” diferă de totalul cu TVA, care se verifică (net + TVA): o cifră citită greșit în rândul de plată.
            probleme.Add(new ProblemaImport(Severitate.Informare, "totaluri",
                $"Totalul de plată citit ({tp:N2}) diferă de totalul cu TVA ({tc:N2}, = net + TVA); am folosit totalul cu TVA."));
            plata = tc;
        }
        var linii = Linii(randuri, c, semn, probleme);
        var referinta = Referinta(randuri);

        if (linii.Count == 0)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "linii", "Nu am găsit liniile facturii în pagina e-Factura. Adaugă-le manual."));
        }

        // Verificări încrucișate: suma liniilor = total net; net + TVA = total cu TVA = de plată.
        var sumaLinii = linii.Sum(l => l.ValoareFaraTva.Valoare);
        var totaluriOk = fara is { } f && Math.Abs(sumaLinii - f * semn) <= VerificareTotaluriFactura.Toleranta(linii.Count) &&
                         (cuTva is null || totalTva is null || Math.Abs(f + totalTva.Value - cuTva.Value) <= 0.01m);
        var ct = totaluriOk ? Certitudine.Sigur : Certitudine.Nesigur;
        if (!totaluriOk && linii.Count > 0 && fara is not null)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "totaluri",
                $"Suma liniilor ({sumaLinii:N2}) diferă de totalul net ({fara * semn:N2}). Verifică liniile."));
        }

        return new FacturaCitita(
            SursaFactura.Pdf,
            numar is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(numar, c, $"Nr. factura {numar}"),
            emitere is { } e ? new CampCitit<DateOnly>(e, Certitudine.Sigur) : CampCitit<DateOnly>.Lipsa(),
            scadenta is { } s ? new CampCitit<DateOnly>(s, Certitudine.Sigur) : CampCitit<DateOnly>.Lipsa(),
            new CampCitit<string>(string.IsNullOrWhiteSpace(moneda) ? "RON" : moneda.Trim().ToUpperInvariant(), Certitudine.Sigur),
            furnizor is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(furnizor, c, $"Nume {furnizor}"),
            cuiFurnizor is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(cuiFurnizor, ValidareFiscala.EsteCuiValid(cuiFurnizor) ? Certitudine.Sigur : Certitudine.Nesigur),
            cuiCumparator is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(cuiCumparator, Certitudine.Sigur),
            fara is { } fa ? new CampCitit<decimal>(fa * semn, ct) : CampCitit<decimal>.Lipsa(),
            totalTva is { } tv ? new CampCitit<decimal>(tv * semn, ct) : (fara is { } a && cuTva is { } b ? new CampCitit<decimal>((b - a) * semn, ct) : CampCitit<decimal>.Lipsa()),
            (plata ?? cuTva) is { } p ? new CampCitit<decimal>(p * semn, ct) : CampCitit<decimal>.Lipsa(),
            linii,
            referinta,
            probleme);
    }

    // ------------------------------------------------------------------ antet

    /// <summary>Cuvintele de după eticheta dată (pe același rând, în coloana [xDe, xPanaLa)).</summary>
    private static string? ValoareDupaEticheta(IReadOnlyList<RandText> randuri, string[] eticheta, int xDe, int xPanaLa)
    {
        foreach (var rand in randuri)
        {
            var cuvinte = rand.Cuvinte.Where(w => w.X0 >= xDe && w.X0 < xPanaLa).ToList();
            for (var i = 0; i + eticheta.Length <= cuvinte.Count; i++)
            {
                if (!eticheta.Select((e, k) => Normalizeaza(cuvinte[i + k].Text).TrimEnd('.', ':').StartsWith(e, StringComparison.Ordinal) &&
                                               (e.Length > 2 || Normalizeaza(cuvinte[i + k].Text).TrimEnd('.', ':') == e)).All(x => x))
                {
                    continue;
                }

                var valoare = string.Join(' ', cuvinte.Skip(i + eticheta.Length).Select(w => w.Text)).Trim(' ', ':');
                if (valoare.Length > 0)
                {
                    return valoare;
                }
            }
        }

        return null;
    }

    private static DateOnly? Data(string? text)
    {
        var m = text is null ? Match.Empty : PotrivireDataIso().Match(text);
        return m.Success && DateOnly.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string? Cui(string? text)
    {
        var m = text is null ? Match.Empty : PotrivireCui().Match(text.Replace(" ", string.Empty, StringComparison.Ordinal).Trim(':', '.'));
        return m.Success ? ValidareFiscala.NormalizeazaCui(m.Value) : null;
    }

    /// <summary>
    /// Totalurile: rândul de antet „TOTAL NET | VALOARE TOTALA fara TVA | VALOARE TOTALA cu TVA …” și sub el valorile,
    /// pe aceleași coloane; „TOTAL PLATA x” și „TOTAL TVA x” pe rânduri proprii.
    /// </summary>
    private static (decimal? Fara, decimal? CuTva, decimal? Tva, decimal? Plata) Totaluri(IReadOnlyList<RandText> randuri)
    {
        decimal? fara = null, cuTva = null, tva = null, plata = null;
        for (var i = 0; i < randuri.Count; i++)
        {
            var t = Normalizeaza(randuri[i].Text);
            if (t.StartsWith("TOTAL NET", StringComparison.Ordinal) && t.Contains("VALOARE", StringComparison.Ordinal))
            {
                var cuv = randuri[i].Cuvinte;
                var xCu = cuv.Zip(cuv.Skip(1)).Where(p => Normalizeaza(p.First.Text) == "TOTALA" && Normalizeaza(p.Second.Text) == "CU")
                    .Select(p => p.First.X0).FirstOrDefault(-1);
                var randValori = randuri.Skip(i + 1).Take(3).FirstOrDefault(r => r.Cuvinte.Count > 0 && r.Cuvinte.All(w => Numar(w.Text) is not null));
                if (randValori is not null)
                {
                    var valori = randValori.Cuvinte.Select(w => (w.X0, V: Numar(w.Text)!.Value)).ToList();
                    fara = valori[0].V;
                    if (xCu >= 0)
                    {
                        cuTva = valori.Where(v => v.X0 >= xCu - 200).Select(v => (decimal?)v.V).FirstOrDefault();
                    }
                }
            }
            else if (t.StartsWith("TOTAL PLATA", StringComparison.Ordinal))
            {
                plata = randuri[i].Cuvinte.Select(w => Numar(w.Text)).LastOrDefault(v => v is not null);
            }
            else if (t.StartsWith("TOTAL TVA", StringComparison.Ordinal))
            {
                tva = randuri[i].Cuvinte.Select(w => Numar(w.Text)).FirstOrDefault(v => v is not null);
            }
        }

        return (fara, cuTva, tva, plata);
    }

    private static string? Referinta(IReadOnlyList<RandText> randuri)
    {
        foreach (var r in randuri)
        {
            var m = PotrivireComanda().Match(r.Text);
            if (m.Success)
            {
                return $"comanda {m.Groups["v"].Value}";
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ linii

    private static List<LinieFacturaCitita> Linii(IReadOnlyList<RandText> randuri, Certitudine c, decimal semn, List<ProblemaImport> probleme)
    {
        var iAntet = -1;
        for (var i = 0; i < randuri.Count; i++)
        {
            var t = Normalizeaza(randuri[i].Text);
            if (t.StartsWith("LINIA", StringComparison.Ordinal) && t.Contains("ARTICOL", StringComparison.Ordinal) && t.Contains("VALOARE", StringComparison.Ordinal))
            {
                iAntet = i;
                break;
            }
        }

        if (iAntet < 0)
        {
            return [];
        }

        // Coloanele, după cuvintele din antet; „Cantitate” apare de două ori (de bază, apoi facturată).
        var coloane = new List<(Col Col, int X)>();
        var cantitati = 0;
        foreach (var w in randuri[iAntet].Cuvinte)
        {
            var t = Normalizeaza(w.Text);
            Col? col = t switch
            {
                "LINIA" => Col.Linie,
                "NUME" => Col.Denumire,
                "TARA" => Col.Tara,
                "PRETUL" => Col.Pret,
                "MONEDA" => Col.Moneda,
                "CANTITATE" => cantitati++ == 0 ? Col.CantitateBaza : Col.Cantitate,
                "UM" => Col.Um,
                "COTA" => Col.Cota,
                "VALOARE" => Col.Valoare,
                _ => null,
            };
            if (col is { } x && coloane.All(k => k.Col != x))
            {
                coloane.Add((x, w.X0));
            }
        }

        int X(Col col) => coloane.Where(k => k.Col == col).Select(k => k.X).DefaultIfEmpty(-1).First();
        if (X(Col.Pret) < 0 || X(Col.Valoare) < 0 || X(Col.Cantitate) < 0)
        {
            return [];
        }

        var xTara = X(Col.Tara) > 0 ? X(Col.Tara) : X(Col.Pret) - 150;
        var xDenumire = X(Col.Denumire) > 0 ? X(Col.Denumire) - 30 : 0;
        var ordine = coloane.Where(k => k.Col is not (Col.Linie or Col.Denumire or Col.Tara)).OrderBy(k => k.X).ToList();

        var linii = new List<(int Nr, List<string> Den, Dictionary<Col, string> Val, float Incredere)>();
        for (var i = iAntet + 1; i < randuri.Count; i++)
        {
            var rand = randuri[i];
            var t = Normalizeaza(rand.Text);
            if (t.StartsWith("PAGINA", StringComparison.Ordinal))
            {
                continue; // subsolul paginii: tabelul poate continua pe pagina următoare
            }

            if (Oprire().IsMatch(t))
            {
                break;
            }

            if (t.Contains("FACTURATA", StringComparison.Ordinal) || t.StartsWith("PROVENIENT", StringComparison.Ordinal))
            {
                continue; // al doilea rând al antetului
            }

            int? nr = null;
            var den = new List<string>();
            var val = new Dictionary<Col, string>();
            foreach (var w in rand.Cuvinte)
            {
                if (w.X1 < xDenumire && int.TryParse(w.Text, out var n))
                {
                    nr = n;
                }
                else if (w.X0 < xTara - 10)
                {
                    den.Add(w.Text);
                }
                else
                {
                    // Valoarea ține de coloana al cărei început e cel mai apropiat la stânga (numerele sunt aliniate la stânga în vizualizare).
                    var col = ordine.LastOrDefault(k => k.X <= w.X0 + 40, ordine[0]).Col;
                    if (col is not (Col.Um or Col.Moneda) && Numar(w.Text) is null)
                    {
                        continue; // zgomot OCR (ștampile, semnături) într-o coloană numerică
                    }

                    val[col] = val.TryGetValue(col, out var v) ? v + w.Text : w.Text;
                }
            }

            var incredere = rand.Cuvinte.Count == 0 ? 100 : rand.Cuvinte.Min(w => w.Incredere);
            if (!val.ContainsKey(Col.Valoare) && !val.ContainsKey(Col.Cantitate))
            {
                if (linii.Count > 0 && den.Count > 0)
                {
                    linii[^1].Den.AddRange(den); // denumirea continuă pe rândul următor
                }

                continue;
            }

            linii.Add((nr ?? linii.Count + 1, den, val, incredere));
        }

        var rezultat = new List<LinieFacturaCitita>();
        var diferenteMici = new List<int>();
        foreach (var (nr, den, val, incredere) in linii)
        {
            var cert = c == Certitudine.Nesigur && incredere < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;
            var pret = Numar(val.GetValueOrDefault(Col.Pret));
            var baza = Numar(val.GetValueOrDefault(Col.CantitateBaza)) is { } b and > 0 ? b : 1m;
            var cantitate = Numar(val.GetValueOrDefault(Col.Cantitate));
            var valoare = Numar(val.GetValueOrDefault(Col.Valoare));
            var cota = Numar(val.GetValueOrDefault(Col.Cota));
            var um = val.GetValueOrDefault(Col.Um);

            // Prețul e pentru „cantitatea de bază”: valoarea = cantitate × preț / bază.
            var pretUnitar = pret is { } p ? p / baza : (decimal?)null;
            var consistent = cantitate is { } q && pretUnitar is { } pu && valoare is { } v && Math.Abs(Rotunjire.Lei.Aplica(q * pu) - v) <= 0.01m;
            var certValori = consistent ? cert : Certitudine.Nesigur;
            if (!consistent && cantitate is { } q2 && pretUnitar is { } p2 && valoare is { } v2)
            {
                // Sub 1%: prețul afișat e rotunjit (ex. carburanți); peste: probabil o cifră citită greșit.
                if (Math.Abs(q2 * p2 - v2) <= Math.Max(0.05m, Math.Abs(v2) * 0.01m))
                    diferenteMici.Add(nr);
                else
                    probleme.Add(new ProblemaImport(Severitate.Avertisment, $"linia {nr}",
                        $"Cantitate × preț ({q2 * p2:N2}) diferă de valoare ({v2:N2}). Verifică linia."));
            }

            rezultat.Add(new LinieFacturaCitita(
                nr,
                den.Count == 0 ? CampCitit<string>.Lipsa() : new CampCitit<string>(string.Join(' ', den).Trim(GhilimeleOcr), cert),
                string.IsNullOrWhiteSpace(um) ? CampCitit<string>.Lipsa() : new CampCitit<string>(InterpretorEFactura.UnitateDinCod(um), cert, um),
                cantitate is { } cq ? new CampCitit<decimal>(cq * semn, certValori) : CampCitit<decimal>.Lipsa(),
                pretUnitar is { } cp ? new CampCitit<decimal>(Math.Round(cp, 4), certValori) : CampCitit<decimal>.Lipsa(),
                valoare is { } cv ? new CampCitit<decimal>(cv * semn, certValori) : CampCitit<decimal>.Lipsa(),
                // Cota e în procente („21”); „2100” / „2108” = punctul zecimal pierdut la OCR → de verificat.
                cota is { } k ? (k <= 100 ? new CampCitit<decimal>(k / 100m, cert) : new CampCitit<decimal>(Math.Round(k / 100m) / 100m, Certitudine.Nesigur, val[Col.Cota]))
                              : CampCitit<decimal>.Lipsa()));
        }

        if (diferenteMici.Count > 0)
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "linii",
                $"Pe {(diferenteMici.Count == 1 ? "linia" : "liniile")} {string.Join(", ", diferenteMici)}, cantitate × preț diferă puțin de valoare " +
                "(prețul afișat e rotunjit). Am păstrat valoarea din factură; verifică cantitatea."));
        }

        return rezultat;
    }

    private static decimal? Numar(string? text) => TextNumeric.ParseazaNumar(text, out var v) ? v : null;

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

    [GeneratedRegex(@"^(REFERINTA|INSTRUCTIUNI|NOTA|NR\. CONT|INFORMATII|NR\. COMANDA|NUMELE|EXPLICATII)", RegexOptions.CultureInvariant)]
    private static partial Regex Oprire();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex PotrivireDataIso();

    [GeneratedRegex(@"^(?:RO)?\d{2,10}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireCui();

    [GeneratedRegex(@"\bNr\.?\s*comanda\s*:?\s*(?<v>[A-Z0-9][A-Z0-9\-/]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireComanda();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpatiiMultiple();
}
