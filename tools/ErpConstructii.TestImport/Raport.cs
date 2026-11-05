using System.Globalization;
using System.Net;
using System.Text;
using ErpConstructii.SharedKernel.Import;
using PDFtoImage;

namespace ErpConstructii.TestImport;

/// <summary>
/// Raportul HTML: pentru fiecare fișier, paginile (imagini) lângă ce s-a citit; câmpurile diferite de valorile confirmate
/// sunt roșii, cele marcate „de verificat” de interpretor portocalii. Un singur fișier, fără dependențe, deschis în browser.
/// </summary>
public static class Raport
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static void Scrie(string cale, List<RezultatFisier> rezultate, Optiuni optiuni)
    {
        var h = new StringBuilder();
        h.Append("""
            <!DOCTYPE html><html lang="ro"><head><meta charset="utf-8"><title>Test import documente</title><style>
            body{font:14px system-ui,Segoe UI,sans-serif;margin:0;background:#f4f6f8;color:#1f2933}
            header{background:#14213d;color:#fff;padding:16px 24px}header h1{margin:0;font-size:20px}
            main{padding:16px 24px}.sumar a{margin-right:10px;text-decoration:none}
            .fisier{background:#fff;border-radius:8px;margin:16px 0;padding:16px;box-shadow:0 1px 3px #0002}
            .fisier h2{margin:0 0 12px;font-size:17px}.grila{display:grid;grid-template-columns:minmax(260px,32%) 1fr;gap:16px}
            .pagini img{width:100%;border:1px solid #ccd;margin-bottom:6px}.pagini figcaption{font-size:12px;color:#667}
            table{border-collapse:collapse;width:100%;margin:6px 0 14px}td,th{border:1px solid #dde;padding:4px 6px;text-align:left;vertical-align:top}
            th{background:#eef1f5;font-weight:600}td.n{text-align:right;font-variant-numeric:tabular-nums}
            .ok{color:#237804}.dif{background:#fff1f0}.dif .conf{color:#a8071a;font-weight:600}.nesigur{background:#fff7e6}
            .eticheta{display:inline-block;padding:1px 8px;border-radius:10px;font-size:12px;font-weight:600}
            .e-ok{background:#d9f7be;color:#237804}.e-dif{background:#ffccc7;color:#a8071a}.e-nec{background:#e6e6e6;color:#444}
            .doc{border-left:4px solid #2f54eb;padding-left:10px;margin-bottom:18px}.doc h3{margin:4px 0;font-size:15px}
            .probleme{color:#ad4e00;font-size:13px;margin:4px 0 8px}details summary{cursor:pointer;color:#2f54eb}
            </style></head><body>
            """);
        h.Append(CultureInfo.InvariantCulture, $"<header><h1>Test import documente — {DateTime.Now:dd.MM.yyyy HH:mm}</h1>");
        h.Append(CultureInfo.InvariantCulture, $"<div>Firma (cumpărătorul): {Enc(optiuni.CuiFirma)} · {rezultate.Count} fișiere</div></header><main>");

        h.Append("<p class=\"sumar\">");
        foreach (var r in rezultate)
        {
            h.Append(CultureInfo.InvariantCulture, $"<a href=\"#{Id(r.Nume)}\">{Stare(r)} {Enc(r.Nume)}</a> ");
        }

        h.Append("</p><p>Legendă: <span class=\"dif\">&nbsp;roșu&nbsp;</span> = diferit de valoarea confirmată · ")
         .Append("<span class=\"nesigur\">&nbsp;portocaliu&nbsp;</span> = interpretorul l-a marcat „de verificat” / „de completat”. ")
         .Append("Confirmarea unui fișier verificat: <code>dotnet run --project tools/ErpConstructii.TestImport -- --confirma &lt;nume&gt;</code>, ")
         .Append("apoi corecturile manuale în <code>Sabloane/_asteptat/&lt;nume&gt;.json</code>.</p>");

        foreach (var r in rezultate)
        {
            Fisier(h, r);
        }

        h.Append("</main></body></html>");
        Directory.CreateDirectory(Path.GetDirectoryName(cale)!);
        File.WriteAllText(cale, h.ToString());
    }

    private static void Fisier(StringBuilder h, RezultatFisier r)
    {
        h.Append(CultureInfo.InvariantCulture, $"<section class=\"fisier\" id=\"{Id(r.Nume)}\"><h2>{Stare(r)} {Enc(r.Nume)}</h2>");
        if (r.Citit.Eroare is not null)
        {
            h.Append(CultureInfo.InvariantCulture, $"<p class=\"probleme\">{Enc(r.Citit.Eroare)}</p></section>");
            return;
        }

        h.Append("<div class=\"grila\"><div class=\"pagini\">");
        foreach (var (pagina, imagine) in Imagini(r.Cale))
        {
            var tip = r.Citit.Pagini.FirstOrDefault(p => p.Pagina == pagina)?.Descriere ?? string.Empty;
            h.Append(CultureInfo.InvariantCulture, $"<figure style=\"margin:0 0 10px\"><figcaption><b>p. {pagina}</b> · {Enc(tip)}</figcaption>")
             .Append(CultureInfo.InvariantCulture, $"<img loading=\"lazy\" src=\"data:image/jpeg;base64,{Convert.ToBase64String(imagine)}\"></figure>");
        }

        h.Append("</div><div>");
        if (r.Diferente is { Count: > 0 } && r.Citit.Fisier.Documente.Count != r.Confirmat!.Documente.Count)
        {
            h.Append(CultureInfo.InvariantCulture, $"<p class=\"probleme\">Documente citite: {Enc(string.Join(" | ", r.Citit.Fisier.Documente.Select(Comparare.Descriere)))}<br>")
             .Append(CultureInfo.InvariantCulture, $"Documente confirmate: {Enc(string.Join(" | ", r.Confirmat.Documente.Select(Comparare.Descriere)))}</p>");
        }

        foreach (var (d, i) in r.Citit.Fisier.Documente.Select((d, i) => (d, i)))
        {
            var conf = r.Confirmat?.Documente.ElementAtOrDefault(i);
            Document(h, d, conf);
        }

        if (r.Citit.Text is { } text)
        {
            h.Append("<details><summary>Textul citit (rânduri, cu poziția X a cuvintelor)</summary><pre style=\"font-size:11px;white-space:pre-wrap\">");
            foreach (var rand in text.Randuri)
            {
                h.Append(CultureInfo.InvariantCulture, $"[p{rand.Pagina}:{rand.Y0}] ")
                 .Append(Enc(rand.Cuvinte.Count > 0 ? string.Join(' ', rand.Cuvinte.Select(c => $"{c.Text}@{c.X0}")) : rand.Text)).Append('\n');
            }

            h.Append("</pre></details>");
        }

        h.Append("</div></div></section>");
    }

    private static void Document(StringBuilder h, DocumentCitit d, DocumentCitit? c)
    {
        h.Append(CultureInfo.InvariantCulture, $"<div class=\"doc\"><h3>{Enc(d.Tip)} · p. {string.Join(", ", d.Pagini)}</h3>");
        if (d.Probleme.Count > 0)
        {
            h.Append("<div class=\"probleme\">").Append(string.Join("<br>", d.Probleme.Select(Enc))).Append("</div>");
        }

        h.Append("<table><tr><th>Câmp</th><th>Citit</th><th>Confirmat</th></tr>");
        Rand(h, "Număr", "numar", d, d.Numar, c?.Numar);
        Rand(h, "Data", "data", d, d.Data?.ToString("dd.MM.yyyy", Ro), c?.Data?.ToString("dd.MM.yyyy", Ro));
        Rand(h, "Furnizor", "furnizor", d, d.Furnizor, c?.Furnizor);
        Rand(h, "CUI furnizor", "furnizorCui", d, d.FurnizorCui, c?.FurnizorCui);
        Rand(h, "Lucrare", "lucrare", d, d.Lucrare, c?.Lucrare);
        Rand(h, "Total fără TVA", "totalFaraTva", d, Bani(d.TotalFaraTva), Bani(c?.TotalFaraTva));
        Rand(h, "TVA", "totalTva", d, Bani(d.TotalTva), Bani(c?.TotalTva));
        Rand(h, "Total de plată", "totalDePlata", d, Bani(d.TotalDePlata), Bani(c?.TotalDePlata));
        h.Append("</table>");

        var linii = d.Linii ?? [];
        if (linii.Count == 0 && c?.Linii is not { Count: > 0 })
        {
            h.Append("</div>");
            return;
        }

        h.Append("<table><tr><th>#</th><th>Denumire</th><th>UM</th><th>Cantitate</th><th>Preț</th><th>Valoare</th><th>Cota</th></tr>");
        var n = Math.Max(linii.Count, c?.Linii?.Count ?? 0);
        for (var i = 0; i < n; i++)
        {
            var x = linii.ElementAtOrDefault(i);
            var y = c?.Linii?.ElementAtOrDefault(i);
            var dif = y is not null && (x is null || !LiniiEgale(x, y));
            h.Append(CultureInfo.InvariantCulture, $"<tr class=\"{(dif ? "dif" : string.Empty)}\"><td>{i + 1}</td>");
            Celule(h, x);
            h.Append("</tr>");
            if (dif)
            {
                h.Append("<tr class=\"dif\"><td class=\"conf\">conf.</td>");
                Celule(h, y, "conf");
                h.Append("</tr>");
            }
        }

        h.Append("</table></div>");
    }

    private static bool LiniiEgale(LinieCitita x, LinieCitita y) =>
        (y.Denumire is null || Comparare.Normalizeaza(x.Denumire) == Comparare.Normalizeaza(y.Denumire)) &&
        (y.Um is null || Comparare.Normalizeaza(x.Um) == Comparare.Normalizeaza(y.Um)) &&
        (y.Cantitate is null || x.Cantitate == y.Cantitate) && (y.Pret is null || x.Pret == y.Pret) &&
        (y.Valoare is null || x.Valoare == y.Valoare) && (y.Cota is null || x.Cota == y.Cota);

    private static void Celule(StringBuilder h, LinieCitita? l, string clasa = "")
    {
        h.Append(CultureInfo.InvariantCulture, $"<td class=\"{clasa}\">{Enc(l?.Denumire)}</td><td class=\"{clasa}\">{Enc(l?.Um)}</td>")
         .Append(CultureInfo.InvariantCulture, $"<td class=\"n {clasa}\">{Numar(l?.Cantitate)}</td><td class=\"n {clasa}\">{Numar(l?.Pret)}</td>")
         .Append(CultureInfo.InvariantCulture, $"<td class=\"n {clasa}\">{Bani(l?.Valoare)}</td><td class=\"n {clasa}\">{(l?.Cota is { } k ? $"{k * 100:0.##}%" : string.Empty)}</td>");
    }

    private static void Rand(StringBuilder h, string eticheta, string camp, DocumentCitit d, string? citit, string? confirmat)
    {
        if (citit is null && confirmat is null && !d.Certitudini.ContainsKey(camp))
        {
            return;
        }

        var dif = confirmat is not null && Comparare.Normalizeaza(citit) != Comparare.Normalizeaza(confirmat);
        var nesigur = d.Certitudini.TryGetValue(camp, out var cert) && cert != Certitudine.Sigur;
        var clasa = dif ? "dif" : nesigur ? "nesigur" : string.Empty;
        var nota = nesigur ? $" <small>({(cert == Certitudine.Lipsa ? "de completat" : "de verificat")})</small>" : string.Empty;
        h.Append(CultureInfo.InvariantCulture, $"<tr class=\"{clasa}\"><td>{eticheta}</td><td>{Enc(citit)}{nota}</td>")
         .Append(CultureInfo.InvariantCulture, $"<td class=\"conf\">{(confirmat is null ? "<span style=\"color:#999\">—</span>" : Enc(confirmat) + (dif ? string.Empty : " <span class=\"ok\">✓</span>"))}</td></tr>");
    }

    private static IEnumerable<(int Pagina, byte[] Imagine)> Imagini(string cale)
    {
        var ext = Path.GetExtension(cale).ToLowerInvariant();
        if (ext == ".pdf")
        {
            var pdf = File.ReadAllBytes(cale);
            int numar;
            try
            {
                numar = Conversion.GetPageCount(pdf);
            }
            catch (Exception)
            {
                yield break;
            }

            for (var i = 0; i < Math.Min(numar, 40); i++)
            {
                using var ms = new MemoryStream();
                Conversion.SaveJpeg(ms, pdf, i, options: new RenderOptions(Dpi: 100, WithAnnotations: true));
                yield return (i + 1, ms.ToArray());
            }
        }
        else if (ext is ".jpg" or ".jpeg")
        {
            yield return (1, File.ReadAllBytes(cale));
        }
    }

    private static string Stare(RezultatFisier r) =>
        r.Citit.Eroare is not null || r.Diferente is { Count: > 0 }
            ? $"<span class=\"eticheta e-dif\">{(r.Citit.Eroare is not null ? "eroare" : $"{r.Diferente!.Count} dif.")}</span>"
            : r.Diferente is null ? "<span class=\"eticheta e-nec\">neconfirmat</span>" : "<span class=\"eticheta e-ok\">OK</span>";

    private static string Id(string nume) => "f" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(nume)))[..10];

    private static string? Bani(decimal? v) => v?.ToString("#,##0.00##", Ro);

    private static string Numar(decimal? v) => v is { } x ? x.ToString("#,##0.####", Ro) : string.Empty;

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
