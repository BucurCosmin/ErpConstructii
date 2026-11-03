namespace ErpConstructii.SharedKernel.Import;

/// <summary>
/// Un cuvânt citit dintr-un document, cu poziția pe orizontală (pentru reconstruirea coloanelor din scanări)
/// și încrederea recunoașterii (0–100; 100 pentru textul citit direct din Word).
/// </summary>
public sealed record CuvantText(string Text, int X0, int X1, float Incredere);

/// <summary>
/// Un rând de text, în ordinea de citire. Pentru Word, <see cref="Cuvinte"/> poate fi gol.
/// <see cref="Y0"/>/<see cref="Y1"/> (sus/jos, în pixeli; 0 dacă nu se cunosc) ajută la unirea rândurilor din aceeași celulă.
/// </summary>
public sealed record RandText(string Text, IReadOnlyList<CuvantText> Cuvinte, int Pagina = 1, int Y0 = 0, int Y1 = 0)
{
    public bool ArePozitieVerticala => Y1 > Y0;

    public static RandText DinText(string text) => new(text, []);
}

/// <summary>Un tabel citit structurat (din Word): rânduri de celule.</summary>
public sealed record TabelText(IReadOnlyList<IReadOnlyList<string>> Randuri);

/// <summary>
/// Textul unui document, independent de sursă: Word (rânduri + tabele exacte) sau scanare (rânduri OCR cu poziții).
/// Extractorii (infrastructură) îl produc; interpretorii (logică pură) îl transformă în modelul canonic.
/// </summary>
public sealed record TextDocument(IReadOnlyList<RandText> Randuri, IReadOnlyList<TabelText> Tabele, bool DinOcr)
{
    /// <summary>Sub acest prag de încredere OCR, un câmp e marcat „de verificat”.</summary>
    public const float PragIncredere = 75f;
}

/// <summary>Extrage textul dintr-un fișier (Word, PDF, imagine). Nu aruncă pentru fișiere greșite: întoarce probleme.</summary>
public interface IExtractorText
{
    bool Accepta(string numeFisier);

    Task<RezultatImport<TextDocument>> ExtrageAsync(SursaImport sursa, CancellationToken ct = default);
}
