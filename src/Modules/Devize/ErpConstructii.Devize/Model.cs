using ErpConstructii.Devize.Calcul;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Devize;

public enum TipVersiuneDeviz
{
    Oferta,
    Contract,
    BugetExecutie,
    ActAditional,
}

public enum StareVersiuneDeviz
{
    Ciorna,
    Aprobata,
    Inlocuita,
}

/// <summary>
/// O versiune de deviz pentru un proiect (ofertă, contract, buget, act adițional).
/// După aprobare devine imuabilă (REQ-DEV-06); orice modificare creează o versiune nouă,
/// legată de cea anterioară prin <see cref="VersiuneAnterioaraId"/>.
///
/// Ierarhia: Versiune → Obiect → Categorie de lucrări → Articol → Consumuri normate.
/// TOATE nivelurile au firma_id (EntitateFirma), ca politica RLS să se aplice și când un tabel-copil
/// e interogat direct (ex. DbSet&lt;ArticolDeviz&gt; din alte rapoarte).
/// </summary>
public sealed class VersiuneDeviz : EntitateFirma, IDocumentCuStare
{
    private readonly List<ObiectDeviz> _obiecte = [];

    private VersiuneDeviz() { }

    public VersiuneDeviz(Guid proiectId, TipVersiuneDeviz tip, int numar, string denumire, Guid? versiuneAnterioaraId = null)
    {
        ProiectId = proiectId;
        Tip = tip;
        Numar = numar;
        Denumire = denumire;
        VersiuneAnterioaraId = versiuneAnterioaraId;
        Stare = StareVersiuneDeviz.Ciorna;
    }

    /// <summary>Referință către modulul Proiecte (doar Id, fără navigare între module).</summary>
    public Guid ProiectId { get; private set; }

    public TipVersiuneDeviz Tip { get; private set; }
    public int Numar { get; private set; }
    public string Denumire { get; private set; } = string.Empty;
    public StareVersiuneDeviz Stare { get; private set; }
    public Guid? VersiuneAnterioaraId { get; private set; }

    /// <summary>De unde vine: „import:windoc”, „manual”, „copie:{id}”.</summary>
    public string Sursa { get; private set; } = "manual";

    /// <summary>Id-ul fișierului original arhivat, pentru versiunile importate.</summary>
    public Guid? FisierSursaId { get; private set; }

    public decimal CotaContributiiManopera { get; private set; }
    public decimal CotaCheltuieliIndirecte { get; private set; }
    public decimal CotaProfit { get; private set; }
    public decimal CotaTva { get; private set; }

    public IReadOnlyList<ObiectDeviz> Obiecte => _obiecte;

    public bool EsteBlocat => Stare != StareVersiuneDeviz.Ciorna;

    public void SeteazaSursa(string sursa, Guid? fisierId)
    {
        AsiguraEditabil();
        Sursa = sursa;
        FisierSursaId = fisierId;
    }

    public void SeteazaParametri(ParametriRecapitulatie p)
    {
        AsiguraEditabil();
        p.Valideaza();
        CotaContributiiManopera = p.CotaContributiiManopera;
        CotaCheltuieliIndirecte = p.CotaCheltuieliIndirecte;
        CotaProfit = p.CotaProfit;
        CotaTva = p.CotaTva;
    }

    public ParametriRecapitulatie Parametri() =>
        new(CotaContributiiManopera, CotaCheltuieliIndirecte, CotaProfit, CotaTva);

    public ObiectDeviz AdaugaObiect(string cod, string denumire)
    {
        AsiguraEditabil();
        var obiect = new ObiectDeviz(Id, cod, denumire, _obiecte.Count);
        _obiecte.Add(obiect);
        return obiect;
    }

    public RecapitulatieDeviz Recapitulatie() =>
        CalculatorDeviz.Calculeaza(
            _obiecte.SelectMany(o => o.Categorii).SelectMany(c => c.Articole).Select(a => a.CaLinie()),
            Parametri());

    public Rezultat Aproba()
    {
        if (Stare != StareVersiuneDeviz.Ciorna)
        {
            return Rezultat.Esuat(Eroare.Validare("Doar o ciornă poate fi aprobată."));
        }

        if (_obiecte.Count == 0)
        {
            return Rezultat.Esuat(Eroare.Validare("Devizul nu are niciun obiect."));
        }

        Stare = StareVersiuneDeviz.Aprobata;
        return Rezultat.Ok();
    }

    private void AsiguraEditabil()
    {
        if (EsteBlocat)
        {
            throw new InvalidOperationException("Versiunea de deviz este aprobată; creează o versiune nouă.");
        }
    }
}

public sealed class ObiectDeviz : EntitateFirma
{
    private readonly List<CategorieLucrari> _categorii = [];

    private ObiectDeviz() { }

    internal ObiectDeviz(Guid versiuneId, string cod, string denumire, int ordine)
    {
        VersiuneId = versiuneId;
        Cod = cod;
        Denumire = denumire;
        Ordine = ordine;
    }

    public Guid VersiuneId { get; private set; }
    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public int Ordine { get; private set; }
    public IReadOnlyList<CategorieLucrari> Categorii => _categorii;

    public CategorieLucrari AdaugaCategorie(string cod, string denumire, bool subcontractata = false)
    {
        var categorie = new CategorieLucrari(Id, cod, denumire, subcontractata, _categorii.Count);
        _categorii.Add(categorie);
        return categorie;
    }
}

public sealed class CategorieLucrari : EntitateFirma
{
    private readonly List<ArticolDeviz> _articole = [];

    private CategorieLucrari() { }

    internal CategorieLucrari(Guid obiectId, string cod, string denumire, bool subcontractata, int ordine)
    {
        ObiectId = obiectId;
        Cod = cod;
        Denumire = denumire;
        Subcontractata = subcontractata;
        Ordine = ordine;
    }

    public Guid ObiectId { get; private set; }
    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public bool Subcontractata { get; private set; }
    public int Ordine { get; private set; }
    public IReadOnlyList<ArticolDeviz> Articole => _articole;

    public ArticolDeviz AdaugaArticol(string simbol, string denumire, string um, decimal cantitate, ValoriResurse pretUnitar)
    {
        var articol = new ArticolDeviz(Id, simbol, denumire, um, cantitate, pretUnitar, _articole.Count);
        _articole.Add(articol);
        return articol;
    }
}

/// <summary>
/// Articolul de deviz este ENTITATEA PIVOT a sistemului (REQ-GEN-02): bugetul, consumurile,
/// pontajul, cantitățile executate, situațiile și facturile se leagă de el.
/// Legăturile din alte module referă <see cref="Entitate.Id"/>; între versiuni, articolul se
/// urmărește prin <see cref="CheieStabila"/>.
/// </summary>
public sealed class ArticolDeviz : EntitateFirma
{
    private readonly List<ConsumNormat> _consumuri = [];

    private ArticolDeviz() { }

    internal ArticolDeviz(Guid categorieId, string simbol, string denumire, string um, decimal cantitate, ValoriResurse pretUnitar, int ordine)
    {
        CategorieId = categorieId;
        Simbol = simbol.Trim();
        Denumire = denumire.Trim();
        UnitateMasura = um.Trim();
        Cantitate = cantitate;
        PretMaterial = pretUnitar.Material;
        PretManopera = pretUnitar.Manopera;
        PretUtilaj = pretUnitar.Utilaj;
        PretTransport = pretUnitar.Transport;
        Ordine = ordine;
        CheieStabila = Guid.CreateVersion7();
    }

    public Guid CategorieId { get; private set; }

    /// <summary>
    /// Identifică „același articol” între versiunile de deviz (ofertă → contract → buget → act adițional),
    /// ca legăturile din grafic și istoricul cantităților să nu se rupă la reversionare.
    /// </summary>
    public Guid CheieStabila { get; private set; }

    /// <summary>Simbolul normei din indicator (ex. CA01A1).</summary>
    public string Simbol { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public string UnitateMasura { get; private set; } = string.Empty;
    public decimal Cantitate { get; private set; }
    public decimal PretMaterial { get; private set; }
    public decimal PretManopera { get; private set; }
    public decimal PretUtilaj { get; private set; }
    public decimal PretTransport { get; private set; }
    public int Ordine { get; private set; }
    public IReadOnlyList<ConsumNormat> Consumuri => _consumuri;

    public ValoriResurse PretUnitar => new(PretMaterial, PretManopera, PretUtilaj, PretTransport);

    public LinieDeviz CaLinie() => new(Cantitate, PretUnitar);

    public void AdaugaConsum(string codResursa, string denumire, TipResursa tip, string um, decimal consumUnitar, decimal pretUnitar) =>
        _consumuri.Add(new ConsumNormat(Id, codResursa, denumire, tip, um, consumUnitar, pretUnitar));

    internal void PastreazaCheia(Guid cheie) => CheieStabila = cheie;
}

/// <summary>
/// Consumul normat al unei resurse pe unitatea de articol (ex. 0,12 mc beton / mp).
/// Codul resursei se mapează la articolul de stoc / meseria / utilajul din ERP prin
/// tabelul de mapare per firmă și per sursă (REQ-IMP-04).
/// </summary>
public sealed class ConsumNormat : EntitateFirma
{
    private ConsumNormat() { }

    internal ConsumNormat(Guid articolId, string codResursa, string denumire, TipResursa tip, string um, decimal consumUnitar, decimal pretUnitar)
    {
        ArticolId = articolId;
        CodResursa = codResursa;
        Denumire = denumire;
        Tip = tip;
        UnitateMasura = um;
        ConsumUnitar = consumUnitar;
        PretUnitar = pretUnitar;
    }

    public Guid ArticolId { get; private set; }
    public string CodResursa { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public TipResursa Tip { get; private set; }
    public string UnitateMasura { get; private set; } = string.Empty;
    public decimal ConsumUnitar { get; private set; }
    public decimal PretUnitar { get; private set; }
}
