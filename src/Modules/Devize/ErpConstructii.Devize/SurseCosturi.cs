namespace ErpConstructii.Devize;

// Contractele prin care celelalte module dau modulului Devize prețuri și costuri (D-55, docs/preturi-resurse.md §2).
// Devize le definește și le citește la cerere; fiecare modul-sursă le implementează într-o clasă din Infrastructure-ul lui.
// Devize nu cunoaște tabelele altor module, iar un modul lipsă înseamnă doar o sursă mai puțin.

public enum SursaPret
{
    Receptie,
    Comanda,
    Oferta,
}

/// <summary>Un preț de achiziție al unui material din stoc, în UM-ul materialului (din NIR, comandă, ofertă).</summary>
/// <param name="Document">Numărul afișat (ex. „NIR/2026/0012 · factura F123”).</param>
public sealed record PretAchizitie(
    Guid ArticolStocId, decimal PretUnitar, decimal Cantitate, DateOnly Data, SursaPret Sursa, string Document, Guid? DocumentId,
    string? Furnizor, Guid? ProiectId);

/// <summary>Prețurile de achiziție ale materialelor din stoc (Stocuri: recepțiile; mai târziu comenzile, ofertele).</summary>
public interface ISursaPreturi
{
    Task<IReadOnlyList<PretAchizitie>> PreturiAsync(IReadOnlyCollection<Guid> articoleStoc, DateOnly? dela, CancellationToken ct = default);
}

/// <summary>Natura unei sume din costurile proiectului.</summary>
public enum NaturaCost
{
    /// <summary>Cost pe un articol de deviz (sau pe cheltuielile generale, fără articol): ex. materialele consumate.</summary>
    Consum,

    /// <summary>Materiale cumpărate pe proiect (facturi cu linii de stoc): devin cost abia la consum.</summary>
    AchizitieMateriale,

    /// <summary>Servicii / lucrări facturate pe proiect (fără stoc), încă nerepartizate pe articole.</summary>
    Serviciu,
}

/// <summary>O sumă din costurile reale ale proiectului, în lei, fără TVA.</summary>
/// <param name="ArticolDevizCheie">Cheia stabilă a articolului de deviz; null = fără articol (cheltuieli generale / nerepartizat).</param>
/// <param name="Componenta">Resursa articolului pe care o consumă (materialele consumate = Material); null când nu se știe.</param>
public sealed record CostRealizat(
    Guid? ArticolDevizCheie, NaturaCost Natura, Calcul.TipResursa? Componenta, decimal Valoare, DateOnly Data, string Document, Guid? DocumentId,
    string Descriere);

/// <summary>Costurile reale ale unui proiect (Stocuri: consumurile pe articole; Facturi: facturile primite pe proiect).</summary>
public interface ISursaCosturi
{
    Task<IReadOnlyList<CostRealizat>> CosturiAsync(Guid proiectId, CancellationToken ct = default);
}
