using ErpConstructii.SharedKernel;

namespace ErpConstructii.Parteneri;

/// <summary>Ce au nevoie alte module despre un partener (ex. facturile primite: furnizorul după CUI).</summary>
public sealed record PartenerRezumat(Guid Id, string Denumire, string? Cui, bool PlatitorTva, bool EsteFurnizor, bool Activ);

/// <summary>
/// Contractul public al modulului Parteneri pentru celelalte module (regula 1: fără referință la infrastructura lui).
/// Lucrează pe firma activă (RLS).
/// </summary>
public interface IServiciuParteneri
{
    /// <summary>Partenerul cu CUI-ul dat (cu sau fără „RO”), dacă există în nomenclatorul firmei.</summary>
    Task<PartenerRezumat?> CautaDupaCuiAsync(string cui, CancellationToken ct = default);

    /// <summary>
    /// Furnizorul cu CUI-ul dat: îl creează în nomenclator dacă lipsește (cu datele minime din factură) sau îi adaugă
    /// rolul de furnizor dacă exista ca client / subantreprenor. Fără CUI se creează mereu unul nou.
    /// </summary>
    Task<Rezultat<PartenerRezumat>> AsiguraFurnizorAsync(string denumire, string? cui, bool platitorTva, CancellationToken ct = default);
}
