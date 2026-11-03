namespace ErpConstructii.SharedKernel;

/// <summary>
/// Eroare de business, cu cod stabil (folosit de frontend pentru traduceri) și mesaj în română.
/// </summary>
public sealed record Eroare(string Cod, string Mesaj)
{
    public static readonly Eroare Niciuna = new(string.Empty, string.Empty);

    public static Eroare NuExista(string ce) => new("nu_exista", $"{ce} nu există.");
    public static Eroare Validare(string mesaj) => new("validare", mesaj);
    public static Eroare Interzis(string mesaj) => new("interzis", mesaj);
}

/// <summary>
/// Rezultatul unei operații de business. Folosit în locul excepțiilor pentru erorile
/// așteptate (validare, stare invalidă); excepțiile rămân pentru erori neprevăzute.
/// </summary>
public class Rezultat
{
    protected Rezultat(bool succes, Eroare eroare)
    {
        Succes = succes;
        Eroare = eroare;
    }

    public bool Succes { get; }
    public bool Esec => !Succes;
    public Eroare Eroare { get; }

    public static Rezultat Ok() => new(true, Eroare.Niciuna);
    public static Rezultat Esuat(Eroare eroare) => new(false, eroare);
    public static Rezultat<T> Ok<T>(T valoare) => new(valoare, true, Eroare.Niciuna);
    public static Rezultat<T> Esuat<T>(Eroare eroare) => new(default, false, eroare);
}

public sealed class Rezultat<T> : Rezultat
{
    private readonly T? _valoare;

    internal Rezultat(T? valoare, bool succes, Eroare eroare) : base(succes, eroare)
    {
        _valoare = valoare;
    }

    public T Valoare => Succes
        ? _valoare!
        : throw new InvalidOperationException($"Rezultat eșuat: {Eroare.Mesaj}");
}
