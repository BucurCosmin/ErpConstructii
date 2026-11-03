using ErpConstructii.Identitate;

namespace ErpConstructii.Teste.Unitare.Identitate;

public class PoliticaMfaTeste
{
    private static IEnumerable<string> PermisiuniRol(string cod) =>
        RoluriPredefinite.Toate.Single(r => r.Cod == cod).ToatePermisiunile();

    [Theory]
    [InlineData(TipUtilizator.AdministratorSistem)]
    [InlineData(TipUtilizator.AdministratorFirma)]
    public void Administratorii_au_MFA_obligatoriu_chiar_fara_permisiuni(TipUtilizator tip)
    {
        Assert.True(PoliticaMfa.EsteObligatorie(tip, []));
    }

    [Theory]
    [InlineData("contabil")] // financiar.editare
    [InlineData("hr")] // date.salarii, date.personale
    [InlineData("director")] // financiar.vizualizare, date.salarii
    [InlineData("auditor")] // financiar.vizualizare
    [InlineData("admin_firma")] // administrare.*
    public void Rolurile_cu_acces_financiar_HR_sau_administrare_cer_MFA(string rol)
    {
        Assert.True(PoliticaMfa.EsteObligatorie(TipUtilizator.Intern, PermisiuniRol(rol)));
    }

    [Theory]
    [InlineData("ofertant")]
    [InlineData("sef_santier")] // hr.editare (pontaj), fără salarii și date personale
    [InlineData("gestionar")]
    [InlineData("dispecer_utilaje")]
    public void Rolurile_operationale_nu_cer_MFA(string rol)
    {
        Assert.False(PoliticaMfa.EsteObligatorie(TipUtilizator.Intern, PermisiuniRol(rol)));
    }
}

public class AccesFirmeTeste
{
    private static readonly Guid FirmaA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid FirmaB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid FirmaC = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly DateOnly Azi = new(2026, 11, 3);

    [Fact]
    public void Doar_atribuirile_valabile_azi_dau_acces_la_firma()
    {
        var u = Guid.NewGuid();
        var atribuiri = new[]
        {
            new Atribuire(u, Guid.NewGuid(), new Domeniu(FirmaA), new DateOnly(2026, 1, 1)),
            new Atribuire(u, Guid.NewGuid(), new Domeniu(FirmaB), new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 31)), // expirată
            new Atribuire(u, Guid.NewGuid(), new Domeniu(FirmaC), new DateOnly(2026, 12, 1)), // viitoare
        };

        Assert.Equal([FirmaA], AccesFirme.Accesibile(atribuiri, Azi));
    }

    [Fact]
    public void Firma_ceruta_accesibila_are_prioritate()
    {
        Assert.Equal(FirmaB, AccesFirme.Alege([FirmaA, FirmaB], ceruta: FirmaB, ultima: FirmaA));
    }

    [Fact]
    public void Firma_ceruta_fara_acces_e_ignorata_si_se_foloseste_ultima()
    {
        Assert.Equal(FirmaA, AccesFirme.Alege([FirmaA, FirmaB], ceruta: FirmaC, ultima: FirmaA));
    }

    [Fact]
    public void Fara_preferinte_se_alege_prima_firma()
    {
        Assert.Equal(FirmaA, AccesFirme.Alege([FirmaA, FirmaB], ceruta: null, ultima: FirmaC));
    }

    [Fact]
    public void Fara_firme_nu_exista_firma_activa()
    {
        Assert.Null(AccesFirme.Alege([], ceruta: FirmaA, ultima: FirmaA));
    }

    [Fact]
    public void Contul_expirat_sau_dezactivat_nu_poate_intra()
    {
        var auditor = new Utilizator("auditor@firma.ro", "Auditor", TipUtilizator.Auditor);
        auditor.SeteazaExpirare(new DateOnly(2026, 11, 2));
        Assert.False(AccesFirme.PoateIntra(auditor, Azi));

        auditor.SeteazaExpirare(Azi); // valabil inclusiv în ziua expirării
        Assert.True(AccesFirme.PoateIntra(auditor, Azi));

        auditor.Dezactiveaza();
        Assert.False(AccesFirme.PoateIntra(auditor, Azi));
    }
}
