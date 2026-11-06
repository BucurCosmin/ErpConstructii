using ErpConstructii.SharedKernel.Import;
using ErpConstructii.Stocuri.Import;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Stocuri;

/// <summary>
/// Bonurile de consum GC-087, câte două pe pagină, cu pozițiile cuvintelor ca după OCR (X la 300 DPI). Rândurile
/// reproduc pagina 2 din formularul real: bonul 54 (denumire pe două rânduri) și bonul 55 (fără „Nr. crt.” în antet).
/// </summary>
public class BonConsumImportTeste
{
    private static RandText R(int y, params (string Text, int X0, int X1)[] c) =>
        new(string.Join(' ', c.Select(x => x.Text)), c.Select(x => new CuvantText(x.Text, x.X0, x.X1, 95)).ToList(), 1, y, y + 34);

    private static IEnumerable<RandText> Antet(int y, string numar, bool cuNrCrt) =>
    [
        R(y, ("UNITATEA:", 394, 560), ("Lucrare:", 1805, 1950)),
        R(y + 64, ("S.C.", 392, 470), ("GENERAL", 484, 680), ("CONCRETE", 697, 920), ("GESTIUNE:", 1804, 1990)),
        R(y + 122, ("NUMAR", 393, 540), ("DOCUMENT:", 559, 760), ("DATA:", 1467, 1580)),
        R(y + 224, ("Ziua", 1471, 1560), ("Luna", 1578, 1680), ("Anul", 1695, 1790), ("predator", 1829, 2010), ("primitor", 2065, 2240)),
        R(y + 299, (numar, 903, 950)),
        R(y + 362, ("19", 1496, 1540), ("12", 1607, 1650), ("2025", 1696, 1790)),
        R(y + 464, cuNrCrt ? [("Nr.", 410, 460), ("Denumirea", 876, 1080), ("Cantitatea", 1814, 2010), ("Cantitatea", 2407, 2600)]
                           : [("Denumirea", 876, 1080), ("Cantitatea", 1814, 2010), ("Cantitatea", 2407, 2600)]),
        R(y + 492, ("Cod", 2104, 2170), ("Um", 2284, 2340), ("Pret", 2659, 2740), ("unitar", 2750, 2860), ("Valoarea", 2940, 3110), ("Obs.", 3236, 3320)),
        R(y + 514, ("(inclusiv", 752, 920), ("necesara", 1826, 2000), ("eliberata", 2421, 2580)),
    ];

    private static TextDocument Pagina() => new(
    [
        R(82, ("formular", 2886, 3020), ("cod", 3036, 3100), ("GC", 3109, 3160), ("-", 3174, 3190), ("087,", 3198, 3270)),
        .. Antet(173, "54", cuNrCrt: true),
        R(771, ("1", 456, 476), ("MANUSI", 495, 650), ("PROTECTIE", 669, 900), ("IMPREGNATE", 1406, 1660), ("PER", 2268, 2340),
            ("12", 2579, 2620), ("1,8900", 2761, 2860), ("22,68", 3051, 3140)),
        R(826, ("2", 456, 476), ("NITRIL", 495, 620), ("PALMA", 642, 780), ("EN388-3121X", 1031, 1290), ("0,00", 3075, 3140)),
        R(879, ("3", 456, 476), ("PLASA", 495, 620), ("SUDATA", 643, 800), ("BUC", 2269, 2340), ("1", 2601, 2620), ("34,7100", 2736, 2860), ("34,71", 3051, 3140)),
        R(936, ("4", 455, 476), ("0,00", 3075, 3140)),
        R(1136, ("8", 457, 476), ("TOTAL", 495, 620), ("57,39", 3053, 3140)),
        R(1163, ("DATA", 395, 480), ("SI", 525, 560), ("SEMNATURA", 578, 800), ("GESTIONAR:GAITA", 1825, 2200), ("ELENA", 2221, 2340), ("PRIMITOR:SERBULEA", 2647, 3060), ("JEAN", 3085, 3190)),
        .. Antet(1314, "55", cuNrCrt: false),
        R(1735, ("1", 461, 480), ("TICHETE", 497, 670), ("DE", 689, 740), ("MASA", 758, 870), ("R20681", 2042, 2190), ("BUC", 2273, 2340),
            ("120", 2560, 2620), ("20,0000", 2739, 2860), ("2400,00", 3008, 3140)),
        R(2107, ("8", 463, 480), ("TOTAL", 500, 620), ("2400,00", 3012, 3140)),
    ], [], DinOcr: true);

    [Fact]
    public void Doua_bonuri_pe_pagina_se_citesc_separat()
    {
        var bonuri = InterpretorBonConsum.Interpreteaza(Pagina());

        Assert.Equal(["54", "55"], bonuri.Select(b => b.Numar.Valoare));
        Assert.All(bonuri, b => Assert.Equal(new DateOnly(2025, 12, 19), b.Data.Valoare)); // celulele Ziua / Luna / Anul: 19 12 2025
    }

    [Fact]
    public void Randul_fara_cantitate_continua_denumirea_iar_cantitatea_aliniata_la_dreapta_ramane_cantitate()
    {
        var b = InterpretorBonConsum.Interpreteaza(Pagina())[0];

        Assert.Equal(2, b.Linii.Count);
        // „2 NITRIL PALMA … EN388-3121X” n-are cantitate și are valoarea 0,00 → continuarea liniei 1.
        Assert.Equal("MANUSI PROTECTIE IMPREGNATE NITRIL PALMA EN388-3121X", b.Linii[0].Denumire.Valoare);
        // 12 × 1,89 = 22,68 ✓
        Assert.Equal(("per", 12m, 1.89m, 22.68m), (b.Linii[0].UnitateMasura.Valoare, b.Linii[0].Cantitate.Valoare, b.Linii[0].PretUnitar.Valoare, b.Linii[0].Valoare.Valoare));
        // „1” (o cifră, la marginea din dreapta a coloanei) e cantitatea, nu începutul prețului: 1 × 34,71 = 34,71 ✓
        Assert.Equal((1m, 34.71m, 34.71m), (b.Linii[1].Cantitate.Valoare, b.Linii[1].PretUnitar.Valoare, b.Linii[1].Valoare.Valoare));
        // 22,68 + 34,71 = 57,39 = TOTAL ✓
        Assert.Equal((57.39m, Certitudine.Sigur), (b.Total.Valoare, b.Total.Certitudine));
        Assert.Equal("SERBULEA JEAN", b.Primitor);
        Assert.Equal("GAITA ELENA", b.Gestionar);
    }

    [Fact]
    public void Fara_Nr_crt_in_antet_numarul_curent_nu_intra_in_denumire()
    {
        var l = Assert.Single(InterpretorBonConsum.Interpreteaza(Pagina())[1].Linii);

        Assert.Equal(("TICHETE DE MASA", "R20681", 120m), (l.Denumire.Valoare, l.Cod, l.Cantitate.Valoare)); // 120 × 20 = 2.400 ✓
    }

    [Fact]
    public void Bonurile_de_consum_se_recunosc_dupa_codul_formularului_si_se_impart_pe_pagina()
    {
        var doc = Pagina();
        var pagini = ClasificarePagini.Clasifica(doc);

        Assert.Equal(TipPagina.BonConsum, Assert.Single(pagini).Tip); // titlul „BON DE CONSUM” lipsește (OCR), „GC - 087” rămâne
        var documente = ImpartireDocumente.Imparte(pagini, doc);
        Assert.Equal([("54", 1), ("55", 2)], documente.Select(d => (d.Numar, d.Zona ?? 0)));
    }

    [Fact]
    public void Bonul_necompletat_nu_are_linii()
    {
        var gol = new TextDocument([.. Antet(173, "57", cuNrCrt: true), R(771, ("1", 456, 476), ("0,00", 3075, 3140)), R(1136, ("8", 457, 476), ("TOTAL", 495, 620), ("0,00", 3075, 3140))], [], true);

        Assert.True(Assert.Single(InterpretorBonConsum.Interpreteaza(gol)).Gol);
    }
}
