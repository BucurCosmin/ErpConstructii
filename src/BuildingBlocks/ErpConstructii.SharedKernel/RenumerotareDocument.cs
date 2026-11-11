namespace ErpConstructii.SharedKernel;

/// <summary>
/// Schimbarea numărului de pe hârtie al unui document importat (necesar de materiale, bon de consum), cu motiv: istoricul
/// renumerotărilor (regula 6: documentul aprobat își schimbă doar numărul extern, iar schimbarea rămâne în evidență).
/// Numărul de sistem (NM/…, BC/…) nu se schimbă niciodată.
/// </summary>
public sealed class RenumerotareDocument : EntitateFirma, ICorectieDocument
{
    private RenumerotareDocument() { }

    public Guid DocumentId { get; private set; }

    /// <summary>Documentul blocat (aprobat) primește corectura în aceeași salvare: interceptorul permite schimbarea numărului.</summary>
    public Guid DocumentCorectatId => DocumentId;
    public string DocumentNumar { get; private set; } = string.Empty;
    public string? NumarVechi { get; private set; }
    public string NumarNou { get; private set; } = string.Empty;
    public string Motiv { get; private set; } = string.Empty;
    public Guid UtilizatorId { get; private set; }
    public string UtilizatorNume { get; private set; } = string.Empty;
    public DateTimeOffset La { get; private set; }

    public static Rezultat<RenumerotareDocument> Creeaza(
        Guid documentId, string documentNumar, string? numarVechi, string? numarNou, string? motiv, Guid utilizatorId, string? utilizatorNume, DateTimeOffset la)
    {
        if (string.IsNullOrWhiteSpace(numarNou) || numarNou.Trim().Length > 50)
            return Rezultat.Esuat<RenumerotareDocument>(Eroare.Validare("Completează numărul nou (cel mult 50 de caractere)."));
        if (!numarNou.Any(char.IsAsciiDigit))
            return Rezultat.Esuat<RenumerotareDocument>(Eroare.Validare("Numărul nou trebuie să conțină cifre."));
        if (string.Equals(numarVechi?.Trim(), numarNou.Trim(), StringComparison.Ordinal))
            return Rezultat.Esuat<RenumerotareDocument>(Eroare.Validare("Numărul nou este același cu cel actual."));
        if (string.IsNullOrWhiteSpace(motiv) || motiv.Trim().Length > 500)
            return Rezultat.Esuat<RenumerotareDocument>(Eroare.Validare("Motivul renumerotării este obligatoriu (cel mult 500 de caractere)."));
        if (documentId == Guid.Empty || utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            return Rezultat.Esuat<RenumerotareDocument>(Eroare.Validare("Documentul și utilizatorul sunt obligatorii."));

        return Rezultat.Ok(new RenumerotareDocument
        {
            DocumentId = documentId,
            DocumentNumar = documentNumar,
            NumarVechi = string.IsNullOrWhiteSpace(numarVechi) ? null : numarVechi.Trim(),
            NumarNou = numarNou.Trim(),
            Motiv = motiv.Trim(),
            UtilizatorId = utilizatorId,
            UtilizatorNume = utilizatorNume.Trim(),
            La = la,
        });
    }
}
