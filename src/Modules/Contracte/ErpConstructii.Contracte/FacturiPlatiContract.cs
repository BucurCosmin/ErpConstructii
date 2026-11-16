using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte;

// Facturile și plățile din fișa contractului (D-56, etapa 2). Facturile firmei active se leagă de factura din modulul
// Facturi (FacturaId); cele adresate celuilalt membru al asocierii (ex. MELSAM, când firma e GCC) se introduc în fișă.
// Valorile de lucrări și avans sunt în moneda contractului (EUR); sumele facturii în RON, la cursul scris pe ea.

public enum TipFacturaContract
{
    /// <summary>Factura de avans (o tranșă).</summary>
    Avans,

    /// <summary>Lucrări executate (situație de plată), eventual cu stornarea unei părți din avans.</summary>
    Lucrari,

    /// <summary>Livrare (ex. structură metalică), eventual cu storno avans.</summary>
    Livrare,
    Altele,
}

/// <param name="Membru">Membrul asocierii căruia i s-a emis factura (ex. „GENERAL CONCRETE CERNAVODĂ SRL”).</param>
/// <param name="FacturaId">Factura din modulul Facturi (doar pentru firma activă); null = introdusă în fișă.</param>
/// <param name="TransaAvans">La factura de avans: tranșa; la storno: tranșa recuperată (null = pro rata / neprecizat).</param>
/// <param name="LunaExecutie">Ex. „Noiembrie 2025”.</param>
/// <param name="Valoare">În moneda contractului: lucrările / livrarea (brut, fără storno) sau avansul facturat.</param>
/// <param name="StornoAvans">În moneda contractului: avansul recuperat pe această factură (pozitiv).</param>
/// <param name="Curs">Cursul BNR scris pe factură (lei pentru o unitate din moneda contractului); null la contractele în RON.</param>
public sealed record DateFacturaContract(
    string Membru, Guid? FacturaId, string Numar, DateOnly Data, TipFacturaContract Tip, int? TransaAvans, string? SituatiePlata, string? LunaExecutie,
    decimal Valoare, decimal StornoAvans, decimal? Curs, decimal NetRon, decimal TvaRon, decimal TotalRon, DateOnly? Scadenta, string? Observatii);

/// <summary>O factură în fișa contractului (foaia „Facturi / situații / plăți”).</summary>
public sealed class FacturaContract : RandFisaContract
{
    private FacturaContract() { }

    public string Membru { get; private set; } = string.Empty;
    public Guid? FacturaId { get; private set; }
    public string Numar { get; private set; } = string.Empty;
    public DateOnly Data { get; private set; }
    public TipFacturaContract Tip { get; private set; }
    public int? TransaAvans { get; private set; }
    public string? SituatiePlata { get; private set; }
    public string? LunaExecutie { get; private set; }
    public decimal Valoare { get; private set; }
    public decimal StornoAvans { get; private set; }
    public decimal? Curs { get; private set; }
    public decimal NetRon { get; private set; }
    public decimal TvaRon { get; private set; }
    public decimal TotalRon { get; private set; }
    public DateOnly? Scadenta { get; private set; }
    public string? Observatii { get; private set; }

    public static Rezultat<FacturaContract> Creeaza(Guid contractId, int ordine, DateFacturaContract d)
    {
        var f = new FacturaContract { ContractId = contractId, Ordine = ordine };
        var r = f.Modifica(d);
        return r.Esec ? Rezultat.Esuat<FacturaContract>(r.Eroare) : Rezultat.Ok(f);
    }

    public Rezultat Modifica(DateFacturaContract d)
    {
        var p = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Membru)) p.Add("Membrul facturat este obligatoriu.");
        if (string.IsNullOrWhiteSpace(d.Numar)) p.Add("Numărul facturii este obligatoriu.");
        if (d.Data.Year < 2000) p.Add("Data facturii nu este validă.");
        if (!Enum.IsDefined(d.Tip)) p.Add("Tipul facturii nu este valid.");
        if (d.StornoAvans < 0) p.Add("Stornarea avansului se trece pozitiv (suma recuperată).");
        if (d.Tip == TipFacturaContract.Avans && d.StornoAvans != 0) p.Add("Factura de avans nu stornează avans.");
        if (d.Curs is <= 0) p.Add("Cursul trebuie să fie pozitiv.");
        if (d.TransaAvans is < 1 or > 20) p.Add("Tranșa de avans nu este validă.");
        // Totalul = net + TVA (cu toleranța rotunjirilor de pe factură).
        if (d.TotalRon != 0 && Math.Abs(d.NetRon + d.TvaRon - d.TotalRon) > 0.05m)
            p.Add($"Totalul facturii ({d.TotalRon:N2}) nu e net + TVA ({d.NetRon + d.TvaRon:N2}).");
        if (d.Scadenta is { } s && s < d.Data) p.Add("Scadența e înaintea datei facturii.");
        if (p.Count > 0)
            return Rezultat.Esuat(Eroare.Validare(string.Join(" ", p)));

        Membru = Text(d.Membru, 300)!;
        FacturaId = d.FacturaId;
        Numar = Text(d.Numar, 50)!;
        Data = d.Data;
        Tip = d.Tip;
        TransaAvans = d.TransaAvans;
        SituatiePlata = Text(d.SituatiePlata, 100);
        LunaExecutie = Text(d.LunaExecutie, 50);
        Valoare = Rotunjire.Lei.Aplica(d.Valoare);
        StornoAvans = Rotunjire.Lei.Aplica(d.StornoAvans);
        Curs = d.Curs;
        NetRon = Rotunjire.Lei.Aplica(d.NetRon);
        TvaRon = Rotunjire.Lei.Aplica(d.TvaRon);
        TotalRon = Rotunjire.Lei.Aplica(d.TotalRon == 0 ? d.NetRon + d.TvaRon : d.TotalRon);
        Scadenta = d.Scadenta;
        Observatii = Text(d.Observatii, 2000);
        return Rezultat.Ok();
    }

    /// <summary>
    /// Leagă rândul de factura din modulul Facturi (aceeași factură, preluată întâi din fișa Excel). Sumele lipsă din rând
    /// (net / TVA / total RON, scadența) se completează din factură; cele existente rămân (au fost verificate în fișă).
    /// </summary>
    public void LeagaDe(FacturaPentruFisa f)
    {
        FacturaId = f.Id;
        var inLei = string.Equals(f.Moneda, "RON", StringComparison.OrdinalIgnoreCase);
        if (inLei && NetRon == 0 && TotalRon == 0)
        {
            NetRon = Rotunjire.Lei.Aplica(f.TotalFaraTva);
            TvaRon = Rotunjire.Lei.Aplica(f.TotalTva);
            TotalRon = Rotunjire.Lei.Aplica(f.TotalDePlata);
        }

        Scadenta ??= f.DataScadenta;
    }

    /// <summary>Rândul nu mai e legat de factura din modulul Facturi (legătură greșită).</summary>
    public void Dezleaga() => FacturaId = null;
}

public enum InstrumentPlata
{
    OrdinDePlata,
    BiletLaOrdin,
    Cec,
    Compensare,
    Altul,
}

/// <param name="FacturaContractId">Factura din fișă plătită (null = plată neatribuită încă).</param>
/// <param name="Confirmata">Plata e dovedită (extras / OP); biletul la ordin doar emis rămâne neconfirmat până la încasare.</param>
/// <param name="Scadenta">Scadența instrumentului (biletul la ordin).</param>
public sealed record DatePlata(
    Guid? FacturaContractId, DateOnly Data, decimal SumaRon, InstrumentPlata Instrument, string? Numar, DateOnly? Scadenta, bool Confirmata, string? Observatii);

/// <summary>
/// O plată către subantreprenor (D-56: plățile se țin în aplicație până la modulul Financiar). „Biletul la ordin emis ≠ plată”:
/// doar plățile confirmate scad soldul; cele emise apar separat.
/// </summary>
public sealed class PlataContract : RandFisaContract
{
    private PlataContract() { }

    public Guid? FacturaContractId { get; private set; }
    public DateOnly Data { get; private set; }
    public decimal SumaRon { get; private set; }
    public InstrumentPlata Instrument { get; private set; }
    public string? Numar { get; private set; }
    public DateOnly? Scadenta { get; private set; }
    public bool Confirmata { get; private set; }
    public string? Observatii { get; private set; }

    public static Rezultat<PlataContract> Creeaza(Guid contractId, int ordine, DatePlata d)
    {
        var x = new PlataContract { ContractId = contractId, Ordine = ordine };
        var r = x.Modifica(d);
        return r.Esec ? Rezultat.Esuat<PlataContract>(r.Eroare) : Rezultat.Ok(x);
    }

    public Rezultat Modifica(DatePlata d)
    {
        if (d.SumaRon <= 0)
            return Rezultat.Esuat(Eroare.Validare("Suma plății trebuie să fie pozitivă."));
        if (!Enum.IsDefined(d.Instrument))
            return Rezultat.Esuat(Eroare.Validare("Instrumentul de plată nu este valid."));
        if (d.Data.Year < 2000)
            return Rezultat.Esuat(Eroare.Validare("Data plății nu este validă."));
        FacturaContractId = d.FacturaContractId;
        Data = d.Data;
        SumaRon = Rotunjire.Lei.Aplica(d.SumaRon);
        Instrument = d.Instrument;
        Numar = Text(d.Numar, 100);
        Scadenta = d.Scadenta;
        Confirmata = d.Confirmata;
        Observatii = Text(d.Observatii, 2000);
        return Rezultat.Ok();
    }
}

/// <summary>
/// Propunerea rândului din fișă pentru o factură a firmei din modulul Facturi (logică pură): tipul (avans când liniile
/// pomenesc avansul și nu sunt stornări), lucrările brute (liniile pozitive) și stornarea avansului (liniile negative), în
/// RON; cu cursul dat, aduse în moneda contractului. Utilizatorul le verifică înainte de salvare.
/// </summary>
public static class PropunereFacturaFisa
{
    public static DateFacturaContract Din(FacturaPentruFisa f, string membru, string monedaContract, decimal? curs, int? termenPlataZile)
    {
        var pozitive = f.Linii.Where(l => l.ValoareFaraTva > 0).Sum(l => l.ValoareFaraTva);
        var negative = -f.Linii.Where(l => l.ValoareFaraTva < 0).Sum(l => l.ValoareFaraTva);
        var avans = f.Linii.Any(l => l.Denumire.Contains("avans", StringComparison.OrdinalIgnoreCase));
        var tip = avans && negative == 0 ? TipFacturaContract.Avans : TipFacturaContract.Lucrari;
        var inLei = string.Equals(monedaContract, "RON", StringComparison.OrdinalIgnoreCase);
        decimal Conv(decimal lei) => inLei ? lei : curs is > 0 ? Rotunjire.Lei.Aplica(lei / curs.Value) : 0;
        // Factura în moneda contractului (ex. EUR): valorile sunt deja în ea.
        var inMonedaContract = string.Equals(f.Moneda, monedaContract, StringComparison.OrdinalIgnoreCase) && !inLei;
        return new DateFacturaContract(
            membru, f.Id, f.Numar, f.DataEmitere, tip, null, null, null,
            inMonedaContract ? pozitive : Conv(pozitive), inMonedaContract ? negative : Conv(negative), inLei || inMonedaContract ? null : curs,
            f.TotalFaraTva, f.TotalTva, f.TotalDePlata,
            f.DataScadenta ?? (termenPlataZile is { } z ? f.DataEmitere.AddDays(z) : null), null);
    }
}
