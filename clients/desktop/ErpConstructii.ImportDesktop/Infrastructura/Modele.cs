namespace ErpConstructii.ImportDesktop.Infrastructura;

// Contractele JSON ale API-ului (aceleași ca în clients/web/src/api). Nume camelCase prin JsonSerializerDefaults.Web.

public sealed record CampCitit<T>(T? Valoare, string Certitudine, string? TextSursa)
{
    public bool DeVerificat => Certitudine != "Sigur";
}

public sealed record Problema(string Severitate, string Locatie, string Mesaj);

public sealed record Firma(Guid Id, string Denumire, string? Cui);

public sealed record Profil(Guid Id, string Email, string Nume, Guid? FirmaActiva, List<Firma> Firme);

public sealed record Proiect(Guid Id, string Cod, string Denumire, string Stare)
{
    public string Eticheta => $"{Cod} — {Denumire}";
}

public sealed record ProiectPropus(Guid Id, string Cod, string Denumire, decimal Scor, string Certitudine);

public sealed record PartenerGasit(Guid Id, string Denumire, string? Cui);

// ------------------------------------------------------------------ centrul de import

public sealed record PaginaImport(int Pagina, string Tip, string Descriere, string? Numar);

public sealed record DocumentPropus(string Tip, List<int> Pagini, string? Numar, DateOnly? Data);

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
    string? Observatii);

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
    string? FurnizorTelefon, List<LinieNotaConfirmata> Linii, string? Observatii);

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

public sealed record ConfirmaNecesar(string? NumarExtern, DateOnly DataDocument, string CompartimentSantier, List<LinieNecesarConfirmata> Linii);

/// <summary>Răspunsul la confirmarea unei note de comandă / unui necesar: numărul de sistem.</summary>
public sealed record DocumentInregistrat(Guid Id, string Numar);
