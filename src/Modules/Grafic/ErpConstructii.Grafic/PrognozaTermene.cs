using ErpConstructii.SharedKernel;

namespace ErpConstructii.Grafic;

public enum MetodaDurataRamasa
{
    /// <summary>Durata rămasă = durata planificată × (1 − progres).</summary>
    Planificata,

    /// <summary>Durata rămasă se extrapolează din ritmul realizat: zile scurse / progres × (1 − progres).</summary>
    Ritm,
}

/// <summary>Starea unei activități la data de raportare, intrare pentru prognoză.</summary>
public sealed record StareActivitate(
    string Cod,
    DateOnly StartPlanificat,
    int DurataZile,
    IReadOnlyList<Dependenta> Dependente,
    decimal Progres,
    DateOnly? StartReal,
    DateOnly? SfarsitReal);

public sealed record TermenPrognozat(string Cod, DateOnly Start, DateOnly Sfarsit);

/// <summary>
/// Prognoza termenelor prin parcurgere înainte pe rețeaua de dependențe (REQ-GRF-03, pasul 4).
///
/// Reguli:
///  - activitate terminată: rămâne pe datele reale;
///  - activitate începută: sfârșit = data raportării + durata rămasă;
///  - activitate neîncepută: start = max(start planificat, data raportării, constrângerile predecesorilor);
///    FS: sfârșit pred + decalaj · SS: start pred + decalaj · FF: sfârșit pred + decalaj − durată ·
///    SF: start pred + decalaj − durată.
///
/// LIMITĂ CUNOSCUTĂ: duratele sunt în zile calendaristice. Calendarele de lucru din Primavera
/// (zile lucrătoare, ture) se adaugă ulterior prin <see cref="ICalendarLucru"/>.
/// </summary>
public static class PrognozaTermene
{
    public static Rezultat<IReadOnlyList<TermenPrognozat>> Calculeaza(
        IReadOnlyList<StareActivitate> activitati,
        DateOnly dataRaportare,
        MetodaDurataRamasa metoda = MetodaDurataRamasa.Planificata)
    {
        var dupaCod = activitati.ToDictionary(a => a.Cod);
        foreach (var a in activitati)
        {
            var lipsa = a.Dependente.FirstOrDefault(d => !dupaCod.ContainsKey(d.CodPredecesor));
            if (lipsa is not null)
            {
                return Rezultat.Esuat<IReadOnlyList<TermenPrognozat>>(
                    Eroare.Validare($"Activitatea {a.Cod} depinde de {lipsa.CodPredecesor}, care nu există."));
            }
        }

        var ordine = OrdineTopologica(activitati);
        if (ordine is null)
        {
            return Rezultat.Esuat<IReadOnlyList<TermenPrognozat>>(
                Eroare.Validare("Graficul conține o dependență circulară."));
        }

        var termene = new Dictionary<string, TermenPrognozat>();
        foreach (var cod in ordine)
        {
            var a = dupaCod[cod];
            termene[cod] = Prognozeaza(a, termene, dataRaportare, metoda);
        }

        return Rezultat.Ok<IReadOnlyList<TermenPrognozat>>(activitati.Select(a => termene[a.Cod]).ToList());
    }

    private static TermenPrognozat Prognozeaza(
        StareActivitate a,
        Dictionary<string, TermenPrognozat> termene,
        DateOnly dataRaportare,
        MetodaDurataRamasa metoda)
    {
        if (a.SfarsitReal is { } sfarsitReal)
        {
            return new TermenPrognozat(a.Cod, a.StartReal ?? sfarsitReal.AddDays(-a.DurataZile), sfarsitReal);
        }

        if (a.StartReal is { } startReal)
        {
            var ramas = DurataRamasa(a, startReal, dataRaportare, metoda);
            var sfarsit = dataRaportare.AddDays(ramas);
            return new TermenPrognozat(a.Cod, startReal, sfarsit < startReal ? startReal : sfarsit);
        }

        var start = Max(a.StartPlanificat, dataRaportare);
        foreach (var d in a.Dependente)
        {
            var p = termene[d.CodPredecesor];
            var constrangere = d.Tip switch
            {
                TipDependenta.FS => p.Sfarsit.AddDays(d.DecalajZile),
                TipDependenta.SS => p.Start.AddDays(d.DecalajZile),
                TipDependenta.FF => p.Sfarsit.AddDays(d.DecalajZile - a.DurataZile),
                TipDependenta.SF => p.Start.AddDays(d.DecalajZile - a.DurataZile),
                _ => start,
            };
            start = Max(start, constrangere);
        }

        return new TermenPrognozat(a.Cod, start, start.AddDays(a.DurataZile));
    }

    private static int DurataRamasa(StareActivitate a, DateOnly startReal, DateOnly dataRaportare, MetodaDurataRamasa metoda)
    {
        var progres = Math.Clamp(a.Progres, 0m, 1m);
        if (metoda == MetodaDurataRamasa.Ritm && progres > 0)
        {
            var scurse = Math.Max(1, dataRaportare.DayNumber - startReal.DayNumber);
            return (int)Math.Ceiling(scurse / progres * (1 - progres));
        }

        return (int)Math.Ceiling(a.DurataZile * (1 - progres));
    }

    /// <summary>Algoritmul lui Kahn; null dacă există un ciclu.</summary>
    private static List<string>? OrdineTopologica(IReadOnlyList<StareActivitate> activitati)
    {
        var grad = activitati.ToDictionary(a => a.Cod, a => a.Dependente.Count);
        var succesori = activitati.ToDictionary(a => a.Cod, _ => new List<string>());
        foreach (var a in activitati)
        {
            foreach (var d in a.Dependente)
            {
                succesori[d.CodPredecesor].Add(a.Cod);
            }
        }

        var coada = new Queue<string>(activitati.Where(a => grad[a.Cod] == 0).Select(a => a.Cod));
        var ordine = new List<string>(activitati.Count);
        while (coada.Count > 0)
        {
            var cod = coada.Dequeue();
            ordine.Add(cod);
            foreach (var s in succesori[cod])
            {
                if (--grad[s] == 0)
                {
                    coada.Enqueue(s);
                }
            }
        }

        return ordine.Count == activitati.Count ? ordine : null;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
