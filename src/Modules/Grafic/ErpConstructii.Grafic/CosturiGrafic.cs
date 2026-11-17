using ErpConstructii.SharedKernel;

namespace ErpConstructii.Grafic;

// ---------------------------------------------------------------- contractul cu modulele-sursă (D-57, etapa 2)
// Grafic îl definește și îl citește la cerere; Facturi (facturile primite) și Stocuri (bonurile de consum) îl implementează.
// Un modul lipsă = o sursă mai puțin. Grafic nu cunoaște tabelele lor.

public enum SursaDocumentCost
{
    Factura,
    BonConsum,
}

/// <param name="Index">Poziția liniei în document (cheia alocării).</param>
/// <param name="Valoare">Fără TVA, în moneda documentului.</param>
/// <param name="Alocabila">Fals pentru liniile care nu sunt cost direct: materialele de stoc de pe factură devin cost abia la consum (bonul de consum).</param>
/// <param name="Grupare">Ce leagă linia de altele asemănătoare, pentru propuneri (ex. articolul de deviz al consumului, contractul facturii).</param>
public sealed record LinieDocumentCost(
    int Index, string Denumire, string? UnitateMasura, decimal Cantitate, decimal Valoare, bool Alocabila, string? Nota, string? Grupare);

/// <param name="Numar">Numărul afișat (ex. „F123”, „BC/2026/0012”).</param>
/// <param name="Detalii">Contractul, nota de comandă, gestiunea etc.</param>
public sealed record DocumentCost(
    SursaDocumentCost Sursa, Guid Id, string Numar, DateOnly Data, string? Partener, string Moneda, IReadOnlyList<LinieDocumentCost> Linii, string? Detalii);

/// <summary>Documentele cu costuri ale unui proiect, din modulul care le deține.</summary>
public interface ISursaDocumenteCost
{
    SursaDocumentCost Sursa { get; }

    Task<IReadOnlyList<DocumentCost>> DocumenteAsync(Guid proiectId, CancellationToken ct = default);
}

// ---------------------------------------------------------------- alocarea

/// <summary>Ținta alocării: o activitate (după cod) sau un nod WBS (după cheie) — ambele stabile la reimport.</summary>
public enum TipTinta
{
    Activitate,
    Wbs,
}

/// <summary>
/// Partea (procentul) dintr-o linie a unui document de cost alocată pe o activitate sau un nod WBS al graficului (D-57).
/// Stă în modulul Grafic: documentele (facturi blocate, bonuri de consum) nu se modifică pentru asta, iar alocarea se poate
/// schimba oricând (jurnalizată).
/// </summary>
public sealed class AlocareCost : EntitateFirma
{
    private AlocareCost() { }

    public Guid GraficId { get; private set; }
    public SursaDocumentCost Sursa { get; private set; }
    public Guid DocumentId { get; private set; }
    public string DocumentNumar { get; private set; } = string.Empty;
    public int LinieIndex { get; private set; }

    /// <summary>Denumirea liniei la alocare: dacă documentul e corectat și linia se schimbă, alocarea e semnalată.</summary>
    public string LinieDenumire { get; private set; } = string.Empty;

    public TipTinta TipTinta { get; private set; }
    public string Cheie { get; private set; } = string.Empty;
    public string DenumireTinta { get; private set; } = string.Empty;

    /// <summary>Procentul din valoarea liniei, (0, 100].</summary>
    public decimal Procent { get; private set; }

    public string CreatDeNume { get; private set; } = string.Empty;

    public static AlocareCost Creeaza(Guid graficId, DocumentCost document, LinieDocumentCost linie, TipTinta tip, string cheie, string denumireTinta,
        decimal procent, string creatDe) => new()
    {
        GraficId = graficId,
        Sursa = document.Sursa,
        DocumentId = document.Id,
        DocumentNumar = GraficExecutie.Taie(document.Numar, 100) ?? string.Empty,
        LinieIndex = linie.Index,
        LinieDenumire = GraficExecutie.Taie(linie.Denumire, 500) ?? string.Empty,
        TipTinta = tip,
        Cheie = cheie,
        DenumireTinta = GraficExecutie.Taie(denumireTinta, 500) ?? cheie,
        Procent = procent,
        CreatDeNume = GraficExecutie.Taie(creatDe, 200) ?? string.Empty,
    };

    public AlocareLinie Linie() => new(Sursa, DocumentId, LinieIndex, TipTinta, Cheie, Procent, LinieDenumire);
}

/// <summary>Alocarea în forma folosită de calcul.</summary>
public sealed record AlocareLinie(SursaDocumentCost Sursa, Guid DocumentId, int LinieIndex, TipTinta Tip, string Cheie, decimal Procent, string? LinieDenumire = null);

/// <summary>O alocare cerută de utilizator pentru o linie a documentului.</summary>
public sealed record AlocareCeruta(int LinieIndex, TipTinta Tip, string Cheie, decimal Procent);

/// <summary>Structura graficului (actualizarea curentă), cât îi trebuie calculului: nodurile cu părinții și activitățile cu nodul lor.</summary>
public sealed record StructuraGrafic(IReadOnlyDictionary<string, string?> ParinteNod, IReadOnlyDictionary<string, string?> NodActivitate,
    IReadOnlyDictionary<string, string> Denumiri)
{
    public bool Exista(TipTinta tip, string cheie) => tip == TipTinta.Activitate ? NodActivitate.ContainsKey(cheie) : ParinteNod.ContainsKey(cheie);

    public string? Denumire(TipTinta tip, string cheie) => Denumiri.GetValueOrDefault((tip == TipTinta.Activitate ? "a:" : "w:") + cheie);

    public static StructuraGrafic Din(IEnumerable<NodWbsCanonic> noduri, IEnumerable<ActivitateCanonica> activitati)
    {
        var n = noduri.ToList();
        var a = activitati.ToList();
        var denumiri = n.Select(x => ("w:" + x.Cheie, x.Denumire)).Concat(a.Select(x => ("a:" + x.Cod, x.Denumire)))
            .GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.First().Item2, StringComparer.Ordinal);
        return new StructuraGrafic(
            n.GroupBy(x => x.Cheie).ToDictionary(g => g.Key, g => g.First().CheieParinte, StringComparer.Ordinal),
            a.GroupBy(x => x.Cod).ToDictionary(g => g.Key, g => g.First().CodWbs, StringComparer.Ordinal),
            denumiri);
    }
}

/// <summary>Cursurile unui document: moneda documentului și moneda graficului, la data documentului (BNR).</summary>
public sealed record CursuriDocument(CursValutar? Document, CursValutar? Grafic);

/// <summary>Suma costurilor alocate pe o țintă: în lei și în moneda graficului (fără alocările cărora le lipsește cursul BNR: <paramref name="FaraCurs"/>).</summary>
public sealed record CostTinta(decimal Lei, decimal? MonedaGrafic, int Documente, int FaraCurs);

/// <summary>O alocare valorizată (pentru detaliul unei ținte).</summary>
public sealed record CostAlocat(
    SursaDocumentCost Sursa, Guid DocumentId, string DocumentNumar, DateOnly Data, string? Partener, int LinieIndex, string Linie,
    TipTinta Tip, string Cheie, string? DenumireTinta, decimal Procent, decimal Lei, decimal? MonedaGrafic, decimal? CursDocument,
    decimal? CursGrafic, DateOnly? DataCurs);

public enum StareAlocare
{
    Nealocat,
    Partial,
    Alocat,
    /// <summary>Documentul nu are linii de cost direct (ex. factură doar cu materiale de stoc).</summary>
    FaraCost,
}

/// <summary>Propunerea pentru o linie nealocată, după o linie asemănătoare (aceeași grupare) alocată deja.</summary>
public sealed record PropunereAlocare(int LinieIndex, TipTinta Tip, string Cheie, decimal Procent, string Motiv);

/// <summary>Starea alocării unui document.</summary>
/// <param name="ValoareLei">Valoarea liniilor alocabile, în lei.</param>
public sealed record StareDocumentCost(
    DocumentCost Document, decimal ValoareLei, decimal AlocatLei, decimal? AlocatMonedaGrafic, decimal ProcentAlocat, StareAlocare Stare,
    decimal? CursDocument, decimal? CursGrafic, DateOnly? DataCurs, IReadOnlyList<AlocareLinie> Alocari, IReadOnlyList<PropunereAlocare> Propuneri,
    IReadOnlyList<string> Probleme);

public sealed record RezultatCosturiGrafic(
    IReadOnlyDictionary<string, CostTinta> PeActivitate,
    IReadOnlyDictionary<string, CostTinta> PeNod,
    CostTinta Total,
    CostTinta FaraTinta,
    IReadOnlyList<CostAlocat> Alocari,
    IReadOnlyList<StareDocumentCost> Documente);

/// <summary>
/// Costurile ERP pe graficul de execuție (D-57, etapa 2): liniile documentelor (facturi, bonuri de consum) alocate procentual pe
/// activități / noduri WBS, valorizate în lei și în moneda graficului la cursul BNR din data documentului, totalizate pe arbore
/// (activitatea → nodul ei → strămoșii). Alocările pe ținte care nu mai există în actualizarea curentă intră doar în total
/// („fără țintă”). Logică pură.
/// </summary>
public static class CalculCosturiGrafic
{
    public const decimal Toleranta = 0.01m;

    public static RezultatCosturiGrafic Calculeaza(
        StructuraGrafic structura, string monedaGrafic, IReadOnlyList<DocumentCost> documente, IReadOnlyList<AlocareLinie> alocari,
        IReadOnlyDictionary<Guid, CursuriDocument> cursuri)
    {
        var peDocument = alocari.ToLookup(a => (a.Sursa, a.DocumentId));
        var peActivitate = new Dictionary<string, Acumulator>(StringComparer.Ordinal);
        var peNod = new Dictionary<string, Acumulator>(StringComparer.Ordinal);
        var total = new Acumulator();
        var faraTinta = new Acumulator();
        var valorizate = new List<CostAlocat>();
        var stari = new List<StareDocumentCost>();
        var propuneri = Propuneri(documente, alocari);

        foreach (var d in documente)
        {
            var c = cursuri.GetValueOrDefault(d.Id) ?? new CursuriDocument(null, null);
            var probleme = new List<string>();
            var linii = d.Linii.ToDictionary(l => l.Index);
            decimal valoareLei = 0, alocatLei = 0;
            decimal? alocatGrafic = 0;
            foreach (var l in d.Linii.Where(l => l.Alocabila))
                valoareLei += Lei(l.Valoare, d.Moneda, c) ?? 0;

            var aleDocumentului = peDocument[(d.Sursa, d.Id)].ToList();
            foreach (var a in aleDocumentului)
            {
                if (!linii.TryGetValue(a.LinieIndex, out var l) || !l.Alocabila)
                {
                    probleme.Add($"Linia {a.LinieIndex + 1} nu mai există pe document sau nu e cost direct: alocarea ei e ignorată.");
                    continue;
                }

                if (a.LinieDenumire is { Length: > 0 } dn && !string.Equals(dn.Trim(), l.Denumire.Trim(), StringComparison.Ordinal))
                    probleme.Add($"Linia {a.LinieIndex + 1} s-a schimbat de la alocare („{dn}” → „{l.Denumire}”): verifică alocarea.");
                var parte = l.Valoare * a.Procent / 100m;
                var lei = Lei(parte, d.Moneda, c);
                var grafic = InMonedaGrafic(parte, d.Moneda, monedaGrafic, c);
                if (lei is null)
                    probleme.Add($"Lipsește cursul BNR pentru {d.Moneda} la {d.Data:dd.MM.yyyy}.");
                var leiR = Math.Round(lei ?? 0, 2, MidpointRounding.AwayFromZero);
                var graficR = grafic is null ? (decimal?)null : Math.Round(grafic.Value, 2, MidpointRounding.AwayFromZero);
                alocatLei += leiR;
                alocatGrafic = alocatGrafic is null || graficR is null ? null : alocatGrafic + graficR;

                total.Adauga(d.Id, leiR, graficR);
                var exista = structura.Exista(a.Tip, a.Cheie);
                if (!exista)
                {
                    faraTinta.Adauga(d.Id, leiR, graficR);
                    probleme.Add($"{(a.Tip == TipTinta.Activitate ? "Activitatea" : "Nodul WBS")} {a.Cheie} nu mai e în actualizarea curentă a graficului.");
                }
                else
                {
                    string? nod;
                    if (a.Tip == TipTinta.Activitate)
                    {
                        Acumuleaza(peActivitate, a.Cheie, d.Id, leiR, graficR);
                        nod = structura.NodActivitate[a.Cheie];
                    }
                    else
                        nod = a.Cheie;

                    // Nodul și strămoșii lui.
                    for (var k = nod; k is not null && structura.ParinteNod.ContainsKey(k); k = structura.ParinteNod[k])
                        Acumuleaza(peNod, k, d.Id, leiR, graficR);
                }

                valorizate.Add(new CostAlocat(d.Sursa, d.Id, d.Numar, d.Data, d.Partener, a.LinieIndex, l.Denumire, a.Tip, a.Cheie,
                    structura.Denumire(a.Tip, a.Cheie), a.Procent, leiR, graficR, c.Document?.LeiPeUnitate, c.Grafic?.LeiPeUnitate,
                    c.Grafic?.Data ?? c.Document?.Data));
            }

            // Procentul alocat pe linie nu poate depăși 100 (semnalat; salvarea îl refuză).
            foreach (var g in aleDocumentului.GroupBy(a => a.LinieIndex).Where(g => g.Sum(a => a.Procent) > 100m + Toleranta))
                probleme.Add($"Linia {g.Key + 1} e alocată peste 100% ({g.Sum(a => a.Procent):0.##}%).");

            var procent = valoareLei == 0 ? 0 : Math.Round(alocatLei / valoareLei * 100m, 2, MidpointRounding.AwayFromZero);
            var alocabile = d.Linii.Where(l => l.Alocabila).ToList();
            var stare = alocabile.Count == 0 ? StareAlocare.FaraCost
                : aleDocumentului.Count == 0 ? StareAlocare.Nealocat
                : alocabile.All(l => aleDocumentului.Where(a => a.LinieIndex == l.Index).Sum(a => a.Procent) >= 100m - Toleranta) ? StareAlocare.Alocat
                : StareAlocare.Partial;
            stari.Add(new StareDocumentCost(d, Math.Round(valoareLei, 2, MidpointRounding.AwayFromZero), alocatLei, alocatGrafic, procent, stare,
                c.Document?.LeiPeUnitate, c.Grafic?.LeiPeUnitate, c.Grafic?.Data ?? c.Document?.Data, aleDocumentului,
                propuneri.GetValueOrDefault((d.Sursa, d.Id)) ?? [], probleme.Distinct().ToList()));
        }

        return new RezultatCosturiGrafic(
            peActivitate.ToDictionary(x => x.Key, x => x.Value.Cost(), StringComparer.Ordinal),
            peNod.ToDictionary(x => x.Key, x => x.Value.Cost(), StringComparer.Ordinal),
            total.Cost(), faraTinta.Cost(), valorizate, stari);
    }

    /// <summary>
    /// Validează alocările cerute pentru un document: liniile există și sunt cost direct, procentele sunt în (0, 100], pe linie
    /// însumează cel mult 100%, țintele există în actualizarea curentă, fără aceeași țintă de două ori pe o linie.
    /// </summary>
    public static Rezultat Valideaza(DocumentCost document, IReadOnlyList<AlocareCeruta> alocari, StructuraGrafic structura)
    {
        var linii = document.Linii.ToDictionary(l => l.Index);
        foreach (var a in alocari)
        {
            if (!linii.TryGetValue(a.LinieIndex, out var l))
                return Rezultat.Esuat(Eroare.Validare($"Linia {a.LinieIndex + 1} nu există pe documentul {document.Numar}."));
            if (!l.Alocabila)
                return Rezultat.Esuat(Eroare.Validare($"Linia {a.LinieIndex + 1} („{l.Denumire}”) nu e cost direct: {l.Nota ?? "nu se alocă"}."));
            if (a.Procent is <= 0 or > 100)
                return Rezultat.Esuat(Eroare.Validare($"Procentul pe linia {a.LinieIndex + 1} trebuie să fie între 0 și 100."));
            if (!structura.Exista(a.Tip, a.Cheie))
                return Rezultat.Esuat(Eroare.Validare($"{(a.Tip == TipTinta.Activitate ? "Activitatea" : "Nodul WBS")} {a.Cheie} nu e în actualizarea curentă a graficului."));
        }

        foreach (var g in alocari.GroupBy(a => a.LinieIndex))
        {
            if (g.Sum(a => a.Procent) > 100m + Toleranta)
                return Rezultat.Esuat(Eroare.Validare($"Linia {g.Key + 1} e alocată peste 100% ({g.Sum(a => a.Procent):0.##}%)."));
            if (g.GroupBy(a => (a.Tip, a.Cheie)).Any(x => x.Count() > 1))
                return Rezultat.Esuat(Eroare.Validare($"Linia {g.Key + 1} are aceeași țintă de mai multe ori."));
        }

        return Rezultat.Ok();
    }

    /// <summary>
    /// Alocarea „tot documentul pe o țintă”: fiecare linie de cost direct primește restul neacoperit până la 100%
    /// (liniile deja alocate integral rămân cum sunt; dacă ținta e deja pe linie, procentul ei crește).
    /// </summary>
    public static IReadOnlyList<AlocareCeruta> CompleteazaPe(DocumentCost document, IReadOnlyList<AlocareLinie> existente, TipTinta tip, string cheie)
    {
        var rezultat = existente.Select(a => new AlocareCeruta(a.LinieIndex, a.Tip, a.Cheie, a.Procent)).ToList();
        foreach (var l in document.Linii.Where(l => l.Alocabila))
        {
            var rest = 100m - rezultat.Where(a => a.LinieIndex == l.Index).Sum(a => a.Procent);
            if (rest <= Toleranta)
                continue;
            var aceeasi = rezultat.FindIndex(a => a.LinieIndex == l.Index && a.Tip == tip && a.Cheie == cheie);
            if (aceeasi >= 0)
                rezultat[aceeasi] = rezultat[aceeasi] with { Procent = rezultat[aceeasi].Procent + rest };
            else
                rezultat.Add(new AlocareCeruta(l.Index, tip, cheie, rest));
        }

        return rezultat;
    }

    /// <summary>
    /// Propuneri pentru liniile nealocate: aceeași grupare (articol de deviz, contract) alocată deja pe altă linie → aceleași
    /// ținte, cu aceleași procente (de pe cea mai recentă linie alocată).
    /// </summary>
    public static IReadOnlyDictionary<(SursaDocumentCost, Guid), IReadOnlyList<PropunereAlocare>> Propuneri(
        IReadOnlyList<DocumentCost> documente, IReadOnlyList<AlocareLinie> alocari)
    {
        var peLinie = alocari.ToLookup(a => (a.Sursa, a.DocumentId, a.LinieIndex));
        var model = new Dictionary<string, (DateOnly Data, string Document, List<AlocareLinie> Alocari)>(StringComparer.Ordinal);
        foreach (var d in documente)
        {
            foreach (var l in d.Linii.Where(l => l.Alocabila && l.Grupare is not null))
            {
                var a = peLinie[(d.Sursa, d.Id, l.Index)].ToList();
                if (a.Count > 0 && (!model.TryGetValue(l.Grupare!, out var m) || m.Data <= d.Data))
                    model[l.Grupare!] = (d.Data, d.Numar, a);
            }
        }

        var rezultat = new Dictionary<(SursaDocumentCost, Guid), IReadOnlyList<PropunereAlocare>>();
        foreach (var d in documente)
        {
            var p = d.Linii
                .Where(l => l.Alocabila && l.Grupare is not null && !peLinie[(d.Sursa, d.Id, l.Index)].Any() && model.ContainsKey(l.Grupare!))
                .SelectMany(l => model[l.Grupare!].Alocari.Select(a => new PropunereAlocare(l.Index, a.Tip, a.Cheie, a.Procent, $"ca pe {model[l.Grupare!].Document}")))
                .ToList();
            if (p.Count > 0)
                rezultat[(d.Sursa, d.Id)] = p;
        }

        return rezultat;
    }

    /// <summary>Valoarea în lei: în lei rămâne; în altă monedă, × cursul BNR al monedei la data documentului.</summary>
    public static decimal? Lei(decimal valoare, string moneda, CursuriDocument c) =>
        string.Equals(moneda, "RON", StringComparison.OrdinalIgnoreCase) ? valoare : c.Document is { } x ? valoare * x.LeiPeUnitate : null;

    /// <summary>Valoarea în moneda graficului: aceeași monedă = neschimbată; altfel prin lei, la cursul BNR al monedei graficului.</summary>
    public static decimal? InMonedaGrafic(decimal valoare, string moneda, string monedaGrafic, CursuriDocument c)
    {
        if (string.Equals(moneda, monedaGrafic, StringComparison.OrdinalIgnoreCase))
            return valoare;
        var lei = Lei(valoare, moneda, c);
        if (string.Equals(monedaGrafic, "RON", StringComparison.OrdinalIgnoreCase))
            return lei;
        return lei is null || c.Grafic is not { LeiPeUnitate: > 0 } g ? null : lei / g.LeiPeUnitate;
    }

    private static void Acumuleaza(Dictionary<string, Acumulator> d, string cheie, Guid document, decimal lei, decimal? grafic)
    {
        if (!d.TryGetValue(cheie, out var a))
            d[cheie] = a = new Acumulator();
        a.Adauga(document, lei, grafic);
    }

    private sealed class Acumulator
    {
        private readonly HashSet<Guid> _documente = [];
        private decimal _lei;
        private decimal _grafic;
        private int _faraCurs;

        public void Adauga(Guid document, decimal lei, decimal? grafic)
        {
            _documente.Add(document);
            _lei += lei;
            if (grafic is null)
                _faraCurs++;
            else
                _grafic += grafic.Value;
        }

        public CostTinta Cost() => new(_lei, _grafic, _documente.Count, _faraCurs);
    }
}
