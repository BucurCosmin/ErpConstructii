using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Formulare;

public enum TipCamp
{
    Text,
    Numar,
    Suma,
    Data,
}

/// <param name="Nume">
/// Numele câmpului = eticheta (Tag) controlului de conținut din șablonul Word.
/// Doar litere, cifre și „_”, fără punct (punctul separă tabelul de coloană).
/// </param>
public sealed record CampFormular(string Nume, string Eticheta, TipCamp Tip = TipCamp.Text, bool Obligatoriu = false);

/// <summary>
/// Un tabel repetat (ex. liniile unei comenzi). În șablon, un singur rând conține controale de
/// conținut etichetate „{tabel}.{coloana}” (ex. <c>linii.cantitate</c>); rândul se clonează per linie.
/// </summary>
public sealed record TabelFormular(string Nume, IReadOnlyList<CampFormular> Coloane, bool CelPutinOLinie = false);

/// <summary>
/// Tipul unui formular: schema de câmpuri. Catalogul este extensibil: un tip nou = o schemă +
/// un șablon Word, fără cod nou (REQ-FRM-02).
/// </summary>
public sealed record TipFormular(
    string Cod,
    string Denumire,
    string ModulErp,
    IReadOnlyList<CampFormular> Campuri,
    IReadOnlyList<TabelFormular> Tabele,
    bool SePoateImporta);

/// <summary>Valorile unui formular, ca text (formatate pentru afișare în Word).</summary>
public sealed record DateFormular(
    IReadOnlyDictionary<string, string> Campuri,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> Tabele)
{
    public static readonly DateFormular Gol = new(
        new Dictionary<string, string>(),
        new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>());
}

/// <summary>
/// Proprietăți ascunse scrise în fișierul generat (REQ-FRM-05). La import, ele spun ce tip de formular
/// și ce versiune de șablon este, și — dacă documentul a pornit din sistem — ce document îi corespunde.
/// </summary>
public sealed record MetadateFormular(string CodTip, int VersiuneSablon, Guid? DocumentId, string? NumarRezervat);

public sealed record RezultatCitireFormular(MetadateFormular? Metadate, DateFormular Date, IReadOnlyList<ProblemaImport> Probleme);

/// <summary>Completează un șablon Word cu date. Implementare: Open XML SDK.</summary>
public interface IGeneratorFormular
{
    /// <param name="randuriGoale">Pentru formularele goale descărcate: câte rânduri goale să aibă tabelele.</param>
    byte[] Genereaza(byte[] sablonDocx, TipFormular tip, DateFormular date, MetadateFormular metadate, int randuriGoale = 0);
}

/// <summary>Citește un formular Word completat (în sistem sau manual).</summary>
public interface ICititorFormular
{
    RezultatCitireFormular Citeste(byte[] docx);
}

/// <summary>Verifică un șablon încărcat de firmă: are toate câmpurile obligatorii ale tipului?</summary>
public interface IValidatorSablon
{
    IReadOnlyList<ProblemaImport> Valideaza(byte[] sablonDocx, TipFormular tip);
}

/// <summary>Conversie DOCX → PDF. Implementare gratuită: LibreOffice headless prin Gotenberg (decizia D-10).</summary>
public interface IConvertorPdf
{
    Task<byte[]> InPdfAsync(byte[] docx, string numeFisier, CancellationToken ct = default);
}
