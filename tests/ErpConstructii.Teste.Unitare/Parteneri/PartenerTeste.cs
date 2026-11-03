using ErpConstructii.Parteneri;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Teste.Unitare.Parteneri;

public class PartenerTeste
{
    private static DatePartener DateFurnizor(
        bool esteFurnizor = true,
        bool esteClient = false,
        bool esteSubantreprenor = false,
        string? email = null) =>
        new(
            "Furnizor Exemplu SRL",
            null,
            null,
            false,
            Adresa.Goala,
            null,
            email,
            null,
            null,
            null,
            [],
            esteFurnizor,
            esteClient,
            esteSubantreprenor);

    [Fact]
    public void Furnizorul_valid_se_creeaza_activ()
    {
        var rezultat = Partener.Creeaza(DateFurnizor());

        Assert.True(rezultat.Succes);
        Assert.True(rezultat.Valoare.EsteFurnizor);
        Assert.True(rezultat.Valoare.Activ);
    }

    [Fact]
    public void Partenerul_fara_rol_nu_se_creeaza()
    {
        var rezultat = Partener.Creeaza(DateFurnizor(esteFurnizor: false));

        Assert.False(rezultat.Succes);
        Assert.Contains("cel puțin un rol", rezultat.Eroare.Mesaj);
    }

    [Fact]
    public void Furnizorul_cu_email_invalid_nu_se_creeaza()
    {
        var rezultat = Partener.Creeaza(DateFurnizor(email: "adresa-invalida"));

        Assert.False(rezultat.Succes);
        Assert.Contains("email", rezultat.Eroare.Mesaj);
    }
}
