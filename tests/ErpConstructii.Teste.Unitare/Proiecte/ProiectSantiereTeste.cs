using ErpConstructii.Aprovizionare;
using ErpConstructii.Proiecte;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Proiecte;

/// <summary>D-38: editarea completă a proiectului, șantierele lui, compartimentele firmei și solicitantul necesarului.</summary>
public class ProiectSantiereTeste
{
    private static Proiect Proiect() => new("p-01", "Școala 5", "Primăria X", null, "Str. Școlii 5");

    [Fact]
    public void Proiectul_se_editeaza_complet_inclusiv_starea_si_datele_contractuale()
    {
        var p = Proiect();

        var r = p.Modifica(" p-01a ", "Școala gimnazială nr. 5", "Primăria Y", "RO123", "Str. Nouă 1", StareProiect.InExecutie,
            new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28));

        Assert.True(r.Succes);
        Assert.Equal(("P-01A", "Școala gimnazială nr. 5", "Primăria Y", "RO123", StareProiect.InExecutie),
            (p.Cod, p.Denumire, p.Beneficiar, p.CuiBeneficiar, p.Stare));
        Assert.Equal(new DateOnly(2027, 2, 28), p.TermenContractual);
    }

    [Fact]
    public void Termenul_inaintea_inceputului_sau_codul_gol_sunt_refuzate_fara_sa_schimbe_proiectul()
    {
        var p = Proiect();

        var r = p.Modifica("", "Școala", "Primăria", null, "", StareProiect.Contractat, new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1));

        Assert.True(r.Esec);
        Assert.Contains("Codul este obligatoriu", r.Eroare.Mesaj);
        Assert.Contains("Termenul contractual este înaintea datei de început", r.Eroare.Mesaj);
        Assert.Equal(("P-01", StareProiect.Ofertare), (p.Cod, p.Stare));
    }

    [Fact]
    public void Proiectul_arhivat_se_reactiveaza_si_nu_se_arhiveaza_de_doua_ori()
    {
        var p = Proiect();
        var moment = new DateTimeOffset(2026, 11, 7, 20, 0, 0, TimeSpan.FromHours(2));

        Assert.True(p.Arhiveaza("Ana Admin", moment).Succes);
        Assert.Equal((true, "Ana Admin", (DateTimeOffset?)moment), (p.Arhivat, p.ArhivatDeNume, p.ArhivatLa));
        Assert.True(p.Arhiveaza("Ana Admin", moment).Esec);

        Assert.True(p.Reactiveaza().Succes);
        Assert.Equal((false, (string?)null, (DateTimeOffset?)null), (p.Arhivat, p.ArhivatDeNume, p.ArhivatLa));
        Assert.True(p.Reactiveaza().Esec);
    }

    [Fact]
    public void Proiectul_arhivat_e_semnalat_documentelor_noi()
    {
        var d = new DateProiectPentruDocument(Guid.NewGuid(), "P-01", "Școala", "", Arhivat: true);

        Assert.Equal("Proiectul P-01 este arhivat: reactivează-l (Proiecte → Arhivate) ca să adaugi documente pe el.", d.MesajArhivat);
    }

    [Fact]
    public void Santierul_si_compartimentul_cer_denumire_si_se_dezactiveaza_in_loc_sa_se_stearga()
    {
        Assert.True(Santier.Creeaza(Guid.NewGuid(), " ", null).Esec);
        Assert.True(Compartiment.Creeaza("").Esec);

        var s = Santier.Creeaza(Guid.NewGuid(), " Tronson 2 ", " DN2 km 10 ").Valoare;
        Assert.Equal(("Tronson 2", "DN2 km 10", true), (s.Denumire, s.Adresa, s.Activ));
        Assert.True(s.Modifica("Tronson 2", null, activ: false).Succes);
        Assert.False(s.Activ);
    }

    private static readonly LocCandidat Principal = new(Guid.NewGuid(), true, "Șantier principal");
    private static readonly LocCandidat Tronson = new(Guid.NewGuid(), true, "Tronson 2 - Podari");
    private static readonly LocCandidat Mecanizare = new(Guid.NewGuid(), false, "Mecanizare");
    private static readonly LocCandidat[] Locuri = [Principal, Tronson, Mecanizare];

    [Theory]
    [InlineData("SANTIER PRINCIPAL", "Șantier principal")] // fără diacritice / majuscule
    [InlineData("  mecanizare ", "Mecanizare")]
    [InlineData("Tronson 2", "Tronson 2 - Podari")] // o singură denumire care îl conține
    [InlineData("Compartiment mecanizare", "Mecanizare")]
    public void Textul_de_pe_necesar_se_potriveste_cu_santierul_sau_compartimentul(string text, string asteptat) =>
        Assert.Equal(asteptat, PotrivireSolicitant.Cauta(text, Locuri)?.Denumire);

    [Theory]
    [InlineData("Comp test")]
    [InlineData("")]
    [InlineData("ant")] // prea scurt / ambiguu
    public void Fara_potrivire_sigura_utilizatorul_alege(string text) =>
        Assert.Null(PotrivireSolicitant.Cauta(text, Locuri));

    [Fact]
    public void Necesarul_retine_santierul_sau_compartimentul_si_ii_tipareste_denumirea()
    {
        var n = NecesarMateriale.Emite(2026, 1, Guid.NewGuid(), "P-01", "Școala", "provizoriu", new DateOnly(2026, 11, 7), Guid.NewGuid(), "Ion",
            Guid.NewGuid(), 1, [new LinieNecesarMateriale("Ciment", "SAC", 10, string.Empty, null)]).Valoare;

        Assert.True(n.StabilesteSolicitantul(Tronson).Succes);
        Assert.Equal((Tronson.Id, (Guid?)null, "Tronson 2 - Podari"), (n.SantierId!.Value, n.CompartimentId, n.CompartimentSantier));

        Assert.True(n.StabilesteSolicitantul(Mecanizare).Succes);
        Assert.Equal(((Guid?)null, Mecanizare.Id, "Mecanizare"), (n.SantierId, n.CompartimentId!.Value, n.CompartimentSantier));

        Assert.True(n.Aproba(Guid.NewGuid(), "Șef", DateTimeOffset.UtcNow).Succes);
        Assert.True(n.StabilesteSolicitantul(Principal).Esec);
    }
}
