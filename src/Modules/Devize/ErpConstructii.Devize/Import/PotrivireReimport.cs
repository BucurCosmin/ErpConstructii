using System.Globalization;
using System.Text;

namespace ErpConstructii.Devize.Import;

public enum StareArticolReimport
{
    Neschimbat,
    Nou,
    Eliminat,
    CantitateSchimbata,
    PretSchimbat,
    CantitateSiPretSchimbate,
}

/// <summary>Un articol la reimport: starea față de versiunea existentă și valorile vechi / noi.</summary>
/// <param name="ResurseSchimbate">Consumurile diferă (coduri, consumuri unitare sau prețuri ale resurselor).</param>
public sealed record DiferentaArticol(
    StareArticolReimport Stare,
    string Simbol,
    string Denumire,
    string UnitateMasura,
    string? CodWbs,
    decimal? CantitateVeche,
    decimal? CantitateNoua,
    decimal? PretVechi,
    decimal? PretNou,
    bool ResurseSchimbate,
    bool PotrivitDupaDenumire);

/// <summary>Rezultatul potrivirii: diferențele, pe articol, și câte articole își păstrează cheia stabilă.</summary>
public sealed record RezultatReimport(IReadOnlyList<DiferentaArticol> Diferente, int Chei)
{
    public int Noi => Diferente.Count(d => d.Stare == StareArticolReimport.Nou);
    public int Eliminate => Diferente.Count(d => d.Stare == StareArticolReimport.Eliminat);
    public int Modificate => Diferente.Count(d => d.Stare is not (StareArticolReimport.Neschimbat or StareArticolReimport.Nou or StareArticolReimport.Eliminat) || d.ResurseSchimbate);
}

/// <summary>
/// Reimportul unei versiuni din programul de devize (docs/import-isdp.md §7.1): articolele noi se potrivesc cu cele ale
/// versiunii existente după (cod WBS, simbol), iar ce rămâne după (simbol, denumire normalizată, UM), pentru WBS-urile
/// renumerotate. Articolele potrivite PĂSTREAZĂ <see cref="ArticolDeviz.CheieStabila"/>, ca legăturile din grafic, comenzi,
/// consumuri și situații să nu se rupă. Logică pură (modifică doar cheile articolelor noi).
/// </summary>
public static class PotrivireReimport
{
    public static RezultatReimport Potriveste(IReadOnlyList<ArticolDeviz> noi, IReadOnlyList<ArticolDeviz> existente)
    {
        var libere = existente.ToList();
        var perechi = new List<(ArticolDeviz Nou, ArticolDeviz? Vechi, bool DupaDenumire)>();

        // 1. Același cod WBS și simbol.
        var faraPereche = new List<ArticolDeviz>();
        foreach (var n in noi)
        {
            var v = n.CodWbsSursa is null ? null : libere.FirstOrDefault(x => x.CodWbsSursa == n.CodWbsSursa && Simbol(x) == Simbol(n));
            if (v is null)
            {
                faraPereche.Add(n);
                continue;
            }

            libere.Remove(v);
            perechi.Add((n, v, false));
        }

        // 2. Același simbol, aceeași denumire normalizată și aceeași UM (WBS renumerotat).
        foreach (var n in faraPereche)
        {
            var v = libere.FirstOrDefault(x => Simbol(x) == Simbol(n) && Text(x.Denumire) == Text(n.Denumire) && Text(x.UnitateMasura) == Text(n.UnitateMasura));
            if (v is not null)
                libere.Remove(v);
            perechi.Add((n, v, v is not null));
        }

        var ordine = noi.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => x.i);
        var diferente = new List<DiferentaArticol>();
        foreach (var (n, v, dupaDenumire) in perechi.OrderBy(p => ordine[p.Nou]))
        {
            if (v is null)
            {
                diferente.Add(new(StareArticolReimport.Nou, n.Simbol, n.Denumire, n.UnitateMasura, n.CodWbsSursa, null, n.Cantitate, null, n.PretUnitar.Total, false, false));
                continue;
            }

            n.PastreazaCheia(v.CheieStabila);
            // Comparația la precizia păstrată în bază (sume 6 zecimale, consumuri 12): versiunea existentă vine din bază.
            var cantitate = Suma(n.Cantitate) != Suma(v.Cantitate);
            var pret = Suma(n.PretUnitar.Total) != Suma(v.PretUnitar.Total) || Suma(n.PretUnitar.Material) != Suma(v.PretUnitar.Material)
                       || Suma(n.PretUnitar.Manopera) != Suma(v.PretUnitar.Manopera) || Suma(n.PretUnitar.Utilaj) != Suma(v.PretUnitar.Utilaj)
                       || Suma(n.PretUnitar.Transport) != Suma(v.PretUnitar.Transport) || Suma(n.PretUnitar.Echipament) != Suma(v.PretUnitar.Echipament);
            var stare = (cantitate, pret) switch
            {
                (true, true) => StareArticolReimport.CantitateSiPretSchimbate,
                (true, false) => StareArticolReimport.CantitateSchimbata,
                (false, true) => StareArticolReimport.PretSchimbat,
                _ => StareArticolReimport.Neschimbat,
            };
            diferente.Add(new(stare, n.Simbol, n.Denumire, n.UnitateMasura, n.CodWbsSursa, v.Cantitate, n.Cantitate, v.PretUnitar.Total, n.PretUnitar.Total,
                ResurseDiferite(n, v), dupaDenumire));
        }

        diferente.AddRange(libere.Select(v => new DiferentaArticol(
            StareArticolReimport.Eliminat, v.Simbol, v.Denumire, v.UnitateMasura, v.CodWbsSursa, v.Cantitate, null, v.PretUnitar.Total, null, false, false)));
        return new RezultatReimport(diferente, perechi.Count(p => p.Vechi is not null));
    }

    private static bool ResurseDiferite(ArticolDeviz n, ArticolDeviz v)
    {
        static string Amprenta(ArticolDeviz a) => string.Join(";", a.Consumuri
            .OrderBy(c => c.CodResursa, StringComparer.Ordinal)
            .Select(c => $"{c.CodResursa.Trim().ToUpperInvariant()}={Consum(c.ConsumUnitar).ToString(CultureInfo.InvariantCulture)}@{Suma(c.PretUnitar).ToString(CultureInfo.InvariantCulture)}"));
        return Amprenta(n) != Amprenta(v);
    }

    /// <summary>Precizia coloanelor din bază: sume și cantități numeric(19,6), consumuri unitare numeric(24,12).</summary>
    private static decimal Suma(decimal v) => Math.Round(v, 6, MidpointRounding.AwayFromZero) / 1.000000000000000000000000000m;

    private static decimal Consum(decimal v) => Math.Round(v, 12, MidpointRounding.AwayFromZero) / 1.000000000000000000000000000m;

    private static string Simbol(ArticolDeviz a) => a.Simbol.Trim().ToUpperInvariant();

    /// <summary>Text comparabil: fără diacritice, majuscule, spații unice.</summary>
    private static string Text(string s)
    {
        var fd = (s ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(fd.Length);
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToUpperInvariant(ch));
        }

        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
