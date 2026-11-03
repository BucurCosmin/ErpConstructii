using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Aprovizionare;

public enum StareNotaComanda
{
    InAsteptareAprobare,
    Aprobata,
    Respinsa,
}

/// <summary>De unde vine nota: emisă în sistem sau importată dintr-un document extern (hârtie scanată, Word).</summary>
public enum SursaNotaComanda
{
    Sistem,
    Import,
}

public sealed record LinieNotaComanda(
    string Denumire,
    string UnitateMasura,
    decimal Cantitate,
    string Observatii,
    Guid? ArticolDevizId);

/// <summary>Notă emisă pe proiect, cu numerotare anuală și flux de aprobare (REQ-APV-01..02).</summary>
public sealed class NotaComanda : EntitateFirma, IDocumentCuStare
{
    private NotaComanda() { }

    private NotaComanda(
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
        IReadOnlyCollection<LinieNotaComanda> linii)
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
        Stare = StareNotaComanda.InAsteptareAprobare;
    }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"NC/{An}/{Numar:D4}";
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
    public IReadOnlyList<LinieNotaComanda> Linii => JsonSerializer.Deserialize<List<LinieNotaComanda>>(LiniiJson) ?? [];
    public StareNotaComanda Stare { get; private set; }
    public SursaNotaComanda Sursa { get; private set; } = SursaNotaComanda.Sistem;

    /// <summary>Numărul de pe documentul original (pentru notele importate), păstrat lângă numărul de sistem.</summary>
    public string? NumarExtern { get; private set; }

    /// <summary>Documentul original importat (scanare / Word), arhivat neschimbat.</summary>
    public Guid? DocumentOriginalId { get; private set; }

    public Guid? DecisaDeId { get; private set; }
    public string? DecisaDeNume { get; private set; }
    public DateTimeOffset? DecisaLa { get; private set; }
    public string? MotivRespingere { get; private set; }
    public bool EsteBlocat => true;

    public static Rezultat<NotaComanda> Emite(
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
        IReadOnlyCollection<LinieNotaComanda> linii) =>
        Creeaza(an, numar, proiectId, proiectCod, lucrare, compartimentSantier, data, emitentId, emitentNume,
            sablonId, versiuneSablon, linii, cereSablon: true);

    private static Rezultat<NotaComanda> Creeaza(
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
        IReadOnlyCollection<LinieNotaComanda> linii,
        bool cereSablon)
    {
        if (an < 2000 || numar < 1)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Numărul notei de comandă nu este valid."));
        if (proiectId == Guid.Empty || string.IsNullOrWhiteSpace(proiectCod) ||
            string.IsNullOrWhiteSpace(lucrare) || string.IsNullOrWhiteSpace(compartimentSantier))
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Proiectul, lucrarea și compartimentul / șantierul sunt obligatorii."));
        if (compartimentSantier.Length > 300)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Compartimentul / șantierul nu poate depăși 300 de caractere."));
        if (emitentId == Guid.Empty || string.IsNullOrWhiteSpace(emitentNume) || emitentNume.Length > 200)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Emitentul este obligatoriu."));
        if (cereSablon && (sablonId == Guid.Empty || versiuneSablon < 1))
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Versiunea șablonului este obligatorie."));
        if (linii.Count == 0 || linii.Any(l =>
                string.IsNullOrWhiteSpace(l.Denumire) ||
                string.IsNullOrWhiteSpace(l.UnitateMasura) ||
                l.Cantitate <= 0))
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Nota trebuie să conțină materiale cu unitate de măsură și cantitate pozitivă."));

        return Rezultat.Ok(new NotaComanda(
            an, numar, proiectId, proiectCod.Trim(), lucrare.Trim(), compartimentSantier.Trim(), data, emitentId, emitentNume.Trim(), sablonId, versiuneSablon, linii));
    }

    /// <summary>
    /// Notă emisă în afara sistemului (REQ-FRM-10): primește număr de sistem ca orice notă, păstrează numărul și
    /// data de pe documentul original și intră în același flux de aprobare (viza de pe hârtie nu o aprobă automat).
    /// Emitentul este cel care importă; el nu o poate aproba (separarea atribuțiilor).
    /// Fără șablon activ (<paramref name="sablonId"/> gol), nota nu poate fi regenerată în Word; rămâne originalul.
    /// </summary>
    public static Rezultat<NotaComanda> Importa(
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
        IReadOnlyCollection<LinieNotaComanda> linii,
        Guid documentOriginalId)
    {
        if (numarExtern?.Length > 50)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Numărul de pe documentul original nu poate depăși 50 de caractere."));
        if (documentOriginalId == Guid.Empty)
            return Rezultat.Esuat<NotaComanda>(Eroare.Validare("Documentul original este obligatoriu pentru o notă importată."));

        // Șablonul e opțional: documentul de referință al unei note importate este originalul arhivat.
        var rezultat = Creeaza(an, numar, proiectId, proiectCod, lucrare, compartimentSantier, dataDocument,
            emitentId, emitentNume, sablonId, versiuneSablon, linii, cereSablon: false);
        if (rezultat.Esec)
            return rezultat;

        var nota = rezultat.Valoare;
        nota.Sursa = SursaNotaComanda.Import;
        nota.NumarExtern = string.IsNullOrWhiteSpace(numarExtern) ? null : numarExtern.Trim();
        nota.DocumentOriginalId = documentOriginalId;
        return Rezultat.Ok(nota);
    }

    public Rezultat Aproba(Guid utilizatorId, string utilizatorNume, DateTimeOffset moment)
    {
        if (Stare != StareNotaComanda.InAsteptareAprobare)
            return Rezultat.Esuat(Eroare.Validare("Doar notele aflate în așteptarea aprobării pot fi aprobate."));
        if (utilizatorId == EmitentId)
            return Rezultat.Esuat(Eroare.Interzis("Persoana care a emis nota nu o poate aproba."));
        if (string.IsNullOrWhiteSpace(utilizatorNume) || utilizatorNume.Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Numele aprobatorului este obligatoriu."));

        Stare = StareNotaComanda.Aprobata;
        DecisaDeId = utilizatorId;
        DecisaDeNume = utilizatorNume.Trim();
        DecisaLa = moment;
        MotivRespingere = null;
        return Rezultat.Ok();
    }

    public Rezultat Respinge(Guid utilizatorId, string utilizatorNume, DateTimeOffset moment, string motiv)
    {
        if (Stare != StareNotaComanda.InAsteptareAprobare)
            return Rezultat.Esuat(Eroare.Validare("Doar notele aflate în așteptarea aprobării pot fi respinse."));
        if (string.IsNullOrWhiteSpace(motiv))
            return Rezultat.Esuat(Eroare.Validare("Motivul respingerii este obligatoriu."));
        if (motiv.Length > 1000)
            return Rezultat.Esuat(Eroare.Validare("Motivul respingerii nu poate depăși 1000 de caractere."));
        if (string.IsNullOrWhiteSpace(utilizatorNume) || utilizatorNume.Length > 200)
            return Rezultat.Esuat(Eroare.Validare("Numele decidentului este obligatoriu."));

        Stare = StareNotaComanda.Respinsa;
        DecisaDeId = utilizatorId;
        DecisaDeNume = utilizatorNume.Trim();
        DecisaLa = moment;
        MotivRespingere = motiv.Trim();
        return Rezultat.Ok();
    }
}
