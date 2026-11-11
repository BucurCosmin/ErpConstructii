using System.Globalization;
using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Aprovizionare;

public enum TipCorectieNotaComanda
{
    /// <summary>Datele notei emise au fost modificate (furnizor, materiale, condiții…).</summary>
    Modificare,

    /// <summary>Comanda venită din necesar a fost anulată: nota a revenit la achiziții („Necesar de achiziție”), fără furnizor.</summary>
    ComandaAnulata,

    /// <summary>Nota importată fără necesar a fost legată de necesarul ei (D-45).</summary>
    NecesarLegat,
}

/// <summary>
/// Corectura unei note de comandă emise (D-44): entitate separată, append-only, cu datele dinainte și de după, motivul și
/// autorul. Nota blocată se modifică doar în aceeași salvare cu corectura ei (regula 6, verificat de interceptorul de audit).
/// </summary>
public sealed class CorectieNotaComanda : EntitateFirma, ICorectieDocument
{
    private CorectieNotaComanda() { }

    public Guid NotaComandaId { get; private set; }
    public TipCorectieNotaComanda Tip { get; private set; }
    public string Motiv { get; private set; } = string.Empty;
    public string DateVechiJson { get; private set; } = "{}";
    public string DateNoiJson { get; private set; } = "{}";

    /// <summary>Ce s-a schimbat, pe scurt (ex. „Furnizor: A → B; linia 2: cantitate 10 → 12”).</summary>
    public string Rezumat { get; private set; } = string.Empty;

    public Guid CorectatDeId { get; private set; }
    public string CorectatDeNume { get; private set; } = string.Empty;
    public DateTimeOffset CorectatLa { get; private set; }

    Guid ICorectieDocument.DocumentCorectatId => NotaComandaId;

    internal static CorectieNotaComanda Creeaza(
        Guid notaId, TipCorectieNotaComanda tip, string motiv, string vechi, string nou, string rezumat, Guid utilizatorId, string utilizatorNume, DateTimeOffset moment) => new()
    {
        NotaComandaId = notaId,
        Tip = tip,
        Motiv = motiv.Trim(),
        DateVechiJson = vechi,
        DateNoiJson = nou,
        Rezumat = rezumat.Length > 4000 ? rezumat[..4000] : rezumat,
        CorectatDeId = utilizatorId,
        CorectatDeNume = utilizatorNume.Trim(),
        CorectatLa = moment,
    };
}

/// <summary>
/// Ștergerea unei note de comandă înregistrate greșit (D-44): nota dispare din evidență, iar aici rămâne cine, când, de ce
/// și ce conținea (instantaneu JSON). Numărul NC rămâne ocupat (nu se refolosește).
/// </summary>
public sealed class StergereNotaComanda : EntitateFirma
{
    private StergereNotaComanda() { }

    public Guid NotaComandaId { get; private set; }
    public string Numar { get; private set; } = string.Empty;
    public string? NumarExtern { get; private set; }
    public DateOnly Data { get; private set; }
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public string FurnizorDenumire { get; private set; } = string.Empty;
    public string Motiv { get; private set; } = string.Empty;

    /// <summary>Ce s-a întâmplat cu materialele (ex. „Materialele au revenit pe NC/2026/0007, la achiziții.”).</summary>
    public string? Efect { get; private set; }

    public string DateJson { get; private set; } = "{}";
    public Guid StearsaDeId { get; private set; }
    public string StearsaDeNume { get; private set; } = string.Empty;
    public DateTimeOffset StearsaLa { get; private set; }

    internal static StergereNotaComanda Creeaza(NotaComanda n, string motiv, string? efect, Guid utilizatorId, string utilizatorNume, DateTimeOffset moment) => new()
    {
        NotaComandaId = n.Id,
        Numar = n.NumarAfisat,
        NumarExtern = n.NumarExtern,
        Data = n.Data,
        ProiectId = n.ProiectId,
        ProiectCod = n.ProiectCod,
        FurnizorDenumire = n.FurnizorDenumire,
        Motiv = motiv.Trim(),
        Efect = efect,
        DateJson = JsonSerializer.Serialize(InstantaneuNotaComanda.Din(n)),
        StearsaDeId = utilizatorId,
        StearsaDeNume = utilizatorNume.Trim(),
        StearsaLa = moment,
    };
}

/// <summary>Datele unei note de comandă la un moment dat (pentru istoric și pentru rezumatul diferențelor).</summary>
public sealed record InstantaneuNotaComanda(
    string Numar, string? NumarExtern, DateOnly Data, string ProiectCod, string? Lucrare, string FurnizorDenumire, string? FurnizorCui,
    string? FurnizorAdresa, string? FurnizorTelefon, string? FurnizorEmail, ConditiiComanda Conditii, IReadOnlyList<LinieNotaComanda> Linii,
    string? Observatii, string Stare)
{
    public static InstantaneuNotaComanda Din(NotaComanda n) => new(n.NumarAfisat, n.NumarExtern, n.Data, n.ProiectCod, n.Lucrare, n.FurnizorDenumire,
        n.FurnizorCui, n.FurnizorAdresa, n.FurnizorTelefon, n.FurnizorEmail, n.Conditii, n.Linii, n.Observatii, n.Stare.ToString());

    /// <summary>„Furnizor: A → B; linia 2 „Ciment”: cantitate 10 → 12; linie nouă: …”. Gol dacă nu diferă nimic.</summary>
    public static string Diferente(InstantaneuNotaComanda a, InstantaneuNotaComanda b)
    {
        var ro = CultureInfo.GetCultureInfo("ro-RO");
        string T(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();
        string N(decimal v) => v.ToString("0.####", ro);
        var p = new List<string>();
        void Camp(string nume, string? x, string? y)
        {
            if (!string.Equals(T(x), T(y), StringComparison.Ordinal)) p.Add($"{nume}: {T(x)} → {T(y)}");
        }

        Camp("Nr. pe hârtie", a.NumarExtern, b.NumarExtern);
        Camp("Data", a.Data.ToString("dd.MM.yyyy", ro), b.Data.ToString("dd.MM.yyyy", ro));
        Camp("Lucrare", a.Lucrare, b.Lucrare);
        Camp("Furnizor", a.FurnizorDenumire, b.FurnizorDenumire);
        Camp("CUI", a.FurnizorCui, b.FurnizorCui);
        Camp("Adresa furnizorului", a.FurnizorAdresa, b.FurnizorAdresa);
        Camp("Telefon", a.FurnizorTelefon, b.FurnizorTelefon);
        Camp("E-mail", a.FurnizorEmail, b.FurnizorEmail);
        Camp("Recepție", a.Conditii.Receptie?.ToString(), b.Conditii.Receptie?.ToString());
        Camp("Clasa de calitate", a.Conditii.ClasaCalitate?.ToString(), b.Conditii.ClasaCalitate?.ToString());
        Camp("Transport", a.Conditii.Transport?.ToString(), b.Conditii.Transport?.ToString());
        Camp("Plata", a.Conditii.Plata?.ToString(), b.Conditii.Plata?.ToString());
        Camp("Observații", a.Observatii, b.Observatii);

        // Liniile pe poziție: modificate, adăugate la sfârșit, scoase de la sfârșit.
        for (var i = 0; i < Math.Max(a.Linii.Count, b.Linii.Count); i++)
        {
            if (i >= a.Linii.Count)
            {
                var l = b.Linii[i];
                p.Add($"linie nouă: {l.Denumire}, {N(l.Cantitate)} {l.UnitateMasura}");
                continue;
            }

            if (i >= b.Linii.Count)
            {
                p.Add($"linia {i + 1} scoasă: {a.Linii[i].Denumire}");
                continue;
            }

            var (x, y) = (a.Linii[i], b.Linii[i]);
            var d = new List<string>();
            if (!string.Equals(x.Denumire, y.Denumire, StringComparison.Ordinal)) d.Add($"denumire „{x.Denumire}” → „{y.Denumire}”");
            if (!string.Equals(x.UnitateMasura, y.UnitateMasura, StringComparison.Ordinal)) d.Add($"UM {x.UnitateMasura} → {y.UnitateMasura}");
            if (x.Cantitate != y.Cantitate) d.Add($"cantitate {N(x.Cantitate)} → {N(y.Cantitate)}");
            if (!string.Equals(T(x.Termen), T(y.Termen), StringComparison.Ordinal)) d.Add($"termen {T(x.Termen)} → {T(y.Termen)}");
            if (d.Count > 0) p.Add($"linia {i + 1} „{y.Denumire}”: {string.Join(", ", d)}");
        }

        return string.Join("; ", p);
    }
}
