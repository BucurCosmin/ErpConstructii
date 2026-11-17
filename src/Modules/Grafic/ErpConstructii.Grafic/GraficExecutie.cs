using System.Text.Json;
using System.Text.Json.Serialization;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Grafic;

/// <summary>
/// Graficul de execuție al unui proiect (D-57): o planificare din Primavera (același „Project ID”), cu actualizările ei ca
/// versiuni. Un XER nou al aceleiași planificări = versiune nouă; activitățile se recunosc după cod, deci legăturile făcute
/// în ERP (facturi, consumuri) rămân. Un proiect poate avea mai multe grafice (ex. pachete de lucrări separate).
/// </summary>
public sealed class GraficExecutie : EntitateFirma
{
    private GraficExecutie() { }

    public Guid ProiectId { get; private set; }
    public string Denumire { get; private set; } = string.Empty;

    /// <summary>Codul planificării în programul sursă (Primavera „Project ID”).</summary>
    public string? CodSursa { get; private set; }

    /// <summary>Moneda costurilor din grafic (ex. EUR).</summary>
    public string? Moneda { get; private set; }

    /// <summary>Formatul importatorului (ex. „primavera”).</summary>
    public string Format { get; private set; } = string.Empty;

    public int NumarVersiuni { get; private set; }
    public Guid? VersiuneCurentaId { get; private set; }
    public string CreatDeNume { get; private set; } = string.Empty;

    public static GraficExecutie Creeaza(Guid proiectId, string denumire, string? codSursa, string? moneda, string format, string creatDe) => new()
    {
        ProiectId = proiectId,
        Denumire = Taie(denumire, 300) ?? "Grafic de execuție",
        CodSursa = Taie(codSursa, 100),
        Moneda = Taie(moneda, 3),
        Format = Taie(format, 30) ?? string.Empty,
        CreatDeNume = Taie(creatDe, 200) ?? string.Empty,
    };

    /// <summary>Versiunea nouă devine actualizarea curentă; întoarce numărul ei.</summary>
    public int UrmatoareaVersiune() => NumarVersiuni + 1;

    public void Actualizeaza(VersiuneGrafic versiune, string? moneda)
    {
        NumarVersiuni = Math.Max(NumarVersiuni, versiune.Numar);
        VersiuneCurentaId = versiune.Id;
        if (!string.IsNullOrWhiteSpace(moneda))
            Moneda = Taie(moneda, 3);
    }

    /// <summary>După ștergerea ultimei versiuni (importată din greșeală), curentă rămâne cea anterioară.</summary>
    public void RevinoLa(VersiuneGrafic? anterioara)
    {
        VersiuneCurentaId = anterioara?.Id;
        NumarVersiuni = anterioara?.Numar ?? 0;
    }

    public Rezultat Redenumeste(string denumire)
    {
        if (string.IsNullOrWhiteSpace(denumire))
            return Rezultat.Esuat(Eroare.Validare("Denumirea graficului este obligatorie."));
        Denumire = Taie(denumire, 300)!;
        return Rezultat.Ok();
    }

    internal static string? Taie(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim() is var t && t.Length > max ? t[..max] : t;
}

/// <summary>
/// O actualizare a graficului (un fișier importat), cu data actualizării (Primavera „Data Date”), totalurile ei și rezumatul
/// comparației cu versiunea anterioară. Conținutul (WBS, activități) e în <see cref="NodWbsGrafic"/> / <see cref="ActivitateGrafic"/>,
/// fișierul original în <see cref="FisierVersiuneGrafic"/>.
/// </summary>
public sealed class VersiuneGrafic : EntitateFirma
{
    private VersiuneGrafic() { }

    public Guid GraficId { get; private set; }
    public int Numar { get; private set; }
    public string Denumire { get; private set; } = string.Empty;
    public DateOnly? DataActualizare { get; private set; }
    public DateOnly? Start { get; private set; }
    public DateOnly? Sfarsit { get; private set; }
    public string NumeFisier { get; private set; } = string.Empty;
    public string Sha256 { get; private set; } = string.Empty;
    public long Dimensiune { get; private set; }
    public string ImportatDeNume { get; private set; } = string.Empty;
    public int Activitati { get; private set; }
    public int Finalizate { get; private set; }
    public int InCurs { get; private set; }
    public int Critice { get; private set; }
    public decimal Procent { get; private set; }
    public decimal CostPlanificat { get; private set; }
    public decimal CostRealizat { get; private set; }
    public decimal CostRamas { get; private set; }

    /// <summary>Valorile codurilor de activitate folosite (pentru filtre), JSON.</summary>
    public string CoduriJson { get; private set; } = "[]";

    /// <summary>Rezumatul comparației cu versiunea anterioară (<see cref="RezumatComparatie"/>), JSON; null la prima versiune.</summary>
    public string? ComparatieJson { get; private set; }

    public IReadOnlyList<CodActivitateCanonic> Coduri => JsonSerializer.Deserialize<List<CodActivitateCanonic>>(CoduriJson, SerializareGrafic.Optiuni) ?? [];

    public RezumatComparatie? Comparatie => ComparatieJson is null ? null : JsonSerializer.Deserialize<RezumatComparatie>(ComparatieJson, SerializareGrafic.Optiuni);

    public static VersiuneGrafic Creeaza(Guid graficId, int numar, GraficCanonic g, string numeFisier, string sha256, long dimensiune, string importatDe,
        ComparatieGrafic? comparatie)
    {
        var total = AnalizaGrafic.Total(g.Activitati);
        return new VersiuneGrafic
        {
            GraficId = graficId,
            Numar = numar,
            Denumire = GraficExecutie.Taie(g.Denumire, 300) ?? string.Empty,
            DataActualizare = g.DataReferinta,
            Start = total.Start,
            Sfarsit = total.Sfarsit,
            NumeFisier = GraficExecutie.Taie(numeFisier, 300) ?? "grafic.xer",
            Sha256 = sha256,
            Dimensiune = dimensiune,
            ImportatDeNume = GraficExecutie.Taie(importatDe, 200) ?? string.Empty,
            Activitati = total.Activitati,
            Finalizate = total.Finalizate,
            InCurs = total.InCurs,
            Critice = total.Critice,
            Procent = total.Procent,
            CostPlanificat = Math.Round(total.CostPlanificat, 2, MidpointRounding.AwayFromZero),
            CostRealizat = Math.Round(total.CostRealizat, 2, MidpointRounding.AwayFromZero),
            CostRamas = Math.Round(total.CostRamas, 2, MidpointRounding.AwayFromZero),
            CoduriJson = JsonSerializer.Serialize(g.Coduri ?? [], SerializareGrafic.Optiuni),
            ComparatieJson = comparatie is null ? null : JsonSerializer.Serialize(RezumatComparatie.Din(comparatie), SerializareGrafic.Optiuni),
        };
    }
}

/// <summary>Rezumatul comparației cu versiunea anterioară, păstrat pe versiune.</summary>
public sealed record RezumatComparatie(
    int Noi, int Sterse, int Modificate, int Intarziate, int Finalizate,
    DateOnly? SfarsitAnterior, DateOnly? Sfarsit, int? DecalajSfarsitProiectZile, decimal ProcentAnterior, decimal Procent)
{
    public static RezumatComparatie Din(ComparatieGrafic c) => new(
        c.Noi.Count, c.Sterse.Count, c.Modificate.Count, c.Intarziate, c.Finalizate, c.SfarsitAnterior, c.Sfarsit,
        c.DecalajSfarsitProiectZile, c.ProcentAnterior, c.Procent);
}

/// <summary>Fișierul original al versiunii (arhivă, descărcabil).</summary>
public sealed class FisierVersiuneGrafic : EntitateFirma, IFaraJurnal
{
    private FisierVersiuneGrafic() { }

    public Guid VersiuneId { get; private set; }
    public byte[] Continut { get; private set; } = [];

    public static FisierVersiuneGrafic Creeaza(Guid versiuneId, byte[] continut) => new() { VersiuneId = versiuneId, Continut = continut };
}

/// <summary>Nodul WBS al unei versiuni. Fără jurnal de modificări: e copia fișierului importat (jurnalizat prin versiune).</summary>
public sealed class NodWbsGrafic : EntitateFirma, IFaraJurnal
{
    private NodWbsGrafic() { }

    public Guid VersiuneId { get; private set; }
    public string Cheie { get; private set; } = string.Empty;
    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public string? CheieParinte { get; private set; }
    public int Ordine { get; private set; }

    public static NodWbsGrafic Din(Guid versiuneId, NodWbsCanonic n) => new()
    {
        VersiuneId = versiuneId,
        Cheie = n.Cheie,
        Cod = n.Cod,
        Denumire = n.Denumire,
        CheieParinte = n.CheieParinte,
        Ordine = n.Ordine,
    };

    public NodWbsCanonic Canonic() => new(Cheie, Cod, Denumire, CheieParinte, Ordine);
}

/// <summary>
/// Activitatea unei versiuni. Legăturile, codurile, resursele și pașii sunt JSON (se citesc doar cu activitatea). Fără jurnal
/// de modificări: e copia fișierului importat.
/// </summary>
public sealed class ActivitateGrafic : EntitateFirma, IFaraJurnal
{
    private ActivitateGrafic() { }

    public Guid VersiuneId { get; private set; }
    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public string? CheieWbs { get; private set; }
    public TipActivitate Tip { get; private set; }
    public StadiuActivitate Stadiu { get; private set; }
    public decimal Procent { get; private set; }
    public DateOnly StartPlanificat { get; private set; }
    public DateOnly SfarsitPlanificat { get; private set; }
    public DateOnly? Start { get; private set; }
    public DateOnly? Sfarsit { get; private set; }
    public DateOnly? StartReal { get; private set; }
    public DateOnly? SfarsitReal { get; private set; }
    public DateOnly? StartTarziu { get; private set; }
    public DateOnly? SfarsitTarziu { get; private set; }
    public decimal DurataZile { get; private set; }
    public decimal DurataRamasaZile { get; private set; }
    public decimal? RezervaTotalaZile { get; private set; }
    public bool Critica { get; private set; }
    public string? Calendar { get; private set; }
    public string? Constrangere { get; private set; }
    public decimal CostPlanificat { get; private set; }
    public decimal CostRealizat { get; private set; }
    public decimal CostRamas { get; private set; }
    public string DependenteJson { get; private set; } = "[]";
    public string CoduriJson { get; private set; } = "{}";
    public string ResurseJson { get; private set; } = "[]";
    public string PasiJson { get; private set; } = "[]";

    public IReadOnlyList<Dependenta> Dependente => JsonSerializer.Deserialize<List<Dependenta>>(DependenteJson, SerializareGrafic.Optiuni) ?? [];
    public IReadOnlyDictionary<string, string> Coduri => JsonSerializer.Deserialize<Dictionary<string, string>>(CoduriJson, SerializareGrafic.Optiuni) ?? [];
    public IReadOnlyList<ResursaActivitate> Resurse => JsonSerializer.Deserialize<List<ResursaActivitate>>(ResurseJson, SerializareGrafic.Optiuni) ?? [];
    public IReadOnlyList<PasActivitate> Pasi => JsonSerializer.Deserialize<List<PasActivitate>>(PasiJson, SerializareGrafic.Optiuni) ?? [];

    public static ActivitateGrafic Din(Guid versiuneId, ActivitateCanonica a) => new()
    {
        VersiuneId = versiuneId,
        Cod = GraficExecutie.Taie(a.Cod, 100) ?? string.Empty,
        Denumire = GraficExecutie.Taie(a.Denumire, 500) ?? string.Empty,
        CheieWbs = a.CodWbs,
        Tip = a.Tip,
        Stadiu = a.Stadiu,
        Procent = a.Procent,
        StartPlanificat = a.StartPlanificat,
        SfarsitPlanificat = a.SfarsitPlanificat,
        Start = a.Start,
        Sfarsit = a.Sfarsit,
        StartReal = a.StartReal,
        SfarsitReal = a.SfarsitReal,
        StartTarziu = a.StartTarziu,
        SfarsitTarziu = a.SfarsitTarziu,
        DurataZile = a.DurataZile,
        DurataRamasaZile = a.DurataRamasaZile,
        RezervaTotalaZile = a.RezervaTotalaZile,
        Critica = a.Critica,
        Calendar = GraficExecutie.Taie(a.Calendar, 200),
        Constrangere = GraficExecutie.Taie(a.Constrangere, 100),
        CostPlanificat = Math.Round(a.CostPlanificat, 2, MidpointRounding.AwayFromZero),
        CostRealizat = Math.Round(a.CostRealizat, 2, MidpointRounding.AwayFromZero),
        CostRamas = Math.Round(a.CostRamas, 2, MidpointRounding.AwayFromZero),
        DependenteJson = JsonSerializer.Serialize(a.Dependente, SerializareGrafic.Optiuni),
        CoduriJson = JsonSerializer.Serialize(a.CoduriActivitate, SerializareGrafic.Optiuni),
        ResurseJson = JsonSerializer.Serialize(a.Resurse ?? [], SerializareGrafic.Optiuni),
        PasiJson = JsonSerializer.Serialize(a.Pasi ?? [], SerializareGrafic.Optiuni),
    };

    /// <summary>Activitatea în modelul canonic, fără resurse / pași (pentru totaluri și comparații: costurile vin din coloane).</summary>
    public ActivitateCanonica Canonic() => new(
        Cod, Denumire, CheieWbs, StartPlanificat, SfarsitPlanificat, DurataZile,
        Tip is TipActivitate.JalonInceput or TipActivitate.JalonSfarsit, [], new Dictionary<string, string>(),
        Tip, Stadiu, Procent, Start, Sfarsit, StartReal, SfarsitReal, StartTarziu, SfarsitTarziu, DurataRamasaZile, RezervaTotalaZile,
        Critica, Calendar, Constrangere,
        [new ResursaActivitate(string.Empty, string.Empty, TipResursaGrafic.Cheltuiala, null, 0, 0, 0, 0, CostPlanificat, CostRealizat, CostRamas)]);
}

public static class SerializareGrafic
{
    public static readonly JsonSerializerOptions Optiuni = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
