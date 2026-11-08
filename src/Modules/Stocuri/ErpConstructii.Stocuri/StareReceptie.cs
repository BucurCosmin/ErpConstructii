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
    /// <summary>
    /// Starea liniilor unui aviz (D-40): recepțiile lui se compară pe poziția din aviz (<see cref="LinieNir.IndexAviz"/>).
    /// </summary>
    public static IReadOnlyList<StareLinieReceptie> CalculeazaAviz(IEnumerable<(int Index, decimal Cantitate)> liniiAviz, IEnumerable<LinieNir> receptii) =>
        Calculeaza(liniiAviz, receptii.Where(l => l.IndexAviz is not null).Select(l => l with { IndexFactura = l.IndexAviz!.Value }));

    /// <summary>
    /// Ce acoperă dintr-o factură NIR-urile ei: cele pe factură închid liniile ca de obicei; cele pe aviz, facturate cu ea
    /// (D-40), contează doar cu cantitatea (restul liniei facturii, dacă e, rămâne de recepționat).
    /// </summary>
    public static IReadOnlyList<StareLinieReceptie> CalculeazaFactura(IEnumerable<(int Index, decimal Cantitate)> liniiFactura, IEnumerable<Nir> receptii) =>
        Calculeaza(liniiFactura, receptii.SelectMany(n => n.AvizId is null ? n.Linii : n.Linii.Select(l => l with { Partiala = true })));

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
