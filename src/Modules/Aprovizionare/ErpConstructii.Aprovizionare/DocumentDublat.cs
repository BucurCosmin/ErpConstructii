using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Aprovizionare;

/// <summary>Un document importat deja, cu ce îl identifică pe hârtie.</summary>
/// <param name="Descriere">Cum apare în mesaj (ex. „necesarul NM/2026/0005”).</param>
/// <param name="FurnizorCheie">Denumirea furnizorului normalizată de apelant (ex. „TRITON SRL” → aceeași cheie ca „S.C. Triton S.R.L.”).</param>
public sealed record DocumentImportat(string Descriere, string? NumarExtern, DateOnly Data, Guid? FurnizorId = null, string? FurnizorCheie = null);

/// <summary>
/// Același document importat a doua oară, din alt fișier (altă scanare, poză, Word): amprenta fișierului diferă, dar
/// numărul de pe hârtie (comparat pe cifre: „081” = „Nr. 81”) e același. Funcții pure.
/// </summary>
public static class DocumentDublat
{
    /// <summary>Necesar de materiale / notă de comandă: același număr extern și aceeași dată.</summary>
    public static DocumentImportat? DupaNumarSiData(string? numarExtern, DateOnly? data, IEnumerable<DocumentImportat> existente)
    {
        var cheie = ClasificarePagini.CheieNumar(numarExtern);
        return cheie is null || data is null
            ? null
            : existente.FirstOrDefault(e => e.Data == data && ClasificarePagini.CheieNumar(e.NumarExtern) == cheie);
    }

    /// <summary>
    /// Aviz: numerele sunt ale furnizorului, deci același număr la același furnizor (după id, altfel după denumire)
    /// e același aviz, oricare ar fi data citită.
    /// </summary>
    public static DocumentImportat? DupaNumarSiFurnizor(string? numarExtern, Guid? furnizorId, string? furnizorCheie, IEnumerable<DocumentImportat> existente)
    {
        var cheie = ClasificarePagini.CheieNumar(numarExtern);
        if (cheie is null || (furnizorId is null && string.IsNullOrWhiteSpace(furnizorCheie)))
        {
            return null;
        }

        return existente.FirstOrDefault(e => ClasificarePagini.CheieNumar(e.NumarExtern) == cheie
            && ((furnizorId is not null && e.FurnizorId == furnizorId) || (!string.IsNullOrWhiteSpace(furnizorCheie) && e.FurnizorCheie == furnizorCheie)));
    }
}
