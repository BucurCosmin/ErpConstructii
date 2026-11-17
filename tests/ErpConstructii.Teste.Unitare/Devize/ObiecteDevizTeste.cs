using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Teste.Unitare.Devize;

/// <summary>Importul pe obiecte (D-58): obiectul se adaugă la sfârșit sau ia locul celui cu același cod.</summary>
public class ObiecteDevizTeste
{
    private static VersiuneDeviz Obiect(string cod, int sectiuni)
    {
        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, cod);
        var o = v.AdaugaObiect(cod, $"Clădirea {cod}");
        for (var i = 0; i < sectiuni; i++)
            v.AdaugaCapitol($"{i}", $"Secțiunea {i}", TipCapitol.Capitol, o).AdaugaArticol($"{cod}-{i}", "art", "buc", 1, new ValoriResurse(1, 0, 0, 0));
        return v;
    }

    [Fact]
    public void Obiectul_nou_ajunge_ultimul_iar_cel_inlocuit_isi_pastreaza_locul()
    {
        var deviz = Obiect("B5", 3);
        foreach (var cod in new[] { "B6", "B10" })
        {
            var sursa = Obiect(cod, 2);
            var create = deviz.CopiazaCapitolDin(sursa, sursa.Radacini.First());
            deviz.MutaCapitol(create[0], int.MaxValue);
        }

        Assert.Equal(["B5", "B6", "B10"], deviz.Radacini.Select(c => c.Cod));
        Assert.Equal([0, 1, 2], deviz.Radacini.Select(c => c.Ordine));

        // B5 reimportat: ia locul celui vechi (primul).
        var vechi = deviz.Radacini.First();
        var b5 = Obiect("B5", 4);
        deviz.ScoateCapitol(vechi);
        var nou = deviz.CopiazaCapitolDin(b5, b5.Radacini.First());
        deviz.MutaCapitol(nou[0], vechi.Ordine);

        Assert.Equal(["B5", "B6", "B10"], deviz.Radacini.Select(c => c.Cod));
        Assert.Equal([0, 1, 2], deviz.Radacini.Select(c => c.Ordine));
        Assert.Equal(4 + 2 + 2, deviz.Articole().Count());
        Assert.Equal(3 + 4 + 2 + 2, deviz.Capitole.Count);   // obiectele + secțiunile lor; secțiunile vechi ale lui B5 au plecat
    }

    [Fact]
    public void La_devizul_de_oferta_fara_cost_cantitatea_noua_recalculeaza_doar_valoarea_ofertei()
    {
        var v = new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "BOQ");
        v.SeteazaParametri(new ParametriRecapitulatie(0, 0, 0, 0));
        var c = v.AdaugaObiect("B5", "Building 5");
        var a = c.AdaugaArticol("B5 C 20000 1 A", "Extruded polystyrene", "sqm", 100, ValoriResurse.Zero);
        a.SeteazaPretOferta(new ValoriResurse(7.8m, 22.6m, 7m, 0), new ValoriResurse(780m, 2260m, 700m, 0));

        var r = v.ModificaArticol(a.Id, new DateArticol(a.Simbol, a.Denumire, a.UnitateMasura, 50, ValoriResurse.Zero));

        Assert.True(r.Succes, r.Eroare.Mesaj);
        Assert.Equal(new ValoriResurse(7.8m, 22.6m, 7m, 0), a.PretOfertaUnitar);    // prețul de ofertă rămâne
        Assert.Equal(new ValoriResurse(390m, 1130m, 350m, 0), a.ValoareOferta);     // 50 × 7,8 / 22,6 / 7
    }
}
