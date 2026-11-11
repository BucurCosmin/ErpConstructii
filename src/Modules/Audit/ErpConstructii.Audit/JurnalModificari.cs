using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ErpConstructii.Audit;

/// <summary>
/// O modificare din jurnalul de audit append-only (scrisă de interceptorul de salvare, în aceeași tranzacție cu modificarea).
/// </summary>
/// <remarks>Tabelul <c>audit.jurnal</c>: aplicația are doar SELECT / INSERT (fără UPDATE / DELETE), cu RLS pe firmă.</remarks>
public sealed class IntrareJurnal
{
    private IntrareJurnal() { }

    public Guid Id { get; private set; }
    public Guid FirmaId { get; private set; }
    public DateTimeOffset Moment { get; private set; }
    public Guid? UtilizatorId { get; private set; }
    public string? UtilizatorNume { get; private set; }

    /// <summary>Schema modulului (facturi, stocuri, aprovizionare…).</summary>
    public string SchemaModul { get; private set; } = string.Empty;

    public string Tabel { get; private set; } = string.Empty;
    public Guid EntitateId { get; private set; }

    /// <summary>Adaugat | Modificat | Sters.</summary>
    public string Actiune { get; private set; } = string.Empty;

    /// <summary>Numărul / denumirea documentului (pentru recunoaștere în raport).</summary>
    public string? Document { get; private set; }

    /// <summary>JSON: la adăugare câmp → valoare; la modificare câmp → [vechi, nou].</summary>
    public string Modificari { get; private set; } = "{}";
}

/// <summary>Categoriile din raportul de modificări.</summary>
public static class CategorieJurnal
{
    public const string Facturi = "Facturi";
    public const string Nir = "NIR";
    public const string Stocuri = "Stocuri";
    public const string Aprovizionare = "Aprovizionare";
}

/// <summary>Cum se citesc intrările jurnalului: categoria, denumirea entității și modificările în cuvinte (logică pură).</summary>
public static class CatalogJurnal
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>Tabelele care țin de NIR (recepția, stornarea și corectura ei); restul schemei stocuri = „Stocuri”.</summary>
    public static readonly IReadOnlySet<string> TabeleNir = new HashSet<string>(StringComparer.Ordinal) { "nir", "storno_nir", "corectii_nir" };

    private static readonly Dictionary<string, string> Entitati = new(StringComparer.Ordinal)
    {
        ["facturi_primite"] = "Factură primită",
        ["corectii_facturi_primite"] = "Corectură factură",
        ["anulari_facturi_primite"] = "Anulare factură",
        ["documente"] = "Document original",
        ["nir"] = "NIR",
        ["storno_nir"] = "Stornare NIR",
        ["corectii_nir"] = "Corectură NIR",
        ["miscari"] = "Mișcare de stoc",
        ["articole"] = "Material (nomenclator stoc)",
        ["aliasuri_articole"] = "Alias material furnizor",
        ["gestiuni"] = "Gestiune",
        ["transferuri"] = "Bon de transfer",
        ["bonuri_consum"] = "Bon de consum",
        ["inventare"] = "Inventar",
        ["necesare_materiale"] = "Necesar de materiale",
        ["note_comanda"] = "Notă de comandă",
        ["corectii_note_comanda"] = "Modificare notă de comandă",
        ["stergeri_note_comanda"] = "Ștergere notă de comandă",
        ["depozite"] = "Depozit",
        ["solduri_initiale"] = "Sold inițial (preluare stoc)",
        ["documente_originale"] = "Document original",
        ["parteneri"] = "Partener",
    };

    /// <summary>Valorile tehnice (tipuri de mișcare, stări) în cuvinte.</summary>
    private static readonly Dictionary<string, string> Valori = new(StringComparer.Ordinal)
    {
        ["IntrareNir"] = "intrare NIR",
        ["CorectieNir"] = "corectură NIR",
        ["IesireStornoNir"] = "stornare NIR",
        ["IesireTransfer"] = "ieșire transfer",
        ["IntrareTransfer"] = "intrare transfer",
        ["IesireConsum"] = "consum",
        ["PlusInventar"] = "plus inventar",
        ["MinusInventar"] = "minus inventar",
        ["Inregistrata"] = "înregistrată",
        ["Anulata"] = "anulată",
        ["InAsteptareAprobare"] = "în așteptarea aprobării",
        ["Aprobata"] = "aprobată",
        ["Respinsa"] = "respinsă",
    };

    public static string Categorie(string schema, string tabel) => schema switch
    {
        "facturi" => CategorieJurnal.Facturi,
        "stocuri" => TabeleNir.Contains(tabel) ? CategorieJurnal.Nir : CategorieJurnal.Stocuri,
        "aprovizionare" => CategorieJurnal.Aprovizionare,
        _ => schema.Length == 0 ? string.Empty : char.ToUpperInvariant(schema[0]) + schema[1..],
    };

    public static string Entitate(string tabel) => Entitati.TryGetValue(tabel, out var e) ? e : tabel.Replace('_', ' ');

    public static string Actiune(string actiune) => actiune switch
    {
        "Adaugat" => "adăugare",
        "Modificat" => "modificare",
        "Sters" => "ștergere",
        _ => actiune,
    };

    /// <summary>„CantitateReceptionata” → „Cantitate receptionata”; „LiniiJson” → „Linii”.</summary>
    public static string Camp(string nume)
    {
        if (nume.EndsWith("Json", StringComparison.Ordinal))
            nume = nume[..^4];
        var sb = new StringBuilder();
        for (var i = 0; i < nume.Length; i++)
        {
            if (i > 0 && char.IsUpper(nume[i]) && !char.IsUpper(nume[i - 1]))
                sb.Append(' ').Append(char.ToLowerInvariant(nume[i]));
            else
                sb.Append(nume[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Modificările în cuvinte. La modificare: „Cantitate: 10 → 11; Linii: modificate”. La adăugare: motivul și rezumatul,
    /// dacă e o corectură / stornare, altfel câteva câmpuri de recunoaștere.
    /// </summary>
    public static string Descriere(string actiune, string modificariJson, int maximCampuriAdaugare = 8)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(modificariJson) ? "{}" : modificariJson);
        var radacina = doc.RootElement;
        if (radacina.ValueKind != JsonValueKind.Object)
            return string.Empty;
        var parti = new List<string>();
        if (actiune == "Modificat")
        {
            foreach (var p in radacina.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() != 2)
                    continue;
                var vechi = Text(p.Value[0], p.Name);
                var nou = Text(p.Value[1], p.Name);
                parti.Add(EsteLung(vechi) || EsteLung(nou) ? $"{Camp(p.Name)}: modificate" : $"{Camp(p.Name)}: {vechi} → {nou}");
            }

            return string.Join("; ", parti);
        }

        var campuri = radacina.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        if (actiune == "Sters")
        {
            // Ștergerile mai vechi nu au valorile înregistrării; cele noi le au (ce conținea înainte de ștergere).
            if (campuri.Count == 0)
                return "șters";
            parti.Add("șters");
        }

        if (campuri.TryGetValue("Motiv", out var motiv))
        {
            parti.Add($"Motiv: {Text(motiv)}");
            if (campuri.TryGetValue("Rezumat", out var rezumat))
                parti.Add(Text(rezumat));
            return string.Join(". ", parti);
        }

        foreach (var (nume, valoare) in campuri)
        {
            var text = Text(valoare, nume);
            // Identificatorii, amprentele și numărul documentului (deja în coloana „Document”) nu ajută la citire.
            if (EsteLung(text) || text.Length == 0 || nume.EndsWith("Id", StringComparison.Ordinal) || nume.StartsWith("Hash", StringComparison.Ordinal)
                || nume is "DocumentNumar" or "NirNumar")
                continue;
            parti.Add($"{Camp(nume)}: {text}");
            if (parti.Count >= maximCampuriAdaugare)
                break;
        }

        return string.Join("; ", parti);
    }

    private static bool EsteLung(string text) => text.Length > 120 || text.StartsWith('[') || text.StartsWith('{');

    /// <summary>Câmpurile numerice care nu sunt cantități / sume (fără separator de mii: 2026, nu 2.026).</summary>
    private static readonly HashSet<string> Identificatori = new(StringComparer.Ordinal) { "An", "Numar", "Versiune", "VersiuneSablon", "IndexFactura" };

    private static string Text(JsonElement v, string? camp = null) => v.ValueKind switch
    {
        JsonValueKind.Number when camp is not null && Identificatori.Contains(camp) => v.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => "—",
        JsonValueKind.True => "da",
        JsonValueKind.False => "nu",
        JsonValueKind.Number => v.TryGetDecimal(out var d) ? d.ToString("#,##0.####", Ro) : v.GetRawText(),
        JsonValueKind.String when DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            => data.ToString("dd.MM.yyyy", Ro),
        JsonValueKind.String => v.GetString() is { } t && Valori.TryGetValue(t, out var cuvinte) ? cuvinte : v.GetString() ?? string.Empty,
        _ => v.GetRawText(),
    };
}
