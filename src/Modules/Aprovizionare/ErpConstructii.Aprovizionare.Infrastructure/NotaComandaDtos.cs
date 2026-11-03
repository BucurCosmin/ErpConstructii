using ErpConstructii.Aprovizionare;

namespace ErpConstructii.Aprovizionare.Infrastructure;

/// <summary>O poziție de material solicitată într-o notă de comandă.</summary>
public sealed record LinieNotaComandaCerere(
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    string? Observatii,
    Guid? ArticolDevizId);

/// <summary>Datele introduse pentru emiterea unei note; numărul, data și lucrarea sunt generate de sistem.</summary>
public sealed record EmiteNotaComandaCerere(string CompartimentSantier, IReadOnlyList<LinieNotaComandaCerere> Linii);

/// <summary>Motivul obligatoriu al respingerii unei note de comandă.</summary>
public sealed record RespingeNotaComandaCerere(string Motiv);

/// <summary>Filtre opționale pentru lista notelor de comandă accesibile utilizatorului curent.</summary>
public sealed record FiltreNoteComanda
{
    /// <summary>Limitează lista la un proiect.</summary>
    public Guid? ProiectId { get; init; }

    /// <summary>Caută după denumirea lucrării.</summary>
    public string? Lucrare { get; init; }

    /// <summary>Caută după denumirea unui material din notă.</summary>
    public string? Material { get; init; }

    /// <summary>Limitează lista la data de început, inclusiv.</summary>
    public DateOnly? DataDe { get; init; }

    /// <summary>Limitează lista la data de sfârșit, inclusiv.</summary>
    public DateOnly? DataPanaLa { get; init; }

    /// <summary>Filtrează după statusul notei.</summary>
    public string? Stare { get; init; }

    /// <summary>Caută după numărul notei.</summary>
    public string? Numar { get; init; }

    /// <summary>Caută după numele persoanei care a întocmit nota.</summary>
    public string? Intocmit { get; init; }

    /// <summary>Caută după numele persoanei care a aprobat nota.</summary>
    public string? AprobatDe { get; init; }
}

/// <summary>Poziție returnată împreună cu nota de comandă.</summary>
public sealed record LinieNotaComandaDto(
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    string Observatii,
    Guid? ArticolDevizId);

/// <summary>Notă de comandă afișată în lista de aprobare.</summary>
public sealed record NotaComandaDto(
    Guid Id,
    Guid ProiectId,
    string ProiectCod,
    string Lucrare,
    string CompartimentSantier,
    string Numar,
    DateOnly Data,
    string Stare,
    Guid EmitentId,
    string EmitentNume,
    Guid? DecisaDeId,
    string? DecisaDeNume,
    DateTimeOffset? DecisaLa,
    string? MotivRespingere,
    int VersiuneSablon,
    IReadOnlyList<LinieNotaComandaDto> Linii);

internal static class MapareNotaComanda
{
    public static NotaComandaDto LaDto(NotaComanda nota) =>
        new(
            nota.Id,
            nota.ProiectId,
            nota.ProiectCod,
            nota.Lucrare,
            nota.CompartimentSantier,
            nota.NumarAfisat,
            nota.Data,
            nota.Stare.ToString(),
            nota.EmitentId,
            nota.EmitentNume,
            nota.DecisaDeId,
            nota.DecisaDeNume,
            nota.DecisaLa,
            nota.MotivRespingere,
            nota.VersiuneSablon,
            nota.Linii.Select(l => new LinieNotaComandaDto(
                l.Denumire, l.UnitateMasura, l.Cantitate, l.Observatii, l.ArticolDevizId)).ToList());
}
