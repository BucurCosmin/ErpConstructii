using ErpConstructii.Aprovizionare;
using ErpConstructii.Aprovizionare.Import;
using ErpConstructii.Parteneri;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Nota de comandă către furnizor (formularul GC 007), din scanare și din Word.</summary>
public class InterpretorNotaComandaTeste
{
    private const string CuiFirma = "23456783";

    private static RandText R(int y, params (string Text, int X0)[] cuvinte) =>
        new(string.Join(' ', cuvinte.Select(c => c.Text)),
            cuvinte.Select(c => new CuvantText(c.Text, c.X0, c.X0 + c.Text.Length * 20, 95)).ToList(), 1, y, y + 30);

    /// <summary>Antetul pe trei coloane: Beneficiar (stânga) | COMANDA, Obiect / Lucrare (mijloc) | Furnizor (dreapta).</summary>
    private static TextDocument Scanare() => new(
    [
        R(150, ("Beneficiar:", 270)),
        R(190, ("COMANDA", 1130)),
        R(207, ("Furnizor:", 1600), ("ALFA", 1800), ("TRADE", 1900)),
        R(226, ("SC", 320), ("CONSTRUCT", 390), ("TEST", 600), ("SRL", 700)),
        R(255, ("INDUSTRY", 1600)),
        R(292, ("Nr.265/08.06.2026", 1075)),
        R(324, ("905200", 420), ("Cernavoda", 560), ("……………………", 1600)),
        R(354, ("str.", 378), ("Unirii", 443), ("ADRESA", 1608), (":", 1800), ("Str.", 1830), ("Exemplu", 1910), ("nr", 2080), ("5", 2140)),
        R(390, ("CUI:", 270), ("RO", 360), (CuiFirma, 430), ("Obiect/", 944), ("Lucrare", 1102), ("Constanta", 1608)),
        R(489, ("Cont:", 271), ("BANCA", 418), ("-STOC", 957)),
        R(549, ("RO20BTRL", 271), ("-CTR.198", 944), ("Tel.0732", 1608), ("334", 1790), ("335", 1874)),
        R(638, ("Fax:", 271), ("-CTRF-HSJV-002", 944)),
        R(899, ("Va", 390), ("rugam", 460), ("sa", 609), ("ne", 675), ("expediati", 742)),
        R(1380, ("MATERIAL", 717), ("/", 944), ("PRODUS", 969), ("U.M", 1547), ("CANTITATE", 1702), ("TERMEN", 2040)),
        R(1460, ("1.", 296), ("LAVETE", 397), ("BUMBAC", 520), ("BUC", 1517), ("1500", 1790)),
        R(1507, ("2.", 296), ("SACI", 397), ("RAFIE", 480), ("50X100", 600), ("BUC", 1517), ("200", 1800), ("imediat", 2040)),
        R(1550, ("PRAF", 397), ("CRETA", 513), ("BUC", 1517)), // OCR a pierdut numărul și cantitatea
        R(2120, ("APROBAT,", 249), ("AVIZAT,", 953), ("INTOCMIT,", 1799)),
    ], [], DinOcr: true);

    [Fact]
    public void Antetul_se_citeste_pe_coloane_fara_textul_vecinilor()
    {
        var n = InterpretorNotaComanda.Interpreteaza(Scanare(), CuiFirma);
        Assert.Equal(("265", new DateOnly(2026, 6, 8)), (n.NumarExtern.Valoare, n.Data.Valoare));
        Assert.Equal("ALFA TRADE INDUSTRY", n.FurnizorDenumire.Valoare);
        Assert.Equal("STOC -CTR.198 -CTRF-HSJV-002", n.Lucrare.Valoare); // fără adresa și telefonul furnizorului din coloana din dreapta
        Assert.Equal("0732 334 335", n.FurnizorTelefon);
    }

    [Fact]
    public void CUI_ul_beneficiarului_nu_devine_al_furnizorului()
    {
        var n = InterpretorNotaComanda.Interpreteaza(Scanare(), CuiFirma);
        Assert.Equal(Certitudine.Lipsa, n.FurnizorCui.Certitudine);
        Assert.Equal(CuiFirma, n.CuiBeneficiar);
        Assert.DoesNotContain(n.Probleme, p => p.Locatie == "beneficiar");
    }

    [Fact]
    public void Linia_fara_cantitate_ramane_de_completat()
    {
        var n = InterpretorNotaComanda.Interpreteaza(Scanare(), CuiFirma);
        Assert.Equal(3, n.Linii.Count);
        Assert.Equal(("SACI RAFIE 50X100", "buc", 200m, "imediat"),
            (n.Linii[1].Denumire.Valoare, n.Linii[1].UnitateMasura.Valoare, n.Linii[1].Cantitate.Valoare, n.Linii[1].Termen));
        Assert.Equal(("PRAF CRETA", Certitudine.Lipsa), (n.Linii[2].Denumire.Valoare, n.Linii[2].Cantitate.Certitudine));
    }

    [Fact]
    public void Comanda_altei_firme_e_semnalata()
    {
        var n = InterpretorNotaComanda.Interpreteaza(Scanare(), "34567892");
        Assert.Contains(n.Probleme, p => p.Locatie == "beneficiar");
    }

    [Fact]
    public void Din_Word_se_citesc_celulele_tabelelor()
    {
        var word = new TextDocument(
            [RandText.DinText("COMANDA"), RandText.DinText("Nr. 1 /09.01.2026")],
            [
                new TabelText([
                    ["Beneficiar: SC CONSTRUCT TEST SRL CUI: RO " + CuiFirma, "COMANDA Nr. 1 /09.01.2026 Obiect/ Lucrare: CTRF-HSJV-002 Ctr.1891", "Furnizor: S.C TRITON S.R.L ADRESA: Bd. Exemplu nr. 217, Constanta Tel. 0241693210"],
                ]),
                new TabelText([
                    ["NR. CRT.", "MATERIAL / PRODUS", "U.M", "CANTITATE", "TERMEN"],
                    ["1.", "MANUSI PROTECTIE MARIMEA 9", "BUC", "60", ""],
                    ["2.", "TRASOR CU SFOARA 30M", "BUC", "2", "15.01.2026"],
                ]),
            ], DinOcr: false);
        var n = InterpretorNotaComanda.Interpreteaza(word, CuiFirma);
        Assert.Equal(("S.C TRITON S.R.L", Certitudine.Sigur), (n.FurnizorDenumire.Valoare, n.FurnizorDenumire.Certitudine));
        Assert.Equal("CTRF-HSJV-002 Ctr.1891", n.Lucrare.Valoare);
        Assert.Equal("0241693210", n.FurnizorTelefon);
        Assert.Equal([60m, 2m], n.Linii.Select(l => l.Cantitate.Valoare));
        Assert.Equal("15.01.2026", n.Linii[1].Termen);
    }
}

public class NotaComandaImportataTeste
{
    private static DateNotaComanda Date(string? cui = null) => new(
        "265", new DateOnly(2026, 6, 8), Guid.NewGuid(), "P-2026-001", "STOC", null, "ALFA TRADE", cui,
        [new LinieNotaComanda(" LAVETE ", "buc", 1500, null, null)], null);

    [Fact]
    public void Primeste_numar_de_sistem_si_pastreaza_numarul_de_pe_hartie()
    {
        var r = NotaComanda.Importa(2026, 3, Date(), Guid.NewGuid(), Guid.NewGuid(), "Ana Pop");
        Assert.True(r.Succes);
        Assert.Equal(("NC/2026/0003", "265", "LAVETE"), (r.Valoare.NumarAfisat, r.Valoare.NumarExtern, r.Valoare.Linii[0].Denumire));
        Assert.True(r.Valoare.EsteBlocat); // imuabilă după înregistrare (regula 6)
    }

    [Fact]
    public void CUI_ul_invalid_al_furnizorului_e_refuzat()
    {
        var r = NotaComanda.Importa(2026, 3, Date("RO123"), Guid.NewGuid(), Guid.NewGuid(), "Ana Pop");
        Assert.True(r.Esec);
        Assert.Contains("CUI", r.Eroare.Mesaj);
    }
}

public class DenumireFirmaTeste
{
    [Theory]
    [InlineData("S.C TRITON S.R.L", "TRITON")]
    [InlineData("Triton SRL", "TRITON")]
    [InlineData("SC GLOBAL TRADE INDUSTRY SRL", "GLOBAL TRADE INDUSTRY")]
    [InlineData("J.T. Grup Oil S.A.", "JT GRUP OIL")]
    [InlineData("Construcții Ălfa s.r.l.", "CONSTRUCTII ALFA")]
    public void Cheia_ignora_forma_juridica_punctuatia_si_diacriticele(string denumire, string cheie) =>
        Assert.Equal(cheie, DenumireFirma.Cheie(denumire));
}
