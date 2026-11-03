namespace ErpConstructii.SharedKernel;

/// <summary>
/// Firma curentă a cererii. Implementată în Host din token (claim <c>firma_id</c>).
/// Valoarea este trimisă la PostgreSQL ca variabilă de sesiune <c>app.firma_id</c>,
/// pe care o citesc politicile Row-Level Security.
/// </summary>
public interface IFirmaCurenta
{
    /// <summary>Null pentru operațiile de sistem (migrări, joburi globale).</summary>
    Guid? FirmaId { get; }
}

/// <summary>Utilizatorul curent al cererii.</summary>
public interface IUtilizatorCurent
{
    Guid? UtilizatorId { get; }
    string? Nume { get; }
    bool EsteAutentificat { get; }
}

/// <summary>Ceas injectabil, ca testele să poată fixa „acum”.</summary>
public interface ICeas
{
    DateTimeOffset Acum { get; }
    DateOnly Azi => DateOnly.FromDateTime(Acum.Date);
}

public sealed class CeasSistem : ICeas
{
    public DateTimeOffset Acum => DateTimeOffset.UtcNow;
}

/// <summary>
/// Calendarul zilelor lucrătoare (weekend + sărbători legale din România).
/// Folosit la termenele legale (ex. transmiterea în e-Factura) și la graficele de execuție.
/// Sărbătorile se încarcă din configurare/bază de date, nu se hardcodează în cod
/// (Paștele și Rusaliile ortodoxe se mută în fiecare an).
/// </summary>
public interface ICalendarLucru
{
    bool EsteZiLucratoare(DateOnly zi);
}

/// <summary>Implementare simplă: weekend + o listă de sărbători dată explicit.</summary>
public sealed class CalendarLucru(IEnumerable<DateOnly> sarbatori) : ICalendarLucru
{
    private readonly HashSet<DateOnly> _sarbatori = [.. sarbatori];

    public bool EsteZiLucratoare(DateOnly zi) =>
        zi.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_sarbatori.Contains(zi);
}

public static class CalendarLucruExtensii
{
    /// <summary>Adaugă <paramref name="zile"/> zile lucrătoare, fără a număra ziua de pornire.</summary>
    public static DateOnly AdaugaZileLucratoare(this ICalendarLucru calendar, DateOnly start, int zile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zile);
        var zi = start;
        var ramase = zile;
        while (ramase > 0)
        {
            zi = zi.AddDays(1);
            if (calendar.EsteZiLucratoare(zi))
            {
                ramase--;
            }
        }

        return zi;
    }
}
