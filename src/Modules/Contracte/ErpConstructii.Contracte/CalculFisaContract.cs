using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte;

/// <summary>Avansul unei tranșe pentru un membru: contractual (procent × valoarea × cota), facturat.</summary>
public sealed record AvansTransa(int Numar, decimal Procent, decimal Contractual, decimal Facturat);

/// <summary>Situația fișei pentru un membru al asocierii (sau pentru total), în moneda contractului și în RON.</summary>
/// <param name="AvansRamas">Avans facturat − recuperat prin storno: cât mai e de recuperat din situații.</param>
/// <param name="LucrariFacturate">Lucrările / livrările facturate (brut, fără storno avans): executatul facturat.</param>
/// <param name="RestDeExecutat">Valoarea actuală − lucrările facturate.</param>
/// <param name="PlatitConfirmat">Plățile dovedite; <paramref name="EmisNeconfirmat"/> = instrumente emise (ex. bilet la ordin), neîncasate încă.</param>
/// <param name="Sold">Facturat RON cu TVA − plătit confirmat.</param>
public sealed record SituatieMembru(
    string Membru, decimal Cota, bool EsteFirmaActiva, decimal ValoareActuala, decimal ValoarePropusa, decimal AvansContractual, decimal AvansFacturat,
    decimal AvansRecuperat, decimal AvansRamas, decimal LucrariFacturate, decimal ProcentExecutat, decimal RestDeExecutat, decimal FacturatNetRon,
    decimal FacturatTvaRon, decimal FacturatTotalRon, decimal PlatitConfirmat, decimal EmisNeconfirmat, decimal Sold, decimal GarantieBunaExecutie,
    decimal GarantieAvans, decimal GarantieTehnica, IReadOnlyList<AvansTransa> Transe, int Facturi);

public enum TipAlerta
{
    FacturaScadenta,
    InstrumentExpira,
    InstrumentExpirat,
    TermenDepasit,
    ActPropus,
    ActiuneDepasita,
}

/// <param name="Data">Data de referință (scadența, expirarea, termenul).</param>
public sealed record AlertaContract(TipAlerta Tip, string Mesaj, DateOnly? Data);

/// <param name="SoldFactura">Pe fiecare factură din fișă: total RON − plăți confirmate legate de ea.</param>
/// <param name="PenalitatePosibila">La termen depășit: zile × penalitatea pe zi × valoarea, plafonată (în moneda contractului).</param>
public sealed record SituatieFisa(
    IReadOnlyList<SituatieMembru> Membri, SituatieMembru Total, IReadOnlyDictionary<Guid, decimal> SoldFactura, decimal? PenalitatePosibila,
    int ZileDepasire, IReadOnlyList<AlertaContract> Alerte);

/// <summary>
/// Calculul fișei de urmărire a contractului (D-56), pe fiecare membru al asocierii și total: valoarea (semnată / propusă),
/// avansul pe tranșe (contractual, facturat, recuperat prin storno, rămas), lucrările facturate și restul de executat,
/// facturat în RON, plătit (confirmat / doar emis), sold, garanțiile datorate; alertele (facturi scadente neplătite,
/// instrumente care expiră, termen depășit cu penalitatea posibilă, acte propuse nesemnate, acțiuni depășite). Funcție pură.
/// </summary>
public static class CalculFisaContract
{
    /// <summary>Instrumentele care expiră în mai puțin de atâtea zile sunt semnalate.</summary>
    public const int ZileAlertaExpirare = 30;

    public static SituatieFisa Calculeaza(
        ContractPartener c, IReadOnlyList<ActAditional> acte, IReadOnlyList<FacturaContract> facturi, IReadOnlyList<PlataContract> plati,
        IReadOnlyList<InstrumentGarantie> instrumente, IReadOnlyList<ActiuneContract> actiuni, bool inchis, DateOnly azi)
    {
        var semnate = acte.Where(a => a.Semnat).ToList();
        var valoare = c.Valoare + semnate.Sum(a => a.DiferentaValoare);
        var propusa = c.Valoare + acte.Sum(a => a.DiferentaValoare);
        var membri = c.Membri.Count > 0 ? c.Membri : [new MembruAsociere("Firma", null, 1m, true)];

        // Plățile confirmate / emise pe fiecare factură din fișă.
        var confirmatePeFactura = plati.Where(p => p.Confirmata && p.FacturaContractId is not null)
            .GroupBy(p => p.FacturaContractId!.Value).ToDictionary(g => g.Key, g => g.Sum(p => p.SumaRon));
        var soldFactura = facturi.ToDictionary(f => f.Id, f => f.TotalRon - confirmatePeFactura.GetValueOrDefault(f.Id));

        // Factura aparține membrului cu aceeași denumire; altfel (sau fără asociere) primului membru / firmei.
        string Cheie(string s) => string.Join(' ', s.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        MembruAsociere MembruAl(FacturaContract f) =>
            membri.FirstOrDefault(m => Cheie(m.Denumire) == Cheie(f.Membru)) ?? (membri.Count == 1 ? membri[0] : membri.FirstOrDefault(m => m.EsteFirmaActiva) ?? membri[0]);

        var peMembru = facturi.GroupBy(MembruAl).ToDictionary(g => g.Key, g => g.ToList());
        var platiPeFactura = plati.Where(p => p.FacturaContractId is not null).ToLookup(p => p.FacturaContractId!.Value);

        SituatieMembru Situatie(MembruAsociere m, IReadOnlyList<FacturaContract> fm, IReadOnlyList<PlataContract> pm, decimal cota)
        {
            var avansFacturat = fm.Where(f => f.Tip == TipFacturaContract.Avans).Sum(f => f.Valoare);
            var recuperat = fm.Sum(f => f.StornoAvans);
            var lucrari = fm.Where(f => f.Tip != TipFacturaContract.Avans).Sum(f => f.Valoare);
            var v = Rotunjire.Lei.Aplica(valoare * cota);
            var transe = c.TranseAvans.Select(t => new AvansTransa(t.Numar, t.Procent, Rotunjire.Lei.Aplica(c.Valoare * t.Procent * cota),
                fm.Where(f => f.Tip == TipFacturaContract.Avans && f.TransaAvans == t.Numar).Sum(f => f.Valoare))).ToList();
            var totalRon = fm.Sum(f => f.TotalRon);
            var confirmat = pm.Where(p => p.Confirmata).Sum(p => p.SumaRon);
            return new SituatieMembru(
                m.Denumire, cota, m.EsteFirmaActiva, v, Rotunjire.Lei.Aplica(propusa * cota), Rotunjire.Lei.Aplica(c.Avans * cota), avansFacturat, recuperat,
                avansFacturat - recuperat, lucrari, v > 0 ? Math.Round(lucrari / v * 100, 1, MidpointRounding.AwayFromZero) : 0, v - lucrari,
                fm.Sum(f => f.NetRon), fm.Sum(f => f.TvaRon), totalRon, confirmat, pm.Where(p => !p.Confirmata).Sum(p => p.SumaRon), totalRon - confirmat,
                Rotunjire.Lei.Aplica(valoare * c.ProcentGarantie * cota), Rotunjire.Lei.Aplica(valoare * c.ProcentGarantieAvans * cota),
                Rotunjire.Lei.Aplica(valoare * c.ProcentGarantieTehnica * cota), transe, fm.Count);
        }

        var situatii = membri.Select(m =>
        {
            var fm = peMembru.GetValueOrDefault(m) ?? [];
            return Situatie(m, fm, fm.SelectMany(f => platiPeFactura[f.Id]).ToList(), m.Cota);
        }).ToList();
        // Totalul: toate facturile și toate plățile (și cele neatribuite unei facturi).
        var total = Situatie(new MembruAsociere("Total", null, 1m, false), facturi, plati, 1m);

        // ---- alertele
        var alerte = new List<AlertaContract>();
        foreach (var f in facturi.Where(f => soldFactura[f.Id] > 0.01m))
        {
            var scadenta = f.Scadenta ?? (c.TermenPlataZile is { } z ? f.Data.AddDays(z) : null);
            if (scadenta is { } s && s < azi)
                alerte.Add(new AlertaContract(TipAlerta.FacturaScadenta,
                    $"Factura {f.Numar} ({f.Membru}) e scadentă din {s:dd.MM.yyyy}; neconfirmat plătit {soldFactura[f.Id]:N2} lei.", s));
        }

        foreach (var i in instrumente.Where(i => i.DataExpirare is not null))
        {
            var zile = i.DataExpirare!.Value.DayNumber - azi.DayNumber;
            var nume = $"{i.Tip switch { TipInstrument.GarantieAvans => "SGB avans", TipInstrument.GarantieBunaExecutie => "SGB bună execuție", TipInstrument.GarantieTehnica => "SGB garanție tehnică", TipInstrument.BiletLaOrdin => "Biletul la ordin", _ => "Instrumentul" }} {i.Numar}".Trim();
            if (zile < 0)
                alerte.Add(new AlertaContract(TipAlerta.InstrumentExpirat, $"{nume} a expirat la {i.DataExpirare:dd.MM.yyyy}.", i.DataExpirare));
            else if (zile <= ZileAlertaExpirare)
                alerte.Add(new AlertaContract(TipAlerta.InstrumentExpira, $"{nume} expiră în {zile} zile ({i.DataExpirare:dd.MM.yyyy}).", i.DataExpirare));
        }

        var termen = semnate.Where(a => a.DataSfarsitNoua is not null).OrderBy(a => a.Numar).LastOrDefault()?.DataSfarsitNoua ?? c.DataSfarsit;
        decimal? penalitate = null;
        var zileDepasire = 0;
        if (!inchis && c.Stare == StareContract.Activ && termen is { } t && azi > t && c.DataPvrtl is null)
        {
            zileDepasire = azi.DayNumber - t.DayNumber;
            if (c.PenalitateExecutieZi > 0)
            {
                var calc = valoare * c.PenalitateExecutieZi * zileDepasire;
                penalitate = Rotunjire.Lei.Aplica(c.PlafonPenalitateExecutie > 0 ? Math.Min(calc, valoare * c.PlafonPenalitateExecutie) : calc);
            }

            alerte.Add(new AlertaContract(TipAlerta.TermenDepasit,
                $"Termenul contractual ({t:dd.MM.yyyy}) e depășit cu {zileDepasire} zile" +
                (penalitate is { } pen ? $"; penalitate posibilă {pen:N2} {c.Moneda} (de analizat culpa)." : "."), t));
        }

        foreach (var a in acte.Where(a => !a.Semnat))
            alerte.Add(new AlertaContract(TipAlerta.ActPropus, $"Actul adițional nr. {a.Numar} ({a.DiferentaValoare:N2} {c.Moneda}) e nesemnat: nu intră în valoare.", a.Data));
        foreach (var a in actiuni.Where(a => a.Stare != StareActiune.Inchisa && a.Termen is { } tt && tt < azi))
            alerte.Add(new AlertaContract(TipAlerta.ActiuneDepasita, $"Acțiunea „{a.Descriere}” a depășit termenul ({a.Termen:dd.MM.yyyy}).", a.Termen));

        return new SituatieFisa(situatii, total, soldFactura, penalitate, zileDepasire, alerte.OrderBy(a => a.Data ?? DateOnly.MaxValue).ToList());
    }
}
