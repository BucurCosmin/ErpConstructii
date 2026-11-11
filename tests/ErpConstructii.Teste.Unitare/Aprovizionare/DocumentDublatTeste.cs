using ErpConstructii.Aprovizionare;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Același document importat a doua oară dintr-un alt fișier (altă scanare): se recunoaște după numărul de pe hârtie.</summary>
public class DocumentDublatTeste
{
    private static readonly DateOnly Ziua = new(2026, 10, 8);
    private static readonly Guid Triton = Guid.NewGuid();

    private static readonly DocumentImportat[] Necesare =
    [
        new("NM/2026/0005", "81", Ziua),
        new("NM/2026/0006", "82", Ziua),
    ];

    [Theory]
    [InlineData("81")]
    [InlineData("081")]
    [InlineData("Nr. 81")]
    public void Acelasi_numar_scris_altfel_si_aceeasi_data_e_dublura(string numar) =>
        Assert.Equal("NM/2026/0005", DocumentDublat.DupaNumarSiData(numar, Ziua, Necesare)?.Descriere);

    [Fact]
    public void Acelasi_numar_la_alta_data_sau_fara_numar_nu_e_dublura()
    {
        Assert.Null(DocumentDublat.DupaNumarSiData("81", Ziua.AddYears(1), Necesare)); // numerotarea reîncepe anual
        Assert.Null(DocumentDublat.DupaNumarSiData(null, Ziua, Necesare));
        Assert.Null(DocumentDublat.DupaNumarSiData("fără număr", Ziua, Necesare));
        Assert.Null(DocumentDublat.DupaNumarSiData("81", null, Necesare));
    }

    [Fact]
    public void Avizul_e_dublura_la_acelasi_numar_de_la_acelasi_furnizor_dupa_id_sau_denumire()
    {
        DocumentImportat[] avize = [new("AV/2026/0003", "INTFSN/20488", Ziua, Triton, "TRITON")];

        Assert.Equal("AV/2026/0003", DocumentDublat.DupaNumarSiFurnizor("20488", Triton, null, avize)?.Descriere);
        Assert.Equal("AV/2026/0003", DocumentDublat.DupaNumarSiFurnizor("INTFSN 20488", null, "TRITON", avize)?.Descriere);
        Assert.Null(DocumentDublat.DupaNumarSiFurnizor("20488", Guid.NewGuid(), "GLOBAL", avize)); // alt furnizor, același număr
        Assert.Null(DocumentDublat.DupaNumarSiFurnizor("20489", Triton, "TRITON", avize));
    }
}
