namespace ErpConstructii.Formulare.Infrastructure;

/// <summary>O versiune încărcată a unui șablon de formular.</summary>
public sealed record SablonFormularDto(
    Guid Id,
    string CodTip,
    int Versiune,
    string NumeFisier,
    string HashSha256,
    bool Activ);

/// <summary>Un document importat, păstrat ca ciornă pentru verificare.</summary>
public sealed record ImportFormularDto(
    Guid Id,
    string CodTip,
    string NumeFisier,
    string HashSha256,
    DateFormular Date,
    string Stare,
    DateTimeOffset CreatLa);
