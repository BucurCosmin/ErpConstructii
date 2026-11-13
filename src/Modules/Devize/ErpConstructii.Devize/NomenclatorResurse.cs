using ErpConstructii.Devize.Calcul;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Devize;

/// <summary>
/// O resursă din nomenclatorul firmei, pe sursă (docs/import-isdp.md §6.3, REQ-IMP-04): codul din programul de devize
/// (ISDP: materialele au 7–8 cifre, meseriile coduri scurte, transportul / utilajul poate fi chiar simbolul normei), cu
/// maparea spre ERP: materialul / echipamentul spre articolul de stoc, manopera spre meserie și categorie, utilajul și
/// transportul (până la modulul Utilaje) ca text. La primul import, codurile necunoscute se creează nemapate; utilizatorul
/// le leagă o singură dată, iar importurile următoare găsesc maparea gata (consumurile referă resursa, nu maparea).
/// </summary>
public sealed class ResursaCatalog : EntitateFirma
{
    private ResursaCatalog() { }

    /// <summary>Sursa codurilor (ex. „isdp”): același cod înseamnă altceva în alt program.</summary>
    public string Sursa { get; private set; } = string.Empty;
    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public string UnitateMasura { get; private set; } = string.Empty;
    public TipResursa Tip { get; private set; }

    /// <summary>Tipul nu reiese din prețuri (resursă fără preț) și a fost dedus din UM: de confirmat la mapare.</summary>
    public bool TipDedus { get; private set; }

    /// <summary>Material / echipament: articolul de stoc (modulul Stocuri), cu denumirea lui la momentul mapării.</summary>
    public Guid? ArticolStocId { get; private set; }
    public string? ArticolStocDenumire { get; private set; }

    /// <summary>UM-ul articolului de stoc la mapare și factorul „1 UM resursă = factor × UM stoc” (D-55; 1 = aceeași unitate).</summary>
    public string? ArticolStocUnitateMasura { get; private set; }
    public decimal? FactorConversie { get; private set; }

    /// <summary>Manoperă: meseria și categoria (ex. „Săpător”, „II”).</summary>
    public string? Meserie { get; private set; }
    public string? Categorie { get; private set; }

    /// <summary>Utilaj / transport: denumirea în ERP (text, până există modulul Utilaje).</summary>
    public string? Utilaj { get; private set; }

    /// <summary>Resursa nu se urmărește în ERP (ex. apa industrială): mapată explicit „fără corespondent”.</summary>
    public bool FaraMapare { get; private set; }

    public string? MapataDeNume { get; private set; }
    public DateTimeOffset? MapataLa { get; private set; }

    public bool EsteMapata =>
        FaraMapare || ArticolStocId is not null || !string.IsNullOrWhiteSpace(Meserie) || !string.IsNullOrWhiteSpace(Utilaj);

    /// <summary>Cheia de potrivire a codului: fără spații la capete, majuscule (codurile ISDP sunt cifre sau simboluri de normă).</summary>
    public static string CheieCod(string cod) => (cod ?? string.Empty).Trim().ToUpperInvariant();

    public static ResursaCatalog Noua(string sursa, string cod, string denumire, string unitateMasura, TipResursa tip, bool tipDedus) => new()
    {
        Sursa = sursa.Trim().ToLowerInvariant(),
        Cod = CheieCod(cod),
        Denumire = Taie(denumire.Trim(), 500),
        UnitateMasura = Taie(unitateMasura.Trim(), 20),
        Tip = tip,
        TipDedus = tipDedus,
    };

    /// <summary>
    /// Leagă resursa de ERP. Tipul se poate corecta (confirmă un tip dedus). Fiecare tip cere legătura lui: material /
    /// echipament → articol de stoc, manoperă → meserie, utilaj / transport → denumire; sau „fără corespondent”.
    /// </summary>
    public Rezultat Mapeaza(MapareResursa m, string utilizator, DateTimeOffset la)
    {
        if (string.IsNullOrWhiteSpace(utilizator))
            return Rezultat.Esuat(Eroare.Validare("Utilizatorul este obligatoriu."));
        var meserie = Gol(m.Meserie, 100);
        var categorie = Gol(m.Categorie, 50);
        var utilaj = Gol(m.Utilaj, 200);
        if (!m.FaraMapare)
        {
            var lipsa = m.Tip switch
            {
                TipResursa.Material or TipResursa.Echipament when m.ArticolStocId is null => "Alege articolul de stoc (sau marchează resursa „fără corespondent”).",
                TipResursa.Manopera when meserie is null => "Completează meseria (și categoria) pentru resursa de manoperă.",
                TipResursa.Utilaj or TipResursa.Transport when utilaj is null => "Completează utilajul / serviciul de transport din ERP.",
                _ => null,
            };
            if (lipsa is not null)
                return Rezultat.Esuat(Eroare.Validare(lipsa));
        }

        // Materialul din stoc poate avea altă UM: factorul dat, altfel cel propus (aceeași unitate / pereche fixă), altfel eroare.
        var esteStoc = !m.FaraMapare && m.Tip is TipResursa.Material or TipResursa.Echipament && m.ArticolStocId is not null;
        decimal? factor = null;
        if (esteStoc && !m.FaraMapare)
        {
            factor = m.FactorConversie ?? ConversieUm.Propune(UnitateMasura, m.ArticolStocUnitateMasura);
            if (factor is null)
                return Rezultat.Esuat(Eroare.Validare(
                    $"Resursa e în „{UnitateMasura}”, materialul din stoc în „{m.ArticolStocUnitateMasura}”: completează factorul de conversie (1 {UnitateMasura} = ? {m.ArticolStocUnitateMasura})."));
            if (factor <= 0)
                return Rezultat.Esuat(Eroare.Validare("Factorul de conversie trebuie să fie pozitiv."));
        }

        Tip = m.Tip;
        TipDedus = false;
        FaraMapare = m.FaraMapare;
        var stoc = !m.FaraMapare && m.Tip is TipResursa.Material or TipResursa.Echipament;
        ArticolStocId = stoc && !m.FaraMapare ? m.ArticolStocId : null;
        ArticolStocDenumire = ArticolStocId is null ? null : Gol(m.ArticolStocDenumire, 300);
        ArticolStocUnitateMasura = ArticolStocId is null ? null : Gol(m.ArticolStocUnitateMasura, 20);
        FactorConversie = ArticolStocId is null ? null : factor;
        Meserie = !m.FaraMapare && m.Tip == TipResursa.Manopera ? meserie : null;
        Categorie = Meserie is null ? null : categorie;
        Utilaj = !m.FaraMapare && m.Tip is TipResursa.Utilaj or TipResursa.Transport ? utilaj : null;
        MapataDeNume = utilizator.Trim();
        MapataLa = la;
        return Rezultat.Ok();
    }

    private static string? Gol(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : Taie(s.Trim(), max);

    private static string Taie(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>Legătura cerută pentru o resursă (vezi <see cref="ResursaCatalog.Mapeaza"/>).</summary>
/// <param name="FactorConversie">1 UM resursă = factor × UM stoc; null = propus din UM-uri (<see cref="ConversieUm.Propune"/>).</param>
public sealed record MapareResursa(
    TipResursa Tip, Guid? ArticolStocId, string? ArticolStocDenumire, string? Meserie, string? Categorie, string? Utilaj, bool FaraMapare,
    string? ArticolStocUnitateMasura = null, decimal? FactorConversie = null);

/// <summary>Rezultatul potrivirii consumurilor unei versiuni cu nomenclatorul: resursele noi (nemapate) și ce e de verificat.</summary>
public sealed record PotrivireCatalog(IReadOnlyList<ResursaCatalog> Noi, IReadOnlyList<ProblemaImport> Probleme);

/// <summary>Potrivirea consumurilor importate cu nomenclatorul firmei (logică pură; leagă consumurile de resurse).</summary>
public static class CatalogResurse
{
    /// <summary>
    /// Fiecare consum se leagă de resursa nomenclatorului cu același cod din aceeași sursă; codurile necunoscute devin
    /// resurse noi (nemapate), câte una pe cod chiar dacă apar în mai multe articole. Un cod cunoscut cu altă UM sau alt tip
    /// e semnalat (resursa din nomenclator rămâne cum e).
    /// </summary>
    public static PotrivireCatalog Potriveste(string sursa, IEnumerable<ConsumNormat> consumuri, IReadOnlyList<ResursaCatalog> existente)
    {
        var s = sursa.Trim().ToLowerInvariant();
        var dupaCod = existente.Where(r => r.Sursa == s).GroupBy(r => r.Cod).ToDictionary(g => g.Key, g => g.First());
        var noi = new List<ResursaCatalog>();
        var probleme = new List<ProblemaImport>();
        var semnalate = new HashSet<string>();
        foreach (var c in consumuri)
        {
            var cod = ResursaCatalog.CheieCod(c.CodResursa);
            if (cod.Length == 0)
                continue;
            if (!dupaCod.TryGetValue(cod, out var r))
            {
                r = ResursaCatalog.Noua(s, cod, c.Denumire, c.UnitateMasura, c.Tip, c.TipDedus);
                dupaCod[cod] = r;
                noi.Add(r);
            }
            else if (semnalate.Add(cod))
            {
                if (!string.Equals(r.UnitateMasura.Trim(), c.UnitateMasura.Trim(), StringComparison.OrdinalIgnoreCase))
                    probleme.Add(new ProblemaImport(Severitate.Avertisment, $"resursa {cod}",
                        $"Resursa {cod} ({c.Denumire}) are UM „{c.UnitateMasura}”, iar în nomenclator „{r.UnitateMasura}”."));
                if (!c.TipDedus && !r.TipDedus && r.Tip != c.Tip)
                    probleme.Add(new ProblemaImport(Severitate.Avertisment, $"resursa {cod}",
                        $"Resursa {cod} ({c.Denumire}) e de tip {c.Tip} în deviz, iar în nomenclator {r.Tip}."));
            }

            c.MapeazaLa(r.Id);
        }

        return new PotrivireCatalog(noi, probleme);
    }
}

/// <summary>Un articol de stoc propus / ales pentru maparea unei resurse de tip material.</summary>
public sealed record ArticolStocMapare(Guid Id, string Cod, string Denumire, string UnitateMasura);

/// <summary>
/// Articolele de stoc pentru maparea resurselor (implementat de modulul Stocuri; Devize nu-l cunoaște, regula 1).
/// Fără modulul Stocuri, materialele se pot marca doar „fără corespondent”.
/// </summary>
public interface IArticoleStocPentruMapare
{
    Task<ArticolStocMapare?> ArticolAsync(Guid id, CancellationToken ct = default);

    /// <summary>Articolele de stoc cu denumirea cea mai apropiată (întâi potrivirea exactă / asemănarea, apoi căutarea pe text).</summary>
    Task<IReadOnlyList<ArticolStocMapare>> PropuneAsync(string denumire, string? unitateMasura, int maxim, CancellationToken ct = default);
}
