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
/// O linie orizontală desenată pe pagină (bordura rândurilor unui tabel), în aceleași coordonate ca rândurile de text.
/// Capetele au fiecare Y-ul lor: o pagină scanată e des ușor înclinată.
/// </summary>
public sealed record LinieOrizontala(int Pagina, int X0, int Y0, int X1, int Y1)
{
    /// <summary>Înălțimea liniei la abscisa dată (interpolare între capete).</summary>
    public double YLa(double x) => X1 == X0 ? (Y0 + Y1) / 2.0 : Y0 + (Y1 - Y0) * (Math.Clamp(x, X0, X1) - X0) / (double)(X1 - X0);
}

/// <summary>
/// Textul unui document, independent de sursă: Word (rânduri + tabele exacte) sau scanare (rânduri OCR cu poziții).
/// Extractorii (infrastructură) îl produc; interpretorii (logică pură) îl transformă în modelul canonic.
/// </summary>
/// <param name="Linii">Liniile orizontale ale tabelelor (PDF / scanare), dacă s-au găsit: delimitează rândurile fizice.</param>
public sealed record TextDocument(IReadOnlyList<RandText> Randuri, IReadOnlyList<TabelText> Tabele, bool DinOcr, IReadOnlyList<LinieOrizontala>? Linii = null)
{
    /// <summary>Sub acest prag de încredere OCR, un câmp e marcat „de verificat”.</summary>
    public const float PragIncredere = 75f;

    public IReadOnlyList<LinieOrizontala> LiniiOrizontale => Linii ?? [];
}

/// <summary>Extrage textul dintr-un fișier (Word, PDF, imagine). Nu aruncă pentru fișiere greșite: întoarce probleme.</summary>
public interface IExtractorText
{
    bool Accepta(string numeFisier);

    Task<RezultatImport<TextDocument>> ExtrageAsync(SursaImport sursa, CancellationToken ct = default);
}
