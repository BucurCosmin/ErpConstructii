using ErpConstructii.Aprovizionare;
using ErpConstructii.SharedKernel;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Aprovizionare;

/// <summary>Nota de comandă emisă în aplicație (formularul GC 007) și rapoartele pe lucrare.</summary>
public class NotaComandaEmisaTeste
{
    private static readonly Guid ProiectA = Guid.NewGuid();
    private static readonly Guid ProiectB = Guid.NewGuid();

    private static DateNotaComanda Date(
        Guid? proiect = null, string furnizor = "TRITON SRL", DateOnly? data = null, ConditiiComanda? conditii = null, params LinieNotaComanda[] linii) => new(
        null, data ?? new DateOnly(2026, 11, 7), proiect ?? ProiectA, proiect == ProiectB ? "P-B" : "P-A", "CTRF-HSJV-002", null, furnizor, null,
        linii.Length > 0 ? linii : [new LinieNotaComanda("Mănuși protecție mărimea 9", "BUC", 60, "imediat", null)],
        null, "Bd. Aurel Vlaicu 217, Constanța", "0241 693 210", "office@triton.ro", conditii);

    private static NotaComanda Emisa(DateNotaComanda date, int numar = 1) =>
        NotaComanda.Emite(2026, numar, date, null, null, Guid.NewGuid(), "Ana Pop").Valoare;

    [Fact]
    public void Nota_emisa_are_numar_de_sistem_conditiile_si_contactul_furnizorului()
    {
        var conditii = new ConditiiComanda(ReceptieComanda.LaPrimire, ClasaCalitateComanda.BestComercial, TransportComanda.Auto, PlataComanda.Op);
        var sablon = Guid.NewGuid();

        var r = NotaComanda.Emite(2026, 12, Date(conditii: conditii), sablon, 2, Guid.NewGuid(), "Ana Pop");

        Assert.True(r.Succes);
        var n = r.Valoare;
        Assert.Equal(("NC/2026/0012", SursaDocument.Sistem, (Guid?)null), (n.NumarAfisat, n.Sursa, n.DocumentOriginalId));
        Assert.Equal(conditii, n.Conditii); // citite înapoi din JSON
        Assert.Equal(("0241 693 210", "office@triton.ro"), (n.FurnizorTelefon, n.FurnizorEmail));
        Assert.Equal((sablon, 2), (n.SablonId!.Value, n.VersiuneSablon!.Value));
        Assert.True(n.EsteBlocat);
    }

    [Fact]
    public void Fara_conditii_bifate_nu_se_salveaza_nimic_si_toate_sunt_nebifate()
    {
        var n = Emisa(Date(conditii: ConditiiComanda.Niciuna));

        Assert.Null(n.ConditiiJson);
        Assert.Equal(ConditiiComanda.Niciuna, n.Conditii);
    }

    [Fact]
    public void Nota_fara_materiale_sau_cu_cantitate_zero_e_refuzata()
    {
        var r = NotaComanda.Emite(2026, 1, Date(linii: new LinieNotaComanda("Saci rafie", "BUC", 0, null, null)), null, null, Guid.NewGuid(), "Ana Pop");

        Assert.True(r.Esec);
        Assert.Contains("cantitate pozitivă", r.Eroare.Mesaj);
    }

    [Fact]
    public void Sablonul_fara_versiune_e_refuzat()
    {
        var r = NotaComanda.Emite(2026, 1, Date(), Guid.NewGuid(), null, Guid.NewGuid(), "Ana Pop");

        Assert.True(r.Esec);
    }

    [Fact]
    public void Raportul_aduna_acelasi_material_pe_lucrare_indiferent_de_scriere()
    {
        var note = new[]
        {
            Emisa(Date(data: new DateOnly(2026, 10, 1), linii: [new("Mănuși protecție", "buc", 60, null, null), new("Saci rafie", "BUC", 200, null, null)]), 1),
            Emisa(Date(furnizor: "GLOBAL TRADE", data: new DateOnly(2026, 10, 15), linii: [new("MANUSI  PROTECTIE", "BUC", 24, null, null)]), 2),
            // altă UM: nu se adună cu bucățile
            Emisa(Date(data: new DateOnly(2026, 10, 20), linii: [new("Mănuși protecție", "set", 3, null, null)]), 3),
            // altă lucrare: rând separat
            Emisa(Date(proiect: ProiectB, linii: [new("Mănuși protecție", "buc", 10, null, null)]), 4),
        };

        var r = RaportNoteComanda.MaterialeComandate(note);

        // P-A: mănuși buc 60 + 24 = 84 (2 note, 2 furnizori), mănuși set 3, saci 200; P-B: mănuși buc 10.
        Assert.Equal(4, r.Count);
        var manusi = r.Single(m => m.ProiectCod == "P-A" && m.UnitateMasura == "buc");
        Assert.Equal(("Mănuși protecție", 84m, 2), (manusi.Denumire, manusi.Cantitate, manusi.Note));
        Assert.Equal(["TRITON SRL", "GLOBAL TRADE"], manusi.Furnizori);
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15)), (manusi.PrimaData, manusi.UltimaData));
        Assert.Equal(3m, r.Single(m => m.UnitateMasura == "set").Cantitate);
        Assert.Equal(10m, r.Single(m => m.ProiectCod == "P-B").Cantitate);
    }
}
