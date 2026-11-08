using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

/// <param name="IndexFactura">Poziția liniei în factură (-1 la NIR-ul pe aviz încă nefacturat).</param>
/// <param name="CantitateFactura">Cât era de recepționat din linie la emiterea NIR-ului (cantitatea de pe factură, minus
/// recepțiile anterioare ale aceleiași linii).</param>
/// <param name="CantitateReceptionata">Ce a primit efectiv gestionarul (diferența = lipsă la recepție).</param>
/// <param name="PretUnitar">Prețul fără TVA de pe factură.</param>
/// <param name="Valoare">Cantitatea recepționată × prețul, rotunjită la ban: valoarea intrată în stoc.</param>
/// <param name="IndexAviz">Poziția liniei pe aviz, la recepția pe aviz (D-40); null la recepția pe factură.</param>
public sealed record LinieNir(
    int IndexFactura,
    Guid ArticolId,
    string ArticolCod,
    string Denumire,
    string UnitateMasura,
    decimal CantitateFactura,
    decimal CantitateReceptionata,
    decimal PretUnitar,
    decimal Valoare,
    string? Observatii,
    bool Partiala = false,
    int? IndexAviz = null)
{
    /// <summary>Diferența față de factură: doar la liniile închise (lipsă definitivă sau surplus); restul unei recepții parțiale nu e diferență.</summary>
    public decimal Diferenta => Partiala ? 0 : CantitateReceptionata - CantitateFactura;

    /// <summary>Cheia liniei în NIR: poziția pe aviz (recepția pe aviz) sau în factură.</summary>
    public int Cheie => IndexAviz ?? IndexFactura;
}

/// <summary>La facturarea avizului: linia de aviz din NIR, linia facturii care o acoperă și prețul facturii.</summary>
public sealed record LinieFacturareAviz(int IndexAviz, int IndexFactura, decimal PretUnitar);

/// <param name="Partiala">Restul liniei vine mai târziu (altă recepție); fără, linia se închide cu ce s-a primit acum.</param>
public sealed record LinieNirCerere(int IndexFactura, Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura,
    decimal CantitateFactura, decimal CantitateReceptionata, decimal PretUnitar, string? Observatii, bool Partiala = false);

/// <summary>
/// Nota de intrare-recepție: gestionarul confirmă ce a primit din factura unui furnizor, în depozitul ales.
/// La emitere, materialele intră în stoc la prețul facturii. Imuabilă (regula 6). O factură se poate recepționa în mai multe
/// NIR-uri, pe linii și cantități (<see cref="StareReceptie"/>).
/// Marfa venită doar cu aviz (D-40) se recepționează pe aviz (<see cref="EmiteDinAviz"/>), fără valoare sau la prețul comenzii;
/// când vine factura, NIR-ul primește factura și prețurile ei (<see cref="Factureaza"/>, cu o corectură în istoric).
/// </summary>
public sealed class Nir : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieNir>? _linii;

    private Nir() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"NIR/{An}/{Numar:D4}";
    public DateOnly Data { get; private set; }
    public Guid GestiuneId { get; private set; }
    public string GestiuneDenumire { get; private set; } = string.Empty;
    /// <summary>Factura recepționată; la NIR-ul pe aviz, factura care l-a acoperit (null până vine).</summary>
    public Guid? FacturaId { get; private set; }

    public string? FacturaNumar { get; private set; }
    public DateOnly? FacturaData { get; private set; }

    /// <summary>Avizul pe care s-a recepționat marfa (D-40); null la recepția pe factură.</summary>
    public Guid? AvizId { get; private set; }

    public string? AvizNumar { get; private set; }
    public DateOnly? AvizData { get; private set; }

    /// <summary>NIR pe aviz pentru care încă nu a venit factura (stocul are valoare provizorie).</summary>
    public bool AsteaptaFactura => AvizId is not null && FacturaId is null;

    /// <summary>„Factura X” / „Aviz Y + factura X” / „Aviz Y (fără factură)”.</summary>
    public string Document => AvizId is null
        ? FacturaNumar ?? string.Empty
        : FacturaId is null ? $"aviz {AvizNumar} (fără factură)" : $"aviz {AvizNumar} + factura {FacturaNumar}";
    public Guid? FurnizorId { get; private set; }
    public string FurnizorDenumire { get; private set; } = string.Empty;

    /// <summary>Proiectul pe care s-a înregistrat factura (informativ: costul ajunge pe proiect la transferul în șantier).</summary>
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieNir> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieNir>>(LiniiJson) ?? [];
    public decimal Valoare { get; private set; }
    public string? Observatii { get; private set; }
    public Guid ReceptionatDeId { get; private set; }
    public string ReceptionatDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;
    public bool AreDiferente => Linii.Any(l => l.Diferenta != 0);

    public static Rezultat<Nir> Emite(
        int an, int numar, DateOnly data, Gestiune gestiune,
        Guid facturaId, string facturaNumar, DateOnly facturaData, Guid? furnizorId, string furnizorDenumire, Guid proiectId, string proiectCod,
        IReadOnlyList<LinieNirCerere> linii, string? observatii, Guid utilizatorId, string utilizatorNume)
    {
        if (facturaId == Guid.Empty || string.IsNullOrWhiteSpace(facturaNumar))
            return Rezultat.Esuat<Nir>(Eroare.Validare("Factura este obligatorie."));
        var r = Construieste(an, numar, data, gestiune, facturaData, "factură", furnizorId, furnizorDenumire, proiectId, proiectCod, linii, observatii,
            utilizatorId, utilizatorNume, aviz: false);
        if (r.Succes)
            (r.Valoare.FacturaId, r.Valoare.FacturaNumar, r.Valoare.FacturaData) = (facturaId, facturaNumar.Trim(), facturaData);
        return r;
    }

    /// <summary>
    /// Recepția mărfii venite doar cu aviz (D-40). <see cref="LinieNirCerere.IndexFactura"/> e aici poziția pe aviz, iar prețul e cel
    /// din nota de comandă (sau 0): valoarea definitivă vine cu factura (<see cref="Factureaza"/>).
    /// </summary>
    public static Rezultat<Nir> EmiteDinAviz(
        int an, int numar, DateOnly data, Gestiune gestiune,
        Guid avizId, string avizNumar, DateOnly avizData, Guid? furnizorId, string furnizorDenumire, Guid proiectId, string proiectCod,
        IReadOnlyList<LinieNirCerere> linii, string? observatii, Guid utilizatorId, string utilizatorNume)
    {
        if (avizId == Guid.Empty || string.IsNullOrWhiteSpace(avizNumar))
            return Rezultat.Esuat<Nir>(Eroare.Validare("Avizul este obligatoriu."));
        var r = Construieste(an, numar, data, gestiune, avizData, "aviz", furnizorId, furnizorDenumire, proiectId, proiectCod, linii, observatii,
            utilizatorId, utilizatorNume, aviz: true);
        if (r.Succes)
            (r.Valoare.AvizId, r.Valoare.AvizNumar, r.Valoare.AvizData) = (avizId, avizNumar.Trim(), avizData);
        return r;
    }

    private static Rezultat<Nir> Construieste(
        int an, int numar, DateOnly data, Gestiune gestiune, DateOnly dataDocument, string document,
        Guid? furnizorId, string furnizorDenumire, Guid proiectId, string proiectCod,
        IReadOnlyList<LinieNirCerere> linii, string? observatii, Guid utilizatorId, string utilizatorNume, bool aviz)
    {
        var probleme = new List<string>();
        if (an < 2000 || numar < 1) probleme.Add("Numărul NIR nu este valid.");
        if (gestiune.Tip != TipGestiune.Depozit || !gestiune.Activa) probleme.Add("Recepția se face într-un depozit activ.");
        if (data < dataDocument) probleme.Add(aviz ? "Data recepției nu poate fi înaintea datei avizului." : "Data recepției nu poate fi înaintea datei facturii.");
        if (linii.Count == 0) probleme.Add("Recepția trebuie să conțină cel puțin un material.");
        if (linii.Any(l => l.ArticolId == Guid.Empty)) probleme.Add("Fiecare linie trebuie legată de un articol de stoc.");
        if (linii.Any(l => l.CantitateReceptionata < 0 || l.CantitateFactura <= 0 || l.PretUnitar < 0)) probleme.Add("Cantitățile și prețurile nu pot fi negative.");
        // Restul care „vine mai târziu” n-are sens când s-a primit tot ce era de primit.
        linii = linii.Select(l => l.Partiala && l.CantitateReceptionata >= l.CantitateFactura ? l with { Partiala = false } : l).ToList();
        if (linii.All(l => l.CantitateReceptionata == 0 && l.Partiala)) probleme.Add("Nu s-a recepționat nimic; verifică cantitățile.");
        if (linii.Any(l => l.Partiala && l.CantitateReceptionata == 0)) probleme.Add("O linie fără cantitate recepționată se lasă în afara recepției.");
        if (linii.Any(l => !l.Partiala && l.CantitateReceptionata != l.CantitateFactura && string.IsNullOrWhiteSpace(l.Observatii)))
            probleme.Add($"La liniile închise cu diferențe față de {document} (lipsă definitivă sau surplus), completează observația.");
        if (linii.GroupBy(l => l.IndexFactura).Any(g => g.Count() > 1)) probleme.Add($"O linie de {document} apare de două ori.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume)) probleme.Add("Gestionarul este obligatoriu.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<Nir>(Eroare.Validare(string.Join(" ", probleme)));

        var liniiNir = linii.Select(l => new LinieNir(
            aviz ? -1 : l.IndexFactura, l.ArticolId, l.ArticolCod, l.Denumire.Trim(), l.UnitateMasura.Trim(), l.CantitateFactura, l.CantitateReceptionata,
            l.PretUnitar, Rotunjire.Lei.Aplica(l.CantitateReceptionata * l.PretUnitar), string.IsNullOrWhiteSpace(l.Observatii) ? null : l.Observatii.Trim(),
            l.Partiala, aviz ? l.IndexFactura : null)).ToList();

        return Rezultat.Ok(new Nir
        {
            An = an,
            Numar = numar,
            Data = data,
            GestiuneId = gestiune.Id,
            GestiuneDenumire = gestiune.Denumire,
            FurnizorId = furnizorId,
            FurnizorDenumire = furnizorDenumire.Trim(),
            ProiectId = proiectId,
            ProiectCod = proiectCod,
            LiniiJson = JsonSerializer.Serialize(liniiNir),
            Valoare = liniiNir.Sum(l => l.Valoare),
            Observatii = string.IsNullOrWhiteSpace(observatii) ? null : observatii.Trim(),
            ReceptionatDeId = utilizatorId,
            ReceptionatDeNume = utilizatorNume.Trim(),
        });
    }

    /// <summary>
    /// Corectează NIR-ul recepționat greșit (cantitatea cf. factură, cantitatea recepționată, prețul, observațiile) și întoarce
    /// corectura cu liniile schimbate, de salvat odată cu NIR-ul. Stocul primește diferențele (infrastructura le aplică prin
    /// <see cref="CalculStoc.Corectie"/>). Liniile nemenționate rămân; nu se adaugă și nu se scot linii (pentru asta: storno și recepție nouă).
    /// </summary>
    public Rezultat<CorectieNir> Corecteaza(IReadOnlyList<LinieNirCorectata> linii, DateOnly data, string motiv, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = new List<string>();
        if (string.IsNullOrWhiteSpace(motiv) || motiv.Trim().Length > 1000) probleme.Add("Motivul corecturii este obligatoriu (cel mult 1000 de caractere).");
        if (data < Data) probleme.Add("Corectura nu poate avea data înaintea NIR-ului.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume)) probleme.Add("Utilizatorul este obligatoriu.");
        var existente = Linii.ToDictionary(l => l.Cheie);
        if (linii.GroupBy(l => l.IndexFactura).Any(g => g.Count() > 1)) probleme.Add("O linie apare de două ori.");
        if (linii.Any(l => !existente.ContainsKey(l.IndexFactura))) probleme.Add("Corectura conține o linie care nu e pe NIR.");
        if (linii.Any(l => l.CantitateReceptionata < 0 || l.CantitateFactura <= 0 || l.PretUnitar < 0)) probleme.Add("Cantitățile și prețurile nu pot fi negative.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<CorectieNir>(Eroare.Validare(string.Join(" ", probleme)));

        var noi = new List<LinieNir>();
        var schimbate = new List<LinieCorectieNir>();
        var corectate = linii.ToDictionary(l => l.IndexFactura);
        foreach (var v in Linii)
        {
            if (!corectate.TryGetValue(v.Cheie, out var c))
            {
                noi.Add(v);
                continue;
            }

            var observatii = string.IsNullOrWhiteSpace(c.Observatii) ? null : c.Observatii.Trim();
            var partiala = v.Partiala && c.CantitateReceptionata < c.CantitateFactura;
            var n = v with
            {
                CantitateFactura = c.CantitateFactura,
                CantitateReceptionata = c.CantitateReceptionata,
                PretUnitar = c.PretUnitar,
                Valoare = Rotunjire.Lei.Aplica(c.CantitateReceptionata * c.PretUnitar),
                Observatii = observatii,
                Partiala = partiala,
            };
            if (!n.Partiala && n.CantitateReceptionata != n.CantitateFactura && observatii is null)
                return Rezultat.Esuat<CorectieNir>(Eroare.Validare($"„{v.Denumire}”: diferența față de factură cere o observație."));
            noi.Add(n);
            if (n != v)
                schimbate.Add(new LinieCorectieNir(v.Cheie, v.ArticolId, v.ArticolCod, v.Denumire, v.UnitateMasura,
                    v.CantitateFactura, n.CantitateFactura, v.CantitateReceptionata, n.CantitateReceptionata, v.PretUnitar, n.PretUnitar, v.Valoare, n.Valoare));
        }

        if (schimbate.Count == 0)
            return Rezultat.Esuat<CorectieNir>(Eroare.Validare("Nu ai modificat nimic."));
        if (noi.All(l => l.CantitateReceptionata == 0))
            return Rezultat.Esuat<CorectieNir>(Eroare.Validare("După corectură NIR-ul n-ar mai avea nimic recepționat: stornează-l."));

        LiniiJson = JsonSerializer.Serialize(noi);
        _linii = null;
        Valoare = noi.Sum(l => l.Valoare);
        return Rezultat.Ok(CorectieNir.Creeaza(this, data, motiv, schimbate, utilizatorId, utilizatorNume));
    }

    /// <summary>
    /// Factura a venit pentru marfa recepționată pe aviz (D-40): fiecare linie a NIR-ului primește linia facturii care o acoperă
    /// și prețul ei, iar NIR-ul primește factura. Întoarce corectura cu liniile al căror preț s-a schimbat (null dacă prețurile
    /// erau deja cele din factură), de salvat împreună cu NIR-ul; stocul primește diferențele de valoare.
    /// </summary>
    public Rezultat<CorectieNir?> Factureaza(
        Guid facturaId, string facturaNumar, DateOnly facturaData, IReadOnlyList<LinieFacturareAviz> linii, DateOnly data, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = new List<string>();
        if (AvizId is null) probleme.Add($"{NumarAfisat} e o recepție pe factură, nu pe aviz.");
        if (FacturaId is not null) probleme.Add($"{NumarAfisat} are deja factura {FacturaNumar}.");
        if (facturaId == Guid.Empty || string.IsNullOrWhiteSpace(facturaNumar)) probleme.Add("Factura este obligatorie.");
        if (data < Data) probleme.Add("Facturarea nu poate avea data înaintea NIR-ului.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume)) probleme.Add("Utilizatorul este obligatoriu.");
        if (linii.GroupBy(l => l.IndexAviz).Any(g => g.Count() > 1)) probleme.Add("O linie a avizului apare de două ori.");
        if (linii.Any(l => l.IndexFactura < 0 || l.PretUnitar < 0)) probleme.Add("Linia facturii și prețul trebuie să fie valide.");
        var dupaAviz = linii.ToLookup(l => l.IndexAviz);
        var lipsa = Linii.Where(l => l.CantitateReceptionata > 0 && !dupaAviz.Contains(l.Cheie)).Select(l => $"„{l.Denumire}”").ToList();
        if (lipsa.Count > 0) probleme.Add($"Alege linia facturii pentru: {string.Join(", ", lipsa)}.");
        if (linii.Any(l => Linii.All(x => x.Cheie != l.IndexAviz))) probleme.Add("O linie aleasă nu e pe NIR.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<CorectieNir?>(Eroare.Validare(string.Join(" ", probleme)));

        var noi = new List<LinieNir>();
        var schimbate = new List<LinieCorectieNir>();
        foreach (var v in Linii)
        {
            if (dupaAviz[v.Cheie].FirstOrDefault() is not { } f)
            {
                noi.Add(v); // linie neprimită (cantitate 0): rămâne fără factură
                continue;
            }

            var n = v with { IndexFactura = f.IndexFactura, PretUnitar = f.PretUnitar, Valoare = Rotunjire.Lei.Aplica(v.CantitateReceptionata * f.PretUnitar) };
            noi.Add(n);
            if (n.Valoare != v.Valoare || n.PretUnitar != v.PretUnitar)
                schimbate.Add(new LinieCorectieNir(v.Cheie, v.ArticolId, v.ArticolCod, v.Denumire, v.UnitateMasura,
                    v.CantitateFactura, n.CantitateFactura, v.CantitateReceptionata, n.CantitateReceptionata, v.PretUnitar, n.PretUnitar, v.Valoare, n.Valoare));
        }

        LiniiJson = JsonSerializer.Serialize(noi);
        _linii = null;
        Valoare = noi.Sum(l => l.Valoare);
        (FacturaId, FacturaNumar, FacturaData) = (facturaId, facturaNumar.Trim(), facturaData);
        return Rezultat.Ok(schimbate.Count == 0
            ? null
            : CorectieNir.Creeaza(this, data, $"Prețurile din factura {FacturaNumar} (marfa recepționată pe avizul {AvizNumar})", schimbate, utilizatorId, utilizatorNume));
    }
}

public enum DirectieTransfer
{
    /// <summary>Din depozit în „Materiale în șantier” ale proiectului.</summary>
    SpreSantier,

    /// <summary>Retur: materialele nefolosite se întorc din șantier în depozit.</summary>
    Retur,
}

/// <param name="PretUnitar">CMP-ul din gestiunea sursă la momentul transferului.</param>
public sealed record LinieTransfer(Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal Valoare);

/// <summary>
/// Bon de transfer: materialele pleacă din depozit în „Materiale în șantier” ale unui proiect, la CMP.
/// Imuabil (regula 6). Valorile liniilor se calculează la emitere, sub blocarea stocului firmei.
/// </summary>
public sealed class TransferStoc : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieTransfer>? _linii;

    private TransferStoc() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"BT/{An}/{Numar:D4}";
    public DirectieTransfer Directie { get; private set; }
    public DateOnly Data { get; private set; }
    public Guid GestiuneSursaId { get; private set; }
    public string GestiuneSursaDenumire { get; private set; } = string.Empty;
    public Guid GestiuneDestinatieId { get; private set; }
    public string GestiuneDestinatieDenumire { get; private set; } = string.Empty;
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieTransfer> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieTransfer>>(LiniiJson) ?? [];
    public decimal Valoare { get; private set; }
    public string? Observatii { get; private set; }
    public Guid EmisDeId { get; private set; }
    public string EmisDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;

    public static Rezultat<TransferStoc> Emite(
        int an, int numar, DateOnly data, Gestiune sursa, Gestiune destinatie, string proiectCod,
        IReadOnlyList<LinieTransfer> linii, string? observatii, Guid utilizatorId, string utilizatorNume,
        DirectieTransfer directie = DirectieTransfer.SpreSantier)
    {
        var probleme = new List<string>();
        var (depozit, santier) = directie == DirectieTransfer.SpreSantier ? (sursa, destinatie) : (destinatie, sursa);
        if (an < 2000 || numar < 1) probleme.Add("Numărul bonului de transfer nu este valid.");
        if (depozit.Tip != TipGestiune.Depozit)
            probleme.Add(directie == DirectieTransfer.SpreSantier ? "Transferul pleacă dintr-un depozit." : "Returul ajunge într-un depozit.");
        if (santier.Tip != TipGestiune.Santier || santier.ProiectId is null)
            probleme.Add(directie == DirectieTransfer.SpreSantier
                ? "Transferul ajunge în gestiunea de șantier a unui proiect."
                : "Returul pleacă din gestiunea de șantier a unui proiect.");
        if (linii.Count == 0) probleme.Add("Transferul trebuie să conțină cel puțin un material.");
        if (linii.Any(l => l.Cantitate <= 0)) probleme.Add("Cantitățile transferate trebuie să fie pozitive.");
        if (linii.GroupBy(l => l.ArticolId).Any(g => g.Count() > 1)) probleme.Add("Un articol apare de două ori; cumulează cantitatea.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume)) probleme.Add("Utilizatorul este obligatoriu.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<TransferStoc>(Eroare.Validare(string.Join(" ", probleme)));

        return Rezultat.Ok(new TransferStoc
        {
            An = an,
            Numar = numar,
            Directie = directie,
            Data = data,
            GestiuneSursaId = sursa.Id,
            GestiuneSursaDenumire = sursa.Denumire,
            GestiuneDestinatieId = destinatie.Id,
            GestiuneDestinatieDenumire = destinatie.Denumire,
            ProiectId = santier.ProiectId!.Value,
            ProiectCod = proiectCod,
            LiniiJson = JsonSerializer.Serialize(linii),
            Valoare = linii.Sum(l => l.Valoare),
            Observatii = string.IsNullOrWhiteSpace(observatii) ? null : observatii.Trim(),
            EmisDeId = utilizatorId,
            EmisDeNume = utilizatorNume.Trim(),
        });
    }
}

public enum TipMiscare
{
    IntrareNir,
    IesireTransfer,
    IntrareTransfer,
    IesireConsum,
    IesireStornoNir,
    PlusInventar,
    MinusInventar,

    /// <summary>Corectura unui NIR (D-35): diferența de cantitate / valoare față de recepția inițială, pe același NIR (DocumentId = NIR-ul).</summary>
    CorectieNir,
}

/// <param name="ArticolDevizCheie">Cheia stabilă a articolului de deviz pe care se consumă (D-05); null = cheltuieli generale de șantier.</param>
public sealed record LinieConsum(
    Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal Valoare,
    Guid? ArticolDevizCheie, string? ArticolDevizSimbol, string? ArticolDevizDenumire);

/// <summary>
/// Bon de consum: materialele din „Materiale în șantier” se consumă pe articolele devizului, la CMP. Așa ajunge
/// costul real al materialelor pe articol (axa costurilor). Imuabil (regula 6).
/// </summary>
public sealed class BonConsum : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieConsum>? _linii;

    private BonConsum() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"BC/{An}/{Numar:D4}";
    public DateOnly Data { get; private set; }
    public Guid GestiuneId { get; private set; }
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieConsum> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieConsum>>(LiniiJson) ?? [];
    public decimal Valoare { get; private set; }
    public string? Observatii { get; private set; }
    public Guid EmisDeId { get; private set; }
    public string EmisDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;

    public static Rezultat<BonConsum> Emite(
        int an, int numar, DateOnly data, Gestiune santier, string proiectCod, IReadOnlyList<LinieConsum> linii, string? observatii,
        Guid utilizatorId, string utilizatorNume)
    {
        var probleme = new List<string>();
        if (an < 2000 || numar < 1) probleme.Add("Numărul bonului de consum nu este valid.");
        if (santier.Tip != TipGestiune.Santier || santier.ProiectId is null) probleme.Add("Consumul se face din gestiunea de șantier a unui proiect.");
        if (linii.Count == 0) probleme.Add("Bonul de consum trebuie să conțină cel puțin un material.");
        if (linii.Any(l => l.Cantitate <= 0)) probleme.Add("Cantitățile consumate trebuie să fie pozitive.");
        if (linii.GroupBy(l => (l.ArticolId, l.ArticolDevizCheie)).Any(g => g.Count() > 1))
            probleme.Add("Același material apare de două ori pe același articol de deviz; cumulează cantitatea.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume)) probleme.Add("Utilizatorul este obligatoriu.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<BonConsum>(Eroare.Validare(string.Join(" ", probleme)));

        return Rezultat.Ok(new BonConsum
        {
            An = an,
            Numar = numar,
            Data = data,
            GestiuneId = santier.Id,
            ProiectId = santier.ProiectId!.Value,
            ProiectCod = proiectCod,
            LiniiJson = JsonSerializer.Serialize(linii),
            Valoare = linii.Sum(l => l.Valoare),
            Observatii = string.IsNullOrWhiteSpace(observatii) ? null : observatii.Trim(),
            EmisDeId = utilizatorId,
            EmisDeNume = utilizatorNume.Trim(),
        });
    }
}

/// <summary>
/// O mișcare în fișa de magazie (append-only): cantitatea și valoarea cu semn (+ intrare, − ieșire) și documentul.
/// Soldul unei gestiuni = suma mișcărilor; <see cref="SoldStoc"/> îl ține calculat.
/// </summary>
public sealed class MiscareStoc : EntitateFirma
{
    private MiscareStoc() { }

    public MiscareStoc(Guid gestiuneId, Guid articolId, DateOnly data, TipMiscare tip, decimal cantitate, decimal valoare, Guid documentId, string documentNumar)
    {
        (GestiuneId, ArticolId, Data, Tip, Cantitate, Valoare, DocumentId, DocumentNumar) =
            (gestiuneId, articolId, data, tip, cantitate, valoare, documentId, documentNumar);
    }

    public Guid GestiuneId { get; private set; }
    public Guid ArticolId { get; private set; }
    public DateOnly Data { get; private set; }
    public TipMiscare Tip { get; private set; }
    public decimal Cantitate { get; private set; }
    public decimal Valoare { get; private set; }
    public Guid DocumentId { get; private set; }
    public string DocumentNumar { get; private set; } = string.Empty;
}

/// <summary>Soldul curent al unui articol într-o gestiune (actualizat în aceeași tranzacție cu mișcarea).</summary>
public sealed class SoldStoc : EntitateFirma, IFaraJurnal
{
    private SoldStoc() { }

    public SoldStoc(Guid gestiuneId, Guid articolId) => (GestiuneId, ArticolId) = (gestiuneId, articolId);

    public Guid GestiuneId { get; private set; }
    public Guid ArticolId { get; private set; }
    public decimal Cantitate { get; private set; }
    public decimal Valoare { get; private set; }

    public Sold Sold => new(Cantitate, Valoare);

    public void Seteaza(Sold sold) => (Cantitate, Valoare) = (sold.Cantitate, sold.Valoare);
}
