using System.Text.RegularExpressions;

namespace ErpConstructii.Devize.Import.Isdp;

/// <summary>
/// Citește raportul ISDP „Deviz ofertă cu resurse” (exportat ca „extras de resurse” .xls).
///
/// Structura observată (vezi docs/import-isdp.md, „Formatul”):
///  - antet: OBIECTIV / OBIECTUL / STADIUL FIZIC / Beneficiar… (etichetă + valoare la dreapta);
///  - titlu: „Deviz Oferta - …”;
///  - rândul de antet de coloane: WBS, Simbol, Denumire, UM, Cantitate, Pret Material/Manopera/Utilaj/
///    Transport/Echipamente, PU, Valoare Material/Manopera/Utilaj/Transport, Total (+ „- lei -”);
///  - rânduri cu WBS ierarhic (0002.1.1 / .1 / .1 / .1): părintele = WBS fără ultimul segment;
///    fără simbol = grup (stadiu fizic, capitol); cu simbol sub grup = articol; cu simbol sub articol = resursă;
///  - recapitulația la final: T0 Total Valoare Directă, CAM %, T1, Cheltuieli indirecte %, T2,
///    Profit %, T3, Cota TVA, Valoare TVA, Valoare cu TVA.
/// </summary>
public static partial class ParserDevizResurse
{
    [GeneratedRegex(@"^[0-9A-Za-z]+(\.[0-9A-Za-z]+)*$")]
    private static partial Regex FormaWbs();

    private sealed record Coloane(
        int Wbs, int Simbol, int Denumire, int Um, int Cantitate,
        int[] Pret, int[] Valoare, int? Total, int? Pu);

    /// <summary>Parsează toate foile care au structura de deviz cu resurse (o foaie = un stadiu fizic).</summary>
    public static IReadOnlyList<RezultatIsdp<DevizIsdp>> ParseazaToate(IEnumerable<FoaieCalcul> foi) =>
        foi.Where(f => GasesteAntet(f) is not null).Select(Parseaza).ToList();

    public static RezultatIsdp<DevizIsdp> Parseaza(FoaieCalcul foaie)
    {
        var probleme = new List<ProblemaIsdp>();
        var antetColoane = GasesteAntet(foaie);
        if (antetColoane is not { } a)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, foaie.Nume, "Nu am găsit rândul de antet (coloanele WBS și Simbol)."));
            return new(null, probleme);
        }

        var (randAntet, col, moneda) = a;
        var antet = CitesteAntet(foaie, randAntet);
        var noduri = new List<NodIsdp>();
        var recap = new ConstructorRecapitulatie();

        for (var r = randAntet + 1; r < foaie.NrRanduri; r++)
        {
            var wbs = foaie.Text(r, col.Wbs);
            var denumire = foaie.Text(r, col.Denumire);

            if (wbs.Length > 0 && FormaWbs().IsMatch(wbs) && wbs.Contains('.', StringComparison.Ordinal))
            {
                noduri.Add(CitesteNod(foaie, r, col, wbs));
                continue;
            }

            if (denumire.Length > 0)
            {
                recap.Incearca(foaie, r, col, TextIsdp.Normalizeaza(denumire));
            }
        }

        if (noduri.Count == 0)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, foaie.Nume, "Devizul nu conține niciun rând cu WBS."));
            return new(null, probleme);
        }

        var radacini = ConstruiesteArbore(noduri, probleme);
        foreach (var radacina in radacini)
        {
            Clasifica(radacina, probleme);
        }

        return new(new DevizIsdp(foaie.Nume, antet, moneda, radacini, recap.Construieste()), probleme);
    }

    /// <summary>Găsește rândul de antet și coloanele, după text (nu după poziții fixe).</summary>
    private static (int Rand, Coloane Coloane, string Moneda)? GasesteAntet(FoaieCalcul f)
    {
        for (var r = 0; r < Math.Min(f.NrRanduri, 60); r++)
        {
            var texte = Enumerable.Range(0, f.NrColoane(r)).Select(c => TextIsdp.Normalizeaza(f.Text(r, c))).ToList();
            int Idx(Func<string, bool> p) => texte.FindIndex(t => p(t));
            int IdxStart(string prefix) => Idx(t => t.StartsWith(prefix, StringComparison.Ordinal));

            var wbs = Idx(t => t == "wbs");
            var simbol = Idx(t => t == "simbol");
            if (wbs < 0 || simbol < 0)
            {
                continue;
            }

            int[] pret = [IdxStart("pret material"), IdxStart("pret manopera"), IdxStart("pret utilaj"), IdxStart("pret transport"), IdxStart("pret echipamente")];
            int[] valoare = [IdxStart("valoare material"), IdxStart("valoare manopera"), IdxStart("valoare utilaj"), IdxStart("valoare transport"), IdxStart("valoare echipamente")];
            var total = IdxStart("total");
            var pu = IdxStart("pu");
            var coloane = new Coloane(wbs, simbol, Idx(t => t == "denumire"), Idx(t => t == "um"), Idx(t => t == "cantitate"),
                pret, valoare, total < 0 ? null : total, pu < 0 ? null : pu);

            var moneda = texte.Any(t => t.Contains("- euro -", StringComparison.Ordinal)) ? "EUR" : "RON";
            return (r, coloane, moneda);
        }

        return null;
    }

    private static AntetIsdp CitesteAntet(FoaieCalcul f, int panaLa)
    {
        string obiectiv = "", obiect = "", stadiu = "", beneficiar = "", titlu = "";
        for (var r = 0; r < panaLa; r++)
        {
            for (var c = 0; c < f.NrColoane(r); c++)
            {
                var t = TextIsdp.Normalizeaza(f.Text(r, c));
                if (t.Length == 0)
                {
                    continue;
                }

                string Valoare() => f.TextDupa(r, c).Trim('_', ' ');
                switch (t)
                {
                    case "obiectiv:": obiectiv = Valoare(); break;
                    case "obiectul:": obiect = Valoare(); break;
                    case "stadiul fizic:": stadiu = Valoare(); break;
                    case "beneficiar:": beneficiar = Valoare(); break;
                    default:
                        if (t.StartsWith("deviz oferta", StringComparison.Ordinal))
                        {
                            titlu = f.Text(r, c);
                        }

                        break;
                }
            }
        }

        return new AntetIsdp(obiectiv, obiect, stadiu, beneficiar, titlu);
    }

    private static NodIsdp CitesteNod(FoaieCalcul f, int r, Coloane col, string wbs)
    {
        decimal N(int c) => c < 0 ? 0 : f.Numar(r, c) ?? 0;
        ValoriIsdp V(int[] cols) => new(N(cols[0]), N(cols[1]), N(cols[2]), N(cols[3]), N(cols[4]));

        var pret = V(col.Pret);
        var valoare = V(col.Valoare);
        decimal? total = col.Total is { } t ? f.Numar(r, t) : null;

        // ISDP nu exportă coloana „Valoare echipamente”: o deducem din Total − celelalte patru.
        if (col.Valoare[4] < 0 && total is { } tot && pret.Echipament != 0)
        {
            valoare = valoare with { Echipament = tot - (valoare.Material + valoare.Manopera + valoare.Utilaj + valoare.Transport) };
        }

        return new NodIsdp
        {
            Wbs = wbs,
            Simbol = f.Text(r, col.Simbol),
            Denumire = f.Text(r, col.Denumire),
            UnitateMasura = col.Um < 0 ? string.Empty : f.Text(r, col.Um),
            Cantitate = N(col.Cantitate),
            PretUnitar = pret,
            Valoare = valoare,
            TotalDeclarat = total,
            Rand = r + 1,
        };
    }

    private static List<NodIsdp> ConstruiesteArbore(List<NodIsdp> noduri, List<ProblemaIsdp> probleme)
    {
        var dupaWbs = new Dictionary<string, NodIsdp>(StringComparer.Ordinal);
        var radacini = new List<NodIsdp>();
        foreach (var n in noduri)
        {
            if (!dupaWbs.TryAdd(n.Wbs, n))
            {
                probleme.Add(new(SeveritateIsdp.Eroare, $"rând {n.Rand}", $"WBS duplicat: {n.Wbs}."));
                continue;
            }

            var parinteWbs = n.Wbs[..n.Wbs.LastIndexOf('.')];
            if (dupaWbs.TryGetValue(parinteWbs, out var parinte))
            {
                parinte.AdaugaCopil(n);
            }
            else
            {
                radacini.Add(n);
            }
        }

        return radacini;
    }

    /// <summary>Stabilește tipul fiecărui nod din poziția în arbore și din simbol.</summary>
    private static void Clasifica(NodIsdp nod, List<ProblemaIsdp> probleme)
    {
        var parinte = nod.Parinte;
        if (nod.Simbol.Length == 0)
        {
            nod.Tip = TipNodIsdp.Grup;
            if (parinte is { Tip: not TipNodIsdp.Grup })
            {
                probleme.Add(new(SeveritateIsdp.Eroare, Loc(nod), "Rând fără simbol sub un articol."));
            }
        }
        else if (parinte is null || parinte.Tip == TipNodIsdp.Grup)
        {
            nod.Tip = TipNodIsdp.Articol;
            ClasificaArticol(nod);
        }
        else if (parinte.Tip == TipNodIsdp.Articol)
        {
            nod.Tip = TipNodIsdp.Resursa;
            ClasificaResursa(nod, parinte, probleme);
        }
        else
        {
            nod.Tip = TipNodIsdp.Resursa;
            probleme.Add(new(SeveritateIsdp.Eroare, Loc(nod), "Resursă sub altă resursă: structură neașteptată."));
        }

        foreach (var copil in nod.Copii)
        {
            Clasifica(copil, probleme);
        }

        if (nod.Tip == TipNodIsdp.Articol && nod.Copii.Count == 0)
        {
            nod.TipArticol = TipArticolIsdp.ResursaDirecta;
        }
    }

    private static void ClasificaArticol(NodIsdp a)
    {
        var parti = a.Simbol.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parti.Length > 1 && TextIsdp.Normalizeaza(parti[^1]) == "asim")
        {
            a.TipArticol = TipArticolIsdp.Asimilat;
            a.SimbolBaza = string.Join(' ', parti[..^1]);
        }
        else
        {
            a.TipArticol = TipArticolIsdp.Normat;
            a.SimbolBaza = a.Simbol;
        }
    }

    private static void ClasificaResursa(NodIsdp r, NodIsdp articol, List<ProblemaIsdp> probleme)
    {
        var cuPret = ValoriIsdp.Tipuri.Where(t => r.PretUnitar[t] != 0).ToList();
        switch (cuPret.Count)
        {
            case 1:
                r.TipResursa = cuPret[0];
                break;
            case 0:
                // Fără preț (ex. apă industrială cu preț 0): tipul se deduce; maparea în nomenclator îl confirmă.
                r.TipResursa = TextIsdp.UnitateMasura(r.UnitateMasura) == "ora" ? TipResursaIsdp.Manopera : TipResursaIsdp.Material;
                r.TipResursaDedus = true;
                probleme.Add(new(SeveritateIsdp.Avertisment, Loc(r),
                    $"Resursa {r.Simbol} are toate prețurile zero; tipul a fost dedus ca {r.TipResursa} din UM „{r.UnitateMasura}”."));
                break;
            default:
                r.TipResursa = cuPret.MaxBy(t => r.PretUnitar[t]);
                probleme.Add(new(SeveritateIsdp.Avertisment, Loc(r),
                    $"Resursa {r.Simbol} are preț pe mai multe coloane ({string.Join(", ", cuPret)}); s-a ales {r.TipResursa}."));
                break;
        }

        r.ConsumUnitar = articol.Cantitate == 0 ? 0 : r.Cantitate / articol.Cantitate;
    }

    internal static string Loc(NodIsdp n) => $"rând {n.Rand}, WBS {n.Wbs} ({n.Simbol})";

    /// <summary>Strânge rândurile de recapitulație după eticheta din coloana Denumire.</summary>
    private sealed class ConstructorRecapitulatie
    {
        private TotalRecapitulatie? _t0, _t1, _t2, _t3;
        private decimal? _cam, _ind, _profit, _cotaTva, _valTva, _cuTva;

        public void Incearca(FoaieCalcul f, int r, Coloane col, string eticheta)
        {
            TotalRecapitulatie T() => new(
                f.Numar(r, col.Pret[0]) ?? 0, f.Numar(r, col.Pret[1]) ?? 0, f.Numar(r, col.Pret[2]) ?? 0, f.Numar(r, col.Pret[3]) ?? 0,
                f.UltimulNumar(r) ?? 0);
            decimal? Procent() => f.Numar(r, col.Cantitate);

            if (eticheta.StartsWith("total valoare directa", StringComparison.Ordinal)) _t0 = T();
            else if (eticheta.StartsWith("total inclusiv cheltuieli directe", StringComparison.Ordinal)) _t1 = T();
            else if (eticheta.StartsWith("total inclusiv cheltuieli indirecte", StringComparison.Ordinal)) _t2 = T();
            else if (eticheta.StartsWith("total inclusiv beneficiu", StringComparison.Ordinal)
                     || eticheta.StartsWith("total inclusiv profit", StringComparison.Ordinal)) _t3 = T();
            else if (eticheta == "cam") _cam = Procent();
            else if (eticheta.StartsWith("cheltuieli indirecte", StringComparison.Ordinal)) _ind = Procent();
            else if (eticheta == "profit" || eticheta == "beneficiu") _profit = Procent();
            else if (eticheta.StartsWith("cota tva", StringComparison.Ordinal)) _cotaTva = f.UltimulNumar(r);
            else if (eticheta.StartsWith("valoare tva", StringComparison.Ordinal)) _valTva = f.UltimulNumar(r);
            else if (eticheta.StartsWith("valoare cu tva", StringComparison.Ordinal)) _cuTva = f.UltimulNumar(r);
        }

        public RecapitulatieIsdp? Construieste() =>
            _t0 is null && _t3 is null
                ? null
                : new RecapitulatieIsdp(_t0, _cam, _t1, _ind, _t2, _profit, _t3, _cotaTva, _valTva, _cuTva);
    }
}
