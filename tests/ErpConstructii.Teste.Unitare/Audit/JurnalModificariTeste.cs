using ErpConstructii.Audit;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Audit;

/// <summary>Citirea jurnalului de modificări în raport: categoria și modificările în cuvinte.</summary>
public class JurnalModificariTeste
{
    [Theory]
    [InlineData("facturi", "facturi_primite", "Facturi")]
    [InlineData("stocuri", "nir", "NIR")]
    [InlineData("stocuri", "corectii_nir", "NIR")]
    [InlineData("stocuri", "storno_nir", "NIR")]
    [InlineData("stocuri", "miscari", "Stocuri")]
    [InlineData("aprovizionare", "note_comanda", "Aprovizionare")]
    public void Categoria_vine_din_schema_si_tabel(string schema, string tabel, string categorie) =>
        Assert.Equal(categorie, CatalogJurnal.Categorie(schema, tabel));

    [Fact]
    public void Modificarea_arata_vechi_si_nou_iar_textele_lungi_doar_ca_modificate()
    {
        var json = """{"Numar":["KZ 46741","KZ 467411"],"TotalDePlata":[2595,2595.5],"LiniiJson":["[{\"Denumire\":\"A\"}]","[{\"Denumire\":\"B\"}]"],"DataEmitere":["2025-11-04","2025-11-05"]}""";

        var text = CatalogJurnal.Descriere("Modificat", json);

        Assert.Equal("Numar: KZ 46741 → KZ 467411; Total de plata: 2.595 → 2.595,5; Linii: modificate; Data emitere: 04.11.2025 → 05.11.2025", text);
    }

    [Fact]
    public void Corectura_adaugata_arata_motivul_si_rezumatul()
    {
        var json = """{"NirId":"0f8fad5b-d9cb-469f-a165-70867728950e","Motiv":"cantitate citită greșit","Rezumat":"Ciment: recepționat 7.000 → 7 sac","DiferentaValoare":-209790}""";

        Assert.Equal("Motiv: cantitate citită greșit. Ciment: recepționat 7.000 → 7 sac", CatalogJurnal.Descriere("Adaugat", json));
    }

    [Fact]
    public void Anul_si_numarul_raman_fara_separator_iar_tipul_miscarii_e_in_cuvinte()
    {
        var json = """{"An":2026,"Numar":4,"Tip":"CorectieNir","Cantitate":7000}""";

        Assert.Equal("An: 2026; Numar: 4; Tip: corectură NIR; Cantitate: 7.000", CatalogJurnal.Descriere("Adaugat", json));
    }
}
