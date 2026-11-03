using ErpConstructii.SharedKernel;

namespace ErpConstructii.Facturi;

/// <summary>
/// Termenul de transmitere în RO e-Factura (REQ-FCT-04): pentru facturile emise din 1 ianuarie 2026,
/// 5 zile LUCRĂTOARE de la data emiterii (OUG 89/2025), fără a număra ziua emiterii.
///
/// Regula vine din lege și se poate schimba din nou: dacă se schimbă, se modifică AICI și în teste.
/// Sistemul trimite automat la emitere; alerta apare pentru facturile netransmise după
/// <see cref="ZileAlerta"/> zile lucrătoare.
/// </summary>
public static class TermenEFactura
{
    public const int ZileTermen = 5;
    public const int ZileAlerta = 3;

    /// <summary>Data de la care se aplică regula zilelor lucrătoare (facturile anterioare: 5 zile calendaristice).</summary>
    public static readonly DateOnly IntrareInVigoare = new(2026, 1, 1);

    public static DateOnly UltimaZi(DateOnly dataEmitere, ICalendarLucru calendar) =>
        dataEmitere >= IntrareInVigoare
            ? calendar.AdaugaZileLucratoare(dataEmitere, ZileTermen)
            : dataEmitere.AddDays(ZileTermen);

    public static bool TrebuieAlerta(DateOnly dataEmitere, DateOnly azi, ICalendarLucru calendar) =>
        azi >= calendar.AdaugaZileLucratoare(dataEmitere, ZileAlerta);
}
