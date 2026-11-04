using ErpConstructii.Formulare;
using ErpConstructii.Formulare.Infrastructure.Pdf;
using UglyToad.PdfPig;
using Xunit;

namespace ErpConstructii.Teste.Formulare;

/// <summary>Formatul standard al documentelor, randat în PDF și recitit cu PdfPig.</summary>
public class PdfStandardTeste
{
    private static readonly AspectDocument Aspect = new(
        "Bon de consum", "14-3-4A", "Nr. BC/2026/0007 din 15.09.2026",
        [new("Gestiunea", "gestiune"), new("Proiect", "proiect_cod")],
        "linii",
        [new("denumire", "Denumirea materialelor", 4), new("cantitate", "Cantitate", 1, AliniereColoana.Dreapta), new("valoare", "Valoare (lei)", 1, AliniereColoana.Dreapta)],
        [new("Total valoare (lei):", "total")],
        ["Predat (gestionar)", "Primit"],
        "observatii");

    private static DateFormular Date(int linii) => new(
        new Dictionary<string, string> { ["gestiune"] = "Șantier P-2026-001", ["proiect_cod"] = "P-2026-001", ["total"] = "1.150,00", ["observatii"] = "Turnare radier" },
        new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["linii"] = Enumerable.Range(1, linii).Select(i => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["denumire"] = $"Oțel beton PC52 Ø{i} — țeavă", ["cantitate"] = "20", ["valoare"] = "230,00",
            }).ToList(),
        });

    /// <summary>Textul fără spații (PdfPig nu păstrează spațiile dintre cuvinte în <c>Page.Text</c>).</summary>
    private static string FaraSpatii(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static string Text(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return FaraSpatii(string.Concat(doc.GetPages().Select(p => p.Text)));
    }

    private static void Contine(string asteptat, string text) => Assert.Contains(FaraSpatii(asteptat), text);

    [Fact]
    public void Documentul_contine_antetul_firmei_datele_si_diacriticele()
    {
        var pdf = new GeneratorPdfStandard().Genereaza(new AntetFirma("Construct Ștefănești SRL", "RO23456783", "J13/1/2020", "Str. Țării 1"), Aspect, Date(2));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var text = Text(pdf);
        Contine("Construct Ștefănești SRL", text);
        Contine("BON DE CONSUM", text);
        Contine("Nr. BC/2026/0007 din 15.09.2026", text);
        Contine("Oțel beton PC52 Ø2 — țeavă", text);
        Contine("1.150,00", text);
        Contine("Turnare radier", text);
    }

    [Fact]
    public void Tabelul_lung_continua_pe_pagina_urmatoare_cu_antetul_repetat()
    {
        using var doc = PdfDocument.Open(new GeneratorPdfStandard().Genereaza(new AntetFirma("Firma", null, null, null), Aspect, Date(90)));
        Assert.True(doc.NumberOfPages > 1);
        var pagina2 = FaraSpatii(doc.GetPage(2).Text);
        Contine("Denumirea materialelor", pagina2); // antetul tabelului se repetă
        Contine($"Pagina 2 din {doc.NumberOfPages}", pagina2);
    }
}
