using System.Globalization;

namespace ErpConstructii.SharedKernel.Import;

/// <summary>Un document cu număr de pe hârtie (necesar de materiale, bon de consum), lângă numărul lui de sistem.</summary>
/// <param name="NumarSistem">Numărul dat automat de aplicație (NM/AAAA/NNNN, BC/AAAA/NNNN); nu se schimbă niciodată.</param>
/// <param name="NumarExtern">Numărul de pe documentul original; null = documentul nu are număr de hârtie.</param>
public sealed record DocumentCuNumarExtern(Guid Id, Guid ProiectId, string NumarSistem, string? NumarExtern, DateOnly Data, string Proiect);

/// <summary>Același număr de hârtie pe mai multe documente ale aceluiași proiect, din același an.</summary>
public sealed record NumarSuprapus(long Numar, IReadOnlyList<DocumentCuNumarExtern> Documente);

/// <summary>Verificarea numerelor de hârtie ale unui proiect dintr-un an (anul datei documentului).</summary>
/// <param name="Lipsa">Numerele sărite dintre primul și ultimul, ca intervale („5, 7–9”); cel mult <see cref="NumerotareExterna.MaximIntervale"/>.</param>
/// <param name="NumereLipsa">Câte numere lipsesc în total.</param>
/// <param name="FaraNumar">Documentele din an fără număr de hârtie (sau cu un număr fără cifre).</param>
/// <param name="UrmatorulLiber">Numărul propus pentru un document nou fără număr: după cel mai mare folosit.</param>
public sealed record VerificareNumereAn(
    int An,
    Guid ProiectId,
    string Proiect,
    int Documente,
    long? Primul,
    long? Ultimul,
    IReadOnlyList<NumarSuprapus> Suprapuneri,
    IReadOnlyList<string> Lipsa,
    long NumereLipsa,
    IReadOnlyList<DocumentCuNumarExtern> FaraNumar,
    long UrmatorulLiber);

/// <summary>
/// Numerele de pe hârtie ale documentelor importate (logică pură). Seria e pe proiect și pe an (anul datei documentului):
/// fiecare lucrare își numerotează documentele de la capăt, deci același număr pe proiecte diferite e firesc. Numărul se compară
/// după primul grup de cifre („Nr. 081/2026” = 81). Numărul de sistem al documentului nu intră aici:
/// e dat automat, unic, și nu se schimbă la renumerotare.
/// </summary>
public static class NumerotareExterna
{
    public const int MaximIntervale = 60;

    /// <summary>Valoarea numerică a numărului de hârtie: primul grup de cifre („NM 081/2026” → 81); null fără cifre.</summary>
    public static long? Valoare(string? numar)
    {
        if (string.IsNullOrWhiteSpace(numar))
            return null;
        var inceput = -1;
        for (var i = 0; i < numar.Length; i++)
        {
            if (char.IsAsciiDigit(numar[i]))
            {
                inceput = i;
                break;
            }
        }

        if (inceput < 0)
            return null;
        var sfarsit = inceput;
        while (sfarsit < numar.Length && char.IsAsciiDigit(numar[sfarsit]))
            sfarsit++;
        var cifre = numar[inceput..sfarsit].TrimStart('0');
        if (cifre.Length == 0)
            return 0;
        return cifre.Length <= 15 ? long.Parse(cifre, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Primul număr liber din serie (proiect + an): după cel mai mare număr folosit (1 dacă nu are niciunul).</summary>
    public static long NumarLiber(IEnumerable<string?> numereDinAn) =>
        (numereDinAn.Select(Valoare).Where(v => v is > 0).Max() ?? 0) + 1;

    /// <summary>Documentele aceluiași proiect, din același an, care folosesc deja numărul (în afara celui renumerotat).</summary>
    public static IReadOnlyList<DocumentCuNumarExtern> Folosit(
        string? numar, Guid proiectId, int an, IEnumerable<DocumentCuNumarExtern> existente, Guid? exceptand = null)
    {
        var valoare = Valoare(numar);
        return valoare is null
            ? []
            : existente.Where(d => d.ProiectId == proiectId && d.Data.Year == an && d.Id != exceptand && Valoare(d.NumarExtern) == valoare).ToList();
    }

    /// <summary>Verificarea pe serii (proiect + an): anul cel mai recent întâi, apoi proiectele după cod.</summary>
    public static IReadOnlyList<VerificareNumereAn> Verifica(IEnumerable<DocumentCuNumarExtern> documente) =>
        documente.GroupBy(d => (d.Data.Year, d.ProiectId))
            .Select(g => VerificaSerie(g.Key.Year, g.Key.ProiectId, g.ToList()))
            .OrderByDescending(v => v.An).ThenBy(v => v.Proiect, StringComparer.Ordinal)
            .ToList();

    private static VerificareNumereAn VerificaSerie(int an, Guid proiectId, IReadOnlyList<DocumentCuNumarExtern> documente)
    {
        var cuNumar = documente.Select(d => (Document: d, Valoare: Valoare(d.NumarExtern))).Where(x => x.Valoare is > 0)
            .Select(x => (x.Document, Valoare: x.Valoare!.Value)).ToList();
        var faraNumar = documente.Where(d => Valoare(d.NumarExtern) is not > 0).OrderBy(d => d.Data).ThenBy(d => d.NumarSistem, StringComparer.Ordinal).ToList();

        var suprapuneri = cuNumar.GroupBy(x => x.Valoare).Where(g => g.Count() > 1).OrderBy(g => g.Key)
            .Select(g => new NumarSuprapus(g.Key, g.Select(x => x.Document).OrderBy(d => d.Data).ThenBy(d => d.NumarSistem, StringComparer.Ordinal).ToList()))
            .ToList();

        var folosite = cuNumar.Select(x => x.Valoare).Distinct().Order().ToList();
        var lipsa = new List<string>();
        long numereLipsa = 0;
        for (var i = 1; i < folosite.Count; i++)
        {
            var (de, panaLa) = (folosite[i - 1] + 1, folosite[i] - 1);
            if (de > panaLa)
                continue;
            numereLipsa += panaLa - de + 1;
            if (lipsa.Count < MaximIntervale)
                lipsa.Add(de == panaLa ? de.ToString(CultureInfo.InvariantCulture) : $"{de}–{panaLa}");
        }

        return new VerificareNumereAn(
            an,
            proiectId,
            documente[0].Proiect,
            documente.Count,
            folosite.Count == 0 ? null : folosite[0],
            folosite.Count == 0 ? null : folosite[^1],
            suprapuneri,
            lipsa,
            numereLipsa,
            faraNumar,
            (folosite.Count == 0 ? 0 : folosite[^1]) + 1);
    }
}
