using ErpConstructii.Aprovizionare;

namespace ErpConstructii.Aprovizionare.Infrastructure;

/// <summary>O poziție de material solicitată într-un necesar de materiale.</summary>
public sealed record LinieNecesarMaterialeCerere(
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    string? Observatii,
    Guid? ArticolDevizId);

/// <summary>Datele introduse pentru emiterea unei necesare; numărul, data și lucrarea sunt generate de sistem.</summary>
/// <param name="SantierId">Șantierul proiectului care cere materialele (D-38); exclusiv cu <paramref name="CompartimentId"/>.</param>
/// <param name="CompartimentSantier">Text: doar dacă nu se dă niciun Id (clienți vechi); se potrivește cu un șantier / compartiment existent.</param>
public sealed record EmiteNecesarMaterialeCerere(
    string? CompartimentSantier, IReadOnlyList<LinieNecesarMaterialeCerere> Linii, Guid? SantierId = null, Guid? CompartimentId = null);

/// <summary>Motivul obligatoriu al respingerii unui necesar de materiale.</summary>
public sealed record RespingeNecesarMaterialeCerere(string Motiv);

/// <summary>Filtre opționale pentru lista necesarelor de materiale accesibile utilizatorului curent.</summary>
public sealed record FiltreNecesareMateriale
{
    /// <summary>Limitează lista la un proiect.</summary>
    public Guid? ProiectId { get; init; }

    /// <summary>Caută după denumirea lucrării.</summary>
    public string? Lucrare { get; init; }

    /// <summary>Caută după denumirea unui material din necesar.</summary>
    public string? Material { get; init; }

    /// <summary>Limitează lista la data de început, inclusiv.</summary>
    public DateOnly? DataDe { get; init; }

    /// <summary>Limitează lista la data de sfârșit, inclusiv.</summary>
    public DateOnly? DataPanaLa { get; init; }

    /// <summary>Filtrează după statusul necesarului.</summary>
    public string? Stare { get; init; }

    /// <summary>Caută după numărul necesarului.</summary>
    public string? Numar { get; init; }

    /// <summary>Caută după numele persoanei care a întocmit necesarul.</summary>
    public string? Intocmit { get; init; }

    /// <summary>Caută după numele persoanei care a aprobat necesarul.</summary>
    public string? AprobatDe { get; init; }
}

/// <summary>Poziție returnată împreună cu necesarul de materiale.</summary>
public sealed record LinieNecesarMaterialeDto(
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    string Observatii,
    Guid? ArticolDevizId);

/// <summary>Necesar de materiale afișat în lista de aprobare.</summary>
public sealed record NecesarMaterialeDto(
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
    IReadOnlyList<LinieNecesarMaterialeDto> Linii,
    string Sursa,
    string? NumarExtern,
    bool AreOriginal,
    Guid? SantierId = null,
    Guid? CompartimentId = null,
    bool DinUrma = false);

internal static class MapareNecesarMateriale
{
    public static NecesarMaterialeDto LaDto(NecesarMateriale necesar) =>
        new(
            necesar.Id,
            necesar.ProiectId,
            necesar.ProiectCod,
            necesar.Lucrare,
            necesar.CompartimentSantier,
            necesar.NumarAfisat,
            necesar.Data,
            necesar.Stare.ToString(),
            necesar.EmitentId,
            necesar.EmitentNume,
            necesar.DecisaDeId,
            necesar.DecisaDeNume,
            necesar.DecisaLa,
            necesar.MotivRespingere,
            necesar.VersiuneSablon,
            necesar.Linii.Select(l => new LinieNecesarMaterialeDto(
                l.Denumire, l.UnitateMasura, l.Cantitate, l.Observatii, l.ArticolDevizId)).ToList(),
            necesar.Sursa.ToString(),
            necesar.NumarExtern,
            necesar.DocumentOriginalId is not null,
            necesar.SantierId,
            necesar.CompartimentId,
            necesar.DinUrma);
}
