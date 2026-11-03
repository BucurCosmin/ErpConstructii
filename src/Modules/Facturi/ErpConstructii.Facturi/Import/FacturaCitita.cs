using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Facturi.Import;

public sealed record LinieFacturaCitita(
    int Rand,
    CampCitit<string> Denumire,
    CampCitit<string> UnitateMasura,
    CampCitit<decimal> Cantitate,
    CampCitit<decimal> PretUnitar,
    CampCitit<decimal> ValoareFaraTva,
    CampCitit<decimal> CotaTva);

/// <summary>
/// Modelul canonic al unei facturi primite, așa cum a fost citită dintr-un XML e-Factura (totul sigur) sau dintr-un
/// PDF (câmpurile neclare marcate). Nu e încă o factură înregistrată: utilizatorul verifică, alege proiectul, confirmă.
/// </summary>
/// <param name="CumparatorCui">CUI-ul cumpărătorului din document; trebuie să fie al firmei active.</param>
/// <param name="Referinta">Comanda / documentul de referință (OrderReference sau text găsit în PDF).</param>
public sealed record FacturaCitita(
    SursaFactura Sursa,
    CampCitit<string> Numar,
    CampCitit<DateOnly> DataEmitere,
    CampCitit<DateOnly> DataScadenta,
    CampCitit<string> Moneda,
    CampCitit<string> FurnizorDenumire,
    CampCitit<string> FurnizorCui,
    CampCitit<string> CumparatorCui,
    CampCitit<decimal> TotalFaraTva,
    CampCitit<decimal> TotalTva,
    CampCitit<decimal> TotalDePlata,
    IReadOnlyList<LinieFacturaCitita> Linii,
    string? Referinta,
    IReadOnlyList<ProblemaImport> Probleme);
