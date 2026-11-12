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

/// <summary>Alte fișiere ale aceluiași deviz, citite împreună cu fișierul principal (ex. lista F3 lângă devizul ISDP).</summary>
/// <param name="Curs">Cursul ofertei (lei / unitate), dacă îl dă utilizatorul; altfel se deduce (ISDP: din F3).</param>
/// <param name="Moneda">Moneda ofertei, când nu reiese din fișiere (ex. fără F3).</param>
public sealed record OptiuniImportDeviz(IReadOnlyList<SursaImport> Anexe, decimal? Curs = null, string? Moneda = null);

/// <summary>Importator care primește și fișiere anexe (ISDP: devizul cu resurse + F3 opțional) și opțiunile ofertei.</summary>
public interface IImportatorDevizCuAnexe : IImportatorDeviz
{
    Task<RezultatImport<DevizCanonic>> CitesteAsync(SursaImport principal, OptiuniImportDeviz optiuni, CancellationToken ct = default);
}
