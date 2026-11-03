using ErpConstructii.Formulare;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Teste.Unitare.Formulare;

public class ValidatorDateFormularTeste
{
    private static Dictionary<string, string> CampuriValide() => new()
    {
        ["numar"] = "CMD-2026-0001",
        ["data"] = "03.11.2026",
        ["proiect_cod"] = "P-001",
        ["furnizor_denumire"] = "Beton SRL",
        ["furnizor_cui"] = "RO123456",
        ["total_fara_tva"] = "1.234,56",
    };

    private static IReadOnlyDictionary<string, string> LinieValida() => new Dictionary<string, string>
    {
        ["denumire"] = "Beton C16/20",
        ["um"] = "mc",
        ["cantitate"] = "12,5",
        ["articol_deviz"] = "CA01A",
    };

    private static DateFormular Date(Dictionary<string, string> campuri, params IReadOnlyDictionary<string, string>[] linii) =>
        new(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });

    [Fact]
    public void Comanda_completa_cu_formate_romanesti_este_valida()
    {
        var probleme = ValidatorDateFormular.Valideaza(CatalogFormulare.ComandaFurnizor, Date(CampuriValide(), LinieValida()));
        Assert.Empty(probleme);
    }

    [Fact]
    public void Campurile_obligatorii_lipsa_si_tabelul_gol_sunt_erori()
    {
        var campuri = CampuriValide();
        campuri.Remove("furnizor_cui");

        var probleme = ValidatorDateFormular.Valideaza(CatalogFormulare.ComandaFurnizor, Date(campuri));

        Assert.Equal(2, probleme.Count(p => p.Severitate == Severitate.Eroare));
    }

    [Fact]
    public void Data_si_numarul_invalide_sunt_semnalate()
    {
        var campuri = CampuriValide();
        campuri["data"] = "31.02.2026";
        campuri["total_fara_tva"] = "o mie";

        var probleme = ValidatorDateFormular.Valideaza(CatalogFormulare.ComandaFurnizor, Date(campuri, LinieValida()));

        Assert.Equal(2, probleme.Count);
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("12,5", 12.5)]
    [InlineData("1 500 lei", 1500)]
    public void Numerele_scrise_de_oameni_se_citesc(string text, double asteptat)
    {
        Assert.True(ValidatorDateFormular.IncearcaNumar(text, out var valoare));
        Assert.Equal((decimal)asteptat, valoare);
    }
}

public class NumerotareTeste
{
    [Fact]
    public void Seria_are_prefix_an_si_numar_cu_zerouri()
    {
        var serie = new SerieNumerotare("CMD", 2026);
        Assert.Equal("CMD-2026-0001", serie.Urmatorul());
        Assert.Equal("CMD-2026-0002", serie.Urmatorul());
    }

    [Fact]
    public void Toate_tipurile_din_catalog_au_coduri_unice()
    {
        Assert.Equal(CatalogFormulare.Toate.Count, CatalogFormulare.Toate.Select(t => t.Cod).Distinct().Count());
    }
}
