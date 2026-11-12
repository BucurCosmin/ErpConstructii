using ErpConstructii.Devize.Import.Isdp;

namespace ErpConstructii.Teste.Isdp;

/// <summary>Formularul F3 real (EUR) și legătura lui cu devizul cu resurse (RON).</summary>
public class ListaF3Teste
{
    private static ListaF3 Lista(Func<int, int, object?, object?>? transforma = null)
    {
        var r = ParserListaF3.Parseaza(transforma is null ? Fixturi.Incarca(Fixturi.ListaF3) : Fixturi.Incarca(Fixturi.ListaF3, transforma));
        Assert.DoesNotContain(r.Probleme, p => p.Severitate == SeveritateIsdp.Eroare);
        return r.Valoare!;
    }

    private static DevizIsdp Deviz() => ParserDevizResurse.Parseaza(Fixturi.Incarca(Fixturi.DevizResurse)).Valoare!;

    [Fact]
    public void Structura_F3_peste_salturile_de_pagina()
    {
        var l = Lista();

        Assert.Equal("EUR", l.Moneda);
        Assert.Equal(3, l.Capitole.Count);
        Assert.Equal(15, l.Articole().Count());
        Assert.Equal(99643.23m, l.TotalGeneral);

        // Articolul 2.3 este pe pagina 2, după rândurile repetate de antet.
        var a23 = l.Articole().Single(a => a.Numar == "2.3");
        Assert.Equal("tona", a23.UnitateMasura);
        Assert.Equal(101.15m, a23.Total);
        Assert.Equal((42.15m, 101.15m), a23.Componente[TipResursaIsdp.Transport]);
    }

    [Fact]
    public void Componentele_articolului()
    {
        var a = Lista().Articole().First();
        Assert.Equal("1.1", a.Numar);
        Assert.Equal(131.37m, a.PretUnitar);
        Assert.Equal((1.26m, 404.15m), a.Componente[TipResursaIsdp.Material]);
        Assert.Equal((130.11m, 41741.30m), a.Componente[TipResursaIsdp.Manopera]);
    }

    [Fact]
    public void Pretul_de_oferta_se_reproduce_exact_din_deviz_cu_cursul_dedus()
    {
        var rezultat = ReconciliereF3.Reconciliaza(Deviz(), Lista());

        Assert.Equal(5.23m, rezultat.Curs);
        Assert.Equal(15, rezultat.Perechi.Count);
        Assert.Equal(60, rezultat.ComponenteIdentice);        // 15 articole × 4 componente
        Assert.Equal(0, rezultat.ComponenteDiferite);
        Assert.Empty(rezultat.Probleme);
    }

    [Fact]
    public void Perechile_leaga_numarul_F3_de_WBS_si_simbol()
    {
        var p = ReconciliereF3.Reconciliaza(Deviz(), Lista()).Perechi;

        Assert.Equal("0002.1.1.1.1", p.Single(x => x.F3.Numar == "1.1").Articol.Wbs);
        Assert.Equal("TSF01A1 asim", p.Single(x => x.F3.Numar == "3.1").Articol.Simbol);
        Assert.Equal(42145.45m, p.Single(x => x.F3.Numar == "1.1").PretCalculat!.Total);
    }

    [Fact]
    public void Cu_un_curs_gresit_preturile_nu_se_potrivesc()
    {
        var rezultat = ReconciliereF3.Reconciliaza(Deviz(), Lista(), curs: 5.0m);
        Assert.True(rezultat.ComponenteDiferite > 0);
        Assert.Contains(rezultat.Probleme, p => p.Severitate == SeveritateIsdp.Eroare);
    }

    [Fact]
    public void Lista_de_la_beneficiar_fara_preturi_se_reconciliaza_doar_pe_cantitati()
    {
        // Coloanele „Prețul unitar” și „TOTALUL” golite (cum vine F3 într-o documentație de atribuire).
        var faraPreturi = Lista((_, c, v) => (c == 10 || c == 11) && v is double ? null : v);

        Assert.False(faraPreturi.ArePreturi);
        var rezultat = ReconciliereF3.Reconciliaza(Deviz(), faraPreturi);
        Assert.Equal(15, rezultat.Perechi.Count);
        Assert.Empty(rezultat.Probleme);
    }

    [Fact]
    public void Detectorul_recunoaste_ambele_rapoarte()
    {
        Assert.Equal(TipRaportIsdp.DevizCuResurse, DetectorIsdp.Detecteaza(Fixturi.Incarca(Fixturi.DevizResurse)).Tip);
        Assert.Equal(TipRaportIsdp.ListaCantitatiF3, DetectorIsdp.Detecteaza(Fixturi.Incarca(Fixturi.ListaF3)).Tip);
    }
}
