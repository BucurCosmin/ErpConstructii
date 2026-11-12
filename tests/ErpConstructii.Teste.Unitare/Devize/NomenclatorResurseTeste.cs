using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Teste.Unitare.Devize;

/// <summary>Nomenclatorul de resurse al firmei (REQ-IMP-04): coduri noi nemapate la import, maparea pe tipuri.</summary>
public class NomenclatorResurseTeste
{
    private static readonly DateTimeOffset Acum = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static ArticolDeviz Articol(decimal cantitate = 10) =>
        new VersiuneDeviz(Guid.NewGuid(), TipVersiuneDeviz.Oferta, 1, "T").AdaugaObiect("01", "O").AdaugaArticol("CA02", "Beton", "mc", cantitate, ValoriResurse.Zero);

    [Fact]
    public void Codurile_necunoscute_devin_resurse_nemapate_iar_cele_cunoscute_se_refolosesc()
    {
        var existenta = ResursaCatalog.Noua("isdp", "2900888", "Lemn rotund", "mc", TipResursa.Material, false);
        var a = Articol();
        var lemn = a.AdaugaConsum(" 2900888 ", "Lemn rot cons", TipResursa.Material, "mc", 0.0007m, 1281);
        var beton = a.AdaugaConsum("6000123", "Beton C16/20", TipResursa.Material, "mc", 1.02m, 410);
        var beton2 = Articol().AdaugaConsum("6000123", "Beton C16/20", TipResursa.Material, "mc", 1.02m, 410);

        var p = CatalogResurse.Potriveste("ISDP", [lemn, beton, beton2], [existenta]);

        var noua = Assert.Single(p.Noi); // betonul, o singură dată deși apare de două ori
        Assert.Equal(("isdp", "6000123", false), (noua.Sursa, noua.Cod, noua.EsteMapata));
        Assert.Equal(existenta.Id, lemn.ResursaCatalogId);
        Assert.Equal((noua.Id, noua.Id), (beton.ResursaCatalogId!.Value, beton2.ResursaCatalogId!.Value));
        Assert.Empty(p.Probleme);
    }

    [Fact]
    public void Alta_UM_sau_alt_tip_decat_in_nomenclator_e_semnalat()
    {
        var existenta = ResursaCatalog.Noua("isdp", "19621", "Săpător cat. II", "ora", TipResursa.Manopera, false);
        var c = Articol().AdaugaConsum("19621", "Săpător", TipResursa.Material, "mc", 1, 10);

        var p = CatalogResurse.Potriveste("isdp", [c], [existenta]);

        Assert.Equal(2, p.Probleme.Count(x => x.Severitate == Severitate.Avertisment));
        Assert.Equal(existenta.Id, c.ResursaCatalogId); // se leagă oricum de resursa din nomenclator
    }

    [Fact]
    public void Acelasi_cod_din_alta_sursa_e_alta_resursa()
    {
        var dinAltProgram = ResursaCatalog.Noua("altul", "19621", "Altceva", "buc", TipResursa.Material, false);
        var c = Articol().AdaugaConsum("19621", "Săpător", TipResursa.Manopera, "ora", 1, 111);

        Assert.Single(CatalogResurse.Potriveste("isdp", [c], [dinAltProgram]).Noi);
    }

    [Fact]
    public void Fiecare_tip_cere_legatura_lui_la_mapare()
    {
        var material = ResursaCatalog.Noua("isdp", "6000123", "Beton", "mc", TipResursa.Material, false);
        Assert.True(material.Mapeaza(new MapareResursa(TipResursa.Material, null, null, "Zidar", null, null, false), "Ana", Acum).Esec);
        var articolStoc = Guid.NewGuid();
        Assert.True(material.Mapeaza(new MapareResursa(TipResursa.Material, articolStoc, "MAT-00012 — Beton C16/20", null, null, null, false), "Ana", Acum).Succes);
        Assert.Equal((articolStoc, true, "Ana"), (material.ArticolStocId!.Value, material.EsteMapata, material.MapataDeNume));

        var manopera = ResursaCatalog.Noua("isdp", "19621", "Săpător", "ora", TipResursa.Manopera, false);
        Assert.True(manopera.Mapeaza(new MapareResursa(TipResursa.Manopera, null, null, " ", "II", null, false), "Ana", Acum).Esec);
        Assert.True(manopera.Mapeaza(new MapareResursa(TipResursa.Manopera, articolStoc, "x", "Săpător", "II", null, false), "Ana", Acum).Succes);
        Assert.Equal(("Săpător", "II", (Guid?)null), (manopera.Meserie, manopera.Categorie, manopera.ArticolStocId)); // articolul de stoc nu se aplică manoperei
    }

    [Fact]
    public void Resursa_fara_corespondent_si_tipul_dedus_confirmat_la_mapare()
    {
        var apa = ResursaCatalog.Noua("isdp", "6202806", "Apa industriala", "mc", TipResursa.Material, tipDedus: true);
        Assert.False(apa.EsteMapata);

        Assert.True(apa.Mapeaza(new MapareResursa(TipResursa.Material, null, null, null, null, null, FaraMapare: true), "Ana", Acum).Succes);

        Assert.Equal((true, true, false), (apa.EsteMapata, apa.FaraMapare, apa.TipDedus));
    }
}
