using ErpConstructii.Proiecte;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Proiecte;

/// <summary>Sediul firmei ca proiect special (D-47): centru de cost pentru cheltuielile de sediu.</summary>
public class SediuFirmaTeste
{
    [Fact]
    public void Sediul_nu_cere_beneficiar_si_porneste_in_executie()
    {
        Assert.Empty(Proiect.Valideaza(TipProiect.Sediu, "SEDIU-CV", "Sediul central Cernavodă", null));

        var sediu = new Proiect("sediu-cv", "Sediul central Cernavodă", string.Empty, null, "Str. Unirii 20", TipProiect.Sediu);

        Assert.Equal((TipProiect.Sediu, true, StareProiect.InExecutie, "SEDIU-CV"), (sediu.Tip, sediu.EsteSediu, sediu.Stare, sediu.Cod));
    }

    [Fact]
    public void Lucrarea_cere_beneficiar_si_porneste_in_ofertare()
    {
        Assert.Contains("Beneficiarul lucrării este obligatoriu.", Proiect.Valideaza(TipProiect.Lucrare, "P-1", "Bloc P+4", " "));
        Assert.Equal(StareProiect.Ofertare, new Proiect("P-1", "Bloc P+4", "Primăria", null, "Str. A 1").Stare);
    }

    [Fact]
    public void Lucrarea_se_poate_schimba_in_sediu_fara_beneficiar()
    {
        var p = new Proiect("STOC", "Stoc general", "Firma", null, "Str. A 1");

        var r = p.Modifica("STOC", "Sediu și depozit", string.Empty, null, "Str. A 1", StareProiect.InExecutie, null, null, TipProiect.Sediu);

        Assert.True(r.Succes);
        Assert.Equal((TipProiect.Sediu, string.Empty), (p.Tip, p.Beneficiar));
        // Înapoi în lucrare, beneficiarul redevine obligatoriu.
        Assert.True(p.Modifica("STOC", "Sediu și depozit", string.Empty, null, "Str. A 1", StareProiect.InExecutie, null, null, TipProiect.Lucrare).Esec);
    }
}
