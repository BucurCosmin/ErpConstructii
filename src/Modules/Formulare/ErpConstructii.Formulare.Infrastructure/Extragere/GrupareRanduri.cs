using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Formulare.Infrastructure.Extragere;

/// <summary>
/// Regrupează cuvintele cu poziții (OCR sau text PDF) pe rânduri vizuale: coloanele unui tabel pot fi citite ca
/// blocuri separate, așa că rândurile se refac după centrul vertical, cu toleranță de jumătate din înălțimea medie.
/// </summary>
internal static class GrupareRanduri
{
    public sealed record Cuvant(string Text, int X0, int X1, int Y0, int Y1, float Incredere);

    public static IEnumerable<RandText> PeRanduri(List<Cuvant> cuvinte, int pagina)
    {
        if (cuvinte.Count == 0)
        {
            yield break;
        }

        var inaltimeMedie = cuvinte.Average(c => c.Y1 - c.Y0);
        var toleranta = Math.Max(4, inaltimeMedie * 0.5);
        var randCurent = new List<Cuvant>();
        double centruRand = 0;

        foreach (var c in cuvinte.OrderBy(c => (c.Y0 + c.Y1) / 2.0))
        {
            var centru = (c.Y0 + c.Y1) / 2.0;
            if (randCurent.Count > 0 && Math.Abs(centru - centruRand) > toleranta)
            {
                yield return CaRand(randCurent, pagina);
                randCurent = [];
            }

            randCurent.Add(c);
            centruRand = randCurent.Average(x => (x.Y0 + x.Y1) / 2.0);
        }

        if (randCurent.Count > 0)
        {
            yield return CaRand(randCurent, pagina);
        }
    }

    private static RandText CaRand(List<Cuvant> cuvinte, int pagina)
    {
        var ordonate = cuvinte.OrderBy(c => c.X0).ToList();
        return new RandText(
            string.Join(' ', ordonate.Select(c => c.Text)),
            ordonate.Select(c => new CuvantText(c.Text, c.X0, c.X1, c.Incredere)).ToList(),
            pagina,
            cuvinte.Min(c => c.Y0),
            cuvinte.Max(c => c.Y1));
    }
}
