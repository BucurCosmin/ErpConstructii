using ErpConstructii.Aprovizionare;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Modificarea și ștergerea notelor de comandă emise, cu istoric (D-44).</summary>
public class CorectieNotaComandaTeste
{
    private static readonly Guid Proiect = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateTimeOffset Acum = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(2));

    private static DateNotaComanda Date(string furnizor = "TRITON SRL", Guid? proiect = null, params LinieNotaComanda[] linii) => new(
        null, new DateOnly(2026, 11, 7), proiect ?? Proiect, "P-A", "CTRF-HSJV-002", null, furnizor, null,
        linii.Length > 0 ? linii : [new LinieNotaComanda("Ciment CEM II 40kg", "SAC", 100, "imediat", null), new LinieNotaComanda("Sârmă neagră", "KG", 15, null, null)],
        null);

    private static NotaComanda Emisa() => NotaComanda.Emite(2026, 4, Date(), null, null, Guid.NewGuid(), "Ion Pop").Valoare;

    private static NotaComanda AcceptataDinNecesar()
    {
        var necesar = NecesarMateriale.Emite(2026, 3, Proiect, "P-A", "Școala 5", "Șantier central", new DateOnly(2026, 11, 6), Ana, "Ion Maistru", Guid.NewGuid(), 1,
            [new LinieNecesarMateriale("Ciment CEM II 40kg", "SAC", 100, "urgent", null), new LinieNecesarMateriale("Plasă sudată 6mm", "BUC", 20, string.Empty, null)]).Valoare;
        var nota = NotaComanda.GenereazaDinNecesar(2026, 8, necesar, Ana, "Ion Maistru").Valoare;
        Assert.True(nota.Accepta(Ana, "Dan Șef", Acum).Succes);
        return nota;
    }

    private static DateComandaFurnizor Furnizor(string denumire) => new(new DateOnly(2026, 11, 8), null, Guid.NewGuid(), denumire, null, null, null, null, null, null);

    [Fact]
    public void Modificarea_schimba_nota_si_da_corectura_cu_rezumatul_diferentelor()
    {
        var n = Emisa();

        var r = n.Corecteaza(Date("ARABESQUE SRL", null, new LinieNotaComanda("Ciment CEM II 40kg", "SAC", 120, "imediat", null)),
            "furnizor greșit", Ana, "Ana Pop", Acum);

        Assert.True(r.Succes);
        Assert.Equal(("NC/2026/0004", "ARABESQUE SRL", 1, StareNotaComanda.Emisa), (n.NumarAfisat, n.FurnizorDenumire, n.Linii.Count, n.Stare));
        var c = r.Valoare;
        Assert.Equal((n.Id, TipCorectieNotaComanda.Modificare, "furnizor greșit", "Ana Pop"), (c.NotaComandaId, c.Tip, c.Motiv, c.CorectatDeNume));
        Assert.Contains("Furnizor: TRITON SRL → ARABESQUE SRL", c.Rezumat);
        Assert.Contains("linia 1 „Ciment CEM II 40kg”: cantitate 100 → 120", c.Rezumat);
        Assert.Contains("linia 2 scoasă: Sârmă neagră", c.Rezumat);
    }

    [Fact]
    public void Modificarea_fara_motiv_fara_schimbari_sau_pe_alt_proiect_e_refuzata()
    {
        var n = Emisa();

        Assert.Contains("Motivul", n.Corecteaza(Date("ALT SRL"), " ", Ana, "Ana Pop", Acum).Eroare.Mesaj);
        Assert.Contains("Nu ai schimbat nimic", n.Corecteaza(Date(), "verificare", Ana, "Ana Pop", Acum).Eroare.Mesaj);
        Assert.Contains("Proiectul", n.Corecteaza(Date(proiect: Guid.NewGuid()), "alt proiect", Ana, "Ana Pop", Acum).Eroare.Mesaj);
    }

    [Fact]
    public void Stergerea_retine_cine_cand_de_ce_si_ce_continea_nota()
    {
        var n = Emisa();

        var r = n.Sterge("dublură", null, Ana, "Ana Pop", Acum);

        Assert.True(r.Succes);
        var s = r.Valoare;
        Assert.Equal(("NC/2026/0004", "TRITON SRL", "dublură", "Ana Pop", Acum), (s.Numar, s.FurnizorDenumire, s.Motiv, s.StearsaDeNume, s.StearsaLa));
        Assert.Contains("Ciment CEM II 40kg", s.DateJson);
        Assert.True(n.Sterge("", null, Ana, "Ana Pop", Acum).Esec);
    }

    [Fact]
    public void Comanda_intreaga_dintr_o_nota_acceptata_revine_la_achizitii_fara_furnizor()
    {
        var nota = AcceptataDinNecesar();
        var emisa = nota.ComandaLaFurnizor(Furnizor("TRITON SRL"), [new(0, null, 100, "luni", 30m), new(1, null, 20, null, null)], 2026, 99, null, null, Ana, "Ana").Valoare;
        Assert.Same(nota, emisa);
        Assert.True(nota.RevineLaAchizitiiLaStergere);
        Assert.True(nota.Sterge("greșit", null, Ana, "Ana", Acum).Esec); // nu se șterge, revine

        var r = nota.AnuleazaComanda("furnizorul nu are marfă", Ana, "Ana Pop", Acum);

        Assert.True(r.Succes);
        Assert.Equal((StareNotaComanda.Acceptata, "", (Guid?)null), (nota.Stare, nota.FurnizorDenumire, nota.FurnizorId));
        Assert.Equal([(100m, (string?)null), (20m, null)], nota.Linii.Select(l => (l.Cantitate, l.Termen)));
        Assert.Equal(TipCorectieNotaComanda.ComandaAnulata, r.Valoare.Tip);
        Assert.Contains("TRITON SRL", r.Valoare.Rezumat);
    }

    [Fact]
    public void Materialele_unei_comenzi_desprinse_si_sterse_revin_pe_nota_acceptata()
    {
        var nota = AcceptataDinNecesar();
        // Se comandă 40 din 100 saci de ciment la TRITON: comanda nouă, pe notă rămân 60 saci + plasa.
        var comanda = nota.ComandaLaFurnizor(Furnizor("TRITON SRL"), [new(0, null, 40, null, null)], 2026, 9, null, null, Ana, "Ana").Valoare;
        Assert.Equal(nota.Id, comanda.NotaSursaId);
        Assert.False(comanda.RevineLaAchizitiiLaStergere);

        Assert.True(nota.PrimesteInapoi(comanda.Linii).Succes);

        // 60 + 40 = 100 saci, aceeași linie; plasa neschimbată.
        Assert.Equal([("Ciment CEM II 40kg", 100m), ("Plasă sudată 6mm", 20m)], nota.Linii.Select(l => (l.Denumire, l.Cantitate)));
        Assert.True(comanda.Sterge("comandă dublă", "Materialele au revenit pe NC/2026/0008.", Ana, "Ana", Acum).Succes);
    }
}
