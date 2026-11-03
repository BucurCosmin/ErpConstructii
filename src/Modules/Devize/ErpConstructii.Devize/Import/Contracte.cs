using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Devize.Import;

/// <summary>
/// Contractul unui importator de devize (REQ-IMP-01). Fiecare format extern este un pachet separat
/// care implementează această interfață; nucleul nu cunoaște niciun format.
///
/// Reguli:
///  - importatorul NU atinge baza de date și NU cunoaște entitățile ERP; produce doar <see cref="DevizCanonic"/>;
///  - nu aruncă excepții pentru fișiere greșite: întoarce <see cref="RezultatImport{T}"/> cu probleme;
///  - vine cu fișiere de test și totalurile așteptate (test de regresie la ban).
/// </summary>
public interface IImportatorDeviz : IImportator<DevizCanonic>;
