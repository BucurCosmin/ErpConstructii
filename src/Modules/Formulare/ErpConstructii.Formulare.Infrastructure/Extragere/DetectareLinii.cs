using ErpConstructii.SharedKernel.Import;
using SkiaSharp;
using UglyToad.PdfPig.Content;

namespace ErpConstructii.Formulare.Infrastructure.Extragere;

/// <summary>
/// Liniile orizontale ale tabelelor de pe o pagină (bordurile rândurilor): interpretorii le folosesc ca să știe ce rânduri
/// de text stau în același rând fizic al tabelului (ex. o denumire scrisă pe două rânduri). Din scanare: segmentele lungi
/// de pixeli închiși, unite peste mici întreruperi și o ușoară înclinare a paginii. Din PDF digital: desenele subțiri și late.
/// </summary>
internal static class DetectareLinii
{
    /// <summary>O linie de tabel e lungă: cel puțin atât din lățimea paginii.</summary>
    private const double LungimeMinima = 0.25;

    /// <summary>Segment orizontal (pixeli sau puncte la scara de 300 DPI).</summary>
    public readonly record struct Segment(int X0, int X1, int Y);

    /// <summary>Liniile dintr-o imagine (pagina randată la 300 DPI sau scanarea), în pixelii ei.</summary>
    public static List<LinieOrizontala> DinImagine(byte[] imagine, int pagina)
    {
        using var original = SKBitmap.Decode(imagine);
        if (original is null)
        {
            return [];
        }

        using var gri = original.Copy(SKColorType.Gray8);
        if (gri is null)
        {
            return [];
        }

        var latime = gri.Width;
        var pixeli = gri.GetPixelSpan();
        var pas = gri.RowBytes;
        // Un segment de linie e o porțiune neîntreruptă de pixeli închiși, mai lungă decât orice cuvânt (≈ 7 mm la 300 DPI).
        // La o pagină înclinată, o linie subțire trece pe rândul următor de pixeli după câteva sute de pixeli: segmentele
        // se unesc mai jos.
        var lungimeSegment = Math.Max(60, latime / 30);
        var segmente = new List<Segment>();
        for (var y = 0; y < gri.Height; y++)
        {
            var rand = pixeli.Slice(y * pas, latime);
            var inceput = -1;
            for (var x = 0; x <= latime; x++)
            {
                var inchis = x < latime && rand[x] < 140;
                if (inchis && inceput < 0)
                {
                    inceput = x;
                }
                else if (!inchis && inceput >= 0)
                {
                    if (x - inceput >= lungimeSegment)
                    {
                        segmente.Add(new Segment(inceput, x - 1, y));
                    }

                    inceput = -1;
                }
            }
        }

        return Uneste(segmente, latime, pagina, golMaxim: 40, abatereMaxima: 6);
    }

    /// <summary>
    /// Liniile dintr-o pagină PDF digitală: dreptunghiurile / segmentele desenate foarte subțiri (≤ 2 pt) și late, aduse la
    /// scara de 300 DPI cu decalajul paginii (paginile stau una sub alta, ca rândurile de text).
    /// </summary>
    public static List<LinieOrizontala> DinPaginaPdf(Page pagina, double scara, int decalaj)
    {
        var segmente = new List<Segment>();
        foreach (var cale in pagina.Paths)
        {
            foreach (var parte in cale)
            {
                if (parte.GetBoundingRectangle() is not { } d || d.Height > 2 || d.Width < 20)
                {
                    continue;
                }

                var y = decalaj + (int)((pagina.Height - (d.Top + d.Bottom) / 2) * scara);
                segmente.Add(new Segment((int)(d.Left * scara), (int)(d.Right * scara), y));
            }
        }

        // Bordurile fiecărei celule sunt des desenate separat: se unesc pe orizontală.
        return Uneste(segmente, (int)(pagina.Width * scara), pagina.Number, golMaxim: 15, abatereMaxima: 4);
    }

    /// <summary>
    /// Unește segmentele aceleiași linii: apropiate pe orizontală (sau suprapuse) și la aceeași înălțime față de linia
    /// strânsă până atunci (înălțimea calculată în dreptul segmentului, ca la o pagină înclinată). O linie groasă dă
    /// segmente pe mai multe rânduri de pixeli, care ajung tot în ea. Rămân liniile lungi.
    /// </summary>
    internal static List<LinieOrizontala> Uneste(List<Segment> segmente, int latimePagina, int pagina, int golMaxim, int abatereMaxima)
    {
        var linii = new List<(int X0, int Y0, int X1, int Y1)>();
        foreach (var s in segmente.OrderBy(s => s.Y).ThenBy(s => s.X0))
        {
            var centru = (s.X0 + s.X1) / 2.0;
            var gasit = -1;
            for (var i = linii.Count - 1; i >= 0; i--)
            {
                var l = linii[i];
                if (s.Y - Math.Max(l.Y0, l.Y1) > abatereMaxima * 3)
                {
                    break; // liniile de mai sus nu mai pot primi segmentul (sunt ordonate după Y)
                }

                var yLa = new LinieOrizontala(pagina, l.X0, l.Y0, l.X1, l.Y1).YLa(centru);
                if (s.X0 <= l.X1 + golMaxim && s.X1 >= l.X0 - golMaxim && Math.Abs(s.Y - yLa) <= abatereMaxima)
                {
                    gasit = i;
                    break;
                }
            }

            if (gasit < 0)
            {
                linii.Add((s.X0, s.Y, s.X1, s.Y));
                continue;
            }

            var (x0, y0, x1, y1) = linii[gasit];
            if (s.X0 < x0)
            {
                (x0, y0) = (s.X0, s.Y);
            }

            if (s.X1 > x1)
            {
                (x1, y1) = (s.X1, s.Y);
            }

            linii[gasit] = (x0, y0, x1, y1);
        }

        // Bucățile aceleiași linii care au pornit separat (ex. o linie ușor frântă la scanare) se unesc la final.
        for (var unite = true; unite;)
        {
            unite = false;
            for (var i = 0; i < linii.Count && !unite; i++)
            {
                for (var j = i + 1; j < linii.Count && !unite; j++)
                {
                    var (a, b) = (linii[i], linii[j]);
                    if (a.X0 > b.X1 + golMaxim || b.X0 > a.X1 + golMaxim)
                    {
                        continue;
                    }

                    var x = (Math.Max(a.X0, b.X0) + Math.Min(a.X1, b.X1)) / 2.0;
                    var ya = new LinieOrizontala(pagina, a.X0, a.Y0, a.X1, a.Y1).YLa(x);
                    var yb = new LinieOrizontala(pagina, b.X0, b.Y0, b.X1, b.Y1).YLa(x);
                    if (Math.Abs(ya - yb) > abatereMaxima)
                    {
                        continue;
                    }

                    var (x0, y0) = a.X0 <= b.X0 ? (a.X0, a.Y0) : (b.X0, b.Y0);
                    var (x1, y1) = a.X1 >= b.X1 ? (a.X1, a.Y1) : (b.X1, b.Y1);
                    linii[i] = (x0, y0, x1, y1);
                    linii.RemoveAt(j);
                    unite = true;
                }
            }
        }

        return linii
            .Where(l => l.X1 - l.X0 >= latimePagina * LungimeMinima)
            .Select(l => new LinieOrizontala(pagina, l.X0, l.Y0, l.X1, l.Y1))
            .OrderBy(l => (l.Y0 + l.Y1) / 2.0)
            .ToList();
    }
}
