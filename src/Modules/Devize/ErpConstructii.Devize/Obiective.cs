using ErpConstructii.SharedKernel;

namespace ErpConstructii.Devize;

/// <summary>
/// Obiectivul proiectului căruia îi aparține un deviz (D-59): un proiect poate avea mai multe devize, fiecare pentru alt
/// obiectiv (ex. „Package A”, „Hala C1”, „Drum acces”), fiecare cu istoria lui de versiuni (ofertă → contract → buget de
/// execuție, ciorne de lucru, înlocuiri). Bugetul se vede pe obiectiv sau pe tot proiectul.
/// </summary>
public sealed class ObiectivDeviz : EntitateFirma
{
    private ObiectivDeviz() { }

    public Guid ProiectId { get; private set; }
    public string Denumire { get; private set; } = string.Empty;
    public string? Descriere { get; private set; }
    public int Ordine { get; private set; }

    public static Rezultat<ObiectivDeviz> Creeaza(Guid proiectId, string denumire, string? descriere, int ordine)
    {
        var o = new ObiectivDeviz { ProiectId = proiectId, Ordine = ordine };
        var r = o.Modifica(denumire, descriere);
        return r.Esec ? Rezultat.Esuat<ObiectivDeviz>(r.Eroare) : Rezultat.Ok(o);
    }

    public Rezultat Modifica(string denumire, string? descriere)
    {
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Denumirea obiectivului e obligatorie (cel mult 200 de caractere)."));
        Denumire = denumire.Trim();
        Descriere = string.IsNullOrWhiteSpace(descriere) ? null : descriere.Trim() is var d && d.Length > 1000 ? d[..1000] : d;
        return Rezultat.Ok();
    }
}

/// <summary>
/// Ștergerea ultimei versiuni a unui deviz a readus în vigoare versiunea pe care o înlocuise (D-59). E corectura versiunii
/// readuse (regula 6: versiunea înlocuită e blocată), cu motivul și numărul versiunii șterse, ca istoric.
/// </summary>
public sealed class RevenireVersiuneDeviz : EntitateFirma, ICorectieDocument
{
    private RevenireVersiuneDeviz() { }

    public RevenireVersiuneDeviz(Guid versiuneReadusaId, Guid versiuneStearsaId, string versiuneStearsa, StareVersiuneDeviz stareReadusa, string motiv,
        string facutaDe, DateTimeOffset la)
    {
        VersiuneReadusaId = versiuneReadusaId;
        VersiuneStearsaId = versiuneStearsaId;
        VersiuneStearsa = versiuneStearsa.Length > 300 ? versiuneStearsa[..300] : versiuneStearsa;
        StareReadusa = stareReadusa;
        Motiv = motiv.Length > 1000 ? motiv[..1000] : motiv;
        FacutaDe = facutaDe.Length > 200 ? facutaDe[..200] : facutaDe;
        La = la;
    }

    public Guid VersiuneReadusaId { get; private set; }
    public Guid VersiuneStearsaId { get; private set; }

    /// <summary>„Ofertă nr. 3 — …”, ca istoric (versiunea ștearsă nu mai există).</summary>
    public string VersiuneStearsa { get; private set; } = string.Empty;

    public StareVersiuneDeviz StareReadusa { get; private set; }
    public string Motiv { get; private set; } = string.Empty;
    public string FacutaDe { get; private set; } = string.Empty;
    public DateTimeOffset La { get; private set; }

    public Guid DocumentCorectatId => VersiuneReadusaId;
}

/// <summary>
/// Implementat de modulele care leagă documente de articolele de deviz (Stocuri: bonurile de consum), apelat de Devize înainte
/// de ștergerea unei versiuni / a unui obiectiv (D-59): un articol folosit nu dispare din sistem. Fără modul: nicio utilizare.
/// </summary>
public interface IUtilizariArticoleDeviz
{
    /// <summary>Cheile stabile (dintre cele date) folosite de documente ale proiectului, cu un exemplu de document.</summary>
    Task<IReadOnlyDictionary<Guid, string>> FolositeAsync(Guid proiectId, IReadOnlyCollection<Guid> chei, CancellationToken ct = default);
}

/// <summary>
/// Ștergerea unei versiuni de deviz (D-59), ca istoric: ce versiune, din ce obiectiv, motivul. O versiune blocată care pornise
/// din cea ștearsă (sau o înlocuise) se leagă de versiunea dinaintea ei; schimbarea ei e corectura de față
/// (<see cref="VersiuneRelegataId"/>, regula 6). Fără versiune relegată, corectura e a versiunii șterse (rămâne în jurnal).
/// </summary>
public sealed class StergereVersiuneDeviz : EntitateFirma, ICorectieDocument
{
    private StergereVersiuneDeviz() { }

    public StergereVersiuneDeviz(Guid proiectId, Guid obiectivId, Guid versiuneStearsaId, string versiuneStearsa, StareVersiuneDeviz stareStearsa,
        Guid? versiuneRelegataId, string motiv, string facutaDe, DateTimeOffset la)
    {
        ProiectId = proiectId;
        ObiectivId = obiectivId;
        VersiuneStearsaId = versiuneStearsaId;
        VersiuneStearsa = versiuneStearsa.Length > 300 ? versiuneStearsa[..300] : versiuneStearsa;
        StareStearsa = stareStearsa;
        VersiuneRelegataId = versiuneRelegataId;
        Motiv = motiv.Length > 1000 ? motiv[..1000] : motiv;
        FacutaDe = facutaDe.Length > 200 ? facutaDe[..200] : facutaDe;
        La = la;
    }

    public Guid ProiectId { get; private set; }
    public Guid ObiectivId { get; private set; }
    public Guid VersiuneStearsaId { get; private set; }

    /// <summary>„Ofertă nr. 3 — …”, ca istoric (versiunea ștearsă nu mai există).</summary>
    public string VersiuneStearsa { get; private set; } = string.Empty;

    public StareVersiuneDeviz StareStearsa { get; private set; }

    /// <summary>Versiunea pornită din cea ștearsă, legată acum de versiunea dinaintea ei.</summary>
    public Guid? VersiuneRelegataId { get; private set; }

    public string Motiv { get; private set; } = string.Empty;
    public string FacutaDe { get; private set; } = string.Empty;
    public DateTimeOffset La { get; private set; }

    public Guid DocumentCorectatId => VersiuneRelegataId ?? VersiuneStearsaId;
}
