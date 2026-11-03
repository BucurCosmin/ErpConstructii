using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Facturi.Import;

/// <summary>
/// Interpretează o factură primită din textul unui PDF (text digital sau OCR), fără șablon per furnizor:
/// <list type="bullet">
/// <item>CUI-urile valide (cifra de control) din document: cel diferit de al firmei e al furnizorului;</item>
/// <item>numărul (cu serie), data, scadența, referința la comandă — după etichetele uzuale din facturile românești;</item>
/// <item>tabelul de linii — coloanele deduse din poziția cuvintelor din antet; fiecare linie verificată cu cantitate × preț = valoare;</item>
/// <item>totalurile — verificate între ele și față de linii.</item>
/// </list>
/// Ce nu se poate confirma prin aceste verificări rămâne „de verificat” pentru utilizator. Funcție pură.
/// </summary>
public static partial class InterpretorFacturaPdf
{
    private enum Col { Nr, Denumire, Um, Cantitate, Pret, Valoare, Cota, ValoareTva, Ignorat }

    private sealed record ColoanaAntet(Col Coloana, int X0, int X1)
    {
        public int Centru => (X0 + X1) / 2;
    }

    public static FacturaCitita Interpreteaza(TextDocument document, string? cuiFirma)
    {
        var randuri = document.Randuri;
        var probleme = new List<ProblemaImport>();
        var firma = string.IsNullOrWhiteSpace(cuiFirma) ? null : ValidareFiscala.NormalizeazaCui(cuiFirma);

        var (furnizorCui, cumparatorCui) = CitesteCui(randuri, firma, probleme);
        var furnizor = CitesteFurnizor(randuri, furnizorCui.Valoare);
        var numar = CitesteNumar(randuri);
        var data = CitesteDataEmitere(randuri);
        var scadenta = CitesteScadenta(randuri, data.Valoare);
        var moneda = CitesteMoneda(randuri);
        var referinta = CitesteReferinta(randuri);

        var (linii, coloane, indexSfarsitTabel) = CitesteLinii(randuri, document.DinOcr, probleme);
        var (fara, tva, plata) = CitesteTotaluri(randuri, coloane, Math.Max(indexSfarsitTabel, 0), linii, probleme);

        var textTot = Normalizeaza(string.Join(' ', randuri.Select(r => r.Text)));
        if (textTot.Contains("COMANDA", StringComparison.Ordinal) && !textTot.Contains("FACTURA", StringComparison.Ordinal) &&
            linii.All(l => l.PretUnitar.Certitudine == Certitudine.Lipsa && l.ValoareFaraTva.Certitudine == Certitudine.Lipsa))
        {
            probleme.Add(new ProblemaImport(Severitate.Eroare, "document",
                "Documentul este o comandă către furnizor, nu o factură: nu are prețuri, valori și TVA. Importă factura primită de la furnizor (XML din SPV sau PDF)."));
        }

        if (linii.Count == 0)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "linii",
                "Nu am găsit tabelul cu produse / servicii. Adaugă liniile manual (cel puțin una, cu totalul facturii)."));
        }

        return new FacturaCitita(SursaFactura.Pdf, numar, data, scadenta, moneda, furnizor, furnizorCui, cumparatorCui,
            fara, tva, plata, linii, referinta, probleme);
    }

    // ------------------------------------------------------------------ părți

    private static (CampCitit<string> Furnizor, CampCitit<string> Cumparator) CitesteCui(
        IReadOnlyList<RandText> randuri, string? firma, List<ProblemaImport> probleme)
    {
        var gasite = new List<(int Rand, string Cui, bool Valid, Certitudine Ocr, string Text, int X)>();
        for (var i = 0; i < randuri.Count; i++)
        {
            foreach (Match m in PotrivireCui().Matches(randuri[i].Text))
            {
                var cui = ValidareFiscala.NormalizeazaCui(m.Groups["v"].Value);
                var x = randuri[i].Cuvinte.FirstOrDefault(c => c.Text.Contains(cui, StringComparison.Ordinal))?.X0 ?? 0;
                gasite.Add((i, cui, ValidareFiscala.EsteCuiValid(cui), CertitudineOcr(randuri[i], m.Groups["v"].Value), m.Value.Trim(), x));
            }
        }

        var valide = gasite.Where(g => g.Valid).ToList();
        CampCitit<string> cumparator;
        if (firma is not null && valide.FirstOrDefault(g => g.Cui == firma) is { Cui: not null } a)
        {
            cumparator = new CampCitit<string>(firma, Certitudine.Sigur, a.Text);
        }
        else
        {
            var langaClient = valide.FirstOrDefault(g => EsteEtichetaClient(randuri, g.Rand, g.X));
            cumparator = langaClient.Cui is null ? CampCitit<string>.Lipsa() : new CampCitit<string>(langaClient.Cui, Certitudine.Nesigur, langaClient.Text);
            if (firma is not null && langaClient.Cui is not null)
            {
                probleme.Add(new ProblemaImport(Severitate.Avertisment, "cumpărător",
                    $"Factura pare emisă către alt CUI ({langaClient.Cui}), nu către firma activă ({firma}). Verifică firma activă."));
            }
        }

        // Furnizorul: primul CUI valid care nu e al firmei (și nu e lângă eticheta clientului, dacă se poate).
        var candidati = valide.Where(g => g.Cui != firma && g.Cui != cumparator.Valoare).ToList();
        // Un CUI de lângă eticheta beneficiarului / cumpărătorului nu e niciodată al furnizorului.
        var furnizor = candidati.FirstOrDefault(g => !EsteEtichetaClient(randuri, g.Rand, g.X));

        if (furnizor.Cui is not null)
        {
            return (new CampCitit<string>(furnizor.Cui, furnizor.Ocr, furnizor.Text), cumparator);
        }

        var invalid = gasite.FirstOrDefault(g => !g.Valid && g.Cui != firma && !EsteEtichetaClient(randuri, g.Rand, g.X));
        if (invalid.Cui is not null)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "furnizor", $"CUI-ul citit „{invalid.Cui}” nu trece verificarea cifrei de control."));
            return (new CampCitit<string>(invalid.Cui, Certitudine.Nesigur, invalid.Text), cumparator);
        }

        return (CampCitit<string>.Lipsa(), cumparator);
    }

    private static CampCitit<string> CitesteFurnizor(IReadOnlyList<RandText> randuri, string? cuiFurnizor)
    {
        // 1) Eticheta „Furnizor”: valoarea pe același rând (până la eticheta clientului) sau pe rândul următor.
        for (var i = 0; i < randuri.Count; i++)
        {
            var m = PotrivireEtichetaFurnizor().Match(randuri[i].Text);
            if (!m.Success)
            {
                continue;
            }

            // Cu poziții (PDF / OCR): doar cuvintele din coloana etichetei, pe rândul ei și pe cele de sub ea.
            var eticheta = randuri[i].Cuvinte.FirstOrDefault(c => EsteCuvantFurnizor(Normalizeaza(c.Text)));
            if (eticheta is not null && DenumireDinColoana(randuri, i, eticheta) is { Length: >= 3 } dinColoana)
            {
                return new CampCitit<string>(Scurteaza(dinColoana, 300), Certitudine.Nesigur, $"Furnizor: {dinColoana}");
            }

            var valoare = TaieLaEticheta(m.Groups["v"].Value).Trim(' ', ':', '-');
            if (valoare.Length < 3 && i + 1 < randuri.Count)
            {
                valoare = TaieLaEticheta(randuri[i + 1].Text).Trim(' ', ':', '-');
            }

            if (valoare.Length >= 3 && !PotrivireCui().IsMatch(valoare))
            {
                return new CampCitit<string>(Scurteaza(valoare, 300), Certitudine.Nesigur, randuri[i].Text.Trim());
            }
        }

        // 2) Fără etichetă: rândul cu formă juridică (SRL, SA…) de deasupra CUI-ului furnizorului.
        if (cuiFurnizor is not null)
        {
            var indexCui = randuri.ToList().FindIndex(r => r.Text.Contains(cuiFurnizor, StringComparison.Ordinal));
            for (var i = indexCui; i >= 0 && i >= indexCui - 3; i--)
            {
                var m = PotrivireDenumireFirma().Match(randuri[i].Text);
                if (m.Success)
                {
                    return new CampCitit<string>(Scurteaza(m.Value.Trim(), 300), Certitudine.Nesigur, randuri[i].Text.Trim());
                }
            }
        }

        return CampCitit<string>.Lipsa();
    }

    /// <summary>
    /// Textul din coloana etichetei „Furnizor”: cuvintele de la poziția etichetei spre dreapta, până la următoarea etichetă
    /// de pe rând; dacă pe rândul etichetei nu e nimic, rândurile de sub ea (cel mult 3), până la adresă / CUI / telefon.
    /// </summary>
    private static string? DenumireDinColoana(IReadOnlyList<RandText> randuri, int index, CuvantText eticheta)
    {
        var stanga = eticheta.X0 - 30;
        var dreapta = randuri[index].Cuvinte.Where(c => c.X0 > eticheta.X1 && EsteCuvantClient(Normalizeaza(c.Text))).Select(c => c.X0).DefaultIfEmpty(int.MaxValue).Min();

        string Coloana(RandText r, int deLa) => string.Join(' ', r.Cuvinte
            .Where(c => !ReferenceEquals(c, eticheta) && c.X0 >= deLa && c.X0 < dreapta && c.Text.Any(char.IsLetterOrDigit))
            .Select(c => c.Text)).Trim();

        var parti = new List<string>();
        var peRand = Coloana(randuri[index], eticheta.X0 + 1);
        if (peRand.Length > 0)
        {
            parti.Add(peRand);
        }

        for (var j = index + 1; j < randuri.Count && j <= index + 3; j++)
        {
            var text = Coloana(randuri[j], stanga);
            if (text.Length == 0)
            {
                continue;
            }

            var t = Normalizeaza(text);
            if (PotrivireEtichetaUrmatoare().IsMatch(text) || t.StartsWith("ADRES", StringComparison.Ordinal) || t.StartsWith("TEL", StringComparison.Ordinal) ||
                t.StartsWith("E:", StringComparison.Ordinal) || t.StartsWith("E-MAIL", StringComparison.Ordinal) || t.StartsWith("RC", StringComparison.Ordinal) ||
                PotrivireCui().IsMatch(text))
            {
                break;
            }

            parti.Add(text);
            if (PotrivireDenumireFirma().IsMatch(string.Join(' ', parti)))
            {
                break; // forma juridică (SRL, SA…) încheie denumirea
            }
        }

        return parti.Count == 0 ? null : string.Join(' ', parti).Trim(' ', ':', '.', '…');
    }

    // ------------------------------------------------------------------ antet

    private static CampCitit<string> CitesteNumar(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            var s = PotrivireSerieNumar().Match(rand.Text);
            if (s.Success)
            {
                var valoare = $"{s.Groups["s"].Value.ToUpperInvariant()} {s.Groups["n"].Value}".Trim();
                return new CampCitit<string>(valoare, CertitudineOcr(rand, s.Groups["n"].Value), s.Value.Trim());
            }
        }

        foreach (var rand in randuri)
        {
            var nd = PotrivireNumarCuData().Match(rand.Text);
            if (nd.Success)
            {
                return new CampCitit<string>(nd.Groups["n"].Value, CertitudineOcr(rand, nd.Groups["n"].Value), nd.Value.Trim());
            }
        }

        foreach (var rand in randuri)
        {
            var m = PotrivireNumarFactura().Match(rand.Text);
            if (m.Success && m.Groups["n"].Value.Any(char.IsAsciiDigit))
            {
                var valoare = SpatiiMultiple().Replace(m.Groups["n"].Value.Trim(), " ");
                return new CampCitit<string>(valoare, CertitudineOcr(rand, m.Groups["n"].Value), m.Value.Trim());
            }
        }

        return CampCitit<string>.Lipsa();
    }

    private static CampCitit<DateOnly> CitesteDataEmitere(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            var nd = PotrivireNumarCuData().Match(rand.Text);
            if (nd.Success && ParseazaData(nd.Groups["d"].Value) is { } dn)
            {
                return new CampCitit<DateOnly>(dn, CertitudineOcr(rand, nd.Groups["d"].Value), nd.Value.Trim());
            }
        }

        foreach (var rand in randuri)
        {
            var m = PotrivireDataEmitere().Match(rand.Text);
            if (m.Success && !EsteContextScadenta(rand.Text[..m.Index]) && ParseazaData(m.Groups["d"].Value) is { } d)
            {
                return new CampCitit<DateOnly>(d, CertitudineOcr(rand, m.Groups["d"].Value), m.Value.Trim());
            }
        }

        // Fără etichetă: prima dată din document care nu e scadența → de verificat.
        foreach (var rand in randuri)
        {
            foreach (Match m in PotrivireDataLibera().Matches(rand.Text))
            {
                if (!EsteContextScadenta(rand.Text) && ParseazaData(m.Value) is { } d)
                {
                    return new CampCitit<DateOnly>(d, Certitudine.Nesigur, m.Value);
                }
            }
        }

        return CampCitit<DateOnly>.Lipsa();
    }

    private static CampCitit<DateOnly> CitesteScadenta(IReadOnlyList<RandText> randuri, DateOnly dataEmitere)
    {
        foreach (var rand in randuri)
        {
            var m = PotrivireScadenta().Match(rand.Text);
            if (m.Success && ParseazaData(m.Groups["d"].Value) is { } d)
            {
                return new CampCitit<DateOnly>(d, CertitudineOcr(rand, m.Groups["d"].Value), m.Value.Trim());
            }

            // „Termen de plată: 30 zile” → data facturii + 30 zile.
            var z = PotrivireTermenZile().Match(rand.Text);
            if (z.Success && dataEmitere != default && int.TryParse(z.Groups["z"].Value, out var zile) && zile is > 0 and <= 365)
            {
                return new CampCitit<DateOnly>(dataEmitere.AddDays(zile), Certitudine.Nesigur, z.Value.Trim());
            }
        }

        return CampCitit<DateOnly>.Lipsa();
    }

    private static CampCitit<string> CitesteMoneda(IReadOnlyList<RandText> randuri)
    {
        var text = Normalizeaza(string.Join(' ', randuri.Select(r => r.Text)));
        if (PotrivireEur().IsMatch(text) && !text.Contains("LEI", StringComparison.Ordinal) && !text.Contains("RON", StringComparison.Ordinal))
        {
            return new CampCitit<string>("EUR", Certitudine.Nesigur, "EUR");
        }

        return text.Contains("LEI", StringComparison.Ordinal) || text.Contains("RON", StringComparison.Ordinal)
            ? new CampCitit<string>("RON", Certitudine.Sigur)
            : new CampCitit<string>("RON", Certitudine.Nesigur);
    }

    private static string? CitesteReferinta(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            var m = PotrivireReferinta().Match(rand.Text);
            if (m.Success)
            {
                return Scurteaza(m.Value.Trim(), 200);
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ linii

    private static (List<LinieFacturaCitita> Linii, List<ColoanaAntet>? Coloane, int Sfarsit) CitesteLinii(
        IReadOnlyList<RandText> randuri, bool dinOcr, List<ProblemaImport> probleme)
    {
        var indexAntet = randuri.ToList().FindIndex(r => EsteAntetTabel(r.Text));
        if (indexAntet < 0)
        {
            return ([], null, -1);
        }

        // Antetul poate continua pe rândul următor (ex. „Preț unitar” / „(fără TVA)”).
        var cuvinteAntet = randuri[indexAntet].Cuvinte.ToList();
        var inceputDate = indexAntet + 1;
        if (inceputDate < randuri.Count && !randuri[inceputDate].Text.Any(char.IsAsciiDigit) && ContineCuvantAntet(randuri[inceputDate].Text))
        {
            cuvinteAntet.AddRange(randuri[inceputDate].Cuvinte);
            inceputDate++;
        }

        var coloane = Coloane(cuvinteAntet);
        var cotaGlobala = CotaDinText(randuri);
        var linii = new List<(int Rand, List<CuvantText> Den, Dictionary<Col, string> Valori, float Incredere, RandText Ultimul)>();
        var sfarsit = randuri.Count;

        for (var i = inceputDate; i < randuri.Count; i++)
        {
            var rand = randuri[i];
            if (EsteSfarsitTabel(rand.Text))
            {
                sfarsit = i;
                break;
            }

            var cuvinte = rand.Cuvinte.Count > 0
                ? rand.Cuvinte.Where(c => c.Text.Any(char.IsLetterOrDigit)).ToList()
                : rand.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => new CuvantText(t, 0, 0, 100)).ToList();
            if (cuvinte.Count == 0)
            {
                continue;
            }

            int? nr = null;
            var denumireStart = coloane?.FirstOrDefault(c => c.Coloana == Col.Denumire)?.X0 ?? int.MaxValue;
            if (int.TryParse(cuvinte[0].Text.TrimEnd('.'), out var n) && n is > 0 and < 1000 && (coloane is null || cuvinte[0].X1 < denumireStart))
            {
                nr = n;
                cuvinte = cuvinte.Skip(1).ToList();
            }

            var den = new List<CuvantText>();
            var valori = new Dictionary<Col, string>();
            foreach (var c in cuvinte)
            {
                var col = coloane is null ? Col.Denumire : ColoanaCuvant(c, coloane);
                if (col == Col.Denumire)
                {
                    den.Add(c);
                }
                else if (col == Col.Ignorat)
                {
                    // termen de livrare, observații: nu țin de valorile liniei
                }
                else
                {
                    valori[col] = valori.TryGetValue(col, out var v) ? v + c.Text : c.Text;
                }
            }

            var numerice = valori.Keys.Count(k => k is Col.Cantitate or Col.Pret or Col.Valoare or Col.ValoareTva);
            var incredere = cuvinte.Count == 0 ? 100 : cuvinte.Min(c => c.Incredere);

            // Rând fără număr și fără valori, sub un produs → continuarea denumirii (un produs nou are cantitate și valoare).
            // Distanța poate fi cât între produse (mulți furnizori nu strâng rândurile); notele de subsol vin după TOTAL.
            if (nr is null && numerice == 0 && linii.Count > 0)
            {
                var anterior = linii[^1];
                var aproape = !rand.ArePozitieVerticala || !anterior.Ultimul.ArePozitieVerticala ||
                              rand.Y0 - anterior.Ultimul.Y1 < (anterior.Ultimul.Y1 - anterior.Ultimul.Y0) * 2.5;
                if (aproape)
                {
                    anterior.Den.AddRange(den);
                    linii[^1] = anterior with { Ultimul = rand };
                    continue;
                }
            }

            if (nr is null && numerice < 2)
            {
                continue; // text între tabel și totaluri, subtitluri etc.
            }

            linii.Add((nr ?? linii.Count + 1, den, valori, incredere, rand));
        }

        var rezultat = linii
            .Select(l => ConstruiesteLinie(l.Rand, string.Join(' ', l.Den.Select(c => c.Text)), l.Valori,
                dinOcr && l.Incredere < TextDocument.PragIncredere, cotaGlobala, probleme))
            .ToList();
        return (rezultat, coloane, sfarsit);
    }

    private static LinieFacturaCitita ConstruiesteLinie(
        int rand, string denumire, Dictionary<Col, string> valori, bool ocrNesigur, decimal? cotaGlobala, List<ProblemaImport> probleme)
    {
        var certitudineOcr = ocrNesigur ? Certitudine.Nesigur : Certitudine.Sigur;
        string? Text(Col c) => valori.TryGetValue(c, out var v) ? v : null;

        decimal? Numar(Col c, bool mii = true) => TextNumeric.ParseazaNumar(Text(c), out var v, mii) ? v : null;

        var pret = Numar(Col.Pret);
        var valoare = Numar(Col.Valoare);
        var cantitate = Numar(Col.Cantitate, mii: false);

        // „1.250” la cantitate: 1,25 sau 1250? Alegem interpretarea care respectă cantitate × preț = valoare.
        if (cantitate is { } c1 && pret is { } p1 && valoare is { } v1 && !Egal(c1 * p1, v1) &&
            TextNumeric.ParseazaNumar(Text(Col.Cantitate), out var c2, miiImplicit: true) && Egal(c2 * p1, v1))
        {
            cantitate = c2;
        }

        var consistent = cantitate is { } c && pret is { } p && valoare is { } v && Egal(c * p, v);
        var faraPreturi = pret is null && valoare is null;
        var certitudineValori = consistent || faraPreturi ? certitudineOcr : Certitudine.Nesigur;

        // Ce lipsește se deduce din celelalte două (de verificat).
        if (valoare is null && cantitate is { } cq && pret is { } pq)
        {
            valoare = Rotunjire.Lei.Aplica(cq * pq);
        }
        else if (pret is null && cantitate is { } cv and not 0 && valoare is { } vv)
        {
            pret = Math.Round(vv / cv, 4, MidpointRounding.AwayFromZero);
        }

        if (!consistent && cantitate is not null && pret is not null && valoare is not null)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, $"linia {rand}",
                $"Cantitate × preț ({cantitate * pret:N2}) diferă de valoare ({valoare:N2}). Verifică linia."));
        }

        // Cota TVA: din coloana cotei, din TVA-ul liniei, din textul facturii (ex. „TVA 21%”).
        CampCitit<decimal> cota;
        var valoareTva = Numar(Col.ValoareTva);
        if (TextNumeric.ParseazaNumar(Text(Col.Cota)?.TrimEnd('%'), out var procent) && procent is >= 0 and <= 100)
        {
            var dinTva = valoare is { } vb and not 0 && valoareTva is { } tb ? Math.Round(tb / vb, 2) : (decimal?)null;
            var sigur = dinTva is null || dinTva == Math.Round(procent / 100m, 2);
            cota = new CampCitit<decimal>(procent / 100m, sigur ? certitudineOcr : Certitudine.Nesigur, Text(Col.Cota));
        }
        else if (valoare is { } vb2 and not 0 && valoareTva is { } tb2)
        {
            cota = new CampCitit<decimal>(Math.Round(tb2 / vb2, 2), Certitudine.Nesigur, Text(Col.ValoareTva));
        }
        else
        {
            cota = cotaGlobala is { } g ? new CampCitit<decimal>(g, Certitudine.Nesigur) : CampCitit<decimal>.Lipsa();
        }

        var um = Text(Col.Um)?.Trim().TrimEnd('.').ToLowerInvariant();
        return new LinieFacturaCitita(
            rand,
            denumire.Length == 0 ? CampCitit<string>.Lipsa() : new CampCitit<string>(Scurteaza(denumire, 500), certitudineOcr, denumire),
            string.IsNullOrEmpty(um) ? CampCitit<string>.Lipsa() : new CampCitit<string>(Scurteaza(um, 30), certitudineOcr, Text(Col.Um)),
            cantitate is { } cc ? new CampCitit<decimal>(cc, certitudineValori, Text(Col.Cantitate)) : CampCitit<decimal>.Lipsa(),
            pret is { } pp ? new CampCitit<decimal>(pp, certitudineValori, Text(Col.Pret)) : CampCitit<decimal>.Lipsa(),
            valoare is { } va ? new CampCitit<decimal>(va, certitudineValori, Text(Col.Valoare)) : CampCitit<decimal>.Lipsa(),
            cota);
    }

    // ------------------------------------------------------------------ totaluri

    private static (CampCitit<decimal> Fara, CampCitit<decimal> Tva, CampCitit<decimal> Plata) CitesteTotaluri(
        IReadOnlyList<RandText> randuri, List<ColoanaAntet>? coloane, int deLa, List<LinieFacturaCitita> linii, List<ProblemaImport> probleme)
    {
        decimal? fara = null, tva = null, plata = null;
        string? textFara = null, textTva = null, textPlata = null;

        for (var i = deLa; i < randuri.Count; i++)
        {
            var rand = randuri[i];
            var t = Normalizeaza(rand.Text);
            var sume = Sume(rand);

            if (PotrivireTotalPlata().IsMatch(t))
            {
                var s = sume.Count > 0 ? sume : i + 1 < randuri.Count ? Sume(randuri[i + 1]) : [];
                if (s.Count > 0)
                {
                    plata ??= s[^1].Valoare;
                    textPlata ??= rand.Text.Trim();
                }

                continue;
            }

            if (PotrivireTotalFaraTva().IsMatch(t) && sume.Count > 0)
            {
                fara ??= sume[^1].Valoare;
                textFara ??= rand.Text.Trim();
                continue;
            }

            if (PotrivireTotalTva().IsMatch(t) && sume.Count > 0)
            {
                tva ??= sume[^1].Valoare;
                textTva ??= rand.Text.Trim();
                continue;
            }

            // Rândul „Total” al tabelului: valoarea fără TVA și TVA-ul, în coloanele lor (sau ultimele două sume).
            if (t.StartsWith("TOTAL", StringComparison.Ordinal) && sume.Count >= 2 && fara is null)
            {
                var dupaColoane = coloane is null ? null : sume.GroupBy(s => ColoanaCuvant(s.Cuvant, coloane)).ToDictionary(g => g.Key, g => g.Last().Valoare);
                fara = dupaColoane?.GetValueOrDefault(Col.Valoare) ?? sume[^2].Valoare;
                tva ??= dupaColoane?.GetValueOrDefault(Col.ValoareTva) ?? sume[^1].Valoare;
                textFara = textTva = rand.Text.Trim();
            }
        }

        // Deducere din ce lipsește (de verificat).
        if (plata is null && fara is { } f1 && tva is { } t1)
        {
            plata = f1 + t1;
        }
        else if (fara is null && plata is { } p2 && tva is { } t2)
        {
            fara = p2 - t2;
        }
        else if (tva is null && plata is { } p3 && fara is { } f3)
        {
            tva = p3 - f3;
        }

        if (fara is null && linii.Count > 0 && linii.All(l => l.ValoareFaraTva.Valoare != 0 || l.ValoareFaraTva.Certitudine != Certitudine.Lipsa))
        {
            fara = linii.Sum(l => l.ValoareFaraTva.Valoare);
        }

        var consistent = fara is { } a && tva is { } b && plata is { } c && Math.Abs(a + b - c) <= 0.01m;
        if (!consistent && fara is not null && tva is not null && plata is not null)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, "totaluri",
                $"Total fără TVA + TVA ({fara + tva:N2}) diferă de totalul de plată ({plata:N2}). Verifică totalurile (poate există un avans dedus)."));
        }

        var certitudine = consistent ? Certitudine.Sigur : Certitudine.Nesigur;
        return (
            fara is { } fv ? new CampCitit<decimal>(fv, certitudine, textFara) : CampCitit<decimal>.Lipsa(),
            tva is { } tv ? new CampCitit<decimal>(tv, certitudine, textTva) : CampCitit<decimal>.Lipsa(),
            plata is { } pv ? new CampCitit<decimal>(pv, certitudine, textPlata) : CampCitit<decimal>.Lipsa());
    }

    /// <summary>Sumele de pe un rând: numerele cu zecimale (ex. „1.234,56”), în ordinea de la stânga la dreapta.</summary>
    private static List<(decimal Valoare, CuvantText Cuvant)> Sume(RandText rand)
    {
        var cuvinte = rand.Cuvinte.Count > 0
            ? rand.Cuvinte
            : rand.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => new CuvantText(t, 0, 0, 100)).ToList();
        return cuvinte
            .Where(c => PotrivireSuma().IsMatch(c.Text))
            .Select(c => TextNumeric.ParseazaNumar(c.Text, out var v, miiImplicit: true) ? (v, c) : ((decimal, CuvantText)?)null)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();
    }

    // ------------------------------------------------------------------ coloane

    private static List<ColoanaAntet>? Coloane(List<CuvantText> antet)
    {
        if (antet.Count == 0)
        {
            return null;
        }

        var coloane = new List<ColoanaAntet>();
        var valoriGasite = 0;
        foreach (var c in antet.OrderBy(c => c.X0))
        {
            var t = Normalizeaza(c.Text).Trim('(', ')', '.', ':', ',');
            Col? col = t switch
            {
                _ when t.StartsWith("NR", StringComparison.Ordinal) && coloane.All(x => x.Coloana != Col.Nr) && coloane.Count == 0 => Col.Nr,
                _ when t.StartsWith("TERMEN", StringComparison.Ordinal) || t.StartsWith("OBSERV", StringComparison.Ordinal) => Col.Ignorat,
                _ when t.StartsWith("DENUMIRE", StringComparison.Ordinal) || t.StartsWith("PRODUS", StringComparison.Ordinal) ||
                       t.StartsWith("MATERIAL", StringComparison.Ordinal) ||
                       t.StartsWith("DESCRIERE", StringComparison.Ordinal) || t.StartsWith("ARTICOL", StringComparison.Ordinal) ||
                       t.StartsWith("SERVICI", StringComparison.Ordinal) => Col.Denumire,
                "UM" or "U.M" or "U M" => Col.Um,
                _ when t.StartsWith("CANT", StringComparison.Ordinal) => Col.Cantitate,
                _ when t.StartsWith("PRET", StringComparison.Ordinal) || t is "P.U" or "PU" => Col.Pret,
                _ when t.StartsWith("COTA", StringComparison.Ordinal) || t == "%" => Col.Cota,
                _ when t.StartsWith("VALOARE", StringComparison.Ordinal) || t.StartsWith("VAL", StringComparison.Ordinal) =>
                    valoriGasite++ == 0 ? Col.Valoare : Col.ValoareTva,
                "TVA" when coloane.Any(x => x.Coloana == Col.Valoare) && coloane.All(x => x.Coloana != Col.ValoareTva) => Col.ValoareTva,
                _ => null,
            };

            // Fiecare coloană o singură dată (primul cuvânt al antetului ei).
            if (col is { } k && coloane.All(x => x.Coloana != k))
            {
                coloane.Add(new ColoanaAntet(k, c.X0, c.X1));
            }
        }

        return coloane.Any(c => c.Coloana == Col.Denumire) && coloane.Count >= 3 && antet.Any(c => c.X1 > 0) ? coloane : null;
    }

    /// <summary>Numerele merg în coloana numerică cu centrul cel mai apropiat; textul în UM (dacă e scurt și aproape) sau în denumire.</summary>
    private static Col ColoanaCuvant(CuvantText c, List<ColoanaAntet> coloane)
    {
        var centru = (c.X0 + c.X1) / 2;

        // Tot ce se termină înaintea primei coloane din dreapta denumirii (UM, cantitate…) ține de denumire,
        // inclusiv cifrele din ea („Ciment 40 kg”, „Nisip 0-4 mm”).
        var inceputDreapta = coloane.Where(x => x.Coloana is not (Col.Nr or Col.Denumire)).Select(x => x.X0).DefaultIfEmpty(int.MaxValue).Min();
        if (c.X1 < inceputDreapta - 5)
        {
            return Col.Denumire;
        }

        var esteNumar = c.Text.TrimEnd('%').Any(char.IsAsciiDigit) && c.Text.TrimEnd('%').All(ch => char.IsAsciiDigit(ch) || ch is ',' or '.' or '-');
        var denumire = coloane.First(x => x.Coloana == Col.Denumire);
        var candidate = coloane.Where(x => esteNumar
            ? x.Coloana is Col.Cantitate or Col.Pret or Col.Valoare or Col.Cota or Col.ValoareTva or Col.Ignorat
            : x.Coloana is Col.Um or Col.Denumire or Col.Ignorat);
        var cea = candidate.OrderBy(x => Math.Abs(x.Centru - centru)).FirstOrDefault();
        if (cea is null)
        {
            return Col.Denumire;
        }

        // Textul din stânga coloanei UM rămâne în denumire (denumirile sunt aliniate la stânga, deci departe de centrul antetului).
        if (!esteNumar && cea.Coloana == Col.Um && (c.Text.Length > 6 || centru < (denumire.X1 + cea.X0) / 2))
        {
            return Col.Denumire;
        }

        return cea.Coloana;
    }

    // ------------------------------------------------------------------ utilitare

    private static bool EsteAntetTabel(string text)
    {
        var t = Normalizeaza(text);
        var denumire = t.Contains("DENUMIRE", StringComparison.Ordinal) || t.Contains("PRODUS", StringComparison.Ordinal) ||
                       t.Contains("DESCRIERE", StringComparison.Ordinal) || t.Contains("ARTICOL", StringComparison.Ordinal) ||
                       t.Contains("MATERIAL", StringComparison.Ordinal);
        return denumire && t.Contains("CANT", StringComparison.Ordinal);
    }

    private static bool ContineCuvantAntet(string text)
    {
        var t = Normalizeaza(text);
        return new[] { "UNITAR", "FARA TVA", "TVA", "LEI", "VALOARE", "PRET", "COTA" }.Any(k => t.Contains(k, StringComparison.Ordinal));
    }

    private static bool EsteSfarsitTabel(string text)
    {
        var t = Normalizeaza(text);
        return t.StartsWith("TOTAL", StringComparison.Ordinal) || t.Contains("SEMNATUR", StringComparison.Ordinal) ||
               t.Contains("TERMEN DE PLATA", StringComparison.Ordinal) || t.Contains("DATE PRIVIND EXPEDI", StringComparison.Ordinal) ||
               t.StartsWith("INTOCMIT", StringComparison.Ordinal) || t.Contains("TOTAL DE PLATA", StringComparison.Ordinal);
    }

    /// <summary>
    /// CUI-ul de pe rândul dat aparține cumpărătorului? Se caută în sus eticheta cea mai apropiată. Când „Furnizor” și
    /// „Cumpărător” sunt pe același rând (două coloane), contează poziția orizontală a CUI-ului față de etichete.
    /// </summary>
    private static bool EsteEtichetaClient(IReadOnlyList<RandText> randuri, int index, int x)
    {
        // Cu poziții: cea mai apropiată etichetă de deasupra, din coloana CUI-ului (eticheta cea mai din dreapta care începe
        // la stânga lui). Antetele au des 2–3 coloane, iar eticheta poate fi cu multe rânduri mai sus (adresă, RC, cont…).
        if (x > 0)
        {
            for (var i = index; i >= 0 && i >= index - 15; i--)
            {
                var eticheta = randuri[i].Cuvinte
                    .Where(c => c.X0 <= x + 40 && (EsteCuvantClient(Normalizeaza(c.Text)) || EsteCuvantFurnizor(Normalizeaza(c.Text))))
                    .OrderByDescending(c => c.X0)
                    .FirstOrDefault();
                if (eticheta is not null)
                {
                    return EsteCuvantClient(Normalizeaza(eticheta.Text));
                }
            }

            return false;
        }

        // Fără poziții (Word): eticheta cea mai apropiată de deasupra, pe text.
        for (var i = index; i >= 0 && i >= index - 6; i--)
        {
            var t = Normalizeaza(randuri[i].Text);
            var areClient = t.Contains("CUMPARATOR", StringComparison.Ordinal) || t.StartsWith("CLIENT", StringComparison.Ordinal) || t.StartsWith("BENEFICIAR", StringComparison.Ordinal);
            var areFurnizor = t.Contains("FURNIZOR", StringComparison.Ordinal) || t.Contains("VANZATOR", StringComparison.Ordinal);
            if (areClient != areFurnizor)
            {
                return areClient;
            }
        }

        return false;
    }

    private static bool EsteCuvantClient(string t) =>
        t.StartsWith("CUMPARATOR", StringComparison.Ordinal) || t.StartsWith("CLIENT", StringComparison.Ordinal) || t.StartsWith("BENEFICIAR", StringComparison.Ordinal);

    private static bool EsteCuvantFurnizor(string t) =>
        t.StartsWith("FURNIZOR", StringComparison.Ordinal) || t.StartsWith("VANZATOR", StringComparison.Ordinal) || t.StartsWith("PRESTATOR", StringComparison.Ordinal);

    private static bool EsteContextScadenta(string text)
    {
        var t = Normalizeaza(text);
        return t.Contains("SCADEN", StringComparison.Ordinal) || t.Contains("TERMEN", StringComparison.Ordinal) ||
               t.Contains("LIVRAR", StringComparison.Ordinal);
    }

    private static decimal? CotaDinText(IReadOnlyList<RandText> randuri)
    {
        foreach (var rand in randuri)
        {
            var m = PotrivireCotaText().Match(rand.Text);
            if (m.Success && TextNumeric.ParseazaNumar(m.Groups["p"].Value, out var p) && p is > 0 and < 100)
            {
                return p / 100m;
            }
        }

        return null;
    }

    private static string TaieLaEticheta(string text)
    {
        var m = PotrivireEtichetaUrmatoare().Match(text);
        return m.Success ? text[..m.Index] : text;
    }

    private static bool Egal(decimal calculat, decimal declarat) =>
        Math.Abs(calculat - declarat) <= Math.Max(0.01m, Math.Abs(declarat) * 0.0005m);

    private static Certitudine CertitudineOcr(RandText rand, string fragment)
    {
        if (rand.Cuvinte.Count == 0)
        {
            return Certitudine.Sigur;
        }

        var tokeni = fragment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cuvinte = rand.Cuvinte.Where(c => tokeni.Any(t => t.Contains(c.Text, StringComparison.Ordinal) || c.Text.Contains(t, StringComparison.Ordinal))).ToList();
        return cuvinte.Count > 0 && cuvinte.Min(c => c.Incredere) < TextDocument.PragIncredere ? Certitudine.Nesigur : Certitudine.Sigur;
    }

    private static DateOnly? ParseazaData(string text)
    {
        var m = PotrivireDataLibera().Match(text);
        if (!m.Success)
        {
            return null;
        }

        int zi, luna, an;
        if (m.Groups["Y"].Success)
        {
            an = int.Parse(m.Groups["Y"].Value, CultureInfo.InvariantCulture);
            luna = int.Parse(m.Groups["M"].Value, CultureInfo.InvariantCulture);
            zi = int.Parse(m.Groups["D"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            zi = int.Parse(m.Groups["z"].Value, CultureInfo.InvariantCulture);
            luna = int.Parse(m.Groups["l"].Value, CultureInfo.InvariantCulture);
            an = int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
            if (an < 100)
            {
                an += 2000;
            }
        }

        return an is >= 2000 and <= 2100 && luna is >= 1 and <= 12 && zi >= 1 && zi <= DateTime.DaysInMonth(an, luna)
            ? new DateOnly(an, luna, zi)
            : null;
    }

    private static string Scurteaza(string text, int maxim) => text.Length <= maxim ? text : text[..maxim];

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

    private const string Data = @"(?:\d{1,2}[./\-]\d{1,2}[./\-](?:\d{4}|\d{2})|\d{4}-\d{2}-\d{2})";

    [GeneratedRegex(@"(?:C\.?\s?U\.?\s?I\.?|C\.?\s?I\.?\s?F\.?|Cod\s+(?:de\s+)?(?:identificare\s+)?fiscal[aă]?|VAT(?:\s+(?:no|number|ID))?)\s*[:.]?\s*(?<v>(?:RO)?\s?\d{2,10})\b|\b(?<v>RO\s?\d{4,10})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireCui();

    [GeneratedRegex(@"\b(?:Furnizor(?:ul)?|V[aâ]nz[aă]tor(?:ul)?|Supplier|Prestator(?:ul)?)\b\s*:?\s*(?<v>.*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireEtichetaFurnizor();

    [GeneratedRegex(@"\s{2,}|\b(?:Cump[aă]r[aă]tor|Client|Beneficiar|C\.?U\.?I|C\.?I\.?F|Nr\.?\s*(?:Reg|ord)|Reg\.?\s*Com|Sediul|Adresa|IBAN|Cont)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireEtichetaUrmatoare();

    [GeneratedRegex(@"[A-ZĂÂÎȘȚ0-9][\w&.\-""'„” ]{1,120}?\b(?:S\.?\s?R\.?\s?L\.?|S\.?\s?A\.?|S\.?\s?N\.?\s?C\.?|P\.?\s?F\.?\s?A\.?|I\.?\s?I\.?|S\.?\s?C\.?\s?S\.?)(?=\W|$)", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireDenumireFirma();

    [GeneratedRegex(@"\bSeri[ae]\s*[:.]?\s*(?<s>[A-Z]{1,10})\s*(?:,?\s*(?:nr\.?|num[aă]r(?:ul)?|no\.?))\s*[:.]?\s*(?<n>\d[\d/\-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireSerieNumar();

    [GeneratedRegex(@"(?:\bFactur[aă](?:\s+fiscal[aă])?|\bInvoice)\s*(?:seria\s*\S+\s*)?(?:nr\.?|num[aă]r(?:ul)?|no\.?)\s*[:.]?\s*(?<n>[A-Z]{0,10}\s?[-/]?\s?\d[\d/\-]*)|\b(?:Nr\.?|Num[aă]r(?:ul)?)\s*(?:factur[aă]|document)\s*[:.]?\s*(?<n>[A-Z0-9][A-Z0-9/\-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireNumarFactura();

    [GeneratedRegex(@"\bNr\.?\s*(?<n>[A-Z0-9][A-Z0-9\-]*)\s*/\s*(?<d>\d{1,2}\.\d{1,2}\.\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireNumarCuData();

    [GeneratedRegex(@"\bData\s*(?:factur[iă]i|emiterii|emitere|documentului)?\s*[:.]?\s*(?<d>" + Data + ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireDataEmitere();

    [GeneratedRegex(@"(?:scaden[tț][aăe]i?|termen(?:ul)?\s+de\s+plat[aă]|due\s+date)\s*[:.]?\s*(?<d>" + Data + ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireScadenta();

    [GeneratedRegex(@"termen(?:ul)?\s+de\s+plat[aă]\s*[:.]?\s*(?<z>\d{1,3})\s*zile", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireTermenZile();

    // Același separator între zi, lună și an („1/09.01.2026” nu e o dată; „09.01.2026” da).
    [GeneratedRegex(@"(?<!\d)(?<z>\d{1,2})(?<s>[./\-])(?<l>\d{1,2})\k<s>(?<a>\d{4}|\d{2})\b|\b(?<Y>\d{4})-(?<M>\d{2})-(?<D>\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireDataLibera();

    [GeneratedRegex(@"\b(?:comand[aă]|comenzii?|order|contract(?:ul)?|aviz(?:ul)?)\s*(?:nr\.?|no\.?)?\s*[:.]?\s*[A-Z0-9][A-Z0-9/\-.]*\d[A-Z0-9/\-.]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireReferinta();

    [GeneratedRegex(@"\b(?:EUR|EURO)\b", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireEur();

    [GeneratedRegex(@"TOTAL\s+(?:DE\s+)?PLAT|TOTAL\s+GENERAL|TOTAL\s+FACTUR|^DE\s+PLAT|TOTAL\s+CU\s+TVA|TOTAL\s+DE\s+INCASAT", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireTotalPlata();

    [GeneratedRegex(@"(?:TOTAL|VALOARE|BAZA)\s+(?:FARA\s+TVA|DE\s+IMPOZITARE)|^SUBTOTAL|^TOTAL\s+NET", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireTotalFaraTva();

    [GeneratedRegex(@"^(?:TOTAL\s+)?(?:VALOARE\s+)?TVA\b(?!.*FARA)", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireTotalTva();

    [GeneratedRegex(@"\bTVA\s*(?:\(\s*)?(?:cota\s*)?[:=]?\s*(?<p>\d{1,2}(?:[.,]\d+)?)\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireCotaText();

    [GeneratedRegex(@"^-?\(?\d{1,3}(?:[.\s]\d{3})*,\d{2}\)?$|^-?\(?\d{1,3}(?:,\d{3})*\.\d{2}\)?$|^-?\d+[.,]\d{2}$")]
    private static partial Regex PotrivireSuma();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpatiiMultiple();
}
