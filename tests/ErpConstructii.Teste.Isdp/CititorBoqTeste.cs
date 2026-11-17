using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Import;
using ErpConstructii.Devize.Import.Boq;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Teste.Isdp;

/// <summary>Cititorul BOQ (D-58) pe foi construite de mână: rezumat, secțiuni cu antete diferite, grupuri, totaluri, foi ignorate.</summary>
public class CititorBoqTeste
{
    private static IReadOnlyList<string?> R(params string?[] c) => c;

    private static readonly string?[] Gol = [];

    /// <summary>Rezumatul: secțiunile 20000 (Architecture) și 50000 (Electric), totalul general.</summary>
    private static FoaieTabel Rezumat() => new("BOQ",
    [
        Gol,
        R("Bill of Quantities - Building 5"),
        Gol,
        R("Work Items", "Specification", "Item Description", "Unit", "Quantity", "Material (A)", null, "Labour (B)", null, "Expense (C)", null, "TOTAL (A+B+C+D)", null, "Remarks"),
        R(null, null, null, null, null, "Rate", "Amount", "Rate", "Amount", "Rate", "Amount", "Rate", "Amount"),
        Gol,
        R("20000", "Architecture", null, null, null, null, null, null, null, null, null, "1", "1500"),
        R("50000", "Electric", null, null, null, null, null, null, null, null, null, "1", "300"),
        R("GRAND TOTAL", null, null, null, null, null, null, null, null, null, null, "2", "1800"),
    ]);

    /// <summary>
    /// Secțiunea 20000: grup „1 WALLS”, două articole (sume ca text), un rând TOTAL fără valoare la mijloc, apoi încă un articol
    /// și TOTAL-ul cu valoare. Componenta „Expense” are antetul gol (se ia după perechea „Rate / Amount” rămasă).
    /// </summary>
    private static FoaieTabel Arhitectura() => new("20000",
    [
        Gol,
        R("Bill of Quantities - 20000 - Architecture"),
        Gol,
        R("No.", "Work Items", "Item Description", "Specification", "Unit", "Quantity", "Material (A)", null, "Labour (B)", null, null, null, "TOTAL (A+B+C)", null, "Remarks"),
        R(null, null, null, null, null, null, "Rate", "Amount", "Rate", "Amount", "Rate", "Amount", "Rate", "Amount"),
        R("1", "B5 C 20000 1 ", "WALLS"),
        R("1.1", "B5 C 20000 1 A", "Sandwich panel", "120mm, RAL9002", "sqm", "100.000", "5", "500", "3", "300", "2", "200", "10", "1000"),
        R("1.2", "B5  C 20000 1 A1", "Paint (included)", null, "kg", "20", "0", "0", "0", "0", "0", "0", "0", "0"),
        R(null, null, "TOTAL"),
        R("2", "B5 C 20000 2 ", "ROOF", null, null, null, "0", "0", "0", "0", "0", "0", "0", "0"),
        R("2.1", "B5 C 20000 2 A", "Roof panel", null, "sqm", "50", "6", "300", "4", "200", "0", "0", "10", "500", "check"),
        R(null, null, "TOTAL", null, null, null, null, null, null, null, null, null, "20", "1500"),
    ]);

    /// <summary>Secțiunea 50000: Specification înaintea descrierii, codurile „E B5 …”, rânduri Activity / Material.</summary>
    private static FoaieTabel Electric() => new("50000 ",
    [
        Gol,
        R(null, "Bill of Quantities - 50000 - Electrical"),
        Gol,
        R("No", "Work Items", "Specification", "Item Description", "Unit", "Quantity", "Material (A)", null, "Labour (B)", null, "Expense (C)", null, "TOTAL (A+B+C+D)", null, "Remarks"),
        R(null, null, null, null, null, null, "Rate", "Amount", "Rate", "Amount", "Rate", "Amount", "Rate", "Amount"),
        R(null, null, "BSI 50000 - Cables"),
        R("1", "E B5 50000 1", "CABLE LAYING", "Activity", "M", "100", "0", "0", "1", "100", "0", "0", "1", "100"),
        R("2", "E B5 50000 2", "CABLE 5X16", "Material", "M", "100", "2", "200", "0", "0", "0", "0", "2", "200"),
        R(null, null, "TOTAL", null, null, null, null, null, null, null, null, null, "3", "300"),
    ]);

    private static BoqCitit Citeste() => CititorBoq.Citeste(
        [
            new FoaieTabel("표지", [R("공 사 비 내 역 서")]),
            Rezumat(),
            Arhitectura(),
            Electric(),
            // Foaie cu antet, dar absentă din rezumat: rămâne deoparte.
            new FoaieTabel("sample_20100", [.. Arhitectura().Randuri]),
        ],
        ProfilBoq.MaterialLabourExpense, "B5_OFFER.xlsx");

    [Fact]
    public void Sectiunile_se_potrivesc_cu_rezumatul_iar_totalurile_se_leaga()
    {
        var b = Citeste();

        Assert.Equal("B5", b.Obiect);
        Assert.Equal("Building 5", b.DenumireObiect);
        Assert.Equal(["20000", "50000"], b.Sectiuni.Select(s => s.Cod));
        Assert.Equal(1500m, b.Sectiuni[0].Valoare);                    // 1.000 + 0 + 500
        Assert.Equal((1500m, 1500m), (b.Sectiuni[0].TotalDeclarat!.Value, b.Sectiuni[0].TotalRezumat!.Value));
        Assert.Equal(300m, b.Sectiuni[1].Valoare);
        Assert.Equal(1800m, b.TotalGeneral);
        Assert.DoesNotContain(b.Probleme, p => p.Severitate != SharedKernel.Import.Severitate.Informare && p.Mesaj.Contains("dau"));
    }

    [Fact]
    public void Totalul_gol_de_la_mijlocul_foii_nu_opreste_citirea_iar_grupurile_devin_subcapitole()
    {
        var s = Citeste().Sectiuni[0];

        Assert.Equal(["WALLS", "ROOF"], s.Grupuri.Select(g => g.Denumire));
        Assert.Equal(["B5 C 20000 1 A", "B5 C 20000 1 A1"], s.Grupuri[0].Articole.Select(a => a.Cod));   // spațiile duble normalizate
        var panou = s.Grupuri[0].Articole[0];
        Assert.Equal((100m, "120mm, RAL9002"), (panou.Cantitate, panou.Specificatie));
        Assert.Equal(new ValoareComponenta(2, 200), panou.Componente["C"]);          // Expense fără antet: perechea rămasă
        Assert.Equal("check", s.Grupuri[1].Articole[0].Observatii);
    }

    [Fact]
    public void Foile_in_alta_limba_si_cele_absente_din_rezumat_raman_deoparte()
    {
        var b = Citeste();

        Assert.Contains(b.Ignorate, i => i.Sursa == "표지" && i.Motiv.Contains("altă limbă"));
        Assert.Contains(b.Ignorate, i => i.Sursa == "sample_20100" && i.Motiv.Contains("nu apare în rezumat"));
    }

    [Fact]
    public void Conversia_pune_obiectul_pe_primul_nivel_cu_oferta_pe_componentele_alese_si_codul_ca_cheie()
    {
        var d = ConversieBoq.Converteste(Citeste(), ProfilBoq.MaterialLabourExpense, "EUR",
            new Dictionary<string, TipResursa> { ["C"] = TipResursa.Transport });

        var obiect = Assert.Single(d.Capitole);
        Assert.Equal((TipCapitol.Obiect, "B5"), (obiect.Tip, obiect.Cod));
        Assert.True(d.PeObiecte);
        var panou = d.Articole().First();
        Assert.Equal("B5C200001A", panou.CodWbs);                                    // ca în Primavera
        Assert.Equal(new ValoriResurse(5, 3, 0, 2), panou.PretOfertaUnitar);         // Expense → transport, la alegere
        Assert.Equal(ValoriResurse.Zero, panou.PretUnitar);                          // costul direct rămâne gol
        Assert.Equal(1800m, d.Articole().Sum(a => a.ValoareOferta!.Value.Total));
        Assert.All(d.Componente!, c => Assert.NotNull(c.Tip));
    }

    [Fact]
    public void Fara_alegere_componenta_neclara_ramane_nealeasa_si_importul_nu_se_confirma()
    {
        var d = ConversieBoq.Converteste(Citeste(), ProfilBoq.MaterialLabourExpense, "EUR", null);

        Assert.Null(d.Componente!.Single(c => c.Cheie == "C").Tip);
        Assert.Contains(ValidatorDevizCanonic.Valideaza(d), p => p.Severitate == SharedKernel.Import.Severitate.Eroare && p.Mesaj.Contains("Expense"));
    }
}
