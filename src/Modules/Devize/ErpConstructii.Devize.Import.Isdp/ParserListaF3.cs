using System.Text.RegularExpressions;

namespace ErpConstructii.Devize.Import.Isdp;

/// <summary>
/// Citește formularul „F3 - LISTA cu cantități de lucrări pe categorii de lucrări”.
///
/// Structura observată: antet (OBIECTIV / OBIECTUL / STADIUL FIZIC), moneda („- euro -”), rândul de antet
/// (Nr. | Capitolul de lucrări | U.M. | Cantitatea | Prețul unitar | TOTALUL), apoi:
///  - capitol:  „1”   + denumire;
///  - articol:  „1.1” + denumire, UM, cantitate, preț unitar, total;
///  - sub fiecare articol: rânduri „material: / manopera: / utilaj: / transport:” cu preț unitar și total;
///  - rândurile repetate la schimbarea de pagină (numerotarea coloanelor 0..5, „Pag N”, „STADIUL FIZIC”) se ignoră;
///  - „TOTAL GENERAL (fara TVA)”.
/// </summary>
public static partial class ParserListaF3
{
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex NumarCapitol();

    [GeneratedRegex(@"^\d+(\.\d+)+$")]
    private static partial Regex NumarArticol();

    private static readonly Dictionary<string, TipResursaIsdp> Componente = new(StringComparer.Ordinal)
    {
        ["material:"] = TipResursaIsdp.Material,
        ["manopera:"] = TipResursaIsdp.Manopera,
        ["utilaj:"] = TipResursaIsdp.Utilaj,
        ["transport:"] = TipResursaIsdp.Transport,
        ["echipamente:"] = TipResursaIsdp.Echipament,
        ["echipament:"] = TipResursaIsdp.Echipament,
    };

    public static RezultatIsdp<ListaF3> Parseaza(FoaieCalcul f)
    {
        var probleme = new List<ProblemaIsdp>();
        int rAntet = -1, cNr = -1, cDen = -1, cUm = -1, cCant = -1, cPu = -1, cTot = -1;
        var moneda = "RON";
        string obiectiv = "", obiect = "", stadiu = "", titlu = "";

        for (var r = 0; r < f.NrRanduri && rAntet < 0; r++)
        {
            for (var c = 0; c < f.NrColoane(r); c++)
            {
                var t = TextIsdp.Normalizeaza(f.Text(r, c));
                if (t.Length == 0)
                {
                    continue;
                }

                if (t.Contains("- euro -", StringComparison.Ordinal)) moneda = "EUR";
                if (t == "obiectiv:") obiectiv = f.TextDupa(r, c);
                else if (t == "obiectul:") obiect = f.TextDupa(r, c);
                else if (t == "stadiul fizic:") stadiu = f.TextDupa(r, c);
                else if (t.StartsWith("f3 - lista", StringComparison.Ordinal)) titlu = f.Text(r, c);
                else if (t == "nr." || t == "nr") cNr = c;
                else if (t.StartsWith("capitolul de lucrari", StringComparison.Ordinal)) { cDen = c; rAntet = r; }
                else if (t == "u.m." || t == "um") cUm = c;
                else if (t.StartsWith("cantitatea", StringComparison.Ordinal)) cCant = c;
                else if (t.StartsWith("pretul unitar", StringComparison.Ordinal)) cPu = c;
                else if (t.StartsWith("totalul", StringComparison.Ordinal)) cTot = c;
            }
        }

        if (rAntet < 0 || cNr < 0 || cUm < 0 || cCant < 0)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, f.Nume, "Nu am găsit antetul formularului F3 (Nr., Capitolul de lucrări, U.M., Cantitatea)."));
            return new(null, probleme);
        }

        var capitole = new List<(string Nr, string Den, List<ArticolF3> Art)>();
        ArticolF3? curent = null;
        Dictionary<TipResursaIsdp, (decimal, decimal)>? comp = null;
        decimal? totalGeneral = null;

        void InchideArticol()
        {
            if (curent is not null)
            {
                capitole[^1].Art.Add(curent with { Componente = comp! });
            }

            curent = null;
            comp = null;
        }

        for (var r = rAntet + 1; r < f.NrRanduri; r++)
        {
            var nr = f.Text(r, cNr);
            var den = f.Text(r, cDen);
            var eticheta = TextIsdp.Normalizeaza(f.Text(r, cCant));

            if (TextIsdp.Normalizeaza(nr).StartsWith("total general", StringComparison.Ordinal))
            {
                InchideArticol();
                totalGeneral = f.UltimulNumar(r);
                continue;
            }

            if (curent is not null && Componente.TryGetValue(eticheta, out var tip))
            {
                comp![tip] = (cPu < 0 ? 0 : f.Numar(r, cPu) ?? 0, cTot < 0 ? 0 : f.Numar(r, cTot) ?? 0);
                continue;
            }

            // Rândul cu numerotarea coloanelor (0 | 1 | 2 | 3 | 4 | 5 = 3 x 4) se repetă pe fiecare pagină.
            if (nr == "0")
            {
                continue;
            }

            if (NumarArticol().IsMatch(nr))
            {
                InchideArticol();
                if (capitole.Count == 0)
                {
                    capitole.Add(("", "", []));
                    probleme.Add(new(SeveritateIsdp.Avertisment, $"rând {r + 1}", "Articol înaintea oricărui capitol."));
                }

                curent = new ArticolF3(nr, den, f.Text(r, cUm), f.Numar(r, cCant),
                    cPu < 0 ? null : f.Numar(r, cPu), cTot < 0 ? null : f.Numar(r, cTot),
                    new Dictionary<TipResursaIsdp, (decimal, decimal)>(), r + 1);
                comp = [];
                continue;
            }

            if (NumarCapitol().IsMatch(nr) && den.Length > 0)
            {
                InchideArticol();
                capitole.Add((nr, den, []));
            }
        }

        InchideArticol();

        if (capitole.Count == 0)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, f.Nume, "Lista F3 nu conține niciun capitol."));
            return new(null, probleme);
        }

        var lista = new ListaF3(
            new AntetIsdp(obiectiv, obiect, stadiu, string.Empty, titlu),
            moneda,
            capitole.Select(c => new CapitolF3(c.Nr, c.Den, c.Art)).ToList(),
            totalGeneral);

        VerificaSume(lista, probleme);
        return new(lista, probleme);
    }

    /// <summary>Totalul articolului = suma componentelor; totalul general = suma articolelor.</summary>
    private static void VerificaSume(ListaF3 l, List<ProblemaIsdp> probleme)
    {
        foreach (var a in l.Articole().Where(a => a.Total is not null && a.Componente.Count > 0))
        {
            var suma = a.Componente.Values.Sum(v => v.Total);
            if (Math.Abs(suma - a.Total!.Value) > 0.01m)
            {
                probleme.Add(new(SeveritateIsdp.Eroare, $"F3 {a.Numar}", $"Totalul {a.Total} diferă de suma componentelor {suma}."));
            }
        }

        if (l.TotalGeneral is { } tg)
        {
            var suma = l.Articole().Sum(a => a.Total ?? 0);
            if (Math.Abs(suma - tg) > 0.01m)
            {
                probleme.Add(new(SeveritateIsdp.Eroare, "F3 total general", $"Totalul general {tg} diferă de suma articolelor {suma}."));
            }
        }
    }
}
