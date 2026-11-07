using ErpConstructii.SharedKernel;

namespace ErpConstructii.Proiecte;

/// <summary>
/// Un punct de lucru al proiectului (D-38): un proiect poate avea mai multe șantiere (ex. tronsoane, obiecte în locuri diferite).
/// Necesarele de materiale se emit pe un șantier sau pe un compartiment al firmei. Nu se șterge (documentele îl referă): se dezactivează.
/// </summary>
public sealed class Santier : EntitateFirma
{
    private Santier() { }

    public Guid ProiectId { get; private set; }
    public string Denumire { get; private set; } = string.Empty;
    public string? Adresa { get; private set; }
    public bool Activ { get; private set; } = true;

    public static Rezultat<Santier> Creeaza(Guid proiectId, string denumire, string? adresa)
    {
        if (proiectId == Guid.Empty)
            return Rezultat.Esuat<Santier>(Eroare.Validare("Proiectul este obligatoriu."));
        var s = new Santier { ProiectId = proiectId };
        var r = s.Modifica(denumire, adresa, true);
        return r.Succes ? Rezultat.Ok(s) : Rezultat.Esuat<Santier>(r.Eroare);
    }

    public Rezultat Modifica(string denumire, string? adresa, bool activ)
    {
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Denumirea șantierului este obligatorie (cel mult 200 de caractere)."));
        if (adresa?.Trim().Length > 500)
            return Rezultat.Esuat(Eroare.Validare("Adresa șantierului are cel mult 500 de caractere."));
        Denumire = denumire.Trim();
        Adresa = string.IsNullOrWhiteSpace(adresa) ? null : adresa.Trim();
        Activ = activ;
        return Rezultat.Ok();
    }
}

/// <summary>
/// Compartiment al firmei (D-38: aprovizionare, mecanizare, birou tehnic, atelier…), care poate cere materiale în afara
/// unui șantier. Ca șantierul, nu se șterge: se dezactivează.
/// </summary>
public sealed class Compartiment : EntitateFirma
{
    private Compartiment() { }

    public string Denumire { get; private set; } = string.Empty;
    public bool Activ { get; private set; } = true;

    public static Rezultat<Compartiment> Creeaza(string denumire)
    {
        var c = new Compartiment();
        var r = c.Modifica(denumire, true);
        return r.Succes ? Rezultat.Ok(c) : Rezultat.Esuat<Compartiment>(r.Eroare);
    }

    public Rezultat Modifica(string denumire, bool activ)
    {
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Denumirea compartimentului este obligatorie (cel mult 200 de caractere)."));
        Denumire = denumire.Trim();
        Activ = activ;
        return Rezultat.Ok();
    }
}
