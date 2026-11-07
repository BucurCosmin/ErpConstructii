using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

/// <summary>O mișcare de stoc, așa cum intră în calculul provenienței (în ordinea în care s-au înregistrat operațiile).</summary>
public sealed record MiscareTrasabilitate(
    Guid GestiuneId, Guid ArticolId, TipMiscare Tip, decimal Cantitate, decimal Valoare, Guid DocumentId, string DocumentNumar, DateOnly Data);

/// <summary>
/// O cantitate din stocul curent al unei gestiuni, cu documentul prin care a intrat în firmă: NIR-ul (deci factura) sau
/// plusul de inventar. Materialul transferat în șantier sau returnat în depozit își păstrează originea.
/// </summary>
/// <param name="PretUnitar">Prețul de intrare (din NIR / inventar), nu CMP-ul gestiunii.</param>
public sealed record LotStoc(
    Guid GestiuneId, Guid ArticolId, TipMiscare TipOrigine, Guid DocumentOrigineId, string DocumentOrigineNumar, DateOnly DataIntrare,
    decimal Cantitate, decimal PretUnitar);

/// <summary>
/// Proveniența stocului (pe ce NIR / factură a intrat ce e acum în gestiune). Evaluarea rămâne la CMP (<see cref="CalculStoc"/>);
/// pentru trasabilitate, ieșirile se atribuie FIFO (iese întâi ce a intrat primul), deci în stoc rămân cele mai recente intrări.
/// Excepția: stornarea unui NIR scoate exact cantitatea acelui NIR (cât mai e din ea în gestiune).
/// </summary>
public static class TrasabilitateStoc
{
    /// <summary>Prețul unitar de intrare, cu 4 zecimale (valoarea / cantitatea mișcării).</summary>
    private static readonly Rotunjire PretIntrare = new(4, MidpointRounding.AwayFromZero);

    private sealed class Bucata(TipMiscare tip, Guid documentId, string documentNumar, DateOnly data, decimal cantitate, decimal pret)
    {
        public TipMiscare Tip { get; } = tip;
        public Guid DocumentId { get; } = documentId;
        public string DocumentNumar { get; } = documentNumar;
        public DateOnly Data { get; } = data;
        public decimal Cantitate { get; set; } = cantitate;
        public decimal Pret { get; } = pret;

        public Bucata Parte(decimal cantitate) => new(Tip, DocumentId, DocumentNumar, Data, cantitate, Pret);
    }

    /// <param name="miscari">Mișcările în ordinea înregistrării (data creării), nu a datei documentului.</param>
    /// <param name="nirStornat">Pentru fiecare stornare: Id-ul NIR-ului stornat.</param>
    public static IReadOnlyList<LotStoc> Calculeaza(IEnumerable<MiscareTrasabilitate> miscari, IReadOnlyDictionary<Guid, Guid> nirStornat)
    {
        var stoc = new Dictionary<(Guid Gestiune, Guid Articol), List<Bucata>>();
        // Ce a scos un transfer din gestiunea sursă, pe articol: intră cu aceeași origine în gestiunea destinație.
        var inTranzit = new Dictionary<(Guid Document, Guid Articol), List<Bucata>>();

        List<Bucata> Coada(Guid gestiune, Guid articol)
        {
            if (!stoc.TryGetValue((gestiune, articol), out var c))
                stoc[(gestiune, articol)] = c = [];
            return c;
        }

        // Un document (transfer) are ieșirea și intrarea: ieșirea se procesează prima.
        var documente = new List<(Guid Id, List<MiscareTrasabilitate> Miscari)>();
        foreach (var m in miscari)
        {
            if (documente.Count > 0 && documente[^1].Id == m.DocumentId)
                documente[^1].Miscari.Add(m);
            else
                documente.Add((m.DocumentId, [m]));
        }

        foreach (var (_, grup) in documente)
        {
            foreach (var m in grup.Where(x => x.Cantitate < 0))
            {
                var coada = Coada(m.GestiuneId, m.ArticolId);
                var scoase = m.Tip == TipMiscare.IesireStornoNir && nirStornat.TryGetValue(m.DocumentId, out var nir)
                    ? Scoate(coada, -m.Cantitate, b => b.DocumentId == nir)
                    : Scoate(coada, -m.Cantitate, _ => true);
                if (m.Tip == TipMiscare.IesireTransfer)
                {
                    if (!inTranzit.TryGetValue((m.DocumentId, m.ArticolId), out var t))
                        inTranzit[(m.DocumentId, m.ArticolId)] = t = [];
                    t.AddRange(scoase);
                }
            }

            foreach (var m in grup.Where(x => x.Cantitate > 0))
            {
                var coada = Coada(m.GestiuneId, m.ArticolId);
                var rest = m.Cantitate;
                if (m.Tip == TipMiscare.IntrareTransfer && inTranzit.Remove((m.DocumentId, m.ArticolId), out var venite))
                {
                    foreach (var b in venite)
                    {
                        var cat = Math.Min(rest, b.Cantitate);
                        if (cat <= 0) break;
                        coada.Add(b.Parte(cat));
                        rest -= cat;
                    }
                }

                // Intrarea propriu-zisă (NIR, plus de inventar) sau un transfer fără origine cunoscută: originea e documentul.
                if (rest > 0)
                    coada.Add(new Bucata(m.Tip, m.DocumentId, m.DocumentNumar, m.Data, rest, PretIntrare.Aplica(m.Valoare / m.Cantitate)));
            }
        }

        return stoc
            .SelectMany(x => x.Value.Where(b => b.Cantitate > 0).Select(b =>
                new LotStoc(x.Key.Gestiune, x.Key.Articol, b.Tip, b.DocumentId, b.DocumentNumar, b.Data, b.Cantitate, b.Pret)))
            .ToList();
    }

    /// <summary>Scoate cantitatea întâi din bucățile care îndeplinesc condiția (FIFO), apoi FIFO din rest.</summary>
    private static List<Bucata> Scoate(List<Bucata> coada, decimal cantitate, Func<Bucata, bool> intai)
    {
        var scoase = new List<Bucata>();
        foreach (var conditie in new[] { intai, _ => true })
        {
            for (var i = 0; i < coada.Count && cantitate > 0; i++)
            {
                var b = coada[i];
                if (b.Cantitate <= 0 || !conditie(b)) continue;
                var cat = Math.Min(cantitate, b.Cantitate);
                scoase.Add(b.Parte(cat));
                b.Cantitate -= cat;
                cantitate -= cat;
            }
        }

        coada.RemoveAll(b => b.Cantitate <= 0);
        return scoase;
    }
}
