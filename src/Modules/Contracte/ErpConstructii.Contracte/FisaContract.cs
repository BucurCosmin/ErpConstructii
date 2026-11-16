using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte;

// Fișa de urmărire a contractului (D-56, docs/contracte.md): condițiile contractului (asocierea, avansul pe tranșe,
// garanțiile, penalitățile) și registrele fișei (obligații, acțiuni, faze de execuție, aspecte de clarificat, instrumente
// de garanție). Condițiile țin de contract (corectate cu motiv după semnare); registrele se editează oricând.

/// <summary>Un membru al asocierii care semnează contractul (ex. MGJV = MELSAM 50% + GCC 50%) și cota lui.</summary>
/// <param name="Cota">Fracție (0,5 = 50%); cotele membrilor fac împreună 100%.</param>
/// <param name="EsteFirmaActiva">Membrul care e firma din aplicație (facturile lui vin din modulul Facturi).</param>
public sealed record MembruAsociere(string Denumire, string? Cui, decimal Cota, bool EsteFirmaActiva);

/// <summary>O tranșă a avansului contractual (ex. 5% + 10% + 5%), ca fracție din valoarea contractului.</summary>
public sealed record TransaAvans(int Numar, decimal Procent, string? Descriere);

/// <param name="TipPret">Ex. „Prețuri unitare / cantități real executate”, „Preț global”.</param>
/// <param name="Durata">Durata de execuție, când e dată în text (ex. „4,5 luni de la predarea amplasamentului”).</param>
/// <param name="Amplasament">Clădiri / zone (ex. „B5, B6 și B10 Parking Area”).</param>
/// <param name="StareText">Stadiul descris (ex. „Aproape de finalizare – comunicat de beneficiar”).</param>
/// <param name="DataPvrtl">Procesul-verbal de recepție la terminarea lucrărilor: de la el curge garanția tehnică.</param>
/// <param name="ProcentGarantieAvans">Garanția de restituire a avansului (SGB), fracție din valoarea contractului.</param>
/// <param name="ProcentGarantieTehnica">Garanția tehnică (după PVRTL), fracție; durata în <paramref name="LuniGarantieTehnica"/>.</param>
/// <param name="PenalitateExecutieZi">Penalitatea de întârziere în execuție, fracție din valoarea contractului pe zi (0,001 = 0,1%/zi).</param>
/// <param name="PlafonPenalitateExecutie">Plafonul penalităților de execuție, fracție din valoarea contractului (0,05 = 5%).</param>
public sealed record ConditiiContract(
    string? TipPret,
    string? Durata,
    string? Amplasament,
    string? StareText,
    DateOnly? DataPredareAmplasament,
    DateOnly? DataPvrtl,
    IReadOnlyList<MembruAsociere> Membri,
    IReadOnlyList<TransaAvans> TranseAvans,
    decimal ProcentGarantieAvans,
    decimal ProcentGarantieTehnica,
    int? LuniGarantieTehnica,
    decimal PenalitateExecutieZi,
    decimal PlafonPenalitateExecutie,
    decimal PenalitatePlataZi,
    decimal PlafonPenalitatePlata)
{
    public static readonly ConditiiContract Goale = new(null, null, null, null, null, null, [], [], 0, 0, null, 0, 0, 0, 0);

    public static List<string> Valideaza(ConditiiContract c)
    {
        var p = new List<string>();
        if (c.TipPret?.Trim().Length > 200) p.Add("Tipul prețului are cel mult 200 de caractere.");
        if (c.Durata?.Trim().Length > 300) p.Add("Durata are cel mult 300 de caractere.");
        if (c.Amplasament?.Trim().Length > 300) p.Add("Amplasamentul are cel mult 300 de caractere.");
        if (c.StareText?.Trim().Length > 500) p.Add("Stadiul are cel mult 500 de caractere.");
        if (c.Membri.Count > 0)
        {
            if (c.Membri.Any(m => string.IsNullOrWhiteSpace(m.Denumire) || m.Denumire.Trim().Length > 300))
                p.Add("Fiecare membru al asocierii are o denumire (cel mult 300 de caractere).");
            if (c.Membri.Any(m => m.Cota is <= 0 or > 1))
                p.Add("Cota fiecărui membru e între 0% și 100%.");
            if (Math.Abs(c.Membri.Sum(m => m.Cota) - 1m) > 0.0001m)
                p.Add($"Cotele membrilor asocierii fac împreună 100% (acum {c.Membri.Sum(m => m.Cota) * 100:0.##}%).");
            if (c.Membri.Count(m => m.EsteFirmaActiva) > 1)
                p.Add("Doar un membru al asocierii e firma din aplicație.");
            if (c.Membri.Any(m => m.Cui is { } cui && !string.IsNullOrWhiteSpace(cui) && !ValidareFiscala.EsteCuiValid(cui)))
                p.Add("CUI-ul unui membru al asocierii nu este valid.");
        }

        if (c.TranseAvans.Any(t => t.Procent is <= 0 or > 1)) p.Add("Fiecare tranșă de avans e între 0% și 100%.");
        if (c.TranseAvans.Sum(t => t.Procent) > 1) p.Add("Tranșele de avans depășesc 100% din contract.");
        if (c.TranseAvans.Any(t => t.Descriere?.Trim().Length > 200)) p.Add("Descrierea unei tranșe are cel mult 200 de caractere.");
        if (c.ProcentGarantieAvans is < 0 or > 1) p.Add("Garanția de avans e între 0% și 100%.");
        if (c.ProcentGarantieTehnica is < 0 or > 0.5m) p.Add("Garanția tehnică e între 0% și 50%.");
        if (c.LuniGarantieTehnica is < 0 or > 240) p.Add("Durata garanției tehnice e între 0 și 240 de luni.");
        if (c.PenalitateExecutieZi is < 0 or > 0.1m || c.PenalitatePlataZi is < 0 or > 0.1m) p.Add("Penalitatea pe zi e între 0% și 10%.");
        if (c.PlafonPenalitateExecutie is < 0 or > 1 || c.PlafonPenalitatePlata is < 0 or > 1) p.Add("Plafonul penalităților e între 0% și 100%.");
        if (c.DataPredareAmplasament is { Year: < 2000 } || c.DataPvrtl is { Year: < 2000 }) p.Add("Datele contractului nu sunt valide.");
        return p;
    }
}

/// <summary>
/// Corectura unui contract semnat (D-56): datele de bază se schimbă doar împreună cu ea (regula 6, verificat de
/// interceptorul de audit), cu motivul și ce s-a schimbat („câmp: vechi → nou”). Append-only.
/// </summary>
public sealed class CorectieContract : EntitateFirma, ICorectieDocument
{
    private CorectieContract() { }

    public CorectieContract(Guid contractId, string motiv, IReadOnlyList<string> modificari, string corectatDeNume, DateTimeOffset la)
    {
        ContractId = contractId;
        Motiv = motiv.Trim();
        Modificari = string.Join("\n", modificari);
        CorectatDeNume = corectatDeNume.Trim();
        La = la;
    }

    public Guid ContractId { get; private set; }
    public string Motiv { get; private set; } = string.Empty;
    public string Modificari { get; private set; } = string.Empty;
    public string CorectatDeNume { get; private set; } = string.Empty;
    public DateTimeOffset La { get; private set; }
    public Guid DocumentCorectatId => ContractId;
}

// ---------------------------------------------------------------- registrele fișei

/// <summary>Un rând dintr-un registru al fișei contractului, în ordinea din fișă.</summary>
public abstract class RandFisaContract : EntitateFirma
{
    public Guid ContractId { get; protected set; }
    public int Ordine { get; protected set; }

    public void Muta(int ordine) => Ordine = ordine;

    protected static string? Text(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());
}

/// <summary>Obligație / reper contractual (foaia „Obligații și termene”).</summary>
public sealed record DateObligatie(string Denumire, string? Temei, string? Termen, DateOnly? DataTermen, string? Stare, string? Responsabil, string? Observatii);

public sealed class ObligatieContract : RandFisaContract
{
    private ObligatieContract() { }

    public string Denumire { get; private set; } = string.Empty;

    /// <summary>Ex. „TCS art. 5.11”.</summary>
    public string? Temei { get; private set; }

    /// <summary>Termenul / condiția în text (ex. „45 zile de la comunicarea facturii”); data, când e una anume.</summary>
    public string? Termen { get; private set; }
    public DateOnly? DataTermen { get; private set; }
    public string? Stare { get; private set; }
    public string? Responsabil { get; private set; }
    public string? Observatii { get; private set; }

    public static Rezultat<ObligatieContract> Creeaza(Guid contractId, int ordine, DateObligatie d)
    {
        var r = new ObligatieContract { ContractId = contractId, Ordine = ordine };
        var m = r.Modifica(d);
        return m.Esec ? Rezultat.Esuat<ObligatieContract>(m.Eroare) : Rezultat.Ok(r);
    }

    public Rezultat Modifica(DateObligatie d)
    {
        if (string.IsNullOrWhiteSpace(d.Denumire))
            return Rezultat.Esuat(Eroare.Validare("Obligația / reperul este obligatoriu."));
        Denumire = Text(d.Denumire, 500)!;
        Temei = Text(d.Temei, 200);
        Termen = Text(d.Termen, 500);
        DataTermen = d.DataTermen;
        Stare = Text(d.Stare, 100);
        Responsabil = Text(d.Responsabil, 200);
        Observatii = Text(d.Observatii, 2000);
        return Rezultat.Ok();
    }
}

public enum PrioritateActiune
{
    Critica,
    Ridicata,
    Medie,
    Normala,
}

public enum StareActiune
{
    Deschisa,
    InLucru,
    Inchisa,
}

/// <summary>Acțiune de urmărit pe contract (foaia „Acțiuni”); responsabilul poate fi un utilizator al aplicației.</summary>
public sealed record DateActiune(
    string Descriere, PrioritateActiune Prioritate, StareActiune Stare, Guid? ResponsabilId, string? ResponsabilNume, DateOnly? Termen, string? Observatii);

public sealed class ActiuneContract : RandFisaContract
{
    private ActiuneContract() { }

    public string Descriere { get; private set; } = string.Empty;
    public PrioritateActiune Prioritate { get; private set; }
    public StareActiune Stare { get; private set; }
    public Guid? ResponsabilId { get; private set; }
    public string? ResponsabilNume { get; private set; }
    public DateOnly? Termen { get; private set; }
    public string? Observatii { get; private set; }

    public static Rezultat<ActiuneContract> Creeaza(Guid contractId, int ordine, DateActiune d)
    {
        var r = new ActiuneContract { ContractId = contractId, Ordine = ordine };
        var m = r.Modifica(d);
        return m.Esec ? Rezultat.Esuat<ActiuneContract>(m.Eroare) : Rezultat.Ok(r);
    }

    public Rezultat Modifica(DateActiune d)
    {
        if (string.IsNullOrWhiteSpace(d.Descriere))
            return Rezultat.Esuat(Eroare.Validare("Acțiunea este obligatorie."));
        if (!Enum.IsDefined(d.Prioritate) || !Enum.IsDefined(d.Stare))
            return Rezultat.Esuat(Eroare.Validare("Prioritatea sau starea acțiunii nu sunt valide."));
        Descriere = Text(d.Descriere, 1000)!;
        Prioritate = d.Prioritate;
        Stare = d.Stare;
        ResponsabilId = d.ResponsabilId;
        ResponsabilNume = Text(d.ResponsabilNume, 200);
        Termen = d.Termen;
        Observatii = Text(d.Observatii, 2000);
        return Rezultat.Ok();
    }
}

/// <summary>Fază / reper de execuție (foaia „Termene de execuție”); datele lipsesc cât sunt „de confirmat”.</summary>
public sealed record DateFaza(string Denumire, DateOnly? Start, DateOnly? Final, string? Conditii, string? Stare, string? Observatii);

public sealed class FazaExecutie : RandFisaContract
{
    private FazaExecutie() { }

    public string Denumire { get; private set; } = string.Empty;
    public DateOnly? Start { get; private set; }
    public DateOnly? Final { get; private set; }
    public string? Conditii { get; private set; }
    public string? Stare { get; private set; }
    public string? Observatii { get; private set; }

    public static Rezultat<FazaExecutie> Creeaza(Guid contractId, int ordine, DateFaza d)
    {
        var r = new FazaExecutie { ContractId = contractId, Ordine = ordine };
        var m = r.Modifica(d);
        return m.Esec ? Rezultat.Esuat<FazaExecutie>(m.Eroare) : Rezultat.Ok(r);
    }

    public Rezultat Modifica(DateFaza d)
    {
        if (string.IsNullOrWhiteSpace(d.Denumire))
            return Rezultat.Esuat(Eroare.Validare("Faza / reperul este obligatoriu."));
        if (d.Start is { } s && d.Final is { } f && f < s)
            return Rezultat.Esuat(Eroare.Validare("Finalul fazei e înaintea startului."));
        Denumire = Text(d.Denumire, 300)!;
        Start = d.Start;
        Final = d.Final;
        Conditii = Text(d.Conditii, 500);
        Stare = Text(d.Stare, 200);
        Observatii = Text(d.Observatii, 2000);
        return Rezultat.Ok();
    }
}

/// <summary>Aspect de clarificat / risc (foaia „Garanții și riscuri”).</summary>
public sealed record DateRisc(string Element, string? Prevedere, string? Stare);

public sealed class RiscContract : RandFisaContract
{
    private RiscContract() { }

    public string Element { get; private set; } = string.Empty;
    public string? Prevedere { get; private set; }
    public string? Stare { get; private set; }

    public static Rezultat<RiscContract> Creeaza(Guid contractId, int ordine, DateRisc d)
    {
        var r = new RiscContract { ContractId = contractId, Ordine = ordine };
        var m = r.Modifica(d);
        return m.Esec ? Rezultat.Esuat<RiscContract>(m.Eroare) : Rezultat.Ok(r);
    }

    public Rezultat Modifica(DateRisc d)
    {
        if (string.IsNullOrWhiteSpace(d.Element))
            return Rezultat.Esuat(Eroare.Validare("Elementul este obligatoriu."));
        Element = Text(d.Element, 300)!;
        Prevedere = Text(d.Prevedere, 1000);
        Stare = Text(d.Stare, 1000);
        return Rezultat.Ok();
    }
}

public enum TipInstrument
{
    GarantieAvans,
    GarantieBunaExecutie,
    GarantieTehnica,
    BiletLaOrdin,
    Altul,
}

/// <summary>
/// Instrument de garanție sau de plată (SGB de avans / bună execuție / tehnică, bilet la ordin): emitent, număr, membrul
/// asocierii beneficiar, valoare, valabilitate. Biletul la ordin emis nu înseamnă plată (plățile se țin separat).
/// </summary>
public sealed record DateInstrument(
    TipInstrument Tip, string? Descriere, string? Emitent, string? Numar, string? Beneficiar, decimal? Valoare, string? Moneda, DateOnly? DataEmitere,
    DateOnly? DataExpirare, string? Stare, string? Observatii);

public sealed class InstrumentGarantie : RandFisaContract
{
    private InstrumentGarantie() { }

    public TipInstrument Tip { get; private set; }
    public string? Descriere { get; private set; }
    public string? Emitent { get; private set; }
    public string? Numar { get; private set; }

    /// <summary>Membrul asocierii pentru care e emis (ex. GCC).</summary>
    public string? Beneficiar { get; private set; }
    public decimal? Valoare { get; private set; }
    public string? Moneda { get; private set; }
    public DateOnly? DataEmitere { get; private set; }

    /// <summary>Expirarea garanției / scadența biletului la ordin.</summary>
    public DateOnly? DataExpirare { get; private set; }
    public string? Stare { get; private set; }
    public string? Observatii { get; private set; }

    public static Rezultat<InstrumentGarantie> Creeaza(Guid contractId, int ordine, DateInstrument d)
    {
        var r = new InstrumentGarantie { ContractId = contractId, Ordine = ordine };
        var m = r.Modifica(d);
        return m.Esec ? Rezultat.Esuat<InstrumentGarantie>(m.Eroare) : Rezultat.Ok(r);
    }

    public Rezultat Modifica(DateInstrument d)
    {
        if (!Enum.IsDefined(d.Tip))
            return Rezultat.Esuat(Eroare.Validare("Tipul instrumentului nu este valid."));
        if (d.Valoare < 0)
            return Rezultat.Esuat(Eroare.Validare("Valoarea instrumentului nu poate fi negativă."));
        if (d.Moneda is { } m && !string.IsNullOrWhiteSpace(m) && m.Trim().Length != 3)
            return Rezultat.Esuat(Eroare.Validare("Moneda are 3 litere (RON, EUR)."));
        if (d.DataEmitere is { } e && d.DataExpirare is { } x && x < e)
            return Rezultat.Esuat(Eroare.Validare("Expirarea e înaintea emiterii."));
        Tip = d.Tip;
        Descriere = Text(d.Descriere, 500);
        Emitent = Text(d.Emitent, 300);
        Numar = Text(d.Numar, 100);
        Beneficiar = Text(d.Beneficiar, 300);
        Valoare = d.Valoare is { } v ? Rotunjire.Lei.Aplica(v) : null;
        Moneda = string.IsNullOrWhiteSpace(d.Moneda) ? null : d.Moneda.Trim().ToUpperInvariant();
        DataEmitere = d.DataEmitere;
        DataExpirare = d.DataExpirare;
        Stare = Text(d.Stare, 200);
        Observatii = Text(d.Observatii, 2000);
        return Rezultat.Ok();
    }
}

/// <summary>Un document al bazei documentare (foaia „Surse”): contractul semnat, acte, facturi, SGB — pomenit, chiar dacă fișierul nu e atașat.</summary>
public sealed record DateSursa(string Document, DateOnly? Data, string? Observatii);

public sealed class SursaContract : RandFisaContract
{
    private SursaContract() { }

    public string Document { get; private set; } = string.Empty;
    public DateOnly? Data { get; private set; }
    public string? Observatii { get; private set; }

    public static Rezultat<SursaContract> Creeaza(Guid contractId, int ordine, DateSursa d)
    {
        var r = new SursaContract { ContractId = contractId, Ordine = ordine };
        var m = r.Modifica(d);
        return m.Esec ? Rezultat.Esuat<SursaContract>(m.Eroare) : Rezultat.Ok(r);
    }

    public Rezultat Modifica(DateSursa d)
    {
        if (string.IsNullOrWhiteSpace(d.Document))
            return Rezultat.Esuat(Eroare.Validare("Documentul este obligatoriu."));
        Document = Text(d.Document, 500)!;
        Data = d.Data;
        Observatii = Text(d.Observatii, 2000);
        return Rezultat.Ok();
    }
}
