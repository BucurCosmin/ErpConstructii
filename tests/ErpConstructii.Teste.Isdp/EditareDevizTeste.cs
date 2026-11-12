using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import;
using ErpConstructii.Devize.Import.Isdp.Excel;

namespace ErpConstructii.Teste.Isdp;

/// <summary>Lucrul pe devizul importat: editarea articolelor ciornei, ciorna nouă dintr-o versiune aprobată, aprobarea.</summary>
public class EditareDevizTeste
{
    private static VersiuneDeviz Versiune()
    {
        var d = ConversieIsdp.Converteste([Fixturi.Incarca(Fixturi.DevizResurse), Fixturi.Incarca(Fixturi.ListaF3)]).Valoare!;
        return ConvertorDevizCanonic.CaVersiune(d, Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "isdp", null, d.Parametri!);
    }

    private static DateArticol Date(ArticolDeviz a, decimal? cantitate = null) =>
        new(a.Simbol, a.Denumire, a.UnitateMasura, cantitate ?? a.Cantitate, a.PretUnitar);

    [Fact]
    public void Articolul_salvat_neschimbat_pastreaza_pretul_de_oferta_din_F3()
    {
        var v = Versiune();
        var a = v.Articole().Single(x => x.CodWbsSursa == "0002.1.1.1.1");

        Assert.True(v.ModificaArticol(a.Id, Date(a)).Succes);

        // Recalculul după regula F3 (curs 5,23) dă exact prețurile din F3 1.1.
        Assert.Equal((131.37m, 42145.45m), (a.PretOfertaUnitar.Total, a.ValoareOferta.Total));
        Assert.Equal(99643.23m, v.Articole().Sum(x => x.ValoareOferta.Total));
    }

    [Fact]
    public void Cantitatea_noua_recalculeaza_resursele_valoarea_si_oferta()
    {
        var v = Versiune();
        var a = v.Articole().Single(x => x.CodWbsSursa == "0002.1.1.1.1"); // 320,82 mc
        var sapator = a.Consumuri.Single(c => c.CodResursa == "19621");
        var consumUnitar = sapator.ConsumUnitar;
        var t3 = v.Recapitulatie().TotalFaraTva;

        Assert.True(v.ModificaArticol(a.Id, Date(a, 400m)).Succes);

        // Resursa păstrează consumul unitar; cantitatea totală = consum × 400.
        Assert.Equal((consumUnitar, consumUnitar * 400m), (sapator.ConsumUnitar, sapator.CantitateTotala));
        // Valoarea de ofertă: 400 / 320,82 din cea veche, rotunjit pe componente (manopera 41.741,30 → 52.042,6…).
        Assert.Equal(Math.Round(41741.30m * 400m / 320.82m, 0), Math.Round(a.ValoareOferta.Manopera, 0));
        Assert.True(v.Recapitulatie().TotalFaraTva > t3);
    }

    [Fact]
    public void Articolul_adaugat_si_cel_sters_schimba_recapitulatia()
    {
        var v = Versiune();
        var capitol = v.Copii(v.Radacini.Single().Id).First();
        var t0 = v.Recapitulatie().CheltuieliDirecteResurse.Total;

        var nou = v.AdaugaArticolManual(capitol.Id, new DateArticol("MAN01", "Lucrare suplimentară", "ora", 10m, new ValoriResurse(0, 111m, 0, 0)));
        Assert.True(nou.Succes);
        Assert.Equal(t0 + 1110m, v.Recapitulatie().CheltuieliDirecteResurse.Total); // 10 ore × 111 lei
        Assert.True(nou.Valoare.ArePretOferta); // devizul are prețuri de ofertă: și articolul nou primește

        Assert.True(v.StergeArticol(nou.Valoare.Id).Succes);
        Assert.Equal(t0, v.Recapitulatie().CheltuieliDirecteResurse.Total);
        Assert.True(v.AdaugaArticolManual(capitol.Id, new DateArticol("X", "Fără UM", " ", 1, ValoriResurse.Zero)).Esec);
    }

    [Fact]
    public void Versiunea_aprobata_nu_se_editeaza_ci_porneste_o_ciorna_noua_cu_aceleasi_chei()
    {
        var v = Versiune();
        Assert.True(v.Aproba().Succes);
        var a = v.Articole().First();
        Assert.True(v.ModificaArticol(a.Id, Date(a, 1m)).Esec);
        Assert.True(v.Redenumeste("Alt nume").Esec);

        var ciorna = v.CopieCiorna(2, "Ofertă revizuită");

        Assert.Equal((StareVersiuneDeviz.Ciorna, v.Id, 2, "Ofertă revizuită"), (ciorna.Stare, ciorna.VersiuneAnterioaraId!.Value, ciorna.Numar, ciorna.Denumire));
        Assert.Equal(v.Articole().Select(x => x.CheieStabila), ciorna.Articole().Select(x => x.CheieStabila));
        Assert.Equal(v.Recapitulatie().TotalCuTva, ciorna.Recapitulatie().TotalCuTva);
        Assert.Equal((v.Moneda, v.Curs, v.PoliticaRotunjire), (ciorna.Moneda, ciorna.Curs, ciorna.PoliticaRotunjire));
        Assert.Equal(v.Articole().Sum(x => x.Consumuri.Count), ciorna.Articole().Sum(x => x.Consumuri.Count));
        Assert.Equal(v.Capitole.Count, ciorna.Capitole.Count);

        // Ciorna se editează și se aprobă; cea veche devine „înlocuită”.
        var primul = ciorna.Articole().First();
        Assert.True(ciorna.ModificaArticol(primul.Id, Date(primul, primul.Cantitate + 1)).Succes);
        Assert.True(ciorna.Aproba().Succes);
        Assert.True(v.Inlocuieste().Succes);
        Assert.Equal(StareVersiuneDeviz.Inlocuita, v.Stare);
    }

    [Fact]
    public void Ciorna_de_lucru_arata_fata_de_original_ce_s_a_modificat_adaugat_si_scos()
    {
        var original = Versiune(); // devizul importat (ciornă, fără precedent): nu e ciornă de lucru
        Assert.False(original.EsteCiornaDeLucru);
        var lucru = original.CopieCiorna(2);
        Assert.True(lucru.EsteCiornaDeLucru);

        var modificat = lucru.Articole().Single(x => x.CodWbsSursa == "0002.1.1.1.1");
        var scos = lucru.Articole().Last();
        var capitol = lucru.Capitole.Single(c => c.Id == modificat.CapitolId);
        Assert.True(lucru.ModificaArticol(modificat.Id, Date(modificat, 400m)).Succes);
        Assert.True(lucru.StergeArticol(scos.Id).Succes);
        var nou = lucru.AdaugaArticolManual(capitol.Id, new DateArticol("MAN01", "Lucrare suplimentară", "ora", 10m, new ValoriResurse(0, 111m, 0, 0))).Valoare;

        var c = ComparareVersiuni.Compara(original, lucru);

        Assert.Equal(3, c.Diferente.Count);
        var m = c.Diferente.Single(d => d.Tip == TipDiferentaVersiune.Modificat);
        Assert.Equal((modificat.CheieStabila, 320.82m, 400m), (m.CheieStabila, m.Vechi!.Cantitate, m.Nou!.Cantitate));
        Assert.Equal(["cantitate"], m.Campuri);
        Assert.Equal(nou.CheieStabila, c.Diferente.Single(d => d.Tip == TipDiferentaVersiune.Adaugat).CheieStabila);
        Assert.Equal(1110m, c.Diferente.Single(d => d.Tip == TipDiferentaVersiune.Adaugat).DiferentaValoare); // 10 ore × 111 lei
        Assert.Equal(scos.CheieStabila, c.Diferente.Single(d => d.Tip == TipDiferentaVersiune.Sters).CheieStabila);
        Assert.Equal(original.Recapitulatie().TotalCuTva, c.RecapitulatieVeche.TotalCuTva);

        // Capitolul se regăsește în copie după cale (pentru „adaugă articol” cerut pe original).
        var capitolOriginal = original.Capitole.Single(x => original.CheieCapitol(x) == lucru.CheieCapitol(capitol));
        Assert.Equal(capitol.Denumire, capitolOriginal.Denumire);

        // Aprobarea ciornei de lucru înlocuiește și originalul neaprobat.
        Assert.True(lucru.Aproba().Succes);
        Assert.True(original.Inlocuieste().Succes);
        Assert.True(original.Inlocuieste().Esec);
    }
}
