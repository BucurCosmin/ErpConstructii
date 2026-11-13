using ErpConstructii.Devize.Calcul;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Devize;

/// <summary>Un articol al devizului de referință, cu bugetul lui (cost direct pe resurse, calculat cu rotunjirea versiunii).</summary>
public sealed record ArticolBugetDeviz(Guid CheieStabila, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, string Capitol, ValoriResurse Buget);

/// <param name="Cheie">Null pentru „cheltuieli generale” (costuri fără articol).</param>
/// <param name="Realizat">Costurile reale ale articolului, pe resurse (deocamdată materialele consumate).</param>
/// <param name="Documente">Câte documente au adus costuri pe articol.</param>
public sealed record RandBuget(
    Guid? Cheie, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, string Capitol, ValoriResurse Buget, ValoriResurse Realizat,
    int Documente, bool InDeviz);

/// <param name="BugetDirect">Costul direct al devizului de referință (fără CAM, indirecte, profit, TVA).</param>
/// <param name="ConsumPeArticole">Costurile pe articolele devizului (materialele consumate pe ele).</param>
/// <param name="CheltuieliGenerale">Consumuri fără articol de deviz.</param>
/// <param name="ServiciiNerepartizate">Facturi de servicii / lucrări pe proiect, încă nerepartizate pe articole.</param>
/// <param name="MaterialeCumparate">Facturi cu materiale de stoc pe proiect (cumpărate; devin cost la consum).</param>
/// <param name="MaterialeNeconsumate">Cumpărate − consumate (aproximativ: consumul e la CMP, achiziția la prețul facturii).</param>
/// <param name="CosturiInregistrate">Consumuri (pe articole + generale) + servicii: costul realizat până acum.</param>
/// <param name="Facturat">Materiale cumpărate + servicii: ce s-a facturat pe proiect.</param>
public sealed record RezumatBuget(
    decimal BugetDirect, ValoriResurse BugetPeResurse, ValoriResurse RealizatPeResurse, decimal ConsumPeArticole, decimal CheltuieliGenerale,
    decimal ServiciiNerepartizate, decimal MaterialeCumparate, decimal MaterialeNeconsumate, decimal CosturiInregistrate, decimal Facturat,
    decimal? ProcentCosturi);

public sealed record BugetCalculat(IReadOnlyList<RandBuget> Articole, RezumatBuget Rezumat);

/// <summary>
/// Bugetul proiectului față de costurile reale (REQ-BUG): fiecare articol al devizului de referință cu bugetul lui pe resurse
/// și costurile realizate pe el; costurile fără articol și cele pe articole care nu mai sunt în deviz, separat; rezumatul
/// pe proiect (consum, servicii, materiale cumpărate și neconsumate). Funcție pură.
/// </summary>
public static class CalculBuget
{
    public static BugetCalculat Calculeaza(IReadOnlyList<ArticolBugetDeviz> deviz, IReadOnlyList<CostRealizat> costuri)
    {
        var consumuri = costuri.Where(c => c.Natura == NaturaCost.Consum).ToList();
        var peArticol = consumuri.Where(c => c.ArticolDevizCheie is not null).GroupBy(c => c.ArticolDevizCheie!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var randuri = deviz.Select(a => Rand(a.CheieStabila, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.Capitol, a.Buget,
            peArticol.GetValueOrDefault(a.CheieStabila) ?? [], true)).ToList();

        var cunoscute = deviz.Select(a => a.CheieStabila).ToHashSet();
        foreach (var (cheie, lista) in peArticol.Where(x => !cunoscute.Contains(x.Key)))
            randuri.Add(Rand(cheie, "—", "Articol care nu mai e în devizul de referință", string.Empty, 0, string.Empty, ValoriResurse.Zero, lista, false));
        var generale = consumuri.Where(c => c.ArticolDevizCheie is null).ToList();
        if (generale.Count > 0)
            randuri.Add(Rand(null, "—", "Cheltuieli generale de șantier (fără articol de deviz)", string.Empty, 0, string.Empty, ValoriResurse.Zero, generale, false));

        var bugetPeResurse = deviz.Aggregate(ValoriResurse.Zero, (s, a) => s + a.Buget);
        var realizatPeResurse = randuri.Aggregate(ValoriResurse.Zero, (s, r) => s + r.Realizat);
        var consumPeArticole = randuri.Where(r => r.Cheie is not null).Sum(r => r.Realizat.Total);
        var cheltuieliGenerale = generale.Sum(c => c.Valoare);
        var servicii = costuri.Where(c => c.Natura == NaturaCost.Serviciu).Sum(c => c.Valoare);
        var cumparate = costuri.Where(c => c.Natura == NaturaCost.AchizitieMateriale).Sum(c => c.Valoare);
        var consumMateriale = consumuri.Where(c => (c.Componenta ?? TipResursa.Material) is TipResursa.Material or TipResursa.Echipament).Sum(c => c.Valoare);
        var inregistrate = consumuri.Sum(c => c.Valoare) + servicii;
        var bugetDirect = Rotunjire.Lei.Aplica(bugetPeResurse.Total);

        return new BugetCalculat(randuri, new RezumatBuget(
            bugetDirect, bugetPeResurse, realizatPeResurse, Rotunjire.Lei.Aplica(consumPeArticole), Rotunjire.Lei.Aplica(cheltuieliGenerale),
            Rotunjire.Lei.Aplica(servicii), Rotunjire.Lei.Aplica(cumparate), Rotunjire.Lei.Aplica(Math.Max(0, cumparate - consumMateriale)),
            Rotunjire.Lei.Aplica(inregistrate), Rotunjire.Lei.Aplica(cumparate + servicii),
            bugetDirect > 0 ? Math.Round(inregistrate / bugetDirect * 100, 1, MidpointRounding.AwayFromZero) : null));
    }

    private static RandBuget Rand(
        Guid? cheie, string simbol, string denumire, string um, decimal cantitate, string capitol, ValoriResurse buget, List<CostRealizat> costuri, bool inDeviz)
    {
        decimal Pe(TipResursa t) => Rotunjire.Lei.Aplica(costuri.Where(c => (c.Componenta ?? TipResursa.Material) == t).Sum(c => c.Valoare));
        var realizat = new ValoriResurse(Pe(TipResursa.Material), Pe(TipResursa.Manopera), Pe(TipResursa.Utilaj), Pe(TipResursa.Transport), Pe(TipResursa.Echipament));
        return new RandBuget(cheie, simbol, denumire, um, cantitate, capitol, buget, realizat, costuri.Select(c => c.Document).Distinct().Count(), inDeviz);
    }
}
