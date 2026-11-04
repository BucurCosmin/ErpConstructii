using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Aprovizionare;

public enum StareNecesarMateriale
{
    InAsteptareAprobare,
    Aprobata,
    Respinsa,
}

/// <summary>De unde vine documentul: emis în sistem sau importată dintr-un document extern (hârtie scanată, Word).</summary>
public enum SursaDocument
{
    Sistem,
    Import,
}

public sealed record LinieNecesarMateriale(
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    string Observatii,
    Guid? ArticolDevizId);

/// <summary>Necesar de materiale emis pe proiect (cererea șantierului), cu numerotare anuală și flux de aprobare (REQ-APV-01..02).</summary>
public sealed class NecesarMateriale : EntitateFirma, IDocumentCuStare
{
    private NecesarMateriale() { }

    private NecesarMateriale(
        int an,
        int numar,
        Guid proiectId,
        string proiectCod,
        string lucrare,
        string compartimentSantier,
        DateOnly data,
        Guid emitentId,
        string emitentNume,
        Guid sablonId,
        int versiuneSablon,
        IReadOnlyCollection<LinieNecesarMateriale> linii)
    {
        An = an;
        Numar = numar;
        ProiectId = proiectId;
        ProiectCod = proiectCod;
        Lucrare = lucrare;
        CompartimentSantier = compartimentSantier;
        Data = data;
        EmitentId = emitentId;
        EmitentNume = emitentNume;
        SablonId = sablonId;
        VersiuneSablon = versiuneSablon;
        LiniiJson = JsonSerializer.Serialize(linii);
        Stare = StareNecesarMateriale.InAsteptareAprobare;
    }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"NM/{An}/{Numar:D4}";
    public Guid ProiectId { get; private set; }
    public string ProiectCod { get; private set; } = string.Empty;
    public string Lucrare { get; private set; } = string.Empty;
    public string CompartimentSantier { get; private set; } = string.Empty;
    public DateOnly Data { get; private set; }
    public Guid EmitentId { get; private set; }
    public string EmitentNume { get; private set; } = string.Empty;
    public Guid SablonId { get; private set; }
    public int VersiuneSablon { get; private set; }
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieNecesarMateriale> Linii => JsonSerializer.Deserialize<List<LinieNecesarMateriale>>(LiniiJson) ?? [];
    public StareNecesarMateriale Stare { get; private set; }
    public SursaDocument Sursa { get; private set; } = SursaDocument.Sistem;

    /// <summary>Numărul de pe documentul original (pentru necesarele importate), păstrat lângă numărul de sistem.</summary>
    public string? NumarExtern { get; private set; }

    /// <summary>Documentul original importat (scanare / Word), arhivat neschimbat.</summary>
    public Guid? DocumentOriginalId { get; private set; }

    public Guid? DecisaDeId { get; private set; }
    public string? DecisaDeNume { get; private set; }
    public DateTimeOffset? DecisaLa { get; private set; }
    public string? MotivRespingere { get; private set; }
    public bool EsteBlocat => true;

    public static Rezultat<NecesarMateriale> Emite(
        int an,
        int numar,
        Guid proiectId,
        string proiectCod,
        string lucrare,
        string compartimentSantier,
        DateOnly data,
        Guid emitentId,
        string emitentNume,
        Guid sablonId,
        int versiuneSablon,
        IReadOnlyCollection<LinieNecesarMateriale> linii) =>
        Creeaza(an, numar, proiectId, proiectCod, lucrare, compartimentSantier, data, emitentId, emitentNume,
            sablonId, versiuneSablon, linii, cereSablon: true);

    private static Rezultat<NecesarMateriale> Creeaza(
        int an,
        int numar,
        Guid proiectId,
        string proiectCod,
        string lucrare,
        string compartimentSantier,
        DateOnly data,
        Guid emitentId,
        string emitentNume,
        Guid sablonId,
        int versiuneSablon,
        IReadOnlyCollection<LinieNecesarMateriale> linii,
        bool cereSablon)
    {
        if (an < 2000 || numar < 1)
            return Rezultat.Esuat<NecesarMateriale>(Eroare.Validare("Numărul necesarului de materiale nu este valid."));
        if (proiectId == Guid.Empty || string.IsNullOrWhiteSpace(proiectCod) ||
            string.IsNullOrWhiteSpace(lucrare) || string.IsNullOrWhiteSpace(compartimentSantier))
            return Rezultat.Esuat<NecesarMateriale>(Eroare.Validare("Proiectul, lucrarea și compartimentul / șantierul sunt obligatorii."));
        if (compartimentSantier.Length > 300)
            return Rezultat.Esuat<NecesarMateriale>(Eroare.Validare("Compartimentul / șantierul nu poate depăși 300 de caractere."));
        if (emitentId == Guid.Empty || string.IsNullOrWhiteSpace(emitentNume) || emitentNume.Length > 200)
            return Rezultat.Esuat<NecesarMateriale>(Eroare.Validare("Emitentul este obligatoriu."));
        if (cereSablon && (sablonId == Guid.Empty || versiuneSablon < 1))
            return Rezultat.Esuat<NecesarMateriale>(Eroare.Validare("Versiunea șablonului este obligatorie."));
        if (linii.Count == 0 || linii.Any(l =>
                string.IsNullOrWhiteSpace(l.Denumire) ||
                string.IsNullOrWhiteSpace(l.UnitateMasura) ||
                l.Cantitate <= 0))
            return Rezultat.Esuat<NecesarMateriale>(Eroare.Validare("Necesarul trebuie să conțină materiale cu unitate de măsură și cantitate pozitivă."));

        return Rezultat.Ok(new NecesarMateriale(
            an, numar, proiectId, proiectCod.Trim(), lucrare.Trim(), compartimentSantier.Trim(), data, emitentId, emitentNume.Trim(), sablonId, versiuneSablon, linii));
    }

    /// <summary>
    /// Necesar emis în afara sistemului (REQ-FRM-10): primește număr de sistem ca orice necesar, păstrează numărul și
    /// data de pe documentul original și intră în același flux de aprobare (viza de pe hârtie nu o aprobă automat).
    /// Emitentul este cel care importă; el nu o poate aproba (separarea atribuțiilor).
    /// Fără șablon activ (<paramref name="sablonId"/> gol), necesarul nu poate fi regenerată în Word; rămâne originalul.
    /// </summary>
    public static Rezultat<NecesarMateriale> Importa(
        int an,
        int numar,
        string? numarExtern,
        Guid proiectId,
        string proiectCod,
        string lucrare,
        string compartimentSantier,
        DateOnly dataDocument,
        Guid emitentId,
        string emitentNume,
        Guid sablonId,
        int versiuneSablon,
        IReadOnlyCollection<LinieNecesarMateriale> linii,
        Guid documentOriginalId)
    {
        if (numarExtern?.Length > 50)
            return Rezultat.Esuat<NecesarMateriale>(Eroare.Validare("Numărul de pe documentul original nu poate depăși 50 de caractere."));
        if (documentOriginalId == Guid.Empty)
            return Rezultat.Esuat<NecesarMateriale>(Eroare.Validare("Documentul original este obligatoriu pentru un necesar importat."));

        // Șablonul e opțional: documentul de referință al unui necesar importat este originalul arhivat.
        var rezultat = Creeaza(an, numar, proiectId, proiectCod, lucrare, compartimentSantier, dataDocument,
            emitentId, emitentNume, sablonId, versiuneSablon, linii, cereSablon: false);
        if (rezultat.Esec)
            return rezultat;

        var necesar = rezultat.Valoare;
        necesar.Sursa = SursaDocument.Import;
        necesar.NumarExtern = string.IsNullOrWhiteSpace(numarExtern) ? null : numarExtern.Trim();
        necesar.DocumentOriginalId = documentOriginalId;
        return Rezultat.Ok(necesar);
    }

    public Rezultat Aproba(Guid utilizatorId, string utilizatorNume, DateTimeOffset moment)
    {
        if (Stare != StareNecesarMateriale.InAsteptareAprobare)
            return Rezultat.Esuat(Eroare.Validare("Doar necesarele aflate în așteptarea aprobării pot fi aprobate."));
        if (utilizatorId == EmitentId)
            return Rezultat.Esuat(Eroare.Interzis("Persoana care a emis necesarul nu îl poate aproba."));
        if (string.IsNullOrWhiteSpace(utilizatorNume) || utilizatorNume.Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Numele aprobatorului este obligatoriu."));

        Stare = StareNecesarMateriale.Aprobata;
        DecisaDeId = utilizatorId;
        DecisaDeNume = utilizatorNume.Trim();
        DecisaLa = moment;
        MotivRespingere = null;
        return Rezultat.Ok();
    }

    public Rezultat Respinge(Guid utilizatorId, string utilizatorNume, DateTimeOffset moment, string motiv)
    {
        if (Stare != StareNecesarMateriale.InAsteptareAprobare)
            return Rezultat.Esuat(Eroare.Validare("Doar necesarele aflate în așteptarea aprobării pot fi respinse."));
        if (string.IsNullOrWhiteSpace(motiv))
            return Rezultat.Esuat(Eroare.Validare("Motivul respingerii este obligatoriu."));
        if (motiv.Length > 1000)
            return Rezultat.Esuat(Eroare.Validare("Motivul respingerii nu poate depăși 1000 de caractere."));
        if (string.IsNullOrWhiteSpace(utilizatorNume) || utilizatorNume.Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Numele decidentului este obligatoriu."));

        Stare = StareNecesarMateriale.Respinsa;
        DecisaDeId = utilizatorId;
        DecisaDeNume = utilizatorNume.Trim();
        DecisaLa = moment;
        MotivRespingere = motiv.Trim();
        return Rezultat.Ok();
    }
}
