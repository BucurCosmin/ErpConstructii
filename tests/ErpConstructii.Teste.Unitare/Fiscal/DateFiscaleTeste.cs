using ErpConstructii.SharedKernel;

namespace ErpConstructii.Teste.Unitare.Fiscal;

public class ValidareFiscalaTeste
{
    // 18547290: corp „1854729” → „001854729” × 753217532
    //   = 0·7 + 0·5 + 1·3 + 8·2 + 5·1 + 4·7 + 7·5 + 2·3 + 9·2 = 3+16+5+28+35+6+18 = 111
    //   111 × 10 = 1110; 1110 mod 11 = 10 → cifra de control 0 ✓
    [Theory]
    [InlineData("18547290")]
    [InlineData("RO18547290")]
    [InlineData("ro 1854 7290")]
    public void CUI_cu_cifra_de_control_corecta_e_valid(string cui)
    {
        Assert.True(ValidareFiscala.EsteCuiValid(cui));
    }

    [Theory]
    [InlineData("18547291")] // cifra de control greșită
    [InlineData("1")] // prea scurt
    [InlineData("12345678901")] // prea lung
    [InlineData("RO18A47290")] // conține litere
    [InlineData("")]
    public void CUI_invalid_e_respins(string cui)
    {
        Assert.False(ValidareFiscala.EsteCuiValid(cui));
    }

    [Fact]
    public void CUI_se_normalizeaza_fara_RO_si_spatii()
    {
        Assert.Equal("18547290", ValidareFiscala.NormalizeazaCui(" RO 1854 7290 "));
    }

    // Exemplul standard de IBAN românesc (ECBS): RO49 AAAA 1B31 0075 9384 0000.
    [Theory]
    [InlineData("RO49AAAA1B31007593840000")]
    [InlineData("ro49 aaaa 1b31 0075 9384 0000")]
    public void IBAN_corect_e_valid(string iban)
    {
        Assert.True(ValidareFiscala.EsteIbanValid(iban));
    }

    [Theory]
    [InlineData("RO48AAAA1B31007593840000")] // cifre de control greșite
    [InlineData("RO49AAAA1B3100759384000")] // RO trebuie să aibă 24 de caractere
    [InlineData("4949AAAA1B31007593840000")] // nu începe cu codul țării
    [InlineData("RO49-AAAA-1B31")]
    public void IBAN_invalid_e_respins(string iban)
    {
        Assert.False(ValidareFiscala.EsteIbanValid(iban));
    }
}

public class ProblemeIdentificareTeste
{
    private const string IbanValid = "RO49AAAA1B31007593840000";

    [Fact]
    public void Date_corecte_nu_au_probleme()
    {
        Assert.Empty(ValidareFiscala.Probleme("RO18547290", [new ContBancar("Banca Exemplu", IbanValid, Principal: true)]));
    }

    [Fact]
    public void CUI_lipsa_e_permis_pentru_persoane_fizice_sau_straine()
    {
        Assert.Empty(ValidareFiscala.Probleme(null, []));
    }

    [Fact]
    public void Fiecare_problema_e_raportata_separat()
    {
        var probleme = ValidareFiscala.Probleme("18547291",
        [
            new ContBancar("", IbanValid, Principal: true),
            new ContBancar("Banca 2", "RO48AAAA1B31007593840000", Principal: true),
        ]);

        // CUI greșit + bancă lipsă + IBAN greșit + două conturi principale în RON = 4
        Assert.Equal(4, probleme.Count);
    }

    [Fact]
    public void Se_poate_avea_cate_un_cont_principal_pe_fiecare_moneda()
    {
        Assert.Empty(ValidareFiscala.Probleme(null,
        [
            new ContBancar("Banca", IbanValid, "RON", Principal: true),
            new ContBancar("Banca", IbanValid, "EUR", Principal: true),
        ]));
    }
}

public class AdresaTeste
{
    [Fact]
    public void Adresa_se_formateaza_pe_un_rand_pentru_documente()
    {
        var a = new Adresa("str. Exemplu nr. 10", "Medgidia", "CT", "905600");
        Assert.Equal("str. Exemplu nr. 10, 905600 Medgidia, jud. Constanța", a.Formatata());
    }

    [Fact]
    public void Bucurestiul_nu_primeste_prefixul_judet_si_nu_se_repeta()
    {
        Assert.Equal("Bd. Unirii 1, București", new Adresa("Bd. Unirii 1", "București", "B", null).Formatata());
        Assert.Equal("Bd. Unirii 1, Sector 3, București", new Adresa("Bd. Unirii 1", "Sector 3", "B", null).Formatata());
    }

    [Fact]
    public void Adresa_din_strainatate_afiseaza_tara()
    {
        Assert.Equal("Hauptstraße 5, 10115 Berlin, DE", new Adresa("Hauptstraße 5", "Berlin", null, "10115", "DE").Formatata());
    }

    [Fact]
    public void Exista_42_de_judete_cu_Bucurestiul()
    {
        Assert.Equal(42, Judete.Toate.Count);
        Assert.Equal("Constanța", Judete.Denumire("CT"));
        Assert.Null(Judete.Denumire("XX"));
    }
}
