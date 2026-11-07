using System.Globalization;
using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

/// <summary>Valorile corectate ale unei linii de NIR (identificată prin poziția ei în factură).</summary>
public sealed record LinieNirCorectata(int IndexFactura, decimal CantitateFactura, decimal CantitateReceptionata, decimal PretUnitar, string? Observatii);

/// <summary>O linie schimbată de corectură: înainte / după și diferența care intră (sau iese) din stoc.</summary>
public sealed record LinieCorectieNir(
    int IndexFactura, Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura,
    decimal CantitateFacturaVeche, decimal CantitateFacturaNoua, decimal CantitateVeche, decimal CantitateNoua,
    decimal PretVechi, decimal PretNou, decimal ValoareVeche, decimal ValoareNoua)
{
    public decimal DiferentaCantitate => CantitateNoua - CantitateVeche;
    public decimal DiferentaValoare => ValoareNoua - ValoareVeche;
}

/// <summary>
/// Corectura unui NIR recepționat greșit (D-35): NIR-ul se modifică împreună cu această înregistrare append-only (regula 6,
/// excepția controlată din D-33), iar stocul primește diferențele (mișcări <see cref="TipMiscare.CorectieNir"/> pe același NIR).
/// </summary>
public sealed class CorectieNir : EntitateFirma, ICorectieDocument
{
    private IReadOnlyList<LinieCorectieNir>? _linii;

    private CorectieNir() { }

    public Guid NirId { get; private set; }
    public string NirNumar { get; private set; } = string.Empty;
    public Guid FacturaId { get; private set; }
    public DateOnly Data { get; private set; }
    public string Motiv { get; private set; } = string.Empty;
    public string Rezumat { get; private set; } = string.Empty;
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieCorectieNir> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieCorectieNir>>(LiniiJson) ?? [];

    /// <summary>Cu cât s-a schimbat valoarea NIR-ului (pozitiv = a crescut).</summary>
    public decimal DiferentaValoare { get; private set; }

    public Guid CorectatDeId { get; private set; }
    public string CorectatDeNume { get; private set; } = string.Empty;
    public Guid DocumentCorectatId => NirId;

    internal static CorectieNir Creeaza(Nir nir, DateOnly data, string motiv, IReadOnlyList<LinieCorectieNir> linii, Guid utilizatorId, string utilizatorNume) => new()
    {
        NirId = nir.Id,
        NirNumar = nir.NumarAfisat,
        FacturaId = nir.FacturaId,
        Data = data,
        Motiv = motiv.Trim(),
        Rezumat = RezumatLinii(linii),
        LiniiJson = JsonSerializer.Serialize(linii),
        DiferentaValoare = linii.Sum(l => l.DiferentaValoare),
        CorectatDeId = utilizatorId,
        CorectatDeNume = utilizatorNume.Trim(),
    };

    /// <summary>„SACI RAFIE: recepționat 7.000 → 7, preț 1,20 → 1,25”.</summary>
    public static string RezumatLinii(IEnumerable<LinieCorectieNir> linii)
    {
        var ro = CultureInfo.GetCultureInfo("ro-RO");
        string Cant(decimal v) => v.ToString("#,##0.####", ro);
        string Pret(decimal v) => v.ToString("#,##0.00##", ro);
        var parti = linii.Select(l =>
        {
            var schimbari = new List<string>();
            if (l.CantitateVeche != l.CantitateNoua) schimbari.Add($"recepționat {Cant(l.CantitateVeche)} → {Cant(l.CantitateNoua)} {l.UnitateMasura}");
            if (l.CantitateFacturaVeche != l.CantitateFacturaNoua) schimbari.Add($"cf. factură {Cant(l.CantitateFacturaVeche)} → {Cant(l.CantitateFacturaNoua)}");
            if (l.PretVechi != l.PretNou) schimbari.Add($"preț {Pret(l.PretVechi)} → {Pret(l.PretNou)}");
            if (schimbari.Count == 0) schimbari.Add("observații");
            return $"{l.Denumire}: {string.Join(", ", schimbari)}";
        });
        var text = string.Join("; ", parti);
        return text.Length <= 4000 ? text : text[..3999] + "…";
    }
}
