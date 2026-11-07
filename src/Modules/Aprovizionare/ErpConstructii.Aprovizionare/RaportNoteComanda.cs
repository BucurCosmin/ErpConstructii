using System.Globalization;
using System.Text;

namespace ErpConstructii.Aprovizionare;

/// <summary>Un material comandat pe o lucrare: cantitatea din toate notele de comandă alese.</summary>
/// <param name="Denumire">Denumirea din prima notă (în ordinea datelor); variantele scrise altfel se adună aici.</param>
public sealed record MaterialComandat(
    Guid ProiectId,
    string ProiectCod,
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    int Note,
    IReadOnlyList<string> Furnizori,
    DateOnly PrimaData,
    DateOnly UltimaData);

/// <summary>Rapoartele notelor de comandă pe lucrare (logică pură).</summary>
public static class RaportNoteComanda
{
    /// <summary>
    /// Materialele comandate pe fiecare proiect: aceeași denumire (fără diferențe de majuscule, diacritice sau spații) și
    /// aceeași UM se adună; o UM diferită rămâne pe linie separată (cantitățile nu se pot aduna).
    /// </summary>
    public static IReadOnlyList<MaterialComandat> MaterialeComandate(IEnumerable<NotaComanda> note) =>
        note.OrderBy(n => n.Data).ThenBy(n => n.An).ThenBy(n => n.Numar)
            .SelectMany(n => n.Linii.Select(l => (Nota: n, Linie: l)))
            .GroupBy(x => (x.Nota.ProiectId, Material: CheieMaterial(x.Linie.Denumire), Um: CheieMaterial(x.Linie.UnitateMasura)))
            .Select(g => new MaterialComandat(
                g.Key.ProiectId,
                g.First().Nota.ProiectCod,
                g.First().Linie.Denumire,
                g.First().Linie.UnitateMasura,
                g.Sum(x => x.Linie.Cantitate),
                g.Select(x => x.Nota.Id).Distinct().Count(),
                g.Select(x => x.Nota.FurnizorDenumire).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                g.Min(x => x.Nota.Data),
                g.Max(x => x.Nota.Data)))
            .OrderBy(m => m.ProiectCod, StringComparer.Ordinal)
            .ThenBy(m => m.Denumire, StringComparer.Create(CultureInfo.GetCultureInfo("ro-RO"), ignoreCase: true))
            .ToList();

    /// <summary>„Mănuși  protecție” = „MANUSI PROTECTIE”.</summary>
    public static string CheieMaterial(string text)
    {
        var fd = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(fd.Length);
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToUpperInvariant(ch));
        }

        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
