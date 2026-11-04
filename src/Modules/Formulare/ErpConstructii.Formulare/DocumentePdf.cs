namespace ErpConstructii.Formulare;

public enum AliniereColoana
{
    Stanga,
    Centru,
    Dreapta,
}

/// <summary>Antetul firmei pe documente (din datele firmei active).</summary>
public sealed record AntetFirma(string Denumire, string? Cui, string? NrRegCom, string? Adresa);

/// <param name="Eticheta">Textul afișat (ex. „Furnizor”).</param>
/// <param name="Camp">Numele câmpului din <see cref="DateFormular.Campuri"/>.</param>
public sealed record ElementAntet(string Eticheta, string Camp);

/// <param name="Latime">Lățimea relativă (coloanele se împart proporțional pe lățimea paginii).</param>
public sealed record ColoanaPdf(string Camp, string Eticheta, double Latime, AliniereColoana Aliniere = AliniereColoana.Stanga);

/// <summary>
/// Formatul standard al unui document (folosit până când firma încarcă propriul șablon Word): titlu, număr / dată,
/// câmpurile de antet pe două coloane, un tabel, totaluri, observații și casetele de semnătură.
/// </summary>
/// <param name="Cod">Codul formularului tipizat afișat sub titlu (ex. „14-3-1A”); opțional.</param>
/// <param name="Tabel">Numele tabelului din <see cref="DateFormular.Tabele"/>.</param>
/// <param name="Peisaj">Pagină orizontală (tabele late, ex. NIR-ul cu diferențe).</param>
public sealed record AspectDocument(
    string Titlu,
    string? Cod,
    string NumarSiData,
    IReadOnlyList<ElementAntet> Antet,
    string Tabel,
    IReadOnlyList<ColoanaPdf> Coloane,
    IReadOnlyList<ElementAntet> Totaluri,
    IReadOnlyList<string> Semnaturi,
    string? CampObservatii = null,
    bool Peisaj = false);

/// <summary>Un document generat, gata de descărcat.</summary>
public sealed record DocumentGenerat(byte[] Continut, string TipContinut, string NumeFisier, bool DinSablonFirma);

/// <summary>Randează un document în formatul standard, direct în PDF (fără Word și fără servicii externe).</summary>
public interface IGeneratorPdfStandard
{
    byte[] Genereaza(AntetFirma firma, AspectDocument aspect, DateFormular date);
}

/// <summary>
/// Generarea PDF-urilor documentelor: din șablonul Word activ al firmei pentru tipul de formular (dacă există și conversia
/// Word → PDF e disponibilă), altfel în formatul standard. Ceilalți moduli o folosesc prin acest contract (regula 1).
/// </summary>
public interface IServiciuDocumente
{
    Task<DocumentGenerat> PdfAsync(string codTip, AntetFirma firma, AspectDocument aspect, DateFormular date, string numeFisier, CancellationToken ct = default);
}
