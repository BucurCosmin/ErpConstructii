using ErpConstructii.Aprovizionare;
using ErpConstructii.Proiecte;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Proiecte;

/// <summary>Șantierul pus din greșeală se șterge; documentele lui se mută pe alt șantier activ al proiectului.</summary>
public class StergereSantierTeste
{
    private static readonly Guid Proiect = Guid.NewGuid();

    private static Santier Santier(string denumire, bool activ = true, Guid? proiect = null)
    {
        var s = ErpConstructii.Proiecte.Santier.Creeaza(proiect ?? Proiect, denumire, null).Valoare;
        if (!activ)
            s.Modifica(denumire, null, false);
        return s;
    }

    [Fact]
    public void Santierul_fara_documente_se_sterge_daca_ramane_alt_santier_activ()
    {
        var principal = Santier("Șantier principal");
        var gresit = Santier("Santer B");

        var r = StergereSantier.Verifica(gresit, [principal, gresit], documente: 0, mutaPe: null);

        Assert.True(r.Succes, r.Eroare.Mesaj);
        Assert.Null(r.Valoare);
    }

    [Fact]
    public void Ultimul_santier_activ_nu_se_sterge_si_nu_se_dezactiveaza()
    {
        var principal = Santier("Șantier principal");
        var inactiv = Santier("Vechi", activ: false);
        var altProiect = Santier("Alt proiect", proiect: Guid.NewGuid());

        Assert.True(StergereSantier.Verifica(principal, [principal, inactiv, altProiect], 0, null).Esec);
        Assert.True(StergereSantier.VerificaDezactivarea(principal, [principal, inactiv, altProiect]).Esec);
        Assert.True(StergereSantier.VerificaDezactivarea(inactiv, [principal, inactiv]).Succes);
    }

    [Fact]
    public void Cu_documente_cere_santierul_activ_al_proiectului_pe_care_se_muta()
    {
        var principal = Santier("Șantier principal");
        var gresit = Santier("Santer B");
        var inactiv = Santier("Vechi", activ: false);
        var altProiect = Santier("Alt proiect", proiect: Guid.NewGuid());
        Santier[] toate = [principal, gresit, inactiv, altProiect];

        var faraAlegere = StergereSantier.Verifica(gresit, toate, documente: 3, mutaPe: null);
        Assert.Equal(StergereSantier.CodDocumenteDeMutat, faraAlegere.Eroare.Cod);
        Assert.Contains("3 documente", faraAlegere.Eroare.Mesaj);

        Assert.True(StergereSantier.Verifica(gresit, toate, 3, inactiv.Id).Esec);
        Assert.True(StergereSantier.Verifica(gresit, toate, 3, altProiect.Id).Esec);
        Assert.True(StergereSantier.Verifica(gresit, toate, 3, gresit.Id).Esec);
        Assert.Same(principal, StergereSantier.Verifica(gresit, toate, 3, principal.Id).Valoare);
    }

    [Fact]
    public void Necesarul_aprobat_se_muta_pe_santierul_ramas()
    {
        var principal = Guid.NewGuid();
        var necesar = NecesarMateriale.Importa(2026, 4, "12", Proiect, "P-A", "Școala", "Santer B", new DateOnly(2026, 3, 2), Guid.NewGuid(), "Ana",
            Guid.Empty, 0, [new LinieNecesarMateriale("Ciment", "sac", 10, string.Empty, null)], Guid.NewGuid()).Valoare;
        Assert.True(necesar.StabilesteSolicitantul(new LocCandidat(Guid.NewGuid(), true, "Santer B")).Succes);
        Assert.True(necesar.MarcheazaDinUrma(DateTimeOffset.UtcNow).Succes);

        var mutare = necesar.MutaPeSantier(new LocCandidat(principal, true, "Șantier principal"), DateTimeOffset.UtcNow);
        Assert.True(mutare.Succes, mutare.Eroare.Mesaj);
        // Corectura care permite schimbarea necesarului aprobat (regula 6), cu șantierul vechi și cel nou.
        Assert.Equal((necesar.Id, "Santer B", "Șantier principal"), (mutare.Valoare.DocumentCorectatId, mutare.Valoare.SantierVechi, mutare.Valoare.SantierNou));

        Assert.Equal((principal, "Șantier principal", StareNecesarMateriale.Aprobata), (necesar.SantierId!.Value, necesar.CompartimentSantier, necesar.Stare));
        Assert.True(necesar.MutaPeSantier(new LocCandidat(Guid.NewGuid(), false, "Compartiment"), DateTimeOffset.UtcNow).Esec);
    }
}
