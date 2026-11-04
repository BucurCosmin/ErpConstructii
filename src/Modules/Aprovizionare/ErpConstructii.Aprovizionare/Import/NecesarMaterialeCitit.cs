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
/// Modelul canonic al unui necesar de materiale emis în afara sistemului (pe hârtie sau Word),
/// așa cum a fost înțeles din document. Nu e încă un necesar: utilizatorul verifică și completează, apoi îl confirmă.
/// </summary>
public sealed record NecesarMaterialeCitit(
    CampCitit<string> NumarExtern,
    CampCitit<DateOnly> Data,
    CampCitit<string> CompartimentSantier,
    CampCitit<string> Lucrare,
    IReadOnlyList<LinieCitita> Linii,
    IReadOnlyList<ProblemaImport> Probleme,
    bool DinOcr);
