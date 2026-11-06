namespace ErpConstructii.Stocuri;

/// <summary>Cât s-a recepționat dintr-o linie de factură prin NIR-urile active (nestornate).</summary>
/// <param name="Inchisa">Linia nu mai așteaptă nimic: s-a primit tot (sau mai mult) ori gestionarul a închis-o („restul nu mai vine”).</param>
public sealed record StareLinieReceptie(int Index, decimal Cantitate, decimal Receptionat, bool Inchisa)
{
    /// <summary>Ce mai e de recepționat din linie.</summary>
    public decimal Ramas => Inchisa ? 0 : Math.Max(0, Cantitate - Receptionat);

    public bool Deschisa => Ramas > 0;
}

/// <summary>
/// Recepția parțială a facturilor: o factură se poate recepționa în mai multe NIR-uri (marfa vine în tranșe). O linie de
/// NIR marcată <see cref="LinieNir.Partiala"/> lasă restul liniei deschis; una nemarcată (inclusiv NIR-urile de dinainte
/// de recepția parțială, care cuprindeau toată factura) închide linia, chiar cu lipsă. Funcție pură.
/// </summary>
public static class StareReceptie
{
    public static IReadOnlyList<StareLinieReceptie> Calculeaza(IEnumerable<(int Index, decimal Cantitate)> liniiFactura, IEnumerable<LinieNir> receptii)
    {
        var dupaIndex = receptii.GroupBy(l => l.IndexFactura).ToDictionary(g => g.Key, g => g.ToList());
        return liniiFactura.Select(l =>
        {
            var primite = dupaIndex.GetValueOrDefault(l.Index) ?? [];
            var receptionat = primite.Sum(r => r.CantitateReceptionata);
            var inchisa = primite.Any(r => !r.Partiala) || receptionat >= l.Cantitate;
            return new StareLinieReceptie(l.Index, l.Cantitate, receptionat, inchisa);
        }).ToList();
    }
}
