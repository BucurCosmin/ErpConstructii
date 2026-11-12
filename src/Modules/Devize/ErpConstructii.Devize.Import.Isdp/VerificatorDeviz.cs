namespace ErpConstructii.Devize.Import.Isdp;

/// <summary>Recapitulația recalculată din articole (fără nicio rotunjire intermediară, ca ISDP).</summary>
public sealed record RecapitulatieCalculata(
    ValoriIsdp T0,
    decimal Cam,
    ValoriIsdp T1,
    ValoriIsdp Indirecte,
    ValoriIsdp T2,
    ValoriIsdp Profit,
    ValoriIsdp T3,
    decimal Tva,
    decimal TotalCuTva);

/// <summary>
/// Verifică coerența devizului citit și recalculează recapitulația.
///
/// Regula de calcul ISDP, confirmată pe fișierul real (diferență 0):
///   T0 = Σ valori articole pe componente
///   CAM = T0.manoperă × %CAM                          (se adaugă la manoperă)
///   T1 = T0 + CAM
///   Indirecte = T1 × %ind   (pe FIECARE componentă)
///   T2 = T1 + Indirecte
///   Profit = T2 × %profit   (pe fiecare componentă)
///   T3 = T2 + Profit
///   TVA = T3 × cota TVA;  Total = T3 + TVA
/// ISDP NU rotunjește nimic pe parcurs; doar afișarea e rotunjită.
/// </summary>
public static class VerificatorDeviz
{
    /// <summary>Toleranța la comparații (lei). Valorile ISDP sunt nerotunjite, deci diferențele reale sunt ~1e-9.</summary>
    public const decimal Toleranta = 0.005m;

    public static (RecapitulatieCalculata? Recalcul, IReadOnlyList<ProblemaIsdp> Probleme) Verifica(DevizIsdp deviz)
    {
        var probleme = new List<ProblemaIsdp>();

        foreach (var a in deviz.Articole())
        {
            VerificaArticol(a, probleme);
        }

        var t0 = deviz.Articole().Aggregate(ValoriIsdp.Zero, (s, a) => s + a.Valoare);

        // Totalul declarat pe grupuri (ex. rândul stadiului fizic are Total = suma articolelor lui).
        foreach (var g in deviz.Toate().Where(n => n.Tip == TipNodIsdp.Grup && n.TotalDeclarat is not null))
        {
            var suma = g.Descendenti().Where(n => n.Tip == TipNodIsdp.Articol).Sum(n => n.Valoare.Total);
            Compara(probleme, ParserDevizResurse.Loc(g), "Total grup", suma, g.TotalDeclarat!.Value);
        }

        if (deviz.Recapitulatie is not { } rd)
        {
            probleme.Add(new(SeveritateIsdp.Avertisment, "recapitulație", "Fișierul nu conține recapitulația; cotele vor veni din setările firmei."));
            return (null, probleme);
        }

        if (rd.ProcentCam is null || rd.ProcentIndirecte is null || rd.ProcentProfit is null)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, "recapitulație", "Lipsește unul dintre procentele CAM / cheltuieli indirecte / profit."));
            return (null, probleme);
        }

        var calc = Calculeaza(t0, rd.ProcentCam.Value, rd.ProcentIndirecte.Value, rd.ProcentProfit.Value, rd.CotaTva ?? 0);

        void ComparaT(string nume, TotalRecapitulatie? declarat, ValoriIsdp calculat)
        {
            if (declarat is null)
            {
                return;
            }

            Compara(probleme, "recapitulație", $"{nume} material", calculat.Material, declarat.Material);
            Compara(probleme, "recapitulație", $"{nume} manoperă", calculat.Manopera, declarat.Manopera);
            Compara(probleme, "recapitulație", $"{nume} utilaj", calculat.Utilaj, declarat.Utilaj);
            Compara(probleme, "recapitulație", $"{nume} transport", calculat.Transport, declarat.Transport);
            Compara(probleme, "recapitulație", $"{nume} total", calculat.Total, declarat.Total);
        }

        ComparaT("T0", rd.T0ValoareDirecta, calc.T0);
        ComparaT("T1", rd.T1CuCam, calc.T1);
        ComparaT("T2", rd.T2CuIndirecte, calc.T2);
        ComparaT("T3", rd.T3CuProfit, calc.T3);
        if (rd.ValoareTva is { } tva) Compara(probleme, "recapitulație", "TVA", calc.Tva, tva);
        if (rd.ValoareCuTva is { } tot) Compara(probleme, "recapitulație", "Total cu TVA", calc.TotalCuTva, tot);

        return (calc, probleme);
    }

    /// <summary>Procentele în procente (2,25 = 2,25%), ca în ISDP.</summary>
    public static RecapitulatieCalculata Calculeaza(ValoriIsdp t0, decimal procentCam, decimal procentIndirecte, decimal procentProfit, decimal cotaTva)
    {
        var cam = t0.Manopera * procentCam / 100m;
        var t1 = t0 with { Manopera = t0.Manopera + cam };
        var ind = t1 * (procentIndirecte / 100m);
        var t2 = t1 + ind;
        var profit = t2 * (procentProfit / 100m);
        var t3 = t2 + profit;
        var tva = t3.Total * cotaTva / 100m;
        return new RecapitulatieCalculata(t0, cam, t1, ind, t2, profit, t3, tva, t3.Total + tva);
    }

    private static void VerificaArticol(NodIsdp a, List<ProblemaIsdp> probleme)
    {
        var loc = ParserDevizResurse.Loc(a);
        if (a.Cantitate <= 0)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, loc, $"Cantitate invalidă: {a.Cantitate}."));
        }

        if (a.UnitateMasura.Length == 0)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, loc, "Lipsește unitatea de măsură."));
        }

        foreach (var t in ValoriIsdp.Tipuri)
        {
            Compara(probleme, loc, $"Valoare {t} = cantitate × preț", a.Cantitate * a.PretUnitar[t], a.Valoare[t]);
        }

        if (a.Copii.Count == 0)
        {
            return;
        }

        var dinResurse = a.Copii.Aggregate(ValoriIsdp.Zero, (s, r) => s + r.Valoare);
        foreach (var t in ValoriIsdp.Tipuri)
        {
            Compara(probleme, loc, $"Valoare {t} = suma resurselor", dinResurse[t], a.Valoare[t]);
        }

        foreach (var r in a.Copii)
        {
            Compara(probleme, ParserDevizResurse.Loc(r), "Valoare resursă = cantitate × preț", r.Cantitate * r.PretUnitar[r.TipResursa], r.Valoare[r.TipResursa]);
        }
    }

    private static void Compara(List<ProblemaIsdp> probleme, string loc, string ce, decimal calculat, decimal declarat)
    {
        if (Math.Abs(calculat - declarat) > Toleranta)
        {
            probleme.Add(new(SeveritateIsdp.Eroare, loc, $"{ce}: calculat {calculat:0.######}, în fișier {declarat:0.######} (diferență {calculat - declarat:0.######})."));
        }
    }
}
