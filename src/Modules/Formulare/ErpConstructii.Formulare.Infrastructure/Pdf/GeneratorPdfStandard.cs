using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace ErpConstructii.Formulare.Infrastructure.Pdf;

/// <summary>
/// Formatul standard al documentelor (NIR, bon de consum, bon de transfer…), randat direct în PDF cu MigraDoc / PDFsharp
/// (MIT, fără servicii externe). E folosit până când firma încarcă propriul șablon Word pentru tipul de formular.
/// </summary>
public sealed class GeneratorPdfStandard : IGeneratorPdfStandard
{
    private const string Font = ResolverFonturi.Familie;
    private static readonly Color Accent = new(0x1f, 0x3a, 0x5f);
    private static readonly Color Gri = new(0xee, 0xf1, 0xf5);

    static GeneratorPdfStandard() => ResolverFonturi.Inregistreaza();

    public byte[] Genereaza(AntetFirma firma, AspectDocument aspect, DateFormular date)
    {
        var doc = new Document();
        doc.Info.Title = $"{aspect.Titlu} {aspect.NumarSiData}";
        doc.Info.Author = firma.Denumire;
        var stil = doc.Styles[StyleNames.Normal]!;
        stil.Font.Name = Font;
        stil.Font.Size = 9;

        var sectiune = doc.AddSection();
        var pagina = sectiune.PageSetup;
        pagina.PageFormat = PageFormat.A4;
        pagina.Orientation = aspect.Peisaj ? Orientation.Landscape : Orientation.Portrait;
        pagina.TopMargin = pagina.BottomMargin = Unit.FromCentimeter(1.5);
        pagina.LeftMargin = pagina.RightMargin = Unit.FromCentimeter(1.5);
        var latime = (aspect.Peisaj ? 29.7 : 21.0) - 3.0;

        Antet(sectiune, firma);
        Titlu(sectiune, aspect);
        CampuriAntet(sectiune, aspect, date, latime);
        Tabel(sectiune, aspect, date, latime);
        Totaluri(sectiune, aspect, date, latime);

        if (aspect.CampObservatii is { } obs && date.Campuri.TryGetValue(obs, out var observatii) && !string.IsNullOrWhiteSpace(observatii))
        {
            var p = sectiune.AddParagraph();
            p.Format.SpaceBefore = Unit.FromPoint(8);
            p.AddFormattedText("Observații: ", TextFormat.Bold);
            p.AddText(observatii);
        }

        Semnaturi(sectiune, aspect, latime);
        Subsol(sectiune);

        var randare = new PdfDocumentRenderer { Document = doc };
        randare.RenderDocument();
        using var ms = new MemoryStream();
        randare.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    private static void Antet(Section s, AntetFirma f)
    {
        var p = s.AddParagraph();
        p.AddFormattedText(f.Denumire, new Font(Font, 11) { Bold = true, Color = Accent });
        var detalii = new[] { f.Cui is null ? null : $"CUI: {f.Cui}", f.NrRegCom is null ? null : $"Reg. Com.: {f.NrRegCom}", f.Adresa }
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (detalii.Count > 0)
        {
            var d = s.AddParagraph(string.Join("  ·  ", detalii));
            d.Format.Font.Size = 8;
            d.Format.Font.Color = Colors.DimGray;
        }

        var linie = s.AddParagraph();
        linie.Format.Borders.Bottom = new Border { Width = 0.75, Color = Accent };
        linie.Format.SpaceAfter = Unit.FromPoint(10);
    }

    private static void Titlu(Section s, AspectDocument a)
    {
        var t = s.AddParagraph();
        t.Format.Alignment = ParagraphAlignment.Center;
        t.AddFormattedText(a.Titlu.ToUpperInvariant(), new Font(Font, 14) { Bold = true, Color = Accent });
        var n = s.AddParagraph(a.NumarSiData);
        n.Format.Alignment = ParagraphAlignment.Center;
        n.Format.Font.Size = 10;
        n.Format.Font.Bold = true;
        if (a.Cod is { } cod)
        {
            var c = s.AddParagraph($"(cod {cod})");
            c.Format.Alignment = ParagraphAlignment.Center;
            c.Format.Font.Size = 7;
            c.Format.Font.Color = Colors.Gray;
        }

        s.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
    }

    /// <summary>Câmpurile de antet pe două coloane: eticheta (gri) și valoarea.</summary>
    private static void CampuriAntet(Section s, AspectDocument a, DateFormular d, double latime)
    {
        var elemente = a.Antet.Where(e => d.Campuri.TryGetValue(e.Camp, out var v) && !string.IsNullOrWhiteSpace(v)).ToList();
        if (elemente.Count == 0)
            return;

        var t = s.AddTable();
        t.Borders.Visible = false;
        var latEticheta = Unit.FromCentimeter(latime * 0.16);
        var latValoare = Unit.FromCentimeter(latime * 0.34);
        foreach (var w in new[] { latEticheta, latValoare, latEticheta, latValoare })
            t.AddColumn(w);

        for (var i = 0; i < elemente.Count; i += 2)
        {
            var r = t.AddRow();
            r.TopPadding = r.BottomPadding = Unit.FromPoint(1.5);
            for (var k = 0; k < 2 && i + k < elemente.Count; k++)
            {
                var e = elemente[i + k];
                var et = r.Cells[k * 2].AddParagraph(e.Eticheta);
                et.Format.Font.Color = Colors.DimGray;
                var valoare = r.Cells[k * 2 + 1].AddParagraph(d.Campuri[e.Camp]);
                valoare.Format.Font.Bold = true;
                valoare.Format.RightIndent = Unit.FromPoint(10); // valorile lungi nu ating eticheta din coloana vecină
            }
        }

        s.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static void Tabel(Section s, AspectDocument a, DateFormular d, double latime)
    {
        var total = a.Coloane.Sum(c => c.Latime);
        var t = s.AddTable();
        t.Borders.Width = 0.5;
        t.Borders.Color = Colors.Gray;
        t.Rows.LeftIndent = 0;
        foreach (var c in a.Coloane)
            t.AddColumn(Unit.FromCentimeter(latime * c.Latime / total));

        var antet = t.AddRow();
        antet.HeadingFormat = true; // se repetă pe fiecare pagină
        antet.Shading.Color = Gri;
        antet.Format.Font.Bold = true;
        antet.Format.Font.Size = 8;
        antet.VerticalAlignment = VerticalAlignment.Center;
        for (var i = 0; i < a.Coloane.Count; i++)
        {
            var p = antet.Cells[i].AddParagraph(a.Coloane[i].Eticheta);
            p.Format.Alignment = ParagraphAlignment.Center;
        }

        var randuri = d.Tabele.TryGetValue(a.Tabel, out var rr) ? rr : [];
        foreach (var rand in randuri)
        {
            var r = t.AddRow();
            r.TopPadding = r.BottomPadding = Unit.FromPoint(1.5);
            for (var i = 0; i < a.Coloane.Count; i++)
            {
                var c = a.Coloane[i];
                var p = r.Cells[i].AddParagraph(rand.TryGetValue(c.Camp, out var v) ? v : string.Empty);
                p.Format.Alignment = c.Aliniere switch
                {
                    AliniereColoana.Dreapta => ParagraphAlignment.Right,
                    AliniereColoana.Centru => ParagraphAlignment.Center,
                    _ => ParagraphAlignment.Left,
                };
            }
        }
    }

    private static void Totaluri(Section s, AspectDocument a, DateFormular d, double latime)
    {
        var elemente = a.Totaluri.Where(e => d.Campuri.TryGetValue(e.Camp, out var v) && !string.IsNullOrWhiteSpace(v)).ToList();
        if (elemente.Count == 0)
            return;

        var t = s.AddTable();
        t.Borders.Visible = false;
        t.AddColumn(Unit.FromCentimeter(latime * 0.50));
        t.AddColumn(Unit.FromCentimeter(latime * 0.35));
        t.AddColumn(Unit.FromCentimeter(latime * 0.15));
        foreach (var e in elemente)
        {
            var r = t.AddRow();
            r.Cells[1].AddParagraph(e.Eticheta).Format.Alignment = ParagraphAlignment.Right;
            var v = r.Cells[2].AddParagraph(d.Campuri[e.Camp]);
            v.Format.Alignment = ParagraphAlignment.Right;
            v.Format.Font.Bold = true;
        }
    }

    private static void Semnaturi(Section s, AspectDocument a, double latime)
    {
        if (a.Semnaturi.Count == 0)
            return;

        s.AddParagraph().Format.SpaceAfter = Unit.FromPoint(18);
        var t = s.AddTable();
        t.Borders.Visible = false;
        t.KeepTogether = true;
        foreach (var _ in a.Semnaturi)
            t.AddColumn(Unit.FromCentimeter(latime / a.Semnaturi.Count));
        var r = t.AddRow();
        var linie = t.AddRow();
        linie.Height = Unit.FromCentimeter(1.4);
        for (var i = 0; i < a.Semnaturi.Count; i++)
        {
            var p = r.Cells[i].AddParagraph(a.Semnaturi[i]);
            p.Format.Alignment = ParagraphAlignment.Center;
            p.Format.Font.Bold = true;
            var sem = linie.Cells[i].AddParagraph("Semnătura ______________________");
            sem.Format.Alignment = ParagraphAlignment.Center;
            sem.Format.SpaceBefore = Unit.FromCentimeter(0.9);
            sem.Format.Font.Color = Colors.Gray;
        }
    }

    private static void Subsol(Section s)
    {
        var p = s.Footers.Primary.AddParagraph();
        p.Format.Font.Size = 7;
        p.Format.Font.Color = Colors.Gray;
        p.AddText($"Generat de ERP Construcții la {DateTime.Now:dd.MM.yyyy HH:mm}  ·  Pagina ");
        p.AddPageField();
        p.AddText(" din ");
        p.AddNumPagesField();
        p.Format.Alignment = ParagraphAlignment.Center;
    }
}

/// <summary>
/// Fonturile pentru PDF (PDFsharp nu are acces implicit la fonturile sistemului): Arial din Windows, altfel DejaVu Sans
/// (Linux); ambele au diacriticele românești (ă, â, î, ș, ț).
/// </summary>
internal sealed class ResolverFonturi : IFontResolver
{
    public const string Familie = "ErpSans";
    private static readonly object Blocare = new();
    private static bool _inregistrat;

    private static readonly string[][] Candidati =
    [
        [@"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf", @"C:\Windows\Fonts\ariali.ttf", @"C:\Windows\Fonts\arialbi.ttf"],
        ["/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
         "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-BoldOblique.ttf"],
    ];

    public static void Inregistreaza()
    {
        lock (Blocare)
        {
            if (_inregistrat)
                return;
            GlobalFontSettings.FontResolver = new ResolverFonturi();
            _inregistrat = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new($"{Familie}|{(bold ? 1 : 0)}{(italic ? 1 : 0)}");

    public byte[]? GetFont(string faceName)
    {
        var stil = faceName.Split('|')[1] switch { "10" => 1, "01" => 2, "11" => 3, _ => 0 };
        var set = Candidati.FirstOrDefault(c => File.Exists(c[0]))
                  ?? throw new InvalidOperationException("Nu găsesc fonturile pentru PDF (Arial în Windows sau DejaVu Sans în Linux: apt install fonts-dejavu-core).");
        return File.ReadAllBytes(File.Exists(set[stil]) ? set[stil] : set[0]);
    }
}
