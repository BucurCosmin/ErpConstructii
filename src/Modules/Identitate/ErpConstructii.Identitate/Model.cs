using ErpConstructii.SharedKernel;

namespace ErpConstructii.Identitate;

/// <summary>Tipurile de utilizatori (REQ-UTL-01). Externii nu au conturi în această etapă.</summary>
public enum TipUtilizator
{
    AdministratorSistem,
    AdministratorFirma,
    Intern,
    Santier,
    Auditor,
    ContServiciu,
}

/// <summary>
/// Utilizator. NU aparține unei singure firme: accesul la firme vine din atribuiri.
/// Parola, MFA și token-urile sunt gestionate de ASP.NET Core Identity + OpenIddict (TODO faza 0);
/// această entitate ține doar datele de business.
/// </summary>
public sealed class Utilizator : Entitate
{
    private Utilizator() { }

    public Utilizator(string email, string nume, TipUtilizator tip)
    {
        Email = email.Trim().ToLowerInvariant();
        Nume = nume.Trim();
        Tip = tip;
        Activ = true;
    }

    public string Email { get; private set; } = string.Empty;
    public string Nume { get; private set; } = string.Empty;
    public TipUtilizator Tip { get; private set; }
    public bool Activ { get; private set; }

    /// <summary>Pentru auditori: contul expiră automat.</summary>
    public DateOnly? ExpiraLa { get; private set; }

    public void Dezactiveaza() => Activ = false;
    public void SeteazaExpirare(DateOnly? data) => ExpiraLa = data;
}

/// <summary>Rol al unei firme (copiat din rolurile predefinite și ajustabil).</summary>
public sealed class Rol : Entitate
{
    private readonly List<string> _permisiuni = [];

    private Rol() { }

    public Rol(Guid firmaId, string cod, string denumire, IEnumerable<string> permisiuni)
    {
        FirmaId = firmaId;
        Cod = cod;
        Denumire = denumire;
        _permisiuni.AddRange(permisiuni.Distinct());
    }

    public Guid FirmaId { get; private set; }
    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public IReadOnlyList<string> Permisiuni => _permisiuni;

    public void InlocuiestePermisiuni(IEnumerable<string> permisiuni)
    {
        _permisiuni.Clear();
        _permisiuni.AddRange(permisiuni.Distinct());
    }
}

/// <summary>Domeniul pe care se aplică un drept: firmă, opțional proiect sau gestiune.</summary>
public readonly record struct Domeniu(Guid FirmaId, Guid? ProiectId = null, Guid? GestiuneId = null);

/// <summary>
/// Atribuire = utilizator + rol + domeniu, cu valabilitate (REQ-DRP-03).
/// ProiectId/GestiuneId null înseamnă „toată firma”.
/// </summary>
public sealed class Atribuire : Entitate
{
    private Atribuire() { }

    public Atribuire(Guid utilizatorId, Guid rolId, Domeniu domeniu, DateOnly valabilDe, DateOnly? valabilPana = null)
    {
        if (valabilPana is not null && valabilPana < valabilDe)
        {
            throw new ArgumentException("Sfârșitul valabilității este înaintea începutului.", nameof(valabilPana));
        }

        UtilizatorId = utilizatorId;
        RolId = rolId;
        FirmaId = domeniu.FirmaId;
        ProiectId = domeniu.ProiectId;
        GestiuneId = domeniu.GestiuneId;
        ValabilDe = valabilDe;
        ValabilPana = valabilPana;
    }

    public Guid UtilizatorId { get; private set; }
    public Guid RolId { get; private set; }
    public Guid FirmaId { get; private set; }
    public Guid? ProiectId { get; private set; }
    public Guid? GestiuneId { get; private set; }
    public DateOnly ValabilDe { get; private set; }
    public DateOnly? ValabilPana { get; private set; }

    public bool EsteValabila(DateOnly zi) => zi >= ValabilDe && (ValabilPana is null || zi <= ValabilPana);
}
