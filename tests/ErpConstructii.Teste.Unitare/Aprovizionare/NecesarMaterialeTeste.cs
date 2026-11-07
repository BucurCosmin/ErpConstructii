using ErpConstructii.Aprovizionare;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

public sealed class NecesarMaterialeTeste
{
    [Fact]
    public void Emite_nota_cu_numar_si_stare_initiala_de_aprobare()
    {
        var rezultat = CreeazaNota();

        Assert.True(rezultat.Succes);
        Assert.Equal("NM/2026/0007", rezultat.Valoare.NumarAfisat);
        Assert.Equal(StareNecesarMateriale.InAsteptareAprobare, rezultat.Valoare.Stare);
        Assert.Equal("Nume Emitent", rezultat.Valoare.EmitentNume);
        Assert.False(rezultat.Valoare.EsteBlocat); // încă se aprobă / respinge; blocat abia după decizie
    }

    [Fact]
    public void Nota_aprobata_nu_mai_poate_fi_aprobata_sau_respinsa()
    {
        var nota = CreeazaNota().Valoare;
        var aprobator = Guid.NewGuid();

        Assert.True(nota.Aproba(aprobator, "Aprobator Test", DateTimeOffset.UtcNow).Succes);
        Assert.Equal(aprobator, nota.DecisaDeId);
        Assert.Equal("Aprobator Test", nota.DecisaDeNume);
        Assert.False(nota.Aproba(Guid.NewGuid(), "Alt aprobator", DateTimeOffset.UtcNow).Succes);
        Assert.False(nota.Respinge(Guid.NewGuid(), "Alt aprobator", DateTimeOffset.UtcNow, "Motiv").Succes);
        Assert.Equal(StareNecesarMateriale.Aprobata, nota.Stare);
        Assert.True(nota.EsteBlocat);
    }

    [Fact]
    public void Emitentul_nu_isi_poate_aproba_nota()
    {
        var nota = CreeazaNota().Valoare;

        var rezultat = nota.Aproba(nota.EmitentId, "Emitent", DateTimeOffset.UtcNow);

        Assert.False(rezultat.Succes);
        Assert.Equal("interzis", rezultat.Eroare.Cod);
        Assert.Equal(StareNecesarMateriale.InAsteptareAprobare, nota.Stare);
    }

    [Fact]
    public void Respingerea_cere_motiv()
    {
        var nota = CreeazaNota().Valoare;

        var rezultat = nota.Respinge(Guid.NewGuid(), "Decident", DateTimeOffset.UtcNow, " ");

        Assert.False(rezultat.Succes);
        Assert.Equal(StareNecesarMateriale.InAsteptareAprobare, nota.Stare);
    }

    [Fact]
    public void Cantitatea_nepozitiva_impiedica_emiterea()
    {
        var rezultat = NecesarMateriale.Emite(
            2026,
            1,
            Guid.NewGuid(),
            "P-01",
            "Lucrare",
            "Șantier central",
            new DateOnly(2026, 11, 3),
            Guid.NewGuid(),
            "Nume Emitent",
            Guid.NewGuid(),
            1,
            [new LinieNecesarMateriale("Ciment", "kg", 0, string.Empty, null)]);

        Assert.False(rezultat.Succes);
    }

    private static ErpConstructii.SharedKernel.Rezultat<NecesarMateriale> CreeazaNota() =>
        NecesarMateriale.Emite(
            2026,
            7,
            Guid.NewGuid(),
            "P-01",
            "Reabilitare",
            "Șantier central",
            new DateOnly(2026, 11, 3),
            Guid.NewGuid(),
            "Nume Emitent",
            Guid.NewGuid(),
            2,
            [new LinieNecesarMateriale("Ciment", "kg", 25, string.Empty, null)]);
}
