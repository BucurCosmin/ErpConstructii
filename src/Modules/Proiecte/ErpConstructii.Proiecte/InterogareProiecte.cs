namespace ErpConstructii.Proiecte;

/// <summary>Informațiile de proiect necesare documentelor din alte module.</summary>
public sealed record DateProiectPentruDocument(Guid Id, string Cod, string Denumire, string AdresaSantier);

/// <summary>Contract public pentru citirea proiectelor fără dependență de infrastructura Proiecte.</summary>
public interface IInterogareProiecte
{
    Task<DateProiectPentruDocument?> CautaPentruDocumentAsync(Guid proiectId, CancellationToken ct = default);

    /// <summary>Proiectele firmei active care nu sunt închise sau pierdute (ex. pentru potrivirea documentelor importate).</summary>
    Task<IReadOnlyList<DateProiectPentruDocument>> ListaActiveAsync(CancellationToken ct = default);
}
