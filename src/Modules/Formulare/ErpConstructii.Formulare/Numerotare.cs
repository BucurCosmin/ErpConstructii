namespace ErpConstructii.Formulare;

/// <summary>
/// Serie de numerotare per firmă, tip de formular și an (REQ-FRM-12), ex. CMD-2026-0001.
/// Persistarea (cu blocare la nivel de rând, ca două cereri simultane să nu primească același număr)
/// se face în infrastructură; aici e doar regula.
/// </summary>
public sealed class SerieNumerotare(string prefix, int an, int ultimulNumar = 0, int cifre = 4)
{
    public string Prefix { get; } = prefix;
    public int An { get; } = an;
    public int UltimulNumar { get; private set; } = ultimulNumar;

    public string Urmatorul()
    {
        UltimulNumar++;
        return Formateaza(Prefix, An, UltimulNumar, cifre);
    }

    public static string Formateaza(string prefix, int an, int numar, int cifre = 4) =>
        $"{prefix}-{an}-{numar.ToString().PadLeft(cifre, '0')}";
}

/// <summary>
/// Număr rezervat pentru un formular gol descărcat. Dacă nu e importat până la expirare, se marchează
/// anulat, ca seria să nu aibă goluri nejustificate.
/// </summary>
public sealed record RezervareNumar(string Numar, Guid UtilizatorId, DateTimeOffset RezervatLa, DateTimeOffset ExpiraLa)
{
    public bool EsteExpirata(DateTimeOffset acum) => acum > ExpiraLa;
}
