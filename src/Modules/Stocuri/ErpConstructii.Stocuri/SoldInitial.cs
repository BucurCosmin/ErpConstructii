using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

/// <param name="CodExtern">Codul materialului în programul de gestiune anterior.</param>
public sealed record LinieSoldInitial(Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate, decimal Valoare, string? CodExtern);

public sealed record ArticolExistent(Guid Id, string Cod, string Denumire, string UnitateMasura, string? CodExtern);

public enum SursaPotrivireSold { CodExtern, Denumire }

/// <summary>Materialul din nomenclator pentru o linie de balanță și depozitul pentru o gestiune din fișier. Funcții pure.</summary>
public static class PotrivireSoldInitial
{
    /// <summary>
    /// Întâi după codul extern (același cod din programul anterior), apoi după denumire (<see cref="PotrivireArticol.Cheie"/>)
    /// cu aceeași UM. Fără UM în fișier („Raport rapid stoc”), denumirea ajunge, dacă un singur material o are.
    /// Altfel null: se creează material nou.
    /// </summary>
    public static (ArticolExistent Articol, SursaPotrivireSold Sursa)? Propune(string denumire, string? codExtern, string unitateMasura, IReadOnlyList<ArticolExistent> articole)
    {
        if (!string.IsNullOrWhiteSpace(codExtern)
            && articole.FirstOrDefault(a => string.Equals(a.CodExtern?.Trim(), codExtern.Trim(), StringComparison.OrdinalIgnoreCase)) is { } dupaCod)
            return (dupaCod, SursaPotrivireSold.CodExtern);

        var cheie = PotrivireArticol.Cheie(denumire);
        if (cheie.Length == 0)
            return null;
        var acelasiNume = articole.Where(a => PotrivireArticol.Cheie(a.Denumire) == cheie).ToList();
        var um = unitateMasura.Trim().ToLowerInvariant();
        if (um.Length == 0)
            return acelasiNume.Count == 1 ? (acelasiNume[0], SursaPotrivireSold.Denumire) : null;
        return acelasiNume.FirstOrDefault(a => a.UnitateMasura.Trim().ToLowerInvariant() == um) is { } dupaDenumire
            ? (dupaDenumire, SursaPotrivireSold.Denumire)
            : null;
    }

    /// <summary>Codul gestiunii din ERP pentru o gestiune din fișier: „01-G5” → „G5” (partea de după ultima cratimă).</summary>
    public static string CodDepozit(string gestiuneExterna)
    {
        var t = gestiuneExterna.Trim();
        var i = t.LastIndexOf('-');
        var cod = (i >= 0 && i < t.Length - 1 ? t[(i + 1)..] : t).Trim().ToUpperInvariant();
        return cod.Length > 20 ? cod[..20] : cod;
    }
}

public sealed record LinieSoldInitialCerere(ArticolStoc Articol, decimal Cantitate, decimal Valoare, string? CodExtern);

/// <summary>
/// Preluarea stocului dintr-un program de gestiune anterior (balanța de stoc, D-42): stocul final al unei gestiuni din
/// fișier intră într-un depozit, ca punct de pornire. Balanța are doar cantități (valoare 0, CMP 0 până la primele recepții);
/// „Raport rapid stoc” are și valorile (CMP-ul din programul anterior). Un document pe (fișier, gestiune), SI/AAAA/NNNN.
/// Imuabil (regula 6); greșelile se corectează prin inventar, iar soldul preluat greșit se anulează cât timp materialele
/// lui nu au alte mișcări în gestiune (<see cref="AnulareSoldInitial"/>).
/// </summary>
public sealed class SoldInitialStoc : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieSoldInitial>? _linii;

    private SoldInitialStoc() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"SI/{An}/{Numar:D4}";
    public DateOnly Data { get; private set; }
    public Guid GestiuneId { get; private set; }
    public string GestiuneDenumire { get; private set; } = string.Empty;

    /// <summary>Codul gestiunii în fișier (ex. „01-G5”).</summary>
    public string GestiuneExterna { get; private set; } = string.Empty;

    public string? Categorie { get; private set; }
    public string NumeFisier { get; private set; } = string.Empty;

    /// <summary>SHA-256 al fișierului: același fișier nu se importă de două ori în aceeași gestiune.</summary>
    public string FisierSha256 { get; private set; } = string.Empty;

    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieSoldInitial> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieSoldInitial>>(LiniiJson) ?? [];
    public decimal Valoare { get; private set; }
    public Guid ImportatDeId { get; private set; }
    public string ImportatDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;

    /// <summary>Același material de mai multe ori (ex. pe rânduri diferite) se adună într-o linie.</summary>
    public static Rezultat<SoldInitialStoc> Emite(
        int an, int numar, DateOnly data, Gestiune gestiune, string gestiuneExterna, string? categorie, string numeFisier, string fisierSha256,
        IReadOnlyList<LinieSoldInitialCerere> linii, Guid utilizatorId, string utilizatorNume)
    {
        if (an < 2000 || numar < 1)
            return Rezultat.Esuat<SoldInitialStoc>(Eroare.Validare("Numărul documentului nu este valid."));
        if (gestiune.Tip != TipGestiune.Depozit)
            return Rezultat.Esuat<SoldInitialStoc>(Eroare.Validare("Soldul inițial se preia doar într-o gestiune de depozit."));
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            return Rezultat.Esuat<SoldInitialStoc>(Eroare.Validare("Utilizatorul este obligatoriu."));
        if (string.IsNullOrWhiteSpace(fisierSha256))
            return Rezultat.Esuat<SoldInitialStoc>(Eroare.Validare("Amprenta fișierului lipsește."));
        if (linii.Any(l => l.Cantitate <= 0 || l.Valoare < 0))
            return Rezultat.Esuat<SoldInitialStoc>(Eroare.Validare("Cantitățile trebuie să fie pozitive, iar valorile nu pot fi negative."));
        if (linii.Count == 0)
            return Rezultat.Esuat<SoldInitialStoc>(Eroare.Validare($"Gestiunea {gestiuneExterna} nu are niciun material cu stoc."));

        var comasate = linii
            .GroupBy(l => l.Articol.Id)
            .Select(g => new LinieSoldInitial(g.Key, g.First().Articol.Cod, g.First().Articol.Denumire, g.First().Articol.UnitateMasura,
                g.Sum(x => x.Cantitate), Rotunjire.Lei.Aplica(g.Sum(x => x.Valoare)), g.First().CodExtern))
            .OrderBy(l => l.Denumire, StringComparer.Ordinal)
            .ToList();
        return Rezultat.Ok(new SoldInitialStoc
        {
            An = an,
            Numar = numar,
            Data = data,
            GestiuneId = gestiune.Id,
            GestiuneDenumire = gestiune.Denumire,
            GestiuneExterna = Scurt(gestiuneExterna.Trim(), 50),
            Categorie = string.IsNullOrWhiteSpace(categorie) ? null : Scurt(categorie.Trim(), 100),
            NumeFisier = Scurt(numeFisier.Trim(), 260),
            FisierSha256 = fisierSha256,
            LiniiJson = JsonSerializer.Serialize(comasate),
            _linii = comasate,
            Valoare = comasate.Sum(l => l.Valoare),
            ImportatDeId = utilizatorId,
            ImportatDeNume = utilizatorNume.Trim(),
        });
    }

    private static string Scurt(string text, int max) => text.Length > max ? text[..max] : text;
}

/// <summary>
/// Anularea unui sold inițial preluat greșit (ex. balanța fără valori, înlocuită de raportul cu prețuri): stocul lui iese din
/// gestiune exact cum a intrat. Se poate doar cât timp materialele lui nu au alte mișcări în gestiune (recepții, transferuri,
/// consumuri, inventar); după aceea, diferențele se corectează prin inventar. Funcție pură.
/// </summary>
public static class AnulareSoldInitial
{
    /// <param name="solduri">Soldul curent al fiecărui material din document, în gestiunea lui.</param>
    /// <param name="cuAlteMiscari">Materialele care au în gestiune și alte mișcări decât soldurile inițiale.</param>
    /// <returns>Soldul rămas pentru fiecare linie.</returns>
    public static Rezultat<IReadOnlyList<(LinieSoldInitial Linie, Sold Ramas)>> Calculeaza(
        SoldInitialStoc sold, IReadOnlyDictionary<Guid, Sold> solduri, IReadOnlySet<Guid> cuAlteMiscari)
    {
        var blocate = sold.Linii.Where(l => cuAlteMiscari.Contains(l.ArticolId)).ToList();
        if (blocate.Count > 0)
            return Rezultat.Esuat<IReadOnlyList<(LinieSoldInitial, Sold)>>(Eroare.Validare(
                $"{sold.NumarAfisat} nu se mai poate anula: {blocate.Count} {(blocate.Count == 1 ? "material are" : "materiale au")} și alte mișcări în " +
                $"{sold.GestiuneDenumire} ({string.Join(", ", blocate.Take(3).Select(l => l.Denumire))}{(blocate.Count > 3 ? ", …" : string.Empty)}). " +
                "Corectează stocul prin inventar."));

        var rezultat = new List<(LinieSoldInitial, Sold)>();
        foreach (var l in sold.Linii)
        {
            var r = CalculStoc.Storno(solduri.GetValueOrDefault(l.ArticolId, Sold.Zero), l.Cantitate, l.Valoare, l.Denumire);
            if (r.Esec)
                return Rezultat.Esuat<IReadOnlyList<(LinieSoldInitial, Sold)>>(r.Eroare);
            rezultat.Add((l, r.Valoare));
        }

        return Rezultat.Ok<IReadOnlyList<(LinieSoldInitial, Sold)>>(rezultat);
    }
}

/// <summary>
/// Urma unui sold inițial anulat (documentul și mișcările lui se șterg): numărul lui nu se mai refolosește, iar lista
/// arată ce s-a anulat, de cine și când.
/// </summary>
public sealed class SoldInitialAnulat : EntitateFirma
{
    private SoldInitialAnulat() { }

    public Guid SoldInitialId { get; private set; }
    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"SI/{An}/{Numar:D4}";
    public DateOnly Data { get; private set; }
    public Guid GestiuneId { get; private set; }
    public string GestiuneDenumire { get; private set; } = string.Empty;
    public string? Categorie { get; private set; }
    public string NumeFisier { get; private set; } = string.Empty;
    public string LiniiJson { get; private set; } = "[]";
    public decimal Valoare { get; private set; }
    public Guid AnulatDeId { get; private set; }
    public string AnulatDeNume { get; private set; } = string.Empty;
    public DateTimeOffset AnulatLa { get; private set; }

    public static SoldInitialAnulat Din(SoldInitialStoc s, Guid utilizatorId, string utilizatorNume, DateTimeOffset moment) => new()
    {
        SoldInitialId = s.Id,
        An = s.An,
        Numar = s.Numar,
        Data = s.Data,
        GestiuneId = s.GestiuneId,
        GestiuneDenumire = s.GestiuneDenumire,
        Categorie = s.Categorie,
        NumeFisier = s.NumeFisier,
        LiniiJson = s.LiniiJson,
        Valoare = s.Valoare,
        AnulatDeId = utilizatorId,
        AnulatDeNume = utilizatorNume.Trim(),
        AnulatLa = moment,
    };
}
