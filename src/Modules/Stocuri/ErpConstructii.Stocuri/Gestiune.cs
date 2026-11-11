using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

public enum TipGestiune
{
    /// <summary>Gestiune dintr-un depozit al firmei (implicit „Depozit central” = DEP-01): aici intră materialele recepționate.</summary>
    Depozit,

    /// <summary>„Materiale în șantier”: gestiunea unui proiect, alimentată prin transferuri din depozit.</summary>
    Santier,
}

/// <summary>
/// Locul fizic în care stau materialele firmei (D-43). Un depozit se împarte în gestiuni, fiecare cu gestionarul ei; stocul
/// se ține pe gestiune, nu pe depozit.
/// </summary>
public sealed class Depozit : EntitateFirma
{
    public const string CodCentral = "DEP-01";

    private Depozit() { }

    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public string? Adresa { get; private set; }
    public bool Activ { get; private set; } = true;

    public static Rezultat<Depozit> Creeaza(string cod, string denumire, string? adresa)
    {
        if (string.IsNullOrWhiteSpace(cod) || cod.Trim().Length > 20)
            return Rezultat.Esuat<Depozit>(Eroare.Validare("Codul depozitului este obligatoriu (cel mult 20 de caractere)."));
        var d = new Depozit { Cod = cod.Trim().ToUpperInvariant() };
        var r = d.Modifica(denumire, adresa, true);
        return r.Esec ? Rezultat.Esuat<Depozit>(r.Eroare) : Rezultat.Ok(d);
    }

    public static Depozit Central() => new() { Cod = CodCentral, Denumire = "Depozit central" };

    public Rezultat Modifica(string denumire, string? adresa, bool activ)
    {
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Denumirea depozitului este obligatorie (cel mult 200 de caractere)."));
        if (adresa?.Trim().Length > 300)
            return Rezultat.Esuat(Eroare.Validare("Adresa are cel mult 300 de caractere."));
        (Denumire, Adresa, Activ) = (denumire.Trim(), string.IsNullOrWhiteSpace(adresa) ? null : adresa.Trim(), activ);
        return Rezultat.Ok();
    }
}

/// <summary>
/// Locul în care se ține evidența stocului: o gestiune dintr-un depozit (cu gestionarul care răspunde de ea) sau șantierul
/// unui proiect (o singură gestiune pe proiect).
/// </summary>
public sealed class Gestiune : EntitateFirma
{
    public const string CodDepozitCentral = "DEP-01";

    private Gestiune() { }

    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public TipGestiune Tip { get; private set; }

    /// <summary>Depozitul din care face parte (gestiunile de depozit); null la șantier.</summary>
    public Guid? DepozitId { get; private set; }

    /// <summary>Utilizatorul care răspunde de gestiune (gestionarul); <see cref="GestionarNume"/> păstrează numele de atunci.</summary>
    public Guid? GestionarId { get; private set; }
    public string? GestionarNume { get; private set; }

    /// <summary>Proiectul, pentru gestiunile de șantier.</summary>
    public Guid? ProiectId { get; private set; }

    public bool Activa { get; private set; } = true;

    /// <summary>O gestiune nouă în depozit, cu gestionarul ei (opțional la creare).</summary>
    public static Rezultat<Gestiune> CreeazaInDepozit(Depozit depozit, string cod, string denumire, Guid? gestionarId, string? gestionarNume)
    {
        if (!depozit.Activ)
            return Rezultat.Esuat<Gestiune>(Eroare.Validare($"Depozitul {depozit.Cod} este inactiv."));
        if (string.IsNullOrWhiteSpace(cod) || cod.Trim().Length > 20)
            return Rezultat.Esuat<Gestiune>(Eroare.Validare("Codul gestiunii este obligatoriu (cel mult 20 de caractere)."));
        var g = new Gestiune { Cod = cod.Trim().ToUpperInvariant(), Tip = TipGestiune.Depozit, DepozitId = depozit.Id };
        var r = g.Modifica(denumire, gestionarId, gestionarNume, true);
        return r.Esec ? Rezultat.Esuat<Gestiune>(r.Eroare) : Rezultat.Ok(g);
    }

    /// <summary>Gestiunea implicită a depozitului central (creată automat dacă firma nu are niciuna).</summary>
    public static Gestiune DepozitCentral(Depozit depozit) =>
        new() { Cod = CodDepozitCentral, Denumire = "Depozit central", Tip = TipGestiune.Depozit, DepozitId = depozit.Id };

    /// <summary>Denumirea, gestionarul (null = fără) și starea. La șantier, gestionarul e opțional, ca și la depozit.</summary>
    public Rezultat Modifica(string denumire, Guid? gestionarId, string? gestionarNume, bool activa)
    {
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Denumirea gestiunii este obligatorie (cel mult 200 de caractere)."));
        if (gestionarId is not null && string.IsNullOrWhiteSpace(gestionarNume))
            return Rezultat.Esuat(Eroare.Validare("Gestionarul ales nu are nume."));
        (Denumire, GestionarId, GestionarNume, Activa) = (denumire.Trim(), gestionarId, gestionarId is null ? null : gestionarNume!.Trim(), activa);
        return Rezultat.Ok();
    }

    /// <summary>Gestiunile de depozit create înaintea depozitelor (D-43) intră în depozitul central.</summary>
    public void MutaInDepozit(Depozit depozit)
    {
        if (Tip == TipGestiune.Depozit)
            DepozitId = depozit.Id;
    }

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
