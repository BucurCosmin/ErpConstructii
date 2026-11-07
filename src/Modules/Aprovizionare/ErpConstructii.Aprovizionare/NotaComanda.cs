using System.Text.Json;
using System.Text.Json.Serialization;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Aprovizionare;

/// <param name="Termen">Termenul de livrare, așa cum e scris pe comandă (dată sau text, ex. „imediat”).</param>
/// <param name="PretUnitar">Prețul negociat, dacă apare pe comandă (formularul uzual nu îl are).</param>
/// <param name="Observatii">Observațiile de pe linia necesarului (la notele generate din necesar).</param>
public sealed record LinieNotaComanda(string Denumire, string UnitateMasura, decimal Cantitate, string? Termen, decimal? PretUnitar, string? Observatii = null);

/// <summary>
/// Starea notei de comandă (D-37). Cea generată din necesar trece prin DeAcceptat → Acceptata (ajunge la achiziții, în
/// „Necesar de achiziție”) sau Respinsa; la achiziții primește furnizorul și devine Emisa. Cele emise direct sau importate
/// sunt Emisa de la început. Doar Emisa / Respinsa sunt blocate (regula 6).
/// </summary>
public enum StareNotaComanda
{
    DeAcceptat,
    Acceptata,
    Respinsa,
    Emisa,
}

/// <summary>O linie a notei acceptate trimisă la furnizor: indexul în nota sursă și cât se comandă acum.</summary>
public sealed record LinieDeComandat(int Index, string? Denumire, decimal Cantitate, string? Termen, decimal? PretUnitar);

/// <summary>Furnizorul și condițiile cu care achizițiile emit nota acceptată.</summary>
public sealed record DateComandaFurnizor(
    DateOnly Data,
    string? Lucrare,
    Guid? FurnizorId,
    string FurnizorDenumire,
    string? FurnizorCui,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    string? FurnizorEmail,
    ConditiiComanda? Conditii,
    string? Observatii);

public enum ReceptieComanda
{
    LaFurnizor,
    LaPrimire,
}

public enum ClasaCalitateComanda
{
    Clasa4,
    BestComercial,
}

public enum TransportComanda
{
    Auto,
    Cfr,
}

public enum PlataComanda
{
    Op,
    FilaCec,
    Alte,
}

/// <summary>Condițiile bifate pe formularul GC 007 (recepție, clasă de calitate, transport, plată); necompletat = nebifat.</summary>
public sealed record ConditiiComanda(ReceptieComanda? Receptie, ClasaCalitateComanda? ClasaCalitate, TransportComanda? Transport, PlataComanda? Plata)
{
    public static readonly ConditiiComanda Niciuna = new(null, null, null, null);
}

/// <summary>Datele unei comenzi către furnizor, verificate de utilizator.</summary>
/// <param name="FurnizorAdresa">Datele de contact ale furnizorului tipărite pe comandă (copiate la emitere: documentul nu se schimbă dacă se schimbă nomenclatorul).</param>
/// <param name="NecesarId">Necesarul de materiale din care provine comanda, dacă e cazul.</param>
public sealed record DateNotaComanda(
    string? NumarExtern,
    DateOnly Data,
    Guid ProiectId,
    string ProiectCod,
    string? Lucrare,
    Guid? FurnizorId,
    string FurnizorDenumire,
    string? FurnizorCui,
    IReadOnlyList<LinieNotaComanda> Linii,
    string? Observatii,
    string? FurnizorAdresa = null,
    string? FurnizorTelefon = null,
    string? FurnizorEmail = null,
    ConditiiComanda? Conditii = null,
    Guid? NecesarId = null);

/// <summary>
/// Notă de comandă către furnizor (formularul GC 007): ce materiale se cer, cui și pentru ce lucrare. Urmează necesarului
/// de materiale aprobat și deschide lanțul notă de comandă → recepție (NIR) → factură, folosit la potrivirea în 3 puncte (REQ-FCT-09).
/// Se emite în sistem (număr NC/AAAA/NNNN, document Word din șablonul firmei) sau se importă cea emisă pe hârtie
/// (scanare / Word), care își păstrează numărul de pe document. Imuabilă după înregistrare (regula 6).
/// </summary>
public sealed class NotaComanda : EntitateFirma, IDocumentCuStare
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private IReadOnlyList<LinieNotaComanda>? _linii;

    private NotaComanda() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"NC/{An}/{Numar:D4}";
    public string? NumarExtern { get; private set; }
    public DateOnly Data { get; private set; }
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;

    /// <summary>„Obiect / Lucrare” de pe comandă (coduri de contract, „STOC”…), păstrat ca pe document.</summary>
    public string? Lucrare { get; private set; }

    public Guid? FurnizorId { get; private set; }
    public string FurnizorDenumire { get; private set; } = string.Empty;
    public string? FurnizorCui { get; private set; }
    public string? FurnizorAdresa { get; private set; }
    public string? FurnizorTelefon { get; private set; }
    public string? FurnizorEmail { get; private set; }
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieNotaComanda> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieNotaComanda>>(LiniiJson) ?? [];
    public string? ConditiiJson { get; private set; }

    public ConditiiComanda Conditii =>
        ConditiiJson is null ? ConditiiComanda.Niciuna : JsonSerializer.Deserialize<ConditiiComanda>(ConditiiJson, Json) ?? ConditiiComanda.Niciuna;

    public string? Observatii { get; private set; }
    public SursaDocument Sursa { get; private set; }
    public Guid? DocumentOriginalId { get; private set; }
    public Guid? NecesarId { get; private set; }

    /// <summary>Șablonul Word al firmei activ la emitere (documentul se regenerează mereu cu aceeași versiune).</summary>
    public Guid? SablonId { get; private set; }

    public int? VersiuneSablon { get; private set; }
    public Guid InregistrataDeId { get; private set; }
    public string InregistrataDeNume { get; private set; } = string.Empty;

    public StareNotaComanda Stare { get; private set; } = StareNotaComanda.Emisa;
    public Guid? AcceptataDeId { get; private set; }
    public string? AcceptataDeNume { get; private set; }
    public DateTimeOffset? AcceptataLa { get; private set; }
    public string? MotivRespingere { get; private set; }

    /// <summary>Nota acceptată din care s-a desprins această comandă (când liniile ei s-au împărțit pe mai mulți furnizori).</summary>
    public Guid? NotaSursaId { get; private set; }

    public bool EsteBlocat => Stare is StareNotaComanda.Emisa or StareNotaComanda.Respinsa;

    /// <summary>
    /// Nota generată automat la crearea unui necesar: materialele necesarului, fără furnizor, în așteptarea acceptării.
    /// Acceptarea ei aprobă și necesarul.
    /// </summary>
    public static Rezultat<NotaComanda> GenereazaDinNecesar(int an, int numar, NecesarMateriale necesar, Guid utilizatorId, string utilizatorNume)
    {
        if (an < 2000 || numar < 1)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Numărul notei de comandă nu este valid."));
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Utilizatorul care înregistrează nota de comandă este obligatoriu."));
        var linii = necesar.Linii.Select(l => new LinieNotaComanda(l.Denumire, l.UnitateMasura, l.Cantitate, null, null,
            string.IsNullOrWhiteSpace(l.Observatii) ? null : l.Observatii.Trim())).ToList();
        if (linii.Count == 0)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Necesarul nu are materiale."));

        var nota = Construieste(an, numar,
            new DateNotaComanda(null, necesar.Data, necesar.ProiectId, necesar.ProiectCod, necesar.Lucrare, null, string.Empty, null, linii,
                $"Din necesarul {necesar.NumarAfisat} ({necesar.CompartimentSantier})", NecesarId: necesar.Id),
            SursaDocument.Sistem, null, utilizatorId, utilizatorNume);
        nota.Stare = StareNotaComanda.DeAcceptat;
        return Rezultat.Ok(nota);
    }

    /// <summary>Necesarul neaprobat a fost modificat: nota încă neacceptată preia materialele lui.</summary>
    public Rezultat ActualizeazaDinNecesar(NecesarMateriale necesar)
    {
        if (Stare != StareNotaComanda.DeAcceptat || NecesarId != necesar.Id)
            return Rezultat.Esuat(Eroare.Validare("Doar nota de comandă încă neacceptată se actualizează din necesar."));
        LiniiJson = JsonSerializer.Serialize(necesar.Linii.Select(l => new LinieNotaComanda(l.Denumire, l.UnitateMasura, l.Cantitate, null, null,
            string.IsNullOrWhiteSpace(l.Observatii) ? null : l.Observatii.Trim())).ToList());
        _linii = null;
        Observatii = $"Din necesarul {necesar.NumarAfisat} ({necesar.CompartimentSantier})";
        return Rezultat.Ok();
    }

    public Rezultat Accepta(Guid utilizatorId, string utilizatorNume, DateTimeOffset moment)
    {
        if (Stare != StareNotaComanda.DeAcceptat)
            return Rezultat.Esuat(Eroare.Validare("Doar notele de comandă în așteptarea acceptării pot fi acceptate."));
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume) || utilizatorNume.Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Numele celui care acceptă este obligatoriu."));
        Stare = StareNotaComanda.Acceptata;
        AcceptataDeId = utilizatorId;
        AcceptataDeNume = utilizatorNume.Trim();
        AcceptataLa = moment;
        return Rezultat.Ok();
    }

    public Rezultat Respinge(Guid utilizatorId, string utilizatorNume, DateTimeOffset moment, string motiv)
    {
        if (Stare != StareNotaComanda.DeAcceptat)
            return Rezultat.Esuat(Eroare.Validare("Doar notele de comandă în așteptarea acceptării pot fi respinse."));
        if (string.IsNullOrWhiteSpace(motiv) || motiv.Length > 1000)
            return Rezultat.Esuat(Eroare.Validare("Motivul respingerii este obligatoriu (cel mult 1000 de caractere)."));
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume) || utilizatorNume.Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Numele celui care respinge este obligatoriu."));
        Stare = StareNotaComanda.Respinsa;
        AcceptataDeId = utilizatorId;
        AcceptataDeNume = utilizatorNume.Trim();
        AcceptataLa = moment;
        MotivRespingere = motiv.Trim();
        return Rezultat.Ok();
    }

    /// <summary>
    /// Achizițiile trimit nota acceptată (sau o parte din ea) la un furnizor. Dacă se comandă tot ce a rămas, nota însăși
    /// primește furnizorul și devine emisă (își păstrează numărul). Altfel liniile alese pleacă într-o notă nouă, emisă,
    /// cu numărul <paramref name="numarNou"/> (legată prin <see cref="NotaSursaId"/>), iar aici rămâne restul, pentru alt furnizor.
    /// Întoarce nota emisă (aceasta sau cea nouă).
    /// </summary>
    public Rezultat<NotaComanda> ComandaLaFurnizor(
        DateComandaFurnizor date, IReadOnlyList<LinieDeComandat> linii, int anNou, int numarNou,
        Guid? sablonId, int? versiuneSablon, Guid utilizatorId, string utilizatorNume)
    {
        if (Stare != StareNotaComanda.Acceptata)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Doar notele de comandă acceptate se trimit la furnizor."));
        if (linii is null || linii.Count == 0)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Alege cel puțin un material de comandat la acest furnizor."));
        if (linii.Select(l => l.Index).Distinct().Count() != linii.Count)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Un material apare de două ori."));
        var actuale = Linii;
        foreach (var l in linii)
        {
            if (l.Index < 0 || l.Index >= actuale.Count)
                return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Un material ales nu mai există pe nota de comandă."));
            var s = actuale[l.Index];
            if (l.Cantitate <= 0 || l.Cantitate > s.Cantitate)
                return Rezultat.Esuat<NotaComanda>(Eroare.Validare(
                    $"Cantitatea pentru „{s.Denumire}” trebuie să fie pozitivă și cel mult {s.Cantitate.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("ro-RO"))} {s.UnitateMasura}."));
        }

        var comandate = linii.OrderBy(l => l.Index).Select(l =>
        {
            var sursa = actuale[l.Index];
            return sursa with
            {
                Denumire = string.IsNullOrWhiteSpace(l.Denumire) ? sursa.Denumire : l.Denumire,
                Cantitate = l.Cantitate,
                Termen = l.Termen,
                PretUnitar = l.PretUnitar,
            };
        }).ToList();
        var dateComanda = new DateNotaComanda(null, date.Data, ProiectId, ProiectCod, string.IsNullOrWhiteSpace(date.Lucrare) ? Lucrare : date.Lucrare,
            date.FurnizorId, date.FurnizorDenumire ?? string.Empty, date.FurnizorCui, comandate, date.Observatii,
            date.FurnizorAdresa, date.FurnizorTelefon, date.FurnizorEmail, date.Conditii, NecesarId);
        var tot = linii.Count == actuale.Count && linii.All(l => l.Cantitate == actuale[l.Index].Cantitate);
        var probleme = Valideaza(tot ? An : anNou, tot ? Numar : numarNou, dateComanda, utilizatorId, utilizatorNume);
        if ((sablonId is null) != (versiuneSablon is null))
            probleme.Add("Șablonul și versiunea lui se dau împreună.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare(string.Join(" ", probleme)));

        var emisa = Construieste(tot ? An : anNou, tot ? Numar : numarNou, dateComanda, SursaDocument.Sistem, null, utilizatorId, utilizatorNume);
        if (tot)
        {
            // Nota însăși devine comanda către furnizor; acceptarea rămâne consemnată pe ea.
            Data = emisa.Data;
            Lucrare = emisa.Lucrare;
            FurnizorId = emisa.FurnizorId;
            FurnizorDenumire = emisa.FurnizorDenumire;
            FurnizorCui = emisa.FurnizorCui;
            FurnizorAdresa = emisa.FurnizorAdresa;
            FurnizorTelefon = emisa.FurnizorTelefon;
            FurnizorEmail = emisa.FurnizorEmail;
            LiniiJson = emisa.LiniiJson;
            _linii = null;
            ConditiiJson = emisa.ConditiiJson;
            Observatii = emisa.Observatii;
            SablonId = sablonId;
            VersiuneSablon = versiuneSablon;
            InregistrataDeId = emisa.InregistrataDeId;
            InregistrataDeNume = emisa.InregistrataDeNume;
            Stare = StareNotaComanda.Emisa;
            return Rezultat.Ok(this);
        }

        emisa.SablonId = sablonId;
        emisa.VersiuneSablon = versiuneSablon;
        emisa.NotaSursaId = Id;
        emisa.AcceptataDeId = AcceptataDeId;
        emisa.AcceptataDeNume = AcceptataDeNume;
        emisa.AcceptataLa = AcceptataLa;
        var ramase = actuale.Select((l, i) => l with { Cantitate = l.Cantitate - (linii.FirstOrDefault(x => x.Index == i)?.Cantitate ?? 0) })
            .Where(l => l.Cantitate > 0).ToList();
        LiniiJson = JsonSerializer.Serialize(ramase);
        _linii = null;
        return Rezultat.Ok(emisa);
    }

    /// <summary>Notă de comandă emisă în sistem; Word-ul se generează din șablonul firmei (dacă există).</summary>
    public static Rezultat<NotaComanda> Emite(
        int an, int numar, DateNotaComanda date, Guid? sablonId, int? versiuneSablon, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = Valideaza(an, numar, date, utilizatorId, utilizatorNume);
        if ((sablonId is null) != (versiuneSablon is null))
            probleme.Add("Șablonul și versiunea lui se dau împreună.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare(string.Join(" ", probleme)));

        var nota = Construieste(an, numar, date, SursaDocument.Sistem, null, utilizatorId, utilizatorNume);
        nota.SablonId = sablonId;
        nota.VersiuneSablon = versiuneSablon;
        return Rezultat.Ok(nota);
    }

    /// <summary>Notă de comandă emisă în afara sistemului, cu originalul arhivat.</summary>
    public static Rezultat<NotaComanda> Importa(
        int an, int numar, DateNotaComanda date, Guid documentOriginalId, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = Valideaza(an, numar, date, utilizatorId, utilizatorNume);
        if (documentOriginalId == Guid.Empty)
            probleme.Add("Documentul original este obligatoriu pentru o notă de comandă importată.");
        if (probleme.Count > 0)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare(string.Join(" ", probleme)));

        return Rezultat.Ok(Construieste(an, numar, date, SursaDocument.Import, documentOriginalId, utilizatorId, utilizatorNume));
    }

    private static List<string> Valideaza(int an, int numar, DateNotaComanda date, Guid utilizatorId, string utilizatorNume)
    {
        var probleme = new List<string>();
        if (an < 2000 || numar < 1)
            probleme.Add("Numărul notei de comandă nu este valid.");
        if (date.NumarExtern?.Length > 50)
            probleme.Add("Numărul de pe comandă nu poate depăși 50 de caractere.");
        if (date.Data.Year < 2000)
            probleme.Add("Data notei de comandă nu este validă.");
        if (date.ProiectId == Guid.Empty || string.IsNullOrWhiteSpace(date.ProiectCod))
            probleme.Add("Proiectul este obligatoriu (pentru stoc general, folosește proiectul de stoc al firmei).");
        if (string.IsNullOrWhiteSpace(date.FurnizorDenumire) || date.FurnizorDenumire.Length > 300)
            probleme.Add("Furnizorul este obligatoriu.");
        if (!string.IsNullOrWhiteSpace(date.FurnizorCui) && !ValidareFiscala.EsteCuiValid(date.FurnizorCui))
            probleme.Add($"CUI-ul furnizorului „{date.FurnizorCui}” nu este valid.");
        if (date.FurnizorAdresa?.Length > 500 || date.FurnizorTelefon?.Length > 100 || date.FurnizorEmail?.Length > 200)
            probleme.Add("Datele de contact ale furnizorului sunt prea lungi.");
        if (date.Linii.Count == 0 || date.Linii.Any(l => string.IsNullOrWhiteSpace(l.Denumire) || string.IsNullOrWhiteSpace(l.UnitateMasura) || l.Cantitate <= 0))
            probleme.Add("Nota de comandă trebuie să conțină materiale cu denumire, UM și cantitate pozitivă.");
        if (date.Linii.Any(l => l.Denumire?.Length > 300 || l.UnitateMasura?.Length > 30 || l.Termen?.Length > 100))
            probleme.Add("Denumirea materialului (300), UM-ul (30) sau termenul (100 de caractere) sunt prea lungi.");
        if (date.Observatii?.Length > 1000)
            probleme.Add("Observațiile nu pot depăși 1000 de caractere.");
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            probleme.Add("Utilizatorul care înregistrează nota de comandă este obligatoriu.");
        return probleme;
    }

    private static NotaComanda Construieste(
        int an, int numar, DateNotaComanda date, SursaDocument sursa, Guid? documentOriginalId, Guid utilizatorId, string utilizatorNume) => new()
    {
        An = an,
        Numar = numar,
        NumarExtern = Gol(date.NumarExtern),
        Data = date.Data,
        ProiectId = date.ProiectId,
        ProiectCod = date.ProiectCod.Trim(),
        Lucrare = Gol(date.Lucrare) is { } lucrare ? lucrare[..Math.Min(lucrare.Length, 500)] : null,
        FurnizorId = date.FurnizorId,
        FurnizorDenumire = date.FurnizorDenumire.Trim(),
        FurnizorCui = string.IsNullOrWhiteSpace(date.FurnizorCui) ? null : ValidareFiscala.NormalizeazaCui(date.FurnizorCui),
        FurnizorAdresa = Gol(date.FurnizorAdresa),
        FurnizorTelefon = Gol(date.FurnizorTelefon),
        FurnizorEmail = Gol(date.FurnizorEmail),
        LiniiJson = JsonSerializer.Serialize(date.Linii.Select(l => l with
        {
            Denumire = l.Denumire.Trim(),
            UnitateMasura = l.UnitateMasura.Trim(),
            Termen = Gol(l.Termen),
        }).ToList()),
        ConditiiJson = date.Conditii is null || date.Conditii == ConditiiComanda.Niciuna ? null : JsonSerializer.Serialize(date.Conditii, Json),
        Observatii = Gol(date.Observatii),
        Sursa = sursa,
        DocumentOriginalId = documentOriginalId,
        NecesarId = date.NecesarId,
        InregistrataDeId = utilizatorId,
        InregistrataDeNume = utilizatorNume.Trim(),
    };

    private static string? Gol(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
