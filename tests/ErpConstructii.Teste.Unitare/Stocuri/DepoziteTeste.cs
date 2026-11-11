using ErpConstructii.Stocuri;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Stocuri;

/// <summary>Depozitul (loc fizic) împărțit în gestiuni, fiecare cu gestionarul ei (D-43).</summary>
public class DepoziteTeste
{
    [Fact]
    public void Gestiunile_aceluiasi_depozit_au_acelasi_depozit_si_gestionari_diferiti()
    {
        var depozit = Depozit.Creeaza("dep-01", "Depozit Cernavodă", "Str. Unirii 20").Valoare;
        var ana = Guid.NewGuid();

        var g5 = Gestiune.CreeazaInDepozit(depozit, "g5", "Gestiunea G5", ana, "Ana Pop").Valoare;
        var g6 = Gestiune.CreeazaInDepozit(depozit, "G6", "Gestiunea G6", null, null).Valoare;

        Assert.Equal("DEP-01", depozit.Cod);
        Assert.Equal((depozit.Id, depozit.Id), (g5.DepozitId!.Value, g6.DepozitId!.Value));
        Assert.Equal(("G5", TipGestiune.Depozit, ana, "Ana Pop"), (g5.Cod, g5.Tip, g5.GestionarId!.Value, g5.GestionarNume));
        Assert.Null(g6.GestionarId);
    }

    [Fact]
    public void Gestionarul_se_schimba_sau_se_scoate()
    {
        var g = Gestiune.CreeazaInDepozit(Depozit.Central(), "G7", "Gestiunea G7", Guid.NewGuid(), "Ana Pop").Valoare;
        var ion = Guid.NewGuid();

        Assert.True(g.Modifica("Gestiunea G7", ion, "Ion Vasile", true).Succes);
        Assert.Equal((ion, "Ion Vasile"), (g.GestionarId!.Value, g.GestionarNume));
        Assert.True(g.Modifica("Gestiunea G7", null, "ignorat", true).Succes);
        Assert.Equal(((Guid?)null, (string?)null), (g.GestionarId, g.GestionarNume));
        Assert.True(g.Modifica("Gestiunea G7", Guid.NewGuid(), " ", true).Esec); // gestionar fără nume
    }

    [Fact]
    public void In_depozitul_inactiv_nu_se_creeaza_gestiuni()
    {
        var depozit = Depozit.Creeaza("DEP-02", "Depozit vechi", null).Valoare;
        depozit.Modifica("Depozit vechi", null, false);

        Assert.True(Gestiune.CreeazaInDepozit(depozit, "G9", "Gestiunea G9", null, null).Esec);
    }

    [Fact]
    public void Gestiunea_veche_fara_depozit_intra_in_depozitul_central_iar_santierul_nu()
    {
        var central = Depozit.Central();
        var santier = Gestiune.Santier(Guid.NewGuid(), "P-1", "Proiect");

        santier.MutaInDepozit(central);

        Assert.Null(santier.DepozitId);
    }
}
