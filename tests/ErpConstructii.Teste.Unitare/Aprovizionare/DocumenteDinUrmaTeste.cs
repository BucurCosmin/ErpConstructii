using ErpConstructii.Aprovizionare;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Documentele din urmă (D-45): importate din arhivă, fără legăturile cerute documentelor curente.</summary>
public class DocumenteDinUrmaTeste
{
    private static readonly Guid Proiect = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateTimeOffset Acum = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(2));

    private static NecesarMateriale NecesarImportat(Guid? proiect = null) => NecesarMateriale.Importa(
        2026, 5, "123", proiect ?? Proiect, "P-A", "Școala 5", "Șantier central", new DateOnly(2026, 3, 2), Ana, "Ana Pop", Guid.Empty, 0,
        [new LinieNecesarMateriale("Ciment CEM II 40kg", "SAC", 100, string.Empty, null)], Guid.NewGuid()).Valoare;

    private static NotaComanda NotaImportata(bool dinUrma = true) => NotaComanda.Importa(2026, 7,
        new DateNotaComanda("45", new DateOnly(2026, 3, 4), Proiect, "P-A", null, null, "TRITON SRL", null,
            [new LinieNotaComanda("Ciment CEM II 40kg", "SAC", 100, null, null)], null),
        Guid.NewGuid(), Ana, "Ana Pop", dinUrma).Valoare;

    [Fact]
    public void Necesarul_din_urma_intra_aprobat_de_cel_care_il_importa()
    {
        var n = NecesarImportat();

        Assert.True(n.MarcheazaDinUrma(Acum).Succes);

        // Fluxul curent nu permite emitentului să-și aprobe necesarul; din arhivă, aprobarea s-a dat pe hârtie.
        Assert.Equal((true, StareNecesarMateriale.Aprobata, "Ana Pop", (DateTimeOffset?)Acum), (n.DinUrma, n.Stare, n.DecisaDeNume, n.DecisaLa));
        Assert.True(n.MarcheazaDinUrma(Acum).Esec); // o singură dată, doar pe necesarul abia importat
    }

    [Fact]
    public void Nota_importata_fara_necesar_se_leaga_ulterior_cu_istoric()
    {
        var nota = NotaImportata();
        var necesar = NecesarImportat();
        Assert.Equal((true, (Guid?)null), (nota.DinUrma, nota.NecesarId));

        var r = nota.LeagaDeNecesar(necesar, Ana, "Ana Pop", Acum);

        Assert.True(r.Succes);
        Assert.Equal(necesar.Id, nota.NecesarId);
        Assert.Equal((nota.Id, TipCorectieNotaComanda.NecesarLegat, "Necesar: — → NM/2026/0005"), (r.Valoare.NotaComandaId, r.Valoare.Tip, r.Valoare.Rezumat));
        Assert.Contains("are deja", nota.LeagaDeNecesar(NecesarImportat(), Ana, "Ana Pop", Acum).Eroare.Mesaj);
    }

    [Fact]
    public void Nota_importata_inainte_de_bifa_se_marcheaza_din_urma_cu_istoric()
    {
        var nota = NotaImportata(dinUrma: false);

        var r = nota.SchimbaDinUrma(true, Ana, "Ana Pop", Acum);

        Assert.True(r.Succes);
        Assert.Equal((true, "Document din urmă: nu → da"), (nota.DinUrma, r.Valoare.Rezumat));
        Assert.True(nota.SchimbaDinUrma(true, Ana, "Ana Pop", Acum).Esec); // deja marcată
    }

    [Fact]
    public void Necesarul_de_pe_alt_proiect_nu_se_leaga()
    {
        var r = NotaImportata(dinUrma: false).LeagaDeNecesar(NecesarImportat(Guid.NewGuid()), Ana, "Ana Pop", Acum);

        Assert.True(r.Esec);
        Assert.Contains("nota pe P-A", r.Eroare.Mesaj);
    }

    [Fact]
    public void Avizul_fara_nota_de_comanda_se_primeste_doar_din_urma()
    {
        var date = new DateAviz("INTFSN/20488", new DateOnly(2026, 3, 5), Proiect, "P-A", null, Guid.NewGuid(), "TRITON SRL", null,
            [new LinieAviz("Ciment CEM II 40kg", "SAC", 100)], null);

        var curent = AvizInsotire.Importa(2026, 1, date, null, null, false, Guid.NewGuid(), Ana, "Ana Pop");
        var dinUrma = AvizInsotire.Importa(2026, 1, date, null, null, false, Guid.NewGuid(), Ana, "Ana Pop", dinUrma: true);

        Assert.Contains("Nota de comandă a avizului este obligatorie", curent.Eroare.Mesaj);
        Assert.True(dinUrma.Succes);
        Assert.Equal((true, (Guid?)null, (string?)null), (dinUrma.Valoare.DinUrma, dinUrma.Valoare.NotaComandaId, dinUrma.Valoare.NotaComandaNumar));
    }
}
