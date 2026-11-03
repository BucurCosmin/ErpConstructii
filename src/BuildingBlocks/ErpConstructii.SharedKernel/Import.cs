namespace ErpConstructii.SharedKernel.Import;

/// <summary>
/// Tipuri comune pentru toți importatorii (devize, grafice, formulare, facturi).
/// Modelul de importatori este același peste tot: detectare → citire într-un model canonic →
/// validare → previzualizare → confirmare (vezi docs/cerinte.md, REQ-IMP-*).
/// </summary>
/// <remarks>Fișierele importate sunt mici (sub câțiva MB), deci conținutul se ține în memorie.</remarks>
public sealed record SursaImport(string NumeFisier, byte[] Continut)
{
    public string Extensie => Path.GetExtension(NumeFisier).ToLowerInvariant();
}

/// <summary>Cât de sigur este un importator că recunoaște fișierul (0..1) și de ce.</summary>
public sealed record RezultatDetectare(double Incredere, string Motiv)
{
    public static RezultatDetectare Nu(string motiv) => new(0, motiv);
}

public enum Severitate
{
    Informare,
    Avertisment,
    Eroare,
}

/// <summary>
/// O problemă găsită la import sau validare. <see cref="Locatie"/> spune unde
/// (ex. „Obiect 1 / Cat. 2 / art. CA01A1”), ca utilizatorul să o găsească în fișierul original.
/// </summary>
public sealed record ProblemaImport(Severitate Severitate, string Locatie, string Mesaj);

public sealed record RezultatImport<T>(T? Valoare, IReadOnlyList<ProblemaImport> Probleme, IReadOnlyDictionary<string, string> MetadateSursa)
{
    public bool AreErori => Valoare is null || Probleme.Any(p => p.Severitate == Severitate.Eroare);

    public static RezultatImport<T> Ok(T valoare, IReadOnlyList<ProblemaImport>? probleme = null) =>
        new(valoare, probleme ?? [], new Dictionary<string, string>());

    public static RezultatImport<T> Esuat(string mesaj) =>
        new(default, [new ProblemaImport(Severitate.Eroare, "fișier", mesaj)], new Dictionary<string, string>());
}

/// <summary>Contract generic: un importator transformă un fișier într-un model canonic <typeparamref name="T"/>.</summary>
public interface IImportator<T>
{
    /// <summary>Identificator stabil, ex. „windoc”, „primavera-xer”, „excel-grafic”.</summary>
    string FormatId { get; }

    string Denumire { get; }

    RezultatDetectare PoateCiti(SursaImport sursa);

    Task<RezultatImport<T>> CitesteAsync(SursaImport sursa, CancellationToken ct = default);
}

/// <summary>
/// Alege importatorul potrivit pentru un fișier. Activarea per firmă se face prin
/// <paramref name="formateActive"/> (setările firmei), ca un client să vadă doar formatele lui.
/// </summary>
public static class AlegereImportator
{
    public const double PragIncredere = 0.5;

    public static (TImportator? Importator, IReadOnlyList<string> Motive) Alege<TImportator, T>(
        IEnumerable<TImportator> importatori,
        SursaImport sursa,
        IReadOnlySet<string>? formateActive = null)
        where TImportator : class, IImportator<T>
    {
        var candidati = importatori
            .Where(i => formateActive is null || formateActive.Contains(i.FormatId))
            .Select(i => (Importator: i, Detectare: i.PoateCiti(sursa)))
            .OrderByDescending(x => x.Detectare.Incredere)
            .ToList();

        var motive = candidati.Select(c => $"{c.Importator.Denumire}: {c.Detectare.Motiv}").ToList();
        var ales = candidati.FirstOrDefault(c => c.Detectare.Incredere >= PragIncredere);
        return (ales.Importator, motive);
    }
}
