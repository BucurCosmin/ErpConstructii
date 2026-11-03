using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte;

/// <summary>
/// Parametrii financiari ai contractului care intră în calculul fiecărei situații de lucrări.
/// </summary>
/// <param name="ProcentRecuperareAvans">
/// Din fiecare situație se recuperează acest procent din valoare, până la epuizarea avansului
/// (ex. avans 10% recuperat proporțional → 0,10).
/// </param>
/// <param name="ProcentRetinereGarantie">
/// Garanția de bună execuție constituită prin rețineri din plăți (ex. 0,05 – 0,10). Se aplică pe
/// valoarea fără TVA a situației; contractul poate cere altă bază → parametru, nu cod nou.
/// </param>
/// <param name="TaxareInversa">Lucrări de construcții între firme plătitoare de TVA: factura fără TVA.</param>
public sealed record ParametriSituatie(
    decimal ProcentRecuperareAvans,
    decimal ProcentRetinereGarantie,
    decimal CotaTva,
    bool TaxareInversa)
{
    public Rotunjire Rotunjire { get; init; } = Rotunjire.Lei;
}

/// <summary>Intrările unei situații lunare.</summary>
/// <param name="ValoareExecutata">Valoarea lucrărilor executate în lună, la prețurile din contract, fără TVA.</param>
/// <param name="Ajustare">Diferența din formula de ajustare a prețului pentru această situație (poate fi negativă).</param>
/// <param name="AvansRamasDeRecuperat">Avansul încasat și încă nerecuperat din situațiile anterioare.</param>
public sealed record IntrariSituatie(decimal ValoareExecutata, decimal Ajustare, decimal AvansRamasDeRecuperat);

/// <summary>Rezultatul: ce se facturează și ce se încasează.</summary>
public sealed record RezultatSituatie(
    decimal ValoareExecutata,
    decimal Ajustare,
    decimal ValoareSituatie,
    decimal RecuperareAvans,
    decimal BazaImpozabila,
    decimal Tva,
    decimal TotalFactura,
    decimal RetinereGarantie,
    decimal DeIncasat,
    decimal AvansRamasDupa);

/// <summary>
/// Calculul unei situații de lucrări (REQ-SIT-03). Funcție pură.
///
///   Valoare situație   = valoare executată + ajustare
///   Recuperare avans   = min(avans rămas, rotunjit(valoare situație × % recuperare))
///   Bază impozabilă    = valoare situație − recuperare avans   (avansul a fost deja facturat cu TVA)
///   TVA                = 0 la taxare inversă, altfel rotunjit(bază × cotă)
///   Total factură      = bază + TVA
///   Reținere garanție  = rotunjit(valoare situație × % garanție)
///   De încasat         = total factură − reținere garanție
///
/// ATENȚIE: tratamentul exact (ex. recuperarea avansului prin factură storno separată, baza garanției)
/// diferă între contracte. Se validează cu contabilul pilotului înainte de faza 1; variațiile
/// devin parametri în <see cref="ParametriSituatie"/>.
/// </summary>
public static class CalculatorSituatie
{
    public static RezultatSituatie Calculeaza(IntrariSituatie intrari, ParametriSituatie p)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intrari.AvansRamasDeRecuperat);
        var r = p.Rotunjire;

        var valoareSituatie = r.Aplica(intrari.ValoareExecutata + intrari.Ajustare);
        var recuperare = Math.Min(intrari.AvansRamasDeRecuperat, r.Aplica(valoareSituatie * p.ProcentRecuperareAvans));
        if (recuperare < 0)
        {
            recuperare = 0; // situație negativă (corecție): nu „returnăm” avans automat
        }

        var baza = valoareSituatie - recuperare;
        var tva = p.TaxareInversa ? 0 : r.Aplica(baza * p.CotaTva);
        var totalFactura = baza + tva;
        var retinere = Math.Max(0, r.Aplica(valoareSituatie * p.ProcentRetinereGarantie));

        return new RezultatSituatie(
            intrari.ValoareExecutata,
            intrari.Ajustare,
            valoareSituatie,
            recuperare,
            baza,
            tva,
            totalFactura,
            retinere,
            totalFactura - retinere,
            intrari.AvansRamasDeRecuperat - recuperare);
    }
}
