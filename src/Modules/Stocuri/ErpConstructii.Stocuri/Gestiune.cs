using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

public enum TipGestiune
{
    /// <summary>Depozit al firmei (implicit „Depozit central” = STOC): aici intră materialele recepționate.</summary>
    Depozit,

    /// <summary>„Materiale în șantier”: gestiunea unui proiect, alimentată prin transferuri din depozit.</summary>
    Santier,
}

/// <summary>Locul în care stau materialele: un depozit sau șantierul unui proiect (o singură gestiune pe proiect).</summary>
public sealed class Gestiune : EntitateFirma
{
    public const string CodDepozitCentral = "DEP-01";

    private Gestiune() { }

    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public TipGestiune Tip { get; private set; }

    /// <summary>Proiectul, pentru gestiunile de șantier.</summary>
    public Guid? ProiectId { get; private set; }

    public bool Activa { get; private set; } = true;

    public static Rezultat<Gestiune> CreeazaDepozit(string cod, string denumire)
    {
        if (string.IsNullOrWhiteSpace(cod) || cod.Trim().Length > 20)
            return Rezultat.Esuat<Gestiune>(Eroare.Validare("Codul depozitului este obligatoriu (cel mult 20 de caractere)."));
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 200)
            return Rezultat.Esuat<Gestiune>(Eroare.Validare("Denumirea depozitului este obligatorie (cel mult 200 de caractere)."));
        return Rezultat.Ok(new Gestiune { Cod = cod.Trim().ToUpperInvariant(), Denumire = denumire.Trim(), Tip = TipGestiune.Depozit });
    }

    public static Gestiune DepozitCentral() => new() { Cod = CodDepozitCentral, Denumire = "Depozit central", Tip = TipGestiune.Depozit };

    /// <summary>„Materiale în șantier” pentru un proiect; creată automat la primul transfer.</summary>
    public static Gestiune Santier(Guid proiectId, string proiectCod, string proiectDenumire)
    {
        var cod = proiectCod.Trim();
        var denumire = $"Șantier {cod} — {proiectDenumire.Trim()}";
        return new Gestiune
        {
            Cod = cod.Length > 20 ? cod[..20] : cod,
            Denumire = denumire.Length > 200 ? denumire[..200] : denumire,
            Tip = TipGestiune.Santier,
            ProiectId = proiectId,
        };
    }
}
