namespace ErpConstructii.Proiecte;

/// <summary>Informațiile de proiect necesare documentelor din alte module.</summary>
public sealed record DateProiectPentruDocument(Guid Id, string Cod, string Denumire, string AdresaSantier);

/// <summary>Cine cere materialele (D-38): un șantier al proiectului sau un compartiment al firmei.</summary>
public sealed record LocSolicitant(Guid Id, bool EsteSantier, string Denumire, bool Activ);

/// <summary>Contract public pentru citirea proiectelor fără dependență de infrastructura Proiecte.</summary>
public interface IInterogareProiecte
{
    Task<DateProiectPentruDocument?> CautaPentruDocumentAsync(Guid proiectId, CancellationToken ct = default);

    /// <summary>Proiectele firmei active care nu sunt închise sau pierdute (ex. pentru potrivirea documentelor importate).</summary>
    Task<IReadOnlyList<DateProiectPentruDocument>> ListaActiveAsync(CancellationToken ct = default);

    /// <summary>Șantierul proiectului sau compartimentul firmei; null dacă nu există (sau șantierul e al altui proiect).</summary>
    Task<LocSolicitant?> LocSolicitantAsync(Guid proiectId, Guid? santierId, Guid? compartimentId, CancellationToken ct = default);

    /// <summary>Șantierele active ale proiectului și compartimentele active ale firmei.</summary>
    Task<IReadOnlyList<LocSolicitant>> LocuriSolicitantAsync(Guid proiectId, CancellationToken ct = default);
}
