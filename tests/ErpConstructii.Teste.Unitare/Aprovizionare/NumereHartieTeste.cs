using ErpConstructii.Aprovizionare;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using ErpConstructii.Stocuri;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Numerele de pe hârtie ale necesarelor și bonurilor de consum importate (D-48): verificare, număr liber, renumerotare, rapoarte.</summary>
public class NumereHartieTeste
{
    private static readonly Guid ProiectA = Guid.NewGuid();
    private static readonly Guid ProiectB = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    private static DocumentCuNumarExtern Doc(string sistem, string? extern_, int luna, int an = 2026, Guid? proiect = null) =>
        new(Guid.NewGuid(), proiect ?? ProiectA, sistem, extern_, new DateOnly(an, luna, 1), proiect == ProiectB ? "P-B" : "P-A");

    [Theory]
    [InlineData("81", 81L)]
    [InlineData("Nr. 081/2026", 81L)]
    [InlineData("NM 12", 12L)]
    [InlineData("fără număr", null)]
    [InlineData(null, null)]
    public void Valoarea_numarului_e_primul_grup_de_cifre(string? numar, long? asteptat) =>
        Assert.Equal(asteptat, NumerotareExterna.Valoare(numar));

    [Fact]
    public void Numarul_liber_urmeaza_celui_mai_mare_numar_din_an()
    {
        // Folosite: 3, 7, 12 („012”) → următorul liber 13; anul fără numere începe de la 1.
        Assert.Equal(13, NumerotareExterna.NumarLiber(["3", "7", null, "012", "fără"]));
        Assert.Equal(1, NumerotareExterna.NumarLiber([]));
    }

    [Fact]
    public void Verificarea_gaseste_suprapunerile_golurile_si_documentele_fara_numar_pe_proiect_si_an()
    {
        var d1 = Doc("NM/2026/0001", "1", 1);
        var d2 = Doc("NM/2026/0002", "2", 2);
        var d3 = Doc("NM/2026/0003", "02", 3); // același număr cu d2, pe același proiect
        var d4 = Doc("NM/2026/0004", "6", 4);
        var d5 = Doc("NM/2026/0005", null, 5);
        var altProiect = Doc("NM/2026/0006", "2", 5, proiect: ProiectB); // alt proiect: seria lui, fără suprapunere
        var vechi = Doc("NM/2025/0009", "2", 12, 2025); // alt an: nu se suprapune cu d2

        var v = NumerotareExterna.Verifica([d1, d2, d3, d4, d5, altProiect, vechi]);

        Assert.Equal([(2026, "P-A"), (2026, "P-B"), (2025, "P-A")], v.Select(x => (x.An, x.Proiect)));
        var serie = v[0];
        Assert.Equal((5, 1L, 6L, 7L), (serie.Documente, serie.Primul!.Value, serie.Ultimul!.Value, serie.UrmatorulLiber));
        var s = Assert.Single(serie.Suprapuneri);
        Assert.Equal(2L, s.Numar);
        Assert.Equal(["NM/2026/0002", "NM/2026/0003"], s.Documente.Select(d => d.NumarSistem));
        // Între 2 și 6 lipsesc 3, 4, 5.
        Assert.Equal(["3–5"], serie.Lipsa);
        Assert.Equal(3L, serie.NumereLipsa);
        Assert.Equal("NM/2026/0005", Assert.Single(serie.FaraNumar).NumarSistem);
        Assert.Empty(v[1].Suprapuneri);
        Assert.Equal(3L, v[1].UrmatorulLiber);
        Assert.Empty(v[2].Suprapuneri);
    }

    [Fact]
    public void Numarul_folosit_se_cauta_pe_acelasi_proiect_si_an_fara_documentul_renumerotat()
    {
        var d2 = Doc("BC/2026/0002", "2", 2);
        var d3 = Doc("BC/2026/0003", "5", 3);
        DocumentCuNumarExtern[] existente = [d2, d3, Doc("BC/2025/0001", "7", 6, 2025), Doc("BC/2026/0004", "9", 4, proiect: ProiectB)];

        Assert.Equal("BC/2026/0002", Assert.Single(NumerotareExterna.Folosit("nr. 2", ProiectA, 2026, existente)).NumarSistem);
        Assert.Empty(NumerotareExterna.Folosit("7", ProiectA, 2026, existente));
        Assert.Empty(NumerotareExterna.Folosit("9", ProiectA, 2026, existente));
        Assert.Empty(NumerotareExterna.Folosit("5", ProiectA, 2026, existente, exceptand: d3.Id));
    }

    [Fact]
    public void Renumerotarea_cere_numar_nou_cu_cifre_si_motiv()
    {
        var id = Guid.NewGuid();
        var acum = DateTimeOffset.UtcNow;
        Assert.True(RenumerotareDocument.Creeaza(id, "NM/2026/0003", "2", "2", "dublură", Ana, "Ana", acum).Esec);
        Assert.True(RenumerotareDocument.Creeaza(id, "NM/2026/0003", "2", "x", "dublură", Ana, "Ana", acum).Esec);
        Assert.True(RenumerotareDocument.Creeaza(id, "NM/2026/0003", "2", "8", " ", Ana, "Ana", acum).Esec);

        var r = RenumerotareDocument.Creeaza(id, "NM/2026/0003", "2", " 8 ", "Număr dublat cu NM/2026/0002", Ana, "Ana Pop", acum);
        Assert.True(r.Succes, r.Eroare.Mesaj);
        Assert.Equal(("2", "8", "NM/2026/0003"), (r.Valoare.NumarVechi, r.Valoare.NumarNou, r.Valoare.DocumentNumar));
    }

    [Fact]
    public void Doar_necesarul_importat_se_renumeroteaza_si_isi_pastreaza_numarul_de_sistem()
    {
        LinieNecesarMateriale[] linii = [new("Ciment", "sac", 10, string.Empty, null)];
        var importat = NecesarMateriale.Importa(2026, 3, "2", ProiectA, "P-A", "Școala", "Șantier", new DateOnly(2026, 3, 2), Ana, "Ana", Guid.Empty, 0,
            linii, Guid.NewGuid()).Valoare;
        // Aprobat deja: numărul de hârtie se corectează oricum, cu istoric.
        Assert.True(importat.MarcheazaDinUrma(DateTimeOffset.UtcNow).Succes);
        var r = RenumerotareDocument.Creeaza(importat.Id, importat.NumarAfisat, importat.NumarExtern, "8", "dublură", Ana, "Ana", DateTimeOffset.UtcNow).Valoare;

        Assert.True(importat.Renumeroteaza(r).Succes);
        Assert.Equal(("8", "NM/2026/0003"), (importat.NumarExtern, importat.NumarAfisat));

        var emis = NecesarMateriale.Emite(2026, 4, ProiectA, "P-A", "Școala", "Șantier", new DateOnly(2026, 3, 2), Ana, "Ana", Guid.NewGuid(), 1, linii).Valoare;
        var re = RenumerotareDocument.Creeaza(emis.Id, emis.NumarAfisat, null, "9", "x", Ana, "Ana", DateTimeOffset.UtcNow).Valoare;
        Assert.True(emis.Renumeroteaza(re).Esec);
    }

    [Fact]
    public void Raportul_necesarelor_aduna_materialele_pe_proiect_cu_codurile_documentelor()
    {
        var n1 = NecesarMateriale.Importa(2026, 1, "5", ProiectA, "P-A", "Școala", "Șantier", new DateOnly(2026, 3, 2), Ana, "Ana", Guid.Empty, 0,
            [new("Ciment CEM II", "sac", 10, string.Empty, null), new("Nisip", "mc", 2, string.Empty, null)], Guid.NewGuid()).Valoare;
        var n2 = NecesarMateriale.Emite(2026, 2, ProiectA, "P-A", "Școala", "Șantier", new DateOnly(2026, 3, 9), Ana, "Ana", Guid.NewGuid(), 1,
            [new("ciment cem  ii", "SAC", 5, string.Empty, null)]).Valoare;

        var m = RaportNecesare.Materiale([n2, n1]);

        // Ciment: 10 + 5 = 15 sac, din ambele necesare; nisipul rămâne separat.
        var ciment = m.Single(x => x.Denumire == "Ciment CEM II");
        Assert.Equal(15m, ciment.Cantitate);
        Assert.Equal(["NM/2026/0001 (nr. 5)", "NM/2026/0002"], ciment.Documente);
        Assert.Equal((new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 9)), (ciment.PrimaData, ciment.UltimaData));
        Assert.Equal(2, m.Count);
    }

    [Fact]
    public void Raportul_bonurilor_aduna_materialul_de_pe_mai_multe_articole()
    {
        var santier = Gestiune.Santier(ProiectA, "P-A", "Școala");
        var ciment = Guid.NewGuid();
        var b1 = BonConsum.Emite(2026, 1, new DateOnly(2026, 9, 1), santier, "P-A",
        [
            new(ciment, "MAT-00001", "Ciment", "sac", 20, 11.5m, 230m, Guid.NewGuid(), "CA01", "Fundații"),
            new(ciment, "MAT-00001", "Ciment", "sac", 5, 11.5m, 57.5m, null, null, null),
        ], null, Ana, "Ana", numarExtern: "245").Valoare;
        var b2 = BonConsum.Emite(2026, 2, new DateOnly(2026, 9, 3), santier, "P-A",
            [new(ciment, "MAT-00001", "Ciment", "sac", 4, 12m, 48m, null, null, null)], null, Ana, "Ana").Valoare;

        var m = Assert.Single(RaportBonuriConsum.Materiale([b1, b2]));

        // 20 + 5 + 4 = 29 sac; 230 + 57,50 + 48 = 335,50 lei.
        Assert.Equal((29m, 335.5m), (m.Cantitate, m.Valoare));
        Assert.Equal(["BC/2026/0001 (nr. 245)", "BC/2026/0002"], m.Documente);
        Assert.Equal("245", b1.NumarExtern);
    }
}
