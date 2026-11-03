using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Aprovizionare.Import;

/// <summary>Cât de sigur e sistemul de valoarea citită dintr-un document extern.</summary>
public enum Certitudine
{
    /// <summary>Citit clar (din Word sau OCR cu încredere mare și valoare validă).</summary>
    Sigur,

    /// <summary>Citit, dar trebuie verificat (OCR neclar, valoare corectată, unitate necunoscută).</summary>
    Nesigur,

    /// <summary>Nu a fost găsit sau e necompletat în document: utilizatorul îl completează.</summary>
    Lipsa,
}

/// <param name="TextSursa">Textul exact din document, arătat utilizatorului lângă valoarea propusă.</param>
public sealed record CampCitit<T>(T? Valoare, Certitudine Certitudine, string? TextSursa = null)
{
    public static CampCitit<T> Lipsa(string? textSursa = null) => new(default, Certitudine.Lipsa, textSursa);
}

/// <param name="Rand">Numărul curent din document (sau poziția, dacă lipsește), pentru mesaje.</param>
public sealed record LinieCitita(
    int Rand,
    CampCitit<string> Denumire,
    CampCitit<string> UnitateMasura,
    CampCitit<decimal> Cantitate,
    CampCitit<string> Observatii)
{
    public bool AreIncertitudini =>
        Denumire.Certitudine != Certitudine.Sigur ||
        UnitateMasura.Certitudine != Certitudine.Sigur ||
        Cantitate.Certitudine != Certitudine.Sigur ||
        Observatii.Certitudine == Certitudine.Nesigur;
}

/// <summary>
/// Modelul canonic al unei note de comandă / necesar de materiale emis în afara sistemului (pe hârtie sau Word),
/// așa cum a fost înțeles din document. Nu e încă o notă: utilizatorul verifică și completează, apoi o confirmă.
/// </summary>
public sealed record NotaComandaCitita(
    CampCitit<string> NumarExtern,
    CampCitit<DateOnly> Data,
    CampCitit<string> CompartimentSantier,
    CampCitit<string> Lucrare,
    IReadOnlyList<LinieCitita> Linii,
    IReadOnlyList<ProblemaImport> Probleme,
    bool DinOcr);
