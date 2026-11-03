namespace ErpConstructii.Proiecte;

/// <summary>Informațiile de proiect necesare documentelor din alte module.</summary>
public sealed record DateProiectPentruDocument(Guid Id, string Cod, string Denumire, string AdresaSantier);

/// <summary>Contract public pentru citirea proiectelor fără dependență de infrastructura Proiecte.</summary>
public interface IInterogareProiecte
{
    Task<DateProiectPentruDocument?> CautaPentruDocumentAsync(Guid proiectId, CancellationToken ct = default);
}
