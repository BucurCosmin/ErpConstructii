using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ErpConstructii.SharedKernel.Import;

/// <summary>Ce document conține o pagină dintr-un dosar scanat.</summary>
public enum TipPagina
{
    Factura,

    /// <summary>Vizualizarea facturii din SPV („RO eFactura”), tipărită de obicei pe orizontală.</summary>
    EFactura,

    /// <summary>Nota de comandă către furnizor (formularul „COMANDA”, GC 007).</summary>
    NotaComanda,

    /// <summary>Necesarul de materiale (cererea șantierului).</summary>
    NecesarMateriale,

    /// <summary>Aviz de însoțire a mărfii.</summary>
    Aviz,

    /// <summary>Certificate de calitate, declarații de conformitate, buletine de analiză.</summary>
    Certificat,

    /// <summary>Anexe ale facturii (ex. situația alimentărilor cu carburant, devize).</summary>
    Anexa,

    Altele,
}

/// <param name="Numar">Numărul documentului, dacă apare clar (ex. „496” din „Nr. 496 /08.09.2026”).</param>
public sealed record PaginaClasificata(int Pagina, TipPagina Tip, string? Numar, DateOnly? Data)
{
    public string Descriere => ClasificarePagini.Denumire(Tip) +
                               (Numar is null ? string.Empty : $" nr. {Numar}") +
                               (Data is { } d ? $" din {d:dd.MM.yyyy}" : string.Empty);
}

/// <summary>
/// Pe hârtie, oamenii țin la un loc documentele aceleiași achiziții: factura, e-Factura, nota de comandă, avizul,
/// certificatele. Scanate împreună, ele ajung într-un singur PDF. Clasificarea spune ce conține fiecare pagină, ca
/// fiecare interpretor să citească doar paginile documentului lui. Reguli pe cuvintele din titlu (partea de sus a
/// paginii) și din tot textul. Funcție pură.
/// </summary>
public static partial class ClasificarePagini
{
    public static string Denumire(TipPagina tip) => tip switch
    {
        TipPagina.Factura => "Factură",
        TipPagina.EFactura => "e-Factura (vizualizare SPV)",
        TipPagina.NotaComanda => "Notă de comandă",
        TipPagina.NecesarMateriale => "Necesar de materiale",
        TipPagina.Aviz => "Aviz de însoțire",
        TipPagina.Certificat => "Certificat / declarație de conformitate",
        TipPagina.Anexa => "Anexă",
        _ => "Altă pagină",
    };

    public static IReadOnlyList<PaginaClasificata> Clasifica(TextDocument document)
    {
        var rezultat = new List<PaginaClasificata>();
        foreach (var grup in document.Randuri.GroupBy(r => r.Pagina).OrderBy(g => g.Key))
        {
            var randuri = grup.ToList();
            var tip = Tip(randuri);
            var text = string.Join(' ', randuri.Select(r => r.Text));

            // „Pagina 2 din 2”: continuarea documentului de pe pagina anterioară.
            if (tip == TipPagina.Altele && rezultat.Count > 0 && PotrivirePaginaDin().Match(Normalizeaza(text)) is { Success: true } p &&
                int.Parse(p.Groups["k"].Value, CultureInfo.InvariantCulture) > 1)
            {
                tip = rezultat[^1].Tip;
            }

            // Anexele (ex. tranzacțiile de carburant) și certificatele continuă des pe pagini fără titlu.
            if (tip == TipPagina.Altele && rezultat.Count > 0 && rezultat[^1].Tip is TipPagina.Anexa or TipPagina.Certificat)
            {
                tip = rezultat[^1].Tip;
            }

            var (numar, data) = tip is TipPagina.NotaComanda or TipPagina.NecesarMateriale or TipPagina.Aviz ? NumarSiData(text) : (null, null);
            rezultat.Add(new PaginaClasificata(grup.Key, tip, numar, data));
        }

        return rezultat;
    }

    /// <summary>Paginile de tipul cerut, ca document separat; null dacă documentul nu are pagini de acest tip.</summary>
    public static TextDocument? Pagini(TextDocument document, IReadOnlyList<PaginaClasificata> clasificare, params TipPagina[] tipuri)
    {
        var pagini = clasificare.Where(p => tipuri.Contains(p.Tip)).Select(p => p.Pagina).ToHashSet();
        return pagini.Count == 0
            ? null
            : document with { Randuri = document.Randuri.Where(r => pagini.Contains(r.Pagina)).ToList() };
    }

    /// <summary>
    /// Documentul pregătit pentru interpretorul unui tip: un document cu o singură pagină (sau Word) rămâne întreg; dintr-un
    /// dosar cu mai multe pagini rămân doar paginile tipului cerut (dacă există).
    /// </summary>
    public static (TextDocument Document, IReadOnlyList<PaginaClasificata> Clasificare) PentruTip(TextDocument document, params TipPagina[] tipuri)
    {
        var clasificare = Clasifica(document);
        if (clasificare.Count <= 1)
        {
            return (document, clasificare);
        }

        return (Pagini(document, clasificare, tipuri) ?? document, clasificare);
    }

    private static TipPagina Tip(IReadOnlyList<RandText> randuri)
    {
        if (randuri.Count == 0)
        {
            return TipPagina.Altele;
        }

        // Titlul: primele rânduri (cel puțin 10, cel mult o treime din pagină).
        var nTitlu = Math.Max(10, randuri.Count / 3);
        var titlu = Normalizeaza(string.Join(' ', randuri.Take(nTitlu).Select(r => r.Text)));
        var tot = Normalizeaza(string.Join(' ', randuri.Select(r => r.Text)));

        // Vizualizarea SPV: antetul „VANZATOR | CUMPARATOR” și sigla „RO eFactura” sus (facturile furnizorilor pomenesc
        // des e-Factura în subsol, deci nu ajunge să apară cuvântul oriunde în pagină).
        if (titlu.Contains("EFACTURA", StringComparison.Ordinal) || (Cuvant(titlu, "VANZATOR") && titlu.Contains("CUMPARATOR", StringComparison.Ordinal)))
            return TipPagina.EFactura;

        // Formularul de comandă: COMANDA + Beneficiar / Furnizor + Obiect / Lucrare (textul lui pomenește și certificatele).
        if (Cuvant(titlu, "COMANDA") && Cuvant(titlu, "FURNIZOR") && (Cuvant(titlu, "BENEFICIAR") || titlu.Contains("OBIECT", StringComparison.Ordinal)) &&
            !Cuvant(titlu, "FACTURA"))
            return TipPagina.NotaComanda;
        if (tot.Contains("GC 007", StringComparison.Ordinal) || tot.Contains("GC007", StringComparison.Ordinal))
            return TipPagina.NotaComanda;

        if (titlu.Contains("NECESAR", StringComparison.Ordinal))
            return TipPagina.NecesarMateriale;

        if (Cuvant(titlu, "AVIZ") && titlu.Contains("INSOTIRE", StringComparison.Ordinal) && !Cuvant(titlu, "FACTURA"))
            return TipPagina.Aviz;

        if (Cuvant(titlu, "FACTURA") || (Cuvant(tot, "FACTURA") && (Cuvant(tot, "FURNIZOR") || Cuvant(tot, "VANZATOR")) &&
                                         (Cuvant(tot, "CUMPARATOR") || Cuvant(tot, "CLIENT"))))
            return TipPagina.Factura;

        if (titlu.Contains("CONFORMIT", StringComparison.Ordinal) || titlu.Contains("CERTIFICAT", StringComparison.Ordinal) ||
            titlu.Contains("DECLARATI", StringComparison.Ordinal) || titlu.Contains("BULETIN", StringComparison.Ordinal) ||
            titlu.Contains("DECLARATION", StringComparison.Ordinal) || titlu.Contains("CONFORMITY", StringComparison.Ordinal))
            return TipPagina.Certificat;

        if (Cuvant(titlu, "ANEXA") || titlu.Contains("SITUATIA", StringComparison.Ordinal) || Cuvant(titlu, "DEVIZ"))
            return TipPagina.Anexa;

        return TipPagina.Altele;
    }

    private static (string? Numar, DateOnly? Data) NumarSiData(string text)
    {
        var m = PotrivireNumarCuData().Match(text);
        if (m.Success)
        {
            return (m.Groups["n"].Value, Data(m.Groups["d"].Value));
        }

        var n = PotrivireNumar().Match(text);
        var d = PotrivireData().Match(text);
        return (n.Success ? n.Groups["n"].Value : null, d.Success ? Data(d.Value) : null);
    }

    private static DateOnly? Data(string text) =>
        DateOnly.TryParseExact(text.Replace('/', '.').Replace('-', '.'), ["d.M.yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static bool Cuvant(string text, string cuvant) =>
        Regex.IsMatch(text, $@"(?<![A-Z]){cuvant}(?![A-Z])", RegexOptions.CultureInvariant);

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

        return SpatiiMultiple().Replace(sb.ToString(), " ");
    }

    [GeneratedRegex(@"\bPAGINA\s*(?<k>\d{1,2})\s*(?:DIN|/)\s*(?<n>\d{1,2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivirePaginaDin();

    [GeneratedRegex(@"\bNr\.?\s*(?<n>[A-Z0-9][A-Z0-9\-]*)\s*/\s*(?<d>\d{1,2}\.\d{1,2}\.\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireNumarCuData();

    [GeneratedRegex(@"\b(?:Nr|Numar|Număr)\.?\s*[:.]?\s*(?<n>\d[\dA-Z\-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireNumar();

    [GeneratedRegex(@"(?<!\d)\d{1,2}[./]\d{1,2}[./]\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PotrivireData();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpatiiMultiple();
}
