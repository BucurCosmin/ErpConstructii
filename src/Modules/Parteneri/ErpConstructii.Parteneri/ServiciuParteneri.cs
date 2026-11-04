using System.Globalization;
using System.Text;
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
    /// Furnizorul după CUI sau, fără CUI, după denumire (comenzile pe hârtie au des doar denumirea):
    /// „S.C TRITON S.R.L” și „Triton SRL” sunt același furnizor (<see cref="DenumireFirma.Cheie"/>).
    /// </summary>
    Task<PartenerRezumat?> CautaFurnizorAsync(string? cui, string? denumire, CancellationToken ct = default);

    /// <summary>
    /// Furnizorul cu CUI-ul dat: îl creează în nomenclator dacă lipsește (cu datele minime din factură) sau îi adaugă
    /// rolul de furnizor dacă exista ca client / subantreprenor. Fără CUI se creează mereu unul nou.
    /// </summary>
    Task<Rezultat<PartenerRezumat>> AsiguraFurnizorAsync(string denumire, string? cui, bool platitorTva, CancellationToken ct = default);

    /// <summary>Ca <see cref="AsiguraFurnizorAsync(string, string?, bool, CancellationToken)"/>, cu adresa și telefonul din document pentru un furnizor nou.</summary>
    Task<Rezultat<PartenerRezumat>> AsiguraFurnizorAsync(string denumire, string? cui, bool platitorTva, string? adresa, string? telefon, CancellationToken ct = default);
}

/// <summary>Compararea denumirilor de firme scrise diferit pe documente.</summary>
public static class DenumireFirma
{
    private static readonly HashSet<string> Forme =
        ["SC", "SRL", "SA", "SNC", "SCS", "PFA", "II", "IF", "SRLD", "COMPANY", "CO", "LTD", "GMBH"];

    /// <summary>
    /// Cheia de comparare: majuscule, fără diacritice, fără punctuație și fără formele juridice
    /// (ex. „S.C. Triton S.R.L.” → „TRITON”; „GLOBAL TRADE INDUSTRY SRL” → „GLOBAL TRADE INDUSTRY”).
    /// </summary>
    public static string Cheie(string denumire)
    {
        var fd = denumire.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(fd.Length);
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark || ch == '.')
            {
                continue; // diacriticele și punctele dispar: „S.R.L” → „SRL”
            }

            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(c => !Forme.Contains(c)));
    }
}
