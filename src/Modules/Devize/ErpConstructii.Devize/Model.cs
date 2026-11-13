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

/// <summary>Nivelul unui capitol din arborele devizului (ISDP: obiect → stadiu fizic → capitole).</summary>
public enum TipCapitol
{
    Obiect,
    StadiuFizic,
    Capitol,
}

/// <summary>Cum a ajuns articolul în deviz: normă din indicator, normă asimilată („asim”) sau resursă pusă direct ca articol.</summary>
public enum TipArticol
{
    Normat,
    Asimilat,
    ResursaDirecta,
}

/// <summary>
/// O versiune de deviz pentru un proiect (ofertă, contract, buget, act adițional).
/// După aprobare devine imuabilă (REQ-DEV-06); orice modificare creează o versiune nouă,
/// legată de cea anterioară prin <see cref="VersiuneAnterioaraId"/>.
///
/// Ierarhia (D-50): Versiune → capitole arborescente (obiect, stadiu fizic, capitole, pe oricâte niveluri, cu cod WBS)
/// → Articol → Consumuri normate. Capitolele stau într-o listă plată a versiunii, legate prin <see cref="CapitolDeviz.ParinteId"/>.
/// TOATE nivelurile au firma_id (EntitateFirma), ca politica RLS să se aplice și când un tabel-copil
/// e interogat direct (ex. DbSet&lt;ArticolDeviz&gt; din alte rapoarte).
/// </summary>
public sealed class VersiuneDeviz : EntitateFirma, IDocumentCuStare
{
    private readonly List<CapitolDeviz> _capitole = [];

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

    /// <summary>De unde vine: „import:isdp”, „manual”, „copie:{id}”.</summary>
    public string Sursa { get; private set; } = "manual";

    /// <summary>Id-ul fișierului original arhivat, pentru versiunile importate.</summary>
    public Guid? FisierSursaId { get; private set; }

    /// <summary>Cotele recapitulației, ca fracții (0,0225 = 2,25%).</summary>
    public decimal CotaContributiiManopera { get; private set; }
    public decimal CotaCheltuieliIndirecte { get; private set; }
    public decimal CotaProfit { get; private set; }
    public decimal CotaTva { get; private set; }

    /// <summary>Moneda ofertei (prețurile de ofertă ale articolelor); costurile directe sunt în lei.</summary>
    public string Moneda { get; private set; } = "RON";

    /// <summary>Lei pentru o unitate din <see cref="Moneda"/> (ex. 5,23 lei / EUR); null pentru RON sau necunoscut.</summary>
    public decimal? Curs { get; private set; }

    /// <summary><see cref="PoliticaRotunjire.Fara"/> pentru versiunile din ISDP: totalurile ies identic cu sursa.</summary>
    public PoliticaRotunjire PoliticaRotunjire { get; private set; } = PoliticaRotunjire.Standard;

    /// <summary>Antetul devizului sursă (afișare, potrivire la reimport).</summary>
    public string? Obiectiv { get; private set; }
    public string? Obiect { get; private set; }
    public string? StadiuFizic { get; private set; }

    /// <summary>Ce a declarat fișierul importat (T0–T3, TVA), ca JSON: auditul „ce a spus sursa”.</summary>
    public string? TotaluriDeclarateJson { get; private set; }

    /// <summary>Toate capitolele versiunii (lista plată; arborele prin <see cref="CapitolDeviz.ParinteId"/>).</summary>
    public IReadOnlyList<CapitolDeviz> Capitole => _capitole;

    /// <summary>Capitolele de pe primul nivel, în ordine.</summary>
    public IEnumerable<CapitolDeviz> Radacini => Copii(null);

    /// <summary>Capitolele direct sub <paramref name="parinteId"/> (null = rădăcini), în ordine.</summary>
    public IEnumerable<CapitolDeviz> Copii(Guid? parinteId) => _capitole.Where(c => c.ParinteId == parinteId).OrderBy(c => c.Ordine);

    /// <summary>Toate articolele versiunii, în ordinea arborelui (capitol cu capitol, în adâncime).</summary>
    public IEnumerable<ArticolDeviz> Articole() => Radacini.SelectMany(ArticoleSub);

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

    public Rezultat SeteazaMoneda(string moneda, decimal? curs)
    {
        AsiguraEditabil();
        var m = (moneda ?? string.Empty).Trim().ToUpperInvariant();
        if (m.Length != 3 || !m.All(char.IsAsciiLetterUpper))
            return Rezultat.Esuat(Eroare.Validare("Moneda are 3 litere (RON, EUR)."));
        if (curs is <= 0)
            return Rezultat.Esuat(Eroare.Validare("Cursul trebuie să fie pozitiv."));
        Moneda = m;
        Curs = m == "RON" ? null : curs;
        return Rezultat.Ok();
    }

    public void SeteazaPoliticaRotunjire(PoliticaRotunjire politica)
    {
        AsiguraEditabil();
        PoliticaRotunjire = politica;
    }

    public void SeteazaAntet(string? obiectiv, string? obiect, string? stadiuFizic, string? totaluriDeclarateJson)
    {
        AsiguraEditabil();
        Obiectiv = Gol(obiectiv);
        Obiect = Gol(obiect);
        StadiuFizic = Gol(stadiuFizic);
        TotaluriDeclarateJson = Gol(totaluriDeclarateJson);
    }

    /// <summary>Cotele versiunii, cu rotunjirile politicii ei.</summary>
    public ParametriRecapitulatie Parametri() =>
        new ParametriRecapitulatie(CotaContributiiManopera, CotaCheltuieliIndirecte, CotaProfit, CotaTva).Cu(PoliticaRotunjire);

    /// <summary>Adaugă un capitol sub <paramref name="parinte"/> (null = pe primul nivel), la sfârșitul fraților lui.</summary>
    public CapitolDeviz AdaugaCapitol(string cod, string denumire, TipCapitol tip, CapitolDeviz? parinte = null, string? codWbs = null, bool subcontractat = false)
    {
        AsiguraEditabil();
        if (parinte is not null && !_capitole.Contains(parinte))
            throw new InvalidOperationException("Capitolul părinte nu e în această versiune.");
        var ordine = _capitole.Count(c => c.ParinteId == parinte?.Id);
        var capitol = new CapitolDeviz(Id, parinte?.Id, cod, denumire, tip, codWbs, subcontractat, ordine);
        _capitole.Add(capitol);
        return capitol;
    }

    /// <summary>Obiectul (capitol de primul nivel), ca în devizele fără WBS.</summary>
    public CapitolDeviz AdaugaObiect(string cod, string denumire) => AdaugaCapitol(cod, denumire, TipCapitol.Obiect);

    public RecapitulatieDeviz Recapitulatie() => CalculatorDeviz.Calculeaza(Articole().Select(a => a.CaLinie()), Parametri());

    /// <summary>
    /// Ciorna de lucru: pornită dintr-o altă versiune („Modifică”). Doar ea se editează pe loc; versiunea originală (importată
    /// sau aprobată) rămâne neschimbată, ca modificările să se vadă față de ea și să se aplice abia la aprobare.
    /// </summary>
    public bool EsteCiornaDeLucru => Stare == StareVersiuneDeviz.Ciorna && VersiuneAnterioaraId is not null;

    /// <summary>Cheia capitolului după calea lui (cod, WBS, denumire de la rădăcină): găsește capitolul corespunzător într-o copie.</summary>
    public string CheieCapitol(CapitolDeviz capitol) =>
        string.Join(" / ", Cale(capitol).Select(c => $"{c.Cod}|{c.CodWbs}|{c.Denumire}"));

    /// <summary>Calea capitolului de la rădăcină (inclusiv el), ex. [Obiect, Stadiu, Capitol].</summary>
    public IReadOnlyList<CapitolDeviz> Cale(CapitolDeviz capitol)
    {
        var cale = new List<CapitolDeviz>();
        for (var c = capitol; c is not null; c = c.ParinteId is { } p ? _capitole.FirstOrDefault(x => x.Id == p) : null)
            cale.Insert(0, c);
        return cale;
    }

    public Rezultat Aproba()
    {
        if (Stare != StareVersiuneDeviz.Ciorna)
        {
            return Rezultat.Esuat(Eroare.Validare("Doar o ciornă poate fi aprobată."));
        }

        if (!Articole().Any())
        {
            return Rezultat.Esuat(Eroare.Validare("Devizul nu are niciun articol."));
        }

        Stare = StareVersiuneDeviz.Aprobata;
        return Rezultat.Ok();
    }

    /// <summary>Denumirea versiunii (ex. dată la import în locul titlului din fișier); doar pe ciornă.</summary>
    public Rezultat Redenumeste(string denumire)
    {
        if (EsteBlocat)
            return Rezultat.Esuat(Eroare.Validare("Versiunea aprobată nu se mai modifică; creează o versiune nouă."));
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 300)
            return Rezultat.Esuat(Eroare.Validare("Denumirea versiunii e obligatorie (cel mult 300 de caractere)."));
        Denumire = denumire.Trim();
        return Rezultat.Ok();
    }

    /// <summary>
    /// Modifică un articol al ciornei. La o cantitate nouă, resursele își păstrează consumul unitar (cantitatea lor totală se
    /// recalculează), iar prețul de ofertă (dacă articolul îl are) se recalculează după regula F3, la cotele și cursul versiunii.
    /// </summary>
    public Rezultat ModificaArticol(Guid articolId, DateArticol date)
    {
        if (EsteBlocat)
            return Rezultat.Esuat(Eroare.Validare("Versiunea aprobată nu se mai modifică; creează o versiune nouă."));
        var articol = Articole().FirstOrDefault(a => a.Id == articolId);
        if (articol is null)
            return Rezultat.Esuat(Eroare.NuExista("Articolul"));
        var r = articol.Modifica(date);
        if (r.Succes && articol.ArePretOferta)
            articol.RecalculeazaPretOferta(Parametri(), Curs ?? 1);
        return r;
    }

    /// <summary>Adaugă un articol introdus manual, la sfârșitul capitolului (fără resurse; prețul pe componente, în lei).</summary>
    public Rezultat<ArticolDeviz> AdaugaArticolManual(Guid capitolId, DateArticol date)
    {
        if (EsteBlocat)
            return Rezultat.Esuat<ArticolDeviz>(Eroare.Validare("Versiunea aprobată nu se mai modifică; creează o versiune nouă."));
        var capitol = _capitole.FirstOrDefault(c => c.Id == capitolId);
        if (capitol is null)
            return Rezultat.Esuat<ArticolDeviz>(Eroare.NuExista("Capitolul"));
        if (ArticolDeviz.Valideaza(date) is { } eroare)
            return Rezultat.Esuat<ArticolDeviz>(Eroare.Validare(eroare));
        var articol = capitol.AdaugaArticol(date.Simbol, date.Denumire, date.UnitateMasura, date.Cantitate, date.PretUnitar);
        if (Articole().Any(a => a.Id != articol.Id && a.ArePretOferta))
            articol.RecalculeazaPretOferta(Parametri(), Curs ?? 1);
        return Rezultat.Ok(articol);
    }

    /// <summary>Scoate articolul din ciornă (cu resursele lui).</summary>
    public Rezultat StergeArticol(Guid articolId)
    {
        if (EsteBlocat)
            return Rezultat.Esuat(Eroare.Validare("Versiunea aprobată nu se mai modifică; creează o versiune nouă."));
        var capitol = _capitole.FirstOrDefault(c => c.Articole.Any(a => a.Id == articolId));
        if (capitol is null)
            return Rezultat.Esuat(Eroare.NuExista("Articolul"));
        capitol.ScoateArticol(articolId);
        return Rezultat.Ok();
    }

    /// <summary>
    /// Ciorna nouă pornită din această versiune (de obicei una aprobată): aceleași capitole, articole (cu aceleași chei
    /// stabile, ca legăturile din alte module să rămână), resurse și prețuri. La aprobarea ei, aceasta devine „înlocuită”.
    /// </summary>
    public VersiuneDeviz CopieCiorna(int numar, string? denumire = null)
    {
        var copie = new VersiuneDeviz(ProiectId, Tip, numar, string.IsNullOrWhiteSpace(denumire) ? Denumire : denumire.Trim(), Id)
        {
            Sursa = $"copie:{Id}",
            CotaContributiiManopera = CotaContributiiManopera,
            CotaCheltuieliIndirecte = CotaCheltuieliIndirecte,
            CotaProfit = CotaProfit,
            CotaTva = CotaTva,
            Moneda = Moneda,
            Curs = Curs,
            PoliticaRotunjire = PoliticaRotunjire,
            Obiectiv = Obiectiv,
            Obiect = Obiect,
            StadiuFizic = StadiuFizic,
        };

        void CopiazaSub(Guid? parinteVechi, CapitolDeviz? parinteNou)
        {
            foreach (var c in Copii(parinteVechi))
            {
                var nou = copie.AdaugaCapitol(c.Cod, c.Denumire, c.Tip, parinteNou, c.CodWbs, c.Subcontractat);
                foreach (var a in c.Articole.OrderBy(x => x.Ordine))
                    a.CopiazaIn(nou);
                CopiazaSub(c.Id, nou);
            }
        }

        CopiazaSub(null, null);
        return copie;
    }

    /// <summary>
    /// Schimbă prețurile resurselor (cod din nomenclator → preț nou, în UM-ul resursei) în toate articolele ciornei (D-55,
    /// „actualizează prețurile”). Fiecare articol își ajustează prețul unitar doar cu diferența adusă de resursele schimbate
    /// (consum unitar × (preț nou − preț vechi)), pe componenta lor; restul prețului rămâne cum era (și modificările manuale).
    /// Prețul de ofertă se recalculează după regula F3. Întoarce câte articole s-au schimbat.
    /// </summary>
    public Rezultat<int> ActualizeazaPreturiResurse(IReadOnlyDictionary<string, decimal> preturiNoi)
    {
        if (EsteBlocat)
            return Rezultat.Esuat<int>(Eroare.Validare("Versiunea aprobată nu se mai modifică; creează o versiune nouă."));
        if (preturiNoi.Values.Any(p => p < 0))
            return Rezultat.Esuat<int>(Eroare.Validare("Prețurile nu pot fi negative."));
        var schimbate = 0;
        foreach (var a in Articole())
        {
            if (a.AplicaPreturiResurse(preturiNoi))
            {
                schimbate++;
                if (a.ArePretOferta)
                    a.RecalculeazaPretOferta(Parametri(), Curs ?? 1);
            }
        }

        return Rezultat.Ok(schimbate);
    }

    /// <summary>
    /// Versiunea e înlocuită de ciorna pornită din ea, la aprobarea acesteia (rămâne în istoric, neschimbată). Și ciorna
    /// originală (ex. devizul importat, neaprobat) se înlocuiește astfel: modificările ei s-au făcut în versiunea nouă.
    /// </summary>
    public Rezultat Inlocuieste()
    {
        if (Stare == StareVersiuneDeviz.Inlocuita)
            return Rezultat.Esuat(Eroare.Validare("Versiunea a fost deja înlocuită."));
        Stare = StareVersiuneDeviz.Inlocuita;
        return Rezultat.Ok();
    }

    private IEnumerable<ArticolDeviz> ArticoleSub(CapitolDeviz capitol) =>
        capitol.Articole.OrderBy(a => a.Ordine).Concat(Copii(capitol.Id).SelectMany(ArticoleSub));

    private void AsiguraEditabil()
    {
        if (EsteBlocat)
        {
            throw new InvalidOperationException("Versiunea de deviz este aprobată; creează o versiune nouă.");
        }
    }

    private static string? Gol(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>
/// Un nivel de grupare al devizului (obiect, stadiu fizic, capitol), pe oricâte niveluri (D-50). Părintele e alt capitol
/// al aceleiași versiuni (<see cref="ParinteId"/>; null = primul nivel). Articolele stau de obicei pe capitolele-frunză.
/// </summary>
public sealed class CapitolDeviz : EntitateFirma
{
    private readonly List<ArticolDeviz> _articole = [];

    private CapitolDeviz() { }

    internal CapitolDeviz(Guid versiuneId, Guid? parinteId, string cod, string denumire, TipCapitol tip, string? codWbs, bool subcontractat, int ordine)
    {
        VersiuneId = versiuneId;
        ParinteId = parinteId;
        Cod = cod.Trim();
        Denumire = denumire.Trim();
        Tip = tip;
        CodWbs = string.IsNullOrWhiteSpace(codWbs) ? null : codWbs.Trim();
        Subcontractat = subcontractat;
        Ordine = ordine;
    }

    public Guid VersiuneId { get; private set; }
    public Guid? ParinteId { get; private set; }

    /// <summary>Codul afișat (ex. „01.2” sau numărul capitolului).</summary>
    public string Cod { get; private set; } = string.Empty;

    /// <summary>Codul ierarhic din sursă (ISDP: „0002.1.1.1”), pentru potrivirea la reimport.</summary>
    public string? CodWbs { get; private set; }
    public string Denumire { get; private set; } = string.Empty;
    public TipCapitol Tip { get; private set; }
    public bool Subcontractat { get; private set; }
    public int Ordine { get; private set; }
    public IReadOnlyList<ArticolDeviz> Articole => _articole;

    internal void ScoateArticol(Guid articolId) => _articole.RemoveAll(a => a.Id == articolId);

    /// <summary>Adaugă un articol (costul direct în lei, pe resurse), la sfârșitul capitolului.</summary>
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
///
/// Are două prețuri (D-50): <see cref="PretUnitar"/> = costul direct în lei pe resurse (buget, control al costurilor) și
/// <see cref="PretOfertaUnitar"/> / <see cref="ValoareOferta"/> = prețul de ofertă / contract în moneda versiunii, cu
/// indirecte și profit incluse (situații de lucrări, facturare).
/// </summary>
public sealed class ArticolDeviz : EntitateFirma
{
    private readonly List<ConsumNormat> _consumuri = [];

    private ArticolDeviz() { }

    internal ArticolDeviz(Guid capitolId, string simbol, string denumire, string um, decimal cantitate, ValoriResurse pretUnitar, int ordine)
    {
        CapitolId = capitolId;
        Simbol = simbol.Trim();
        SimbolBaza = Simbol;
        Denumire = denumire.Trim();
        UnitateMasura = um.Trim();
        Cantitate = cantitate;
        PretMaterial = pretUnitar.Material;
        PretManopera = pretUnitar.Manopera;
        PretUtilaj = pretUnitar.Utilaj;
        PretTransport = pretUnitar.Transport;
        PretEchipament = pretUnitar.Echipament;
        Ordine = ordine;
        CheieStabila = Guid.CreateVersion7();
    }

    public Guid CapitolId { get; private set; }

    /// <summary>
    /// Identifică „același articol” între versiunile de deviz (ofertă → contract → buget → act adițional),
    /// ca legăturile din grafic și istoricul cantităților să nu se rupă la reversionare.
    /// </summary>
    public Guid CheieStabila { get; private set; }

    /// <summary>Simbolul normei din indicator (ex. CA01A1, „TSF01A1 asim”).</summary>
    public string Simbol { get; private set; } = string.Empty;

    /// <summary>Simbolul fără sufixul „asim” (la articolele asimilate).</summary>
    public string SimbolBaza { get; private set; } = string.Empty;
    public TipArticol TipArticol { get; private set; } = TipArticol.Normat;

    /// <summary>Codul WBS din sursă (ISDP: „0002.1.1.1.1”), pentru potrivirea la reimport și cu lista F3.</summary>
    public string? CodWbsSursa { get; private set; }
    public string Denumire { get; private set; } = string.Empty;
    public string UnitateMasura { get; private set; } = string.Empty;
    public decimal Cantitate { get; private set; }

    // Costul direct unitar, în lei, pe resurse.
    public decimal PretMaterial { get; private set; }
    public decimal PretManopera { get; private set; }
    public decimal PretUtilaj { get; private set; }
    public decimal PretTransport { get; private set; }
    public decimal PretEchipament { get; private set; }

    // Prețul de ofertă, în moneda versiunii: unitar și total pe componente (rotunjite separat, ca în F3).
    public decimal PretOfertaMaterial { get; private set; }
    public decimal PretOfertaManopera { get; private set; }
    public decimal PretOfertaUtilaj { get; private set; }
    public decimal PretOfertaTransport { get; private set; }
    public decimal PretOfertaEchipament { get; private set; }
    public decimal ValoareOfertaMaterial { get; private set; }
    public decimal ValoareOfertaManopera { get; private set; }
    public decimal ValoareOfertaUtilaj { get; private set; }
    public decimal ValoareOfertaTransport { get; private set; }
    public decimal ValoareOfertaEchipament { get; private set; }

    public int Ordine { get; private set; }
    public IReadOnlyList<ConsumNormat> Consumuri => _consumuri;

    public ValoriResurse PretUnitar => new(PretMaterial, PretManopera, PretUtilaj, PretTransport, PretEchipament);

    public ValoriResurse PretOfertaUnitar =>
        new(PretOfertaMaterial, PretOfertaManopera, PretOfertaUtilaj, PretOfertaTransport, PretOfertaEchipament);

    public ValoriResurse ValoareOferta =>
        new(ValoareOfertaMaterial, ValoareOfertaManopera, ValoareOfertaUtilaj, ValoareOfertaTransport, ValoareOfertaEchipament);

    /// <summary>Articolul are preț de ofertă (din F3 sau calculat la curs).</summary>
    public bool ArePretOferta => PretOfertaUnitar.Total != 0 || ValoareOferta.Total != 0;

    public LinieDeviz CaLinie() => new(Cantitate, PretUnitar);

    /// <summary>Proveniența din sursă: tipul articolului, simbolul de bază, codul WBS.</summary>
    public void SeteazaSursa(TipArticol tip, string? simbolBaza, string? codWbs)
    {
        TipArticol = tip;
        SimbolBaza = string.IsNullOrWhiteSpace(simbolBaza) ? Simbol : simbolBaza.Trim();
        CodWbsSursa = string.IsNullOrWhiteSpace(codWbs) ? null : codWbs.Trim();
    }

    /// <summary>Prețul de ofertă, în moneda versiunii: unitar și total pe componente.</summary>
    public void SeteazaPretOferta(ValoriResurse unitar, ValoriResurse total)
    {
        (PretOfertaMaterial, PretOfertaManopera, PretOfertaUtilaj, PretOfertaTransport, PretOfertaEchipament) =
            (unitar.Material, unitar.Manopera, unitar.Utilaj, unitar.Transport, unitar.Echipament);
        (ValoareOfertaMaterial, ValoareOfertaManopera, ValoareOfertaUtilaj, ValoareOfertaTransport, ValoareOfertaEchipament) =
            (total.Material, total.Manopera, total.Utilaj, total.Transport, total.Echipament);
    }

    /// <summary>
    /// Adaugă o resursă a articolului. <paramref name="cantitateTotala"/> = cantitatea din sursă, pe tot articolul; fără ea,
    /// se calculează din consumul unitar × cantitatea articolului.
    /// </summary>
    public ConsumNormat AdaugaConsum(
        string codResursa, string denumire, TipResursa tip, string um, decimal consumUnitar, decimal pretUnitar,
        decimal? cantitateTotala = null, bool tipDedus = false)
    {
        var consum = new ConsumNormat(Id, codResursa, denumire, tip, um, consumUnitar, pretUnitar,
            cantitateTotala ?? consumUnitar * Cantitate, tipDedus, _consumuri.Count);
        _consumuri.Add(consum);
        return consum;
    }

    internal void PastreazaCheia(Guid cheie) => CheieStabila = cheie;

    /// <summary>Ce e greșit în datele unui articol (null = bune).</summary>
    public static string? Valideaza(DateArticol d)
    {
        if (string.IsNullOrWhiteSpace(d.Simbol) || d.Simbol.Trim().Length > 40)
            return "Simbolul e obligatoriu (cel mult 40 de caractere).";
        if (string.IsNullOrWhiteSpace(d.Denumire) || d.Denumire.Trim().Length > 1000)
            return "Denumirea e obligatorie (cel mult 1000 de caractere).";
        if (string.IsNullOrWhiteSpace(d.UnitateMasura) || d.UnitateMasura.Trim().Length > 20)
            return "Unitatea de măsură e obligatorie (cel mult 20 de caractere).";
        if (d.Cantitate <= 0)
            return "Cantitatea trebuie să fie pozitivă.";
        var p = d.PretUnitar;
        if (p.Material < 0 || p.Manopera < 0 || p.Utilaj < 0 || p.Transport < 0 || p.Echipament < 0)
            return "Prețurile nu pot fi negative.";
        return null;
    }

    internal Rezultat Modifica(DateArticol d)
    {
        if (Valideaza(d) is { } eroare)
            return Rezultat.Esuat(Eroare.Validare(eroare));
        var simbol = d.Simbol.Trim();
        if (simbol != Simbol)
        {
            Simbol = simbol;
            SimbolBaza = simbol.EndsWith(" asim", StringComparison.OrdinalIgnoreCase) ? simbol[..^5].Trim() : simbol;
        }

        Denumire = d.Denumire.Trim();
        UnitateMasura = d.UnitateMasura.Trim();
        if (d.Cantitate != Cantitate)
        {
            Cantitate = d.Cantitate;
            foreach (var c in _consumuri)
                c.Rescaleaza(Cantitate);
        }

        (PretMaterial, PretManopera, PretUtilaj, PretTransport, PretEchipament) =
            (d.PretUnitar.Material, d.PretUnitar.Manopera, d.PretUnitar.Utilaj, d.PretUnitar.Transport, d.PretUnitar.Echipament);
        return Rezultat.Ok();
    }

    /// <summary>
    /// Prețul de ofertă după regula F3 (docs/import-isdp.md §3.1): factor manoperă = (1 + CAM)(1 + indirecte)(1 + profit),
    /// celelalte (1 + indirecte)(1 + profit); unitar = rotunjit₂(preț lei × factor / curs), total = rotunjit₂(valoare lei × factor / curs).
    /// </summary>
    /// <summary>
    /// Suma resurselor pe componente (consum unitar × preț), când nu se potrivește la 1 ban cu prețul unitar al articolului
    /// (prețul a fost modificat manual după import); null pentru articolele fără resurse sau care se potrivesc.
    /// </summary>
    public ValoriResurse? DiferaDeResurse()
    {
        if (_consumuri.Count == 0)
            return null;
        decimal Pe(TipResursa t) => _consumuri.Where(c => c.Tip == t).Sum(c => c.ConsumUnitar * c.PretUnitar);
        var suma = new ValoriResurse(Pe(TipResursa.Material), Pe(TipResursa.Manopera), Pe(TipResursa.Utilaj), Pe(TipResursa.Transport), Pe(TipResursa.Echipament));
        var pu = PretUnitar;
        var diferite = Math.Abs(suma.Material - pu.Material) >= 0.01m || Math.Abs(suma.Manopera - pu.Manopera) >= 0.01m
                       || Math.Abs(suma.Utilaj - pu.Utilaj) >= 0.01m || Math.Abs(suma.Transport - pu.Transport) >= 0.01m
                       || Math.Abs(suma.Echipament - pu.Echipament) >= 0.01m;
        return diferite ? suma : null;
    }

    /// <summary>Prețurile noi ale resurselor (cod → preț): prețul unitar primește diferența, pe componenta resursei.</summary>
    internal bool AplicaPreturiResurse(IReadOnlyDictionary<string, decimal> preturiNoi)
    {
        decimal m = 0, mo = 0, u = 0, t = 0, e = 0;
        var schimbat = false;
        foreach (var c in _consumuri)
        {
            if (!preturiNoi.TryGetValue(ResursaCatalog.CheieCod(c.CodResursa), out var pret) || pret == c.PretUnitar)
                continue;
            var delta = c.ConsumUnitar * (pret - c.PretUnitar);
            switch (c.Tip)
            {
                case TipResursa.Material: m += delta; break;
                case TipResursa.Manopera: mo += delta; break;
                case TipResursa.Utilaj: u += delta; break;
                case TipResursa.Transport: t += delta; break;
                default: e += delta; break;
            }

            c.SchimbaPret(pret);
            schimbat = true;
        }

        if (!schimbat)
            return false;
        PretMaterial += m;
        PretManopera += mo;
        PretUtilaj += u;
        PretTransport += t;
        PretEchipament += e;
        return true;
    }

    internal void RecalculeazaPretOferta(ParametriRecapitulatie p, decimal curs)
    {
        var f = (1 + p.CotaCheltuieliIndirecte) * (1 + p.CotaProfit);
        var fm = f * (1 + p.CotaContributiiManopera);
        decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        var pu = PretUnitar;
        SeteazaPretOferta(
            new ValoriResurse(R(pu.Material * f / curs), R(pu.Manopera * fm / curs), R(pu.Utilaj * f / curs), R(pu.Transport * f / curs), R(pu.Echipament * f / curs)),
            new ValoriResurse(R(pu.Material * Cantitate * f / curs), R(pu.Manopera * Cantitate * fm / curs), R(pu.Utilaj * Cantitate * f / curs),
                R(pu.Transport * Cantitate * f / curs), R(pu.Echipament * Cantitate * f / curs)));
    }

    /// <summary>Copia articolului în capitolul altei versiuni: aceeași cheie stabilă, aceleași prețuri și resurse.</summary>
    internal void CopiazaIn(CapitolDeviz capitol)
    {
        var a = capitol.AdaugaArticol(Simbol, Denumire, UnitateMasura, Cantitate, PretUnitar);
        a.PastreazaCheia(CheieStabila);
        a.SeteazaSursa(TipArticol, SimbolBaza, CodWbsSursa);
        a.SeteazaPretOferta(PretOfertaUnitar, ValoareOferta);
        foreach (var c in _consumuri.OrderBy(x => x.Ordine))
            a.AdaugaConsum(c.CodResursa, c.Denumire, c.Tip, c.UnitateMasura, c.ConsumUnitar, c.PretUnitar, c.CantitateTotala, c.TipDedus)
                .MapeazaLa(c.ResursaCatalogId);
    }
}

/// <summary>Datele unui articol editat sau adăugat manual: costul direct unitar în lei, pe resurse.</summary>
public sealed record DateArticol(string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, ValoriResurse PretUnitar);

/// <summary>
/// O resursă a articolului: cantitatea totală pe articol (din sursă; ISDP o exportă așa) și consumul pe unitatea de
/// articol (= cantitate totală / cantitatea articolului). Aprovizionarea lucrează cu necesarul total.
/// Codul resursei se mapează la nomenclatorul firmei (<see cref="ResursaCatalogId"/>, REQ-IMP-04).
/// </summary>
public sealed class ConsumNormat : EntitateFirma
{
    private ConsumNormat() { }

    internal ConsumNormat(
        Guid articolId, string codResursa, string denumire, TipResursa tip, string um, decimal consumUnitar, decimal pretUnitar,
        decimal cantitateTotala, bool tipDedus, int ordine)
    {
        ArticolId = articolId;
        CodResursa = codResursa.Trim();
        Denumire = denumire.Trim();
        Tip = tip;
        UnitateMasura = um.Trim();
        ConsumUnitar = consumUnitar;
        PretUnitar = pretUnitar;
        CantitateTotala = cantitateTotala;
        TipDedus = tipDedus;
        Ordine = ordine;
    }

    public Guid ArticolId { get; private set; }
    public string CodResursa { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public TipResursa Tip { get; private set; }

    /// <summary>Tipul resursei nu reiese din prețuri (toate zero) și a fost dedus din UM; de confirmat la mapare.</summary>
    public bool TipDedus { get; private set; }
    public string UnitateMasura { get; private set; } = string.Empty;

    /// <summary>Cantitatea pe tot articolul (din sursă).</summary>
    public decimal CantitateTotala { get; private set; }

    /// <summary>Consumul pe unitatea de articol.</summary>
    public decimal ConsumUnitar { get; private set; }
    public decimal PretUnitar { get; private set; }
    public int Ordine { get; private set; }

    /// <summary>Resursa din nomenclatorul firmei (null = nemapată încă).</summary>
    public Guid? ResursaCatalogId { get; private set; }

    public void MapeazaLa(Guid? resursaCatalogId) => ResursaCatalogId = resursaCatalogId;

    /// <summary>Cantitatea articolului s-a schimbat: consumul unitar rămâne, cantitatea totală urmează articolul.</summary>
    internal void Rescaleaza(decimal cantitateArticol) => CantitateTotala = ConsumUnitar * cantitateArticol;

    /// <summary>Prețul nou al resursei (actualizarea prețurilor din achiziții, D-55).</summary>
    internal void SchimbaPret(decimal pret) => PretUnitar = pret;
}

/// <summary>
/// Un fișier original al unei versiuni importate (devizul cu resurse, lista F3), arhivat neschimbat, cu amprenta SHA-256
/// (REQ-IMP-03: „confirmare ca versiune nouă, cu fișierul original arhivat”).
/// </summary>
public sealed class FisierSursaDeviz : EntitateFirma
{
    private FisierSursaDeviz() { }

    public FisierSursaDeviz(Guid versiuneId, string numeFisier, string tipContinut, byte[] continut, bool principal)
    {
        VersiuneId = versiuneId;
        NumeFisier = numeFisier.Length > 260 ? numeFisier[..260] : numeFisier;
        TipContinut = tipContinut;
        Continut = continut;
        Marime = continut.LongLength;
        Amprenta = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(continut)).ToLowerInvariant();
        Principal = principal;
    }

    public Guid VersiuneId { get; private set; }
    public string NumeFisier { get; private set; } = string.Empty;
    public string TipContinut { get; private set; } = string.Empty;
    public long Marime { get; private set; }

    /// <summary>SHA-256 (hex): același fișier importat din nou se recunoaște.</summary>
    public string Amprenta { get; private set; } = string.Empty;

    /// <summary>Fișierul principal (devizul); celelalte sunt anexe (ex. F3).</summary>
    public bool Principal { get; private set; }
    public byte[] Continut { get; private set; } = [];

    public static string Amprenteaza(byte[] continut) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(continut)).ToLowerInvariant();
}

/// <summary>
/// O versiune aprobată înlocuită de una aprobată după ea (ciorna pornită din ea): istoricul și corectura care permite
/// schimbarea stării versiunii aprobate în „înlocuită” (regula 6; nimic altceva din ea nu se schimbă).
/// </summary>
public sealed class InlocuireVersiuneDeviz : EntitateFirma, ICorectieDocument
{
    private InlocuireVersiuneDeviz() { }

    public InlocuireVersiuneDeviz(Guid versiuneInlocuitaId, Guid versiuneNouaId, DateTimeOffset la) =>
        (VersiuneInlocuitaId, VersiuneNouaId, La) = (versiuneInlocuitaId, versiuneNouaId, la);

    public Guid VersiuneInlocuitaId { get; private set; }
    public Guid VersiuneNouaId { get; private set; }
    public DateTimeOffset La { get; private set; }

    public Guid DocumentCorectatId => VersiuneInlocuitaId;
}
