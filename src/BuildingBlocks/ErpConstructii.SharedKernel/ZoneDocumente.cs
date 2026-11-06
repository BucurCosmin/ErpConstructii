using System.Globalization;
using System.Text;

namespace ErpConstructii.SharedKernel.Import;

/// <summary>
/// Documentele mici tipărite câte mai multe pe o pagină. Bonul de consum (formularul GC-087) se tipărește câte două pe o
/// foaie A4 (sus și jos): fiecare începe cu rândul „UNITATEA” al antetului. Zonele se folosesc la împărțirea fișierului
/// (<see cref="ImpartireDocumente"/>) și de interpretorul bonului, deci în aceeași ordine. Funcție pură.
/// </summary>
public static class ZoneDocumente
{
    /// <summary>Rândurile fiecărui bon, în ordinea din document. Fără rândul „UNITATEA”: o singură zonă, tot documentul.</summary>
    public static IReadOnlyList<IReadOnlyList<RandText>> Bonuri(IEnumerable<RandText> randuri)
    {
        var zone = new List<List<RandText>>();
        foreach (var pagina in randuri.GroupBy(r => r.Pagina))
        {
            var lista = pagina.ToList();
            var inceputuri = lista.Select((r, i) => (r, i)).Where(x => EsteInceputBon(x.r)).Select(x => x.i).ToList();
            if (inceputuri.Count == 0)
            {
                // Pagină fără antet de bon: continuarea bonului anterior (sau primul bon, dacă nu există încă).
                if (zone.Count == 0)
                {
                    zone.Add([]);
                }

                zone[^1].AddRange(lista);
                continue;
            }

            for (var k = 0; k < inceputuri.Count; k++)
            {
                // Rândurile de deasupra primului antet de pe pagină (ex. „formular cod GC-087”) țin de primul bon al paginii.
                var de = k == 0 ? 0 : inceputuri[k];
                var pana = k + 1 < inceputuri.Count ? inceputuri[k + 1] : lista.Count;
                zone.Add(lista.GetRange(de, pana - de));
            }
        }

        return zone;
    }

    /// <summary>
    /// Numărul bonului: numărul singur din coloana „NUMĂR DOCUMENT” (sub etichetă, la stânga coloanei „DATA”), ex. „56”.
    /// </summary>
    public static string? NumarBon(IReadOnlyList<RandText> zona)
    {
        var iEticheta = -1;
        var xEticheta = 0;
        for (var i = 0; i < zona.Count && iEticheta < 0; i++)
        {
            var c = zona[i].Cuvinte.FirstOrDefault(w => Normalizeaza(w.Text).StartsWith("NUMAR", StringComparison.Ordinal));
            if (c is not null && Normalizeaza(zona[i].Text).Contains("DOCUMENT", StringComparison.Ordinal))
            {
                (iEticheta, xEticheta) = (i, c.X0);
            }
        }

        if (iEticheta < 0)
        {
            return null;
        }

        // Limita din dreapta: coloana „DATA” din antet, lângă eticheta numărului (nu „DATA ȘI SEMNĂTURA” din subsol);
        // numerele din dreapta ei sunt ziua / luna / anul sau zgomot OCR.
        var xData = zona.Skip(iEticheta).Take(3).SelectMany(r => r.Cuvinte)
            .Where(w => Normalizeaza(w.Text).TrimEnd(':') == "DATA" && w.X0 > xEticheta + 300)
            .Select(w => w.X0).DefaultIfEmpty(int.MaxValue).Min();
        foreach (var rand in zona.Skip(iEticheta).Take(6))
        {
            var numar = rand.Cuvinte.FirstOrDefault(w => w.X0 >= xEticheta && w.X0 < xData && w.Text.Length is >= 1 and <= 7 && w.Text.All(char.IsAsciiDigit));
            if (numar is not null)
            {
                return numar.Text;
            }
        }

        return null;
    }

    private static bool EsteInceputBon(RandText r) => Normalizeaza(r.Text).StartsWith("UNITATEA", StringComparison.Ordinal);

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

        return sb.ToString().Trim();
    }
}
