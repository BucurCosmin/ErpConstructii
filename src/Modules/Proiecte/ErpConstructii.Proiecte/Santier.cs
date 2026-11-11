using ErpConstructii.SharedKernel;

namespace ErpConstructii.Proiecte;

/// <summary>
/// Un punct de lucru al proiectului (D-38): un proiect poate avea mai multe șantiere (ex. tronsoane, obiecte în locuri diferite).
/// Necesarele de materiale se emit pe un șantier. Proiectul are mereu cel puțin un șantier activ („Șantier principal”, creat
/// cu proiectul, redenumibil). Șantierul pus din greșeală se șterge; documentele lui se mută întâi pe alt șantier al proiectului
/// (<see cref="StergereSantier"/>).
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
/// Regulile ștergerii și dezactivării unui șantier (logică pură): proiectul rămâne cu cel puțin un șantier activ, iar documentele
/// șantierului șters se mută pe alt șantier activ al aceluiași proiect.
/// </summary>
public static class StergereSantier
{
    /// <summary>Codul erorii când șantierul are documente și nu s-a ales unde se mută.</summary>
    public const string CodDocumenteDeMutat = "documente_de_mutat";

    /// <summary>
    /// Verifică ștergerea; întoarce șantierul pe care se mută documentele (null dacă nu are documente).
    /// </summary>
    /// <param name="santiereProiect">Toate șantierele proiectului, inclusiv cel de șters.</param>
    /// <param name="documente">Câte documente (necesare de materiale) sunt emise pe șantierul de șters.</param>
    /// <param name="mutaPe">Șantierul ales pentru documente (obligatoriu când există documente).</param>
    public static Rezultat<Santier?> Verifica(Santier deSters, IReadOnlyList<Santier> santiereProiect, int documente, Guid? mutaPe)
    {
        var ramase = santiereProiect.Where(s => s.Id != deSters.Id && s.ProiectId == deSters.ProiectId).ToList();
        if (!ramase.Any(s => s.Activ))
            return Rezultat.Esuat<Santier?>(Eroare.Validare(
                "Proiectul trebuie să rămână cu cel puțin un șantier activ: adaugă sau activează altul înainte de a-l șterge pe acesta."));
        if (documente == 0)
            return Rezultat.Ok<Santier?>(null);
        if (mutaPe is null)
            return Rezultat.Esuat<Santier?>(new Eroare(StergereSantier.CodDocumenteDeMutat, 
                $"Pe șantierul „{deSters.Denumire}” {(documente == 1 ? "este un document" : $"sunt {documente} documente")} (necesare de materiale). " +
                "Alege șantierul proiectului pe care se mută."));
        var tinta = ramase.FirstOrDefault(s => s.Id == mutaPe);
        if (tinta is null || !tinta.Activ)
            return Rezultat.Esuat<Santier?>(Eroare.Validare("Documentele se mută pe un alt șantier activ al aceluiași proiect."));
        return Rezultat.Ok<Santier?>(tinta);
    }

    /// <summary>Ultimul șantier activ al proiectului nu se dezactivează.</summary>
    public static Rezultat VerificaDezactivarea(Santier santier, IReadOnlyList<Santier> santiereProiect) =>
        santiereProiect.Any(s => s.Id != santier.Id && s.ProiectId == santier.ProiectId && s.Activ)
            ? Rezultat.Ok()
            : Rezultat.Esuat(Eroare.Validare("Proiectul trebuie să rămână cu cel puțin un șantier activ."));
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
