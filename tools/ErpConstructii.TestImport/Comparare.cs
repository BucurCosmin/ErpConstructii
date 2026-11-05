using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.TestImport;

public sealed record Diferenta(string Document, string Camp, string? Citit, string? Confirmat);

public sealed record RezultatFisier(string Nume, string Cale, RezultatCitire Citit, FisierCitit? Confirmat, List<Diferenta>? Diferente);

/// <summary>
/// Compară citirea cu valorile confirmate. Un câmp confirmat lipsă (null) nu se verifică: se pot confirma doar câmpurile
/// sigure. Textele se compară doar pe litere și cifre (fără diacritice, majuscule, punctuație); sumele exact.
/// </summary>
public static class Comparare
{
    public static List<Diferenta> Diferente(FisierCitit citit, FisierCitit confirmat)
    {
        var d = new List<Diferenta>();
        if (citit.Documente.Count != confirmat.Documente.Count)
        {
            d.Add(new Diferenta("fișier", "documente",
                string.Join(" | ", citit.Documente.Select(Descriere)), string.Join(" | ", confirmat.Documente.Select(Descriere))));
        }

        foreach (var (c, a) in citit.Documente.Zip(confirmat.Documente))
        {
            var doc = $"{a.Tip} p.{string.Join(",", a.Pagini)}";
            Camp(d, doc, "tip", c.Tip, a.Tip);
            Camp(d, doc, "pagini", string.Join(",", c.Pagini), string.Join(",", a.Pagini));
            Camp(d, doc, "număr", c.Numar, a.Numar);
            Camp(d, doc, "data", c.Data?.ToString("dd.MM.yyyy"), a.Data?.ToString("dd.MM.yyyy"));
            Camp(d, doc, "furnizor", c.Furnizor, a.Furnizor);
            Camp(d, doc, "CUI furnizor", c.FurnizorCui, a.FurnizorCui);
            Camp(d, doc, "lucrare", c.Lucrare, a.Lucrare);
            Camp(d, doc, "total fără TVA", c.TotalFaraTva, a.TotalFaraTva);
            Camp(d, doc, "TVA", c.TotalTva, a.TotalTva);
            Camp(d, doc, "total de plată", c.TotalDePlata, a.TotalDePlata);
            if (a.Linii is null)
            {
                continue;
            }

            var lc = c.Linii ?? [];
            if (lc.Count != a.Linii.Count)
            {
                d.Add(new Diferenta(doc, "număr de linii", lc.Count.ToString(CultureInfo.InvariantCulture), a.Linii.Count.ToString(CultureInfo.InvariantCulture)));
            }

            foreach (var (i, (x, y)) in lc.Zip(a.Linii).Index())
            {
                var l = $"linia {i + 1}";
                Camp(d, doc, $"{l} denumire", x.Denumire, y.Denumire);
                Camp(d, doc, $"{l} UM", x.Um, y.Um);
                Camp(d, doc, $"{l} cantitate", x.Cantitate, y.Cantitate);
                Camp(d, doc, $"{l} preț", x.Pret, y.Pret);
                Camp(d, doc, $"{l} valoare", x.Valoare, y.Valoare);
                Camp(d, doc, $"{l} cota TVA", x.Cota, y.Cota);
            }
        }

        return d;
    }

    public static string Descriere(DocumentCitit d) => $"{d.Tip} p.{string.Join(",", d.Pagini)}";

    private static void Camp(List<Diferenta> d, string doc, string camp, string? citit, string? confirmat)
    {
        if (confirmat is not null && Normalizeaza(citit) != Normalizeaza(confirmat))
        {
            d.Add(new Diferenta(doc, camp, citit, confirmat));
        }
    }

    private static void Camp(List<Diferenta> d, string doc, string camp, decimal? citit, decimal? confirmat)
    {
        if (confirmat is { } a && (citit is not { } c || decimal.Round(c, 4) != decimal.Round(a, 4)))
        {
            d.Add(new Diferenta(doc, camp, citit?.ToString(CultureInfo.InvariantCulture), a.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public static string Normalizeaza(string? text)
    {
        var fd = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        // Doar literele și cifrele: punctuația și spațiile („35L(MAI GROSI)” / „35L (MAI GROSI)”) nu contează.
        return new string(sb.ToString().Where(char.IsLetterOrDigit).ToArray());
    }
}

public static class Json
{
    private static readonly JsonSerializerOptions Optiuni = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // diacriticele rămân lizibile
    };

    private static readonly JsonSerializerOptions OptiuniText = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Scrie(FisierCitit f) => JsonSerializer.Serialize(f, Optiuni);

    public static FisierCitit Citeste(string json) => JsonSerializer.Deserialize<FisierCitit>(json, Optiuni)!;

    public static string ScrieText(TextDocument t) => JsonSerializer.Serialize(t, OptiuniText);

    public static TextDocument CitesteText(string json) => JsonSerializer.Deserialize<TextDocument>(json, OptiuniText)!;
}

public static class Cai
{
    /// <summary>Rădăcina repository-ului: primul folder părinte cu ErpConstructii.slnx.</summary>
    public static string Radacina()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "ErpConstructii.slnx")))
                {
                    return d.FullName;
                }
            }
        }

        return Directory.GetCurrentDirectory();
    }

    public static string NumeJson(string numeRelativ) => numeRelativ.Replace('/', '_') + ".json";
}

public sealed record Optiuni(string? Filtru, bool Confirma, bool FaraCache, string CuiFirma)
{
    public static Optiuni Din(string[] args)
    {
        var cui = Array.IndexOf(args, "--cui") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : "RO5160991";
        var filtru = args.Where((a, j) => !a.StartsWith("--", StringComparison.Ordinal) && (j == 0 || args[j - 1] != "--cui")).FirstOrDefault();
        return new Optiuni(filtru, args.Contains("--confirma"), args.Contains("--fara-cache"), cui);
    }
}
