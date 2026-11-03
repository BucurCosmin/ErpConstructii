using ErpConstructii.Aprovizionare;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

public sealed class NotaComandaTeste
{
    [Fact]
    public void Emite_nota_cu_numar_si_stare_initiala_de_aprobare()
    {
        var rezultat = CreeazaNota();

        Assert.True(rezultat.Succes);
        Assert.Equal("NC/2026/0007", rezultat.Valoare.NumarAfisat);
        Assert.Equal(StareNotaComanda.InAsteptareAprobare, rezultat.Valoare.Stare);
        Assert.Equal("Nume Emitent", rezultat.Valoare.EmitentNume);
        Assert.True(rezultat.Valoare.EsteBlocat);
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
        Assert.Equal(StareNotaComanda.Aprobata, nota.Stare);
    }

    [Fact]
    public void Emitentul_nu_isi_poate_aproba_nota()
    {
        var nota = CreeazaNota().Valoare;

        var rezultat = nota.Aproba(nota.EmitentId, "Emitent", DateTimeOffset.UtcNow);

        Assert.False(rezultat.Succes);
        Assert.Equal("interzis", rezultat.Eroare.Cod);
        Assert.Equal(StareNotaComanda.InAsteptareAprobare, nota.Stare);
    }

    [Fact]
    public void Respingerea_cere_motiv()
    {
        var nota = CreeazaNota().Valoare;

        var rezultat = nota.Respinge(Guid.NewGuid(), "Decident", DateTimeOffset.UtcNow, " ");

        Assert.False(rezultat.Succes);
        Assert.Equal(StareNotaComanda.InAsteptareAprobare, nota.Stare);
    }

    [Fact]
    public void Cantitatea_nepozitiva_impiedica_emiterea()
    {
        var rezultat = NotaComanda.Emite(
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
            [new LinieNotaComanda("Ciment", "kg", 0, string.Empty, null)]);

        Assert.False(rezultat.Succes);
    }

    private static ErpConstructii.SharedKernel.Rezultat<NotaComanda> CreeazaNota() =>
        NotaComanda.Emite(
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
            [new LinieNotaComanda("Ciment", "kg", 25, string.Empty, null)]);
}
