namespace ErpConstructii.Facturi;

/// <param name="Index">Poziția liniei în factură (0, 1, …): cheia liniei la recepție.</param>
public sealed record LinieFacturaPentruReceptie(int Index, string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal ValoareFaraTva);

/// <summary>O factură primită, neanulată, cu liniile ei marcate „intră în stoc”.</summary>
public sealed record FacturaPentruReceptie(
    Guid Id,
    string Numar,
    DateOnly DataEmitere,
    Guid? FurnizorId,
    string FurnizorDenumire,
    Guid ProiectId,
    string ProiectCod,
    IReadOnlyList<LinieFacturaPentruReceptie> Linii,
    Guid? NotaComandaId = null,
    string? NotaComandaNumar = null);

/// <summary>
/// Contractul public al modulului Facturi pentru Stocuri (regula 1): facturile cu materiale de recepționat.
/// Lucrează pe firma activă (RLS).
/// </summary>
public interface IInterogareFacturiPrimite
{
    /// <summary>Facturile neanulate și nestornate care au cel puțin o linie „intră în stoc”, cele mai noi primele.</summary>
    Task<IReadOnlyList<FacturaPentruReceptie>> CuMaterialeDeStocAsync(CancellationToken ct = default);

    /// <summary>Id-urile facturilor anulate (pentru semnalarea NIR-urilor care trebuie stornate).</summary>
    Task<IReadOnlySet<Guid>> FacturiAnulateAsync(CancellationToken ct = default);

    /// <summary>Factura, dacă există, nu e anulată și are linii de stoc.</summary>
    Task<FacturaPentruReceptie?> PentruReceptieAsync(Guid facturaId, CancellationToken ct = default);
}

/// <summary>
/// Contractul invers, implementat de modulul Stocuri: ce linii ale unei facturi au intrat deja în stoc (NIR-uri active).
/// Corectura facturii nu le mai poate schimba (întâi se stornează NIR-ul). Fără modulul Stocuri: nicio linie.
/// </summary>
public interface IInterogareReceptiiFactura
{
    Task<IReadOnlySet<int>> LiniiReceptionateAsync(Guid facturaId, CancellationToken ct = default);
}
