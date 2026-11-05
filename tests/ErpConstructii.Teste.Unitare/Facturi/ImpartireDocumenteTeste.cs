using ErpConstructii.SharedKernel.Import;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Facturi;

/// <summary>Un fișier scanat cu mai multe documente: fiecare factură cu dosarul ei, comenzi, necesare, bonuri de consum.</summary>
public class ImpartireDocumenteTeste
{
    private static PaginaClasificata P(int pagina, TipPagina tip, string? numar = null, DateOnly? data = null) => new(pagina, tip, numar, data);

    private static string Rezumat(IReadOnlyList<DocumentDetectat> d) =>
        string.Join(" | ", d.Select(x => $"{x.Tip}:{string.Join(",", x.Pagini)}"));

    [Fact]
    public void Fisierul_cu_mai_multe_dosare_se_imparte_pe_documente()
    {
        // Ca dosarul real de test: e-Factura pe 2 pagini + factura tipărită + comandă + aviz + declarație fără titlu;
        // o factură SmartBill; o e-Factura cu număr cu punct; un necesar; o comandă.
        var pagini = new[]
        {
            P(1, TipPagina.EFactura, "23558"), P(2, TipPagina.EFactura), P(3, TipPagina.Factura),
            P(4, TipPagina.NotaComanda, "496", new DateOnly(2026, 9, 8)), P(5, TipPagina.Aviz, "23558"), P(6, TipPagina.Altele),
            P(7, TipPagina.Factura, "DQUI 2011"),
            P(8, TipPagina.EFactura, "JTG.00046467"), P(9, TipPagina.EFactura), P(10, TipPagina.NotaComanda, "530"), P(11, TipPagina.Aviz, "50621"),
            P(12, TipPagina.NecesarMateriale, "81"),
            P(13, TipPagina.EFactura, "MPP.76484.0"), P(14, TipPagina.EFactura), P(15, TipPagina.Factura, "MPP 76484"),
        };

        var d = ImpartireDocumente.Imparte(pagini);

        // Pagina 6 (fără titlu) continuă documentul paginii 5 (avizul, deci factura 23558), nu comanda.
        // Avizul de pe p. 11 merge la ultima factură (JTG), nu la comanda dintre ele.
        Assert.Equal(
            "Factura:1,2,3,5,6 | NotaComanda:4 | Factura:7 | Factura:8,9,11 | NotaComanda:10 | NecesarMateriale:12 | Factura:13,14,15",
            Rezumat(d));
        // Numărul facturii: cel din factura tipărită are prioritate față de e-Factura.
        Assert.Equal(["23558", "496", "DQUI 2011", "JTG.00046467", "530", "81", "MPP 76484"], d.Select(x => x.Numar));
    }

    [Fact]
    public void Paginile_de_acelasi_tip_cu_numere_diferite_sunt_documente_diferite()
    {
        var d = ImpartireDocumente.Imparte([
            P(1, TipPagina.NotaComanda, "1"), P(2, TipPagina.NotaComanda, "2"),
            P(3, TipPagina.BonConsum, "BC 7"), P(4, TipPagina.BonConsum), P(5, TipPagina.BonConsum, "BC 8"),
        ]);

        // p. 4 fără număr continuă bonul 7; „1” și „2” sunt numere scurte, comparate exact.
        Assert.Equal("NotaComanda:1 | NotaComanda:2 | BonConsum:3,4 | BonConsum:5", Rezumat(d));
    }

    [Fact]
    public void Aceeasi_comanda_cu_date_diferite_e_alt_document()
    {
        var d = ImpartireDocumente.Imparte([
            P(1, TipPagina.NecesarMateriale, null, new DateOnly(2026, 10, 1)),
            P(2, TipPagina.NecesarMateriale, null, new DateOnly(2026, 10, 8)),
        ]);

        Assert.Equal("NecesarMateriale:1 | NecesarMateriale:2", Rezumat(d));
    }

    [Fact]
    public void Certificatele_dinaintea_facturii_merg_la_factura_iar_fara_factura_raman_anexe()
    {
        var cuFactura = ImpartireDocumente.Imparte([P(1, TipPagina.Certificat), P(2, TipPagina.Factura, "Seria A nr. 5")]);
        Assert.Equal("Factura:1,2", Rezumat(cuFactura));

        var doarCertificate = ImpartireDocumente.Imparte([P(1, TipPagina.Certificat), P(2, TipPagina.Certificat)]);
        Assert.Equal("Anexe:1,2", Rezumat(doarCertificate));
    }

    [Theory]
    [InlineData("MPP.76484.0", "MPP 76484", true)] // e-Factura vs. factura tipărită
    [InlineData("DQUI 0002011", "2011", true)]     // zerourile din față nu contează
    [InlineData("12", "123", false)]               // numere scurte: doar egale
    [InlineData("23558", "23559", false)]
    public void Numerele_se_compara_pe_cifre(string a, string b, bool acelasi)
    {
        var (ka, kb) = (ClasificarePagini.CheieNumar(a)!, ClasificarePagini.CheieNumar(b)!);
        Assert.Equal(acelasi, ClasificarePagini.AcelasiNumar(ka, kb));
    }

    [Fact]
    public void Bonul_de_consum_se_recunoaste_dupa_titlu()
    {
        var doc = new TextDocument([Pagini.T(1, "BON DE CONSUM nr. 12 / 05.11.2026"), Pagini.T(1, "Gestiunea: Materiale în șantier")], [], false);
        var p = Assert.Single(ClasificarePagini.Clasifica(doc));
        Assert.Equal((TipPagina.BonConsum, "12"), (p.Tip, p.Numar));
    }
}
