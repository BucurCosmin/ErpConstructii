using ErpConstructii.Aprovizionare;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>D-37: necesarul generează nota de comandă de acceptat; achizițiile o trimit la unul sau mai mulți furnizori.</summary>
public class NotaComandaDinNecesarTeste
{
    private static readonly Guid Emitent = Guid.NewGuid();
    private static readonly Guid SefSantier = Guid.NewGuid();
    private static readonly DateTimeOffset Acum = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(2));

    private static NecesarMateriale Necesar() => NecesarMateriale.Emite(
        2026, 3, Guid.NewGuid(), "P-01", "Reabilitare școala 5", "Șantier central", new DateOnly(2026, 11, 6), Emitent, "Ion Maistru", Guid.NewGuid(), 1,
        [
            new LinieNecesarMateriale("Ciment CEM II 40kg", "SAC", 100, "urgent", null),
            new LinieNecesarMateriale("Plasă sudată 6mm", "BUC", 20, string.Empty, null),
            new LinieNecesarMateriale("Sârmă neagră", "KG", 15, string.Empty, null),
        ]).Valoare;

    private static NotaComanda Acceptata(NecesarMateriale? necesar = null)
    {
        var nota = NotaComanda.GenereazaDinNecesar(2026, 8, necesar ?? Necesar(), Emitent, "Ion Maistru").Valoare;
        Assert.True(nota.Accepta(SefSantier, "Dan Șef", Acum).Succes);
        return nota;
    }

    private static DateComandaFurnizor Furnizor(string denumire) =>
        new(new DateOnly(2026, 11, 8), null, Guid.NewGuid(), denumire, null, null, null, null, null, null);

    [Fact]
    public void Nota_generata_are_materialele_necesarului_fara_furnizor_si_asteapta_acceptarea()
    {
        var necesar = Necesar();

        var nota = NotaComanda.GenereazaDinNecesar(2026, 8, necesar, Emitent, "Ion Maistru").Valoare;

        Assert.Equal(("NC/2026/0008", StareNotaComanda.DeAcceptat, necesar.Id, ""), (nota.NumarAfisat, nota.Stare, nota.NecesarId!.Value, nota.FurnizorDenumire));
        Assert.Equal(["Ciment CEM II 40kg", "Plasă sudată 6mm", "Sârmă neagră"], nota.Linii.Select(l => l.Denumire));
        Assert.Equal("urgent", nota.Linii[0].Observatii);
        Assert.Null(nota.Linii[1].Observatii);
        Assert.False(nota.EsteBlocat);
    }

    [Fact]
    public void Acceptata_nu_e_blocata_dar_respinsa_da_si_nu_mai_poate_fi_acceptata()
    {
        var acceptata = Acceptata();
        Assert.Equal((StareNotaComanda.Acceptata, "Dan Șef", false), (acceptata.Stare, acceptata.AcceptataDeNume, acceptata.EsteBlocat));

        var respinsa = NotaComanda.GenereazaDinNecesar(2026, 9, Necesar(), Emitent, "Ion Maistru").Valoare;
        Assert.True(respinsa.Respinge(SefSantier, "Dan Șef", Acum, "nu e în buget").Succes);
        Assert.Equal((StareNotaComanda.Respinsa, "nu e în buget", true), (respinsa.Stare, respinsa.MotivRespingere, respinsa.EsteBlocat));
        Assert.True(respinsa.Accepta(SefSantier, "Dan Șef", Acum).Esec);
    }

    [Fact]
    public void Respingerea_fara_motiv_e_refuzata()
    {
        var nota = NotaComanda.GenereazaDinNecesar(2026, 9, Necesar(), Emitent, "Ion Maistru").Valoare;

        Assert.True(nota.Respinge(SefSantier, "Dan Șef", Acum, "  ").Esec);
        Assert.Equal(StareNotaComanda.DeAcceptat, nota.Stare);
    }

    [Fact]
    public void Nota_neacceptata_nu_se_trimite_la_furnizor()
    {
        var nota = NotaComanda.GenereazaDinNecesar(2026, 8, Necesar(), Emitent, "Ion Maistru").Valoare;

        var r = nota.ComandaLaFurnizor(Furnizor("TRITON SRL"), [new LinieDeComandat(0, null, 100, null, null)], 2026, 9, null, null, Guid.NewGuid(), "Ana Achiziții");

        Assert.True(r.Esec);
    }

    [Fact]
    public void Toata_nota_la_un_furnizor_devine_emisa_si_isi_pastreaza_numarul()
    {
        var nota = Acceptata();
        var linii = new[] { new LinieDeComandat(0, null, 100, "10.11.2026", null), new LinieDeComandat(1, null, 20, null, null), new LinieDeComandat(2, null, 15, null, null) };

        var r = nota.ComandaLaFurnizor(Furnizor("TRITON SRL"), linii, 2026, 9, null, null, Guid.NewGuid(), "Ana Achiziții");

        Assert.True(r.Succes);
        Assert.Same(nota, r.Valoare);
        Assert.Equal(("NC/2026/0008", StareNotaComanda.Emisa, "TRITON SRL", "Ana Achiziții"), (nota.NumarAfisat, nota.Stare, nota.FurnizorDenumire, nota.InregistrataDeNume));
        Assert.Equal("10.11.2026", nota.Linii[0].Termen);
        Assert.Equal("Dan Șef", nota.AcceptataDeNume); // acceptarea rămâne consemnată
        Assert.True(nota.EsteBlocat);
    }

    [Fact]
    public void Liniile_impartite_pe_furnizori_pleaca_in_nota_noua_iar_restul_ramane_acceptat()
    {
        var nota = Acceptata();

        // La TRITON: tot cimentul și 5 din cele 20 de plase → rămân 15 plase și sârma, pentru alt furnizor.
        var r = nota.ComandaLaFurnizor(Furnizor("TRITON SRL"),
            [new LinieDeComandat(0, "Ciment CEM II/A-LL 42,5R 40kg", 100, null, null), new LinieDeComandat(1, null, 5, null, null)],
            2026, 9, null, null, Guid.NewGuid(), "Ana Achiziții");

        Assert.True(r.Succes);
        var emisa = r.Valoare;
        Assert.NotSame(nota, emisa);
        Assert.Equal(("NC/2026/0009", StareNotaComanda.Emisa, nota.Id, nota.NecesarId), (emisa.NumarAfisat, emisa.Stare, emisa.NotaSursaId!.Value, emisa.NecesarId));
        Assert.Equal([("Ciment CEM II/A-LL 42,5R 40kg", 100m), ("Plasă sudată 6mm", 5m)], emisa.Linii.Select(l => (l.Denumire, l.Cantitate)));
        Assert.Equal("Dan Șef", emisa.AcceptataDeNume);

        Assert.Equal(StareNotaComanda.Acceptata, nota.Stare);
        Assert.Equal([("Plasă sudată 6mm", 15m), ("Sârmă neagră", 15m)], nota.Linii.Select(l => (l.Denumire, l.Cantitate)));

        // Restul, la al doilea furnizor: nota inițială devine emisă cu numărul ei.
        var r2 = nota.ComandaLaFurnizor(Furnizor("DEDEMAN SRL"),
            [new LinieDeComandat(0, null, 15, null, null), new LinieDeComandat(1, null, 15, null, null)], 2026, 10, null, null, Guid.NewGuid(), "Ana Achiziții");
        Assert.Same(nota, r2.Valoare);
        Assert.Equal(("NC/2026/0008", StareNotaComanda.Emisa, "DEDEMAN SRL"), (nota.NumarAfisat, nota.Stare, nota.FurnizorDenumire));
    }

    [Fact]
    public void Cantitatea_peste_cea_ramasa_sau_fara_furnizor_e_refuzata()
    {
        var nota = Acceptata();

        var preaMult = nota.ComandaLaFurnizor(Furnizor("TRITON SRL"), [new LinieDeComandat(1, null, 21, null, null)], 2026, 9, null, null, Guid.NewGuid(), "Ana");
        var faraFurnizor = nota.ComandaLaFurnizor(Furnizor(" "), [new LinieDeComandat(1, null, 2, null, null)], 2026, 9, null, null, Guid.NewGuid(), "Ana");

        Assert.Contains("cel mult 20 BUC", preaMult.Eroare.Mesaj);
        Assert.Contains("Furnizorul este obligatoriu", faraFurnizor.Eroare.Mesaj);
        Assert.Equal(3, nota.Linii.Count);
        Assert.Equal(StareNotaComanda.Acceptata, nota.Stare);
    }
}
