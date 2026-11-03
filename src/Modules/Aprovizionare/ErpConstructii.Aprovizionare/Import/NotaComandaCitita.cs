using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Aprovizionare.Import;

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
