using ErpConstructii.Stocuri;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Stocuri;

/// <summary>Proveniența stocului: pe ce NIR a intrat ce e acum în gestiune (ieșiri atribuite FIFO).</summary>
public class TrasabilitateStocTeste
{
    private static readonly Guid Depozit = Guid.NewGuid();
    private static readonly Guid Santier = Guid.NewGuid();
    private static readonly Guid Ciment = Guid.NewGuid();
    private static readonly Guid Nir1 = Guid.NewGuid();
    private static readonly Guid Nir2 = Guid.NewGuid();
    private static readonly DateOnly Zi = new(2026, 11, 2);

    private static MiscareTrasabilitate M(Guid gestiune, TipMiscare tip, decimal cantitate, decimal valoare, Guid document, string numar) =>
        new(gestiune, Ciment, tip, cantitate, valoare, document, numar, Zi);

    private static readonly Dictionary<Guid, Guid> FaraStornari = [];

    [Fact]
    public void Iesirile_se_atribuie_FIFO_si_in_stoc_raman_intrarile_cele_mai_noi()
    {
        // NIR 1: 10 saci × 30 = 300; NIR 2: 20 saci × 32 = 640. Consum/transfer 15 saci: 10 din NIR 1 + 5 din NIR 2.
        // Rămân 20 − 5 = 15 saci, toți din NIR 2, la prețul lui (32).
        var bt = Guid.NewGuid();
        var loturi = TrasabilitateStoc.Calculeaza(
            [
                M(Depozit, TipMiscare.IntrareNir, 10, 300, Nir1, "NIR/2026/0001"),
                M(Depozit, TipMiscare.IntrareNir, 20, 640, Nir2, "NIR/2026/0002"),
                M(Depozit, TipMiscare.IesireTransfer, -15, -470, bt, "BT/2026/0001"),
                M(Santier, TipMiscare.IntrareTransfer, 15, 470, bt, "BT/2026/0001"),
            ], FaraStornari);

        var dep = Assert.Single(loturi, l => l.GestiuneId == Depozit);
        Assert.Equal((Nir2, 15m, 32m), (dep.DocumentOrigineId, dep.Cantitate, dep.PretUnitar));
    }

    [Fact]
    public void Materialul_transferat_in_santier_isi_pastreaza_NIR_ul_de_origine()
    {
        // Transferul de 15 saci a scos 10 din NIR 1 + 5 din NIR 2: în șantier sunt două loturi, cu originea lor.
        var bt = Guid.NewGuid();
        var loturi = TrasabilitateStoc.Calculeaza(
            [
                M(Depozit, TipMiscare.IntrareNir, 10, 300, Nir1, "NIR/2026/0001"),
                M(Depozit, TipMiscare.IntrareNir, 20, 640, Nir2, "NIR/2026/0002"),
                M(Depozit, TipMiscare.IesireTransfer, -15, -470, bt, "BT/2026/0001"),
                M(Santier, TipMiscare.IntrareTransfer, 15, 470, bt, "BT/2026/0001"),
            ], FaraStornari);

        var santier = loturi.Where(l => l.GestiuneId == Santier).OrderBy(l => l.DocumentOrigineNumar).ToList();
        Assert.Equal([(Nir1, 10m), (Nir2, 5m)], santier.Select(l => (l.DocumentOrigineId, l.Cantitate)));
    }

    [Fact]
    public void Returul_in_depozit_aduce_inapoi_originea_din_santier()
    {
        // 10 saci din NIR 1 merg în șantier, 4 se consumă, 6 se returnează: în depozit sunt 6 saci din NIR 1.
        var bt = Guid.NewGuid();
        var bc = Guid.NewGuid();
        var retur = Guid.NewGuid();
        var loturi = TrasabilitateStoc.Calculeaza(
            [
                M(Depozit, TipMiscare.IntrareNir, 10, 300, Nir1, "NIR/2026/0001"),
                M(Depozit, TipMiscare.IesireTransfer, -10, -300, bt, "BT/2026/0001"),
                M(Santier, TipMiscare.IntrareTransfer, 10, 300, bt, "BT/2026/0001"),
                M(Santier, TipMiscare.IesireConsum, -4, -120, bc, "BC/2026/0001"),
                M(Santier, TipMiscare.IesireTransfer, -6, -180, retur, "BT/2026/0002"),
                M(Depozit, TipMiscare.IntrareTransfer, 6, 180, retur, "BT/2026/0002"),
            ], FaraStornari);

        var lot = Assert.Single(loturi);
        Assert.Equal((Depozit, Nir1, 6m), (lot.GestiuneId, lot.DocumentOrigineId, lot.Cantitate));
    }

    [Fact]
    public void Stornarea_unui_NIR_scoate_cantitatea_lui_nu_pe_cea_mai_veche()
    {
        // NIR 1 (10) și NIR 2 (20); NIR 2 se stornează: rămân cei 10 saci din NIR 1 (FIFO ar fi scos întâi NIR 1).
        var storno = Guid.NewGuid();
        var loturi = TrasabilitateStoc.Calculeaza(
            [
                M(Depozit, TipMiscare.IntrareNir, 10, 300, Nir1, "NIR/2026/0001"),
                M(Depozit, TipMiscare.IntrareNir, 20, 640, Nir2, "NIR/2026/0002"),
                M(Depozit, TipMiscare.IesireStornoNir, -20, -640, storno, "STORNO NIR/2026/0002"),
            ], new Dictionary<Guid, Guid> { [storno] = Nir2 });

        var lot = Assert.Single(loturi);
        Assert.Equal((Nir1, 10m), (lot.DocumentOrigineId, lot.Cantitate));
    }

    [Fact]
    public void Plusul_de_inventar_e_o_origine_proprie()
    {
        // NIR 1: 10 saci; inventarul găsește 12 → plus 2 saci la 30 (60 / 2).
        var inv = Guid.NewGuid();
        var loturi = TrasabilitateStoc.Calculeaza(
            [
                M(Depozit, TipMiscare.IntrareNir, 10, 300, Nir1, "NIR/2026/0001"),
                M(Depozit, TipMiscare.PlusInventar, 2, 60, inv, "INV/2026/0001"),
            ], FaraStornari);

        Assert.Equal(12m, loturi.Sum(l => l.Cantitate));
        var plus = Assert.Single(loturi, l => l.TipOrigine == TipMiscare.PlusInventar);
        Assert.Equal((inv, 2m, 30m), (plus.DocumentOrigineId, plus.Cantitate, plus.PretUnitar));
    }
}
