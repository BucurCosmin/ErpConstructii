namespace ErpConstructii.ImportDesktop.Infrastructura;

// Contractele JSON ale API-ului (aceleași ca în clients/web/src/api). Nume camelCase prin JsonSerializerDefaults.Web.

public sealed record CampCitit<T>(T? Valoare, string Certitudine, string? TextSursa)
{
    public bool DeVerificat => Certitudine != "Sigur";
}

public sealed record Problema(string Severitate, string Locatie, string Mesaj);

public sealed record Firma(Guid Id, string Denumire, string? Cui);

public sealed record Profil(Guid Id, string Email, string Nume, Guid? FirmaActiva, List<Firma> Firme);

/// <param name="Tip">„Lucrare” sau „Sediu” (D-47: sediul firmei, pentru cheltuielile de sediu).</param>
public sealed record Proiect(Guid Id, string Cod, string Denumire, string Stare, string? Tip = null)
{
    public string Eticheta => $"{Cod} — {Denumire}{(Tip == "Sediu" ? " (sediu)" : string.Empty)}";
}

public sealed record ProiectPropus(Guid Id, string Cod, string Denumire, decimal Scor, string Certitudine);

public sealed record PartenerGasit(Guid Id, string Denumire, string? Cui);

// ------------------------------------------------------------------ centrul de import

public sealed record PaginaImport(int Pagina, string Tip, string Descriere, string? Numar);

public sealed record DocumentPropus(string Tip, List<int> Pagini, string? Numar, DateOnly? Data, int? Zona);

public sealed record AnalizaImport(
    string Cheie, string NumeFisier, bool Separabil, bool DinOcr, List<PaginaImport> Pagini, List<DocumentPropus> Documente, List<Problema> Probleme);

// ------------------------------------------------------------------ facturi

public sealed record LinieFacturaCitita(
    int Rand, CampCitit<string> Denumire, CampCitit<string> UnitateMasura, CampCitit<decimal?> Cantitate,
    CampCitit<decimal?> PretUnitar, CampCitit<decimal?> ValoareFaraTva, CampCitit<decimal?> CotaTva);

public sealed record InterpretareFactura(
    string NumeFisier,
    string Sursa,
    bool DinOcr,
    CampCitit<string> Numar,
    CampCitit<DateOnly?> DataEmitere,
    CampCitit<DateOnly?> DataScadenta,
    CampCitit<string> Moneda,
    CampCitit<string> FurnizorDenumire,
    CampCitit<string> FurnizorCui,
    CampCitit<decimal?> TotalFaraTva,
    CampCitit<decimal?> TotalTva,
    CampCitit<decimal?> TotalDePlata,
    List<LinieFacturaCitita> Linii,
    string? Referinta,
    PartenerGasit? FurnizorInNomenclator,
    ProiectPropus? ProiectPropus,
    List<Problema> Probleme,
    string? FacturaExistenta);

public sealed record LinieFacturaConfirmata(
    string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal ValoareFaraTva, decimal CotaTva, bool IntraInStoc);

public sealed record ConfirmaFactura(
    string Numar,
    DateOnly DataEmitere,
    DateOnly? DataScadenta,
    string Moneda,
    string FurnizorDenumire,
    string? FurnizorCui,
    bool AdaugaFurnizorInNomenclator,
    List<LinieFacturaConfirmata> Linii,
    decimal TotalFaraTva,
    decimal TotalTva,
    decimal TotalDePlata,
    string? Referinta,
    string? Observatii,
    bool DinUrma = false);

public sealed record FacturaInregistrata(Guid Id, string Numar, string FurnizorDenumire, string ProiectCod, decimal TotalDePlata);

// ------------------------------------------------------------------ note de comandă

public sealed record LinieNotaCitita(int Rand, CampCitit<string> Denumire, CampCitit<string> UnitateMasura, CampCitit<decimal?> Cantitate, string? Termen);

public sealed record InterpretareNotaComanda(
    string NumeFisier,
    bool DinOcr,
    CampCitit<string> NumarExtern,
    CampCitit<DateOnly?> Data,
    CampCitit<string> FurnizorDenumire,
    CampCitit<string> FurnizorCui,
    string? FurnizorAdresa,
    string? FurnizorTelefon,
    PartenerGasit? FurnizorGasit,
    CampCitit<string> Lucrare,
    ProiectPropus? ProiectPropus,
    List<LinieNotaCitita> Linii,
    List<Problema> Probleme,
    string? ComandaExistenta);

public sealed record LinieNotaConfirmata(string Denumire, string UnitateMasura, decimal Cantitate, string? Termen);

public sealed record ConfirmaNotaComanda(
    string? NumarExtern, DateOnly Data, string? Lucrare, string FurnizorDenumire, string? FurnizorCui, string? FurnizorAdresa,
    string? FurnizorTelefon, List<LinieNotaConfirmata> Linii, string? Observatii, bool DinUrma = false);

// ------------------------------------------------------------------ avize de însoțire (D-40)

public sealed record LinieAvizCitita(int Rand, CampCitit<string> Denumire, CampCitit<string> UnitateMasura, CampCitit<decimal?> Cantitate);

/// <summary>Nota de comandă emisă care pare a fi a avizului (numărul pomenit pe aviz, același furnizor).</summary>
public sealed record NotaComandaPropusa(Guid Id, string Numar, string? NumarExtern, DateOnly Data, Guid ProiectId, string ProiectCod, string FurnizorDenumire);

public sealed record InterpretareAviz(
    string NumeFisier,
    bool DinOcr,
    CampCitit<string> NumarExtern,
    CampCitit<DateOnly?> Data,
    CampCitit<string> FurnizorDenumire,
    CampCitit<string> FurnizorCui,
    PartenerGasit? FurnizorGasit,
    CampCitit<string> Lucrare,
    ProiectPropus? ProiectPropus,
    string? ReferintaComanda,
    NotaComandaPropusa? NotaComandaPropusa,
    List<LinieAvizCitita> Linii,
    List<Problema> Probleme,
    string? AvizExistent,
    List<int> Pagini);

public sealed record LinieAvizConfirmata(string Denumire, string UnitateMasura, decimal Cantitate);

/// <param name="NotaComandaId">Nota de comandă emisă a mărfii; null = se generează una din aviz (la documentele din urmă, niciuna).</param>
public sealed record ConfirmaAviz(
    string NumarExtern, DateOnly Data, string? Lucrare, string FurnizorDenumire, string? FurnizorCui, List<LinieAvizConfirmata> Linii,
    string? Observatii, Guid? NotaComandaId, bool DinUrma = false);

/// <summary>O notă de comandă din lista proiectului (GET /api/aprovizionare/note-comanda).</summary>
public sealed record NotaComandaLista(Guid Id, string Numar, string? NumarExtern, DateOnly Data, string FurnizorDenumire);

/// <summary>Alegerea notei de comandă a avizului: o notă emisă sau (Id null) una nouă, generată din aviz.</summary>
public sealed record OptiuneNota(Guid? Id, string Eticheta);

// ------------------------------------------------------------------ necesare de materiale

public sealed record LinieNecesarCitita(int Rand, CampCitit<string> Denumire, CampCitit<string> UnitateMasura, CampCitit<decimal?> Cantitate, CampCitit<string> Observatii);

public sealed record InterpretareNecesar(
    string NumeFisier,
    bool DinOcr,
    CampCitit<string> NumarExtern,
    CampCitit<DateOnly?> Data,
    CampCitit<string> CompartimentSantier,
    CampCitit<string> Lucrare,
    ProiectPropus? ProiectPropus,
    List<LinieNecesarCitita> Linii,
    List<Problema> Probleme,
    string? NecesarExistent);

public sealed record LinieNecesarConfirmata(string Denumire, string UnitateMasura, decimal Cantitate, string? Observatii, Guid? ArticolDevizId);

/// <param name="DinUrma">D-45: document vechi din arhivă (necesarul intră aprobat, fără nota „de acceptat”).</param>
/// <param name="SantierId">D-38: șantierul proiectului care cere materialele (sau <paramref name="CompartimentId"/>, nu amândouă).</param>
public sealed record ConfirmaNecesar(string? NumarExtern, DateOnly DataDocument, string CompartimentSantier, List<LinieNecesarConfirmata> Linii, bool DinUrma = false,
    Guid? SantierId = null, Guid? CompartimentId = null);

/// <summary>Șantierele proiectului (GET /api/proiecte/{id}/santiere).</summary>
public sealed record Santier(Guid Id, Guid ProiectId, string Denumire, string? Adresa, bool Activ);

/// <summary>Cine cere materialele: un șantier al proiectului ales, dintr-o listă.</summary>
public sealed record LocSolicitant(Guid Id, bool EsteSantier, string Denumire, string? Adresa)
{
    public string Eticheta => string.IsNullOrWhiteSpace(Adresa) ? Denumire : $"{Denumire} · {Adresa}";
}


/// <summary>D-48: primul număr de hârtie liber din seria proiectului (proiect + an).</summary>
public sealed record NumarLiber(long Numar, int An);

/// <summary>Răspunsul la confirmarea unei note de comandă / unui necesar: numărul de sistem.</summary>
public sealed record DocumentInregistrat(Guid Id, string Numar);

// ------------------------------------------------------------------ bonuri de consum

public sealed record LinieBonCitita(
    int Rand, CampCitit<string> Denumire, string? Cod, CampCitit<string> UnitateMasura, CampCitit<decimal?> Cantitate,
    CampCitit<decimal?> PretUnitar, CampCitit<decimal?> Valoare);

public sealed record InterpretareBonConsum(
    string NumeFisier,
    bool DinOcr,
    int NumarBonuri,
    int Zona,
    bool Gol,
    CampCitit<string> Numar,
    CampCitit<DateOnly?> Data,
    CampCitit<string> Lucrare,
    CampCitit<string> Gestiune,
    string? Primitor,
    string? Gestionar,
    string? SefCompartiment,
    List<LinieBonCitita> Linii,
    CampCitit<decimal?> Total,
    ProiectPropus? ProiectPropus,
    List<Problema> Probleme,
    string? BonExistent,
    string ObservatiiPropuse);

public sealed record MaterialSantier(Guid ArticolId, string Cod, string Denumire, string UnitateMasura, decimal Cantitate)
{
    public string Eticheta => $"{Cod} — {Denumire} ({Cantitate:0.###} {UnitateMasura} în șantier)";
}

public sealed record PropunereMaterial(Guid ArticolId, string Sursa);

public sealed record PotrivireBon(Guid? GestiuneId, List<MaterialSantier> Materiale, List<PropunereMaterial?> Propuneri, string? Mesaj);

public sealed record ArticolDeviz(Guid Cheie, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, string Obiect, string Categorie)
{
    public string Eticheta => $"{Simbol} — {Denumire}";
}

public sealed record DevizReferinta(Guid VersiuneId, string Denumire, string Tip, string Stare, List<ArticolDeviz> Articole);

public sealed record LinieConsumCerere(Guid ArticolId, decimal Cantitate, Guid? ArticolDevizCheie);

/// <param name="NumarExtern">Numărul de pe bonul de hârtie (D-48); numărul de sistem BC/AAAA/NNNN se dă automat.</param>
public sealed record ConsumCerere(DateOnly Data, List<LinieConsumCerere> Linii, string? Observatii, string? NumarExtern = null);
