namespace ErpConstructii.Facturi;

public enum StareMesajEFactura
{
    InPrelucrare,
    Validata,
    Eroare,
}

public sealed record RaspunsIncarcare(string IndexIncarcare);

public sealed record StareIncarcare(StareMesajEFactura Stare, string? IdDescarcare, string? Mesaj);

public enum TipMesajSpv
{
    FacturaPrimita,
    FacturaTrimisa,
    Eroare,
    MesajCumparator,
}

public sealed record MesajSpv(string Id, TipMesajSpv Tip, string CifEmitent, string CifBeneficiar, DateTimeOffset DataCreare, string Detalii);

/// <summary>
/// Clientul pentru API-ul RO e-Factura al ANAF (REQ-FCT-03, REQ-FCT-07).
/// Implementarea (faza 1) stă într-un proiect separat de integrări, ca schimbările de API ale
/// statului să nu atingă modulul.
///
/// Note pentru implementare — DE VERIFICAT în documentația oficială ANAF înainte de cod:
///  - autentificare OAuth2 cu certificatul digital calificat al firmei (aplicație înregistrată în
///    portalul ANAF; token de acces + refresh token);
///  - medii separate de test și producție;
///  - operații: încărcare XML (standard UBL / CN, cu CIF-ul emitentului), stare mesaj după index,
///    listă mesaje pe ultimele N zile, descărcare arhivă ZIP (XML + semnătura ANAF),
///    validare XML și transformare XML → PDF (servicii publice);
///  - limite de apeluri: descărcarea automată rulează periodic (ex. orar), nu în buclă.
/// Arhiva ZIP descărcată se păstrează neschimbată ca original (REQ-FCT-11).
/// </summary>
public interface IClientEFactura
{
    Task<RaspunsIncarcare> IncarcaAsync(string cifEmitent, byte[] xmlUbl, CancellationToken ct = default);

    Task<StareIncarcare> StareAsync(string indexIncarcare, CancellationToken ct = default);

    Task<IReadOnlyList<MesajSpv>> ListaMesajeAsync(string cif, int zile, CancellationToken ct = default);

    /// <summary>Arhiva ZIP originală (XML + semnătura ANAF).</summary>
    Task<byte[]> DescarcaAsync(string idDescarcare, CancellationToken ct = default);
}

/// <summary>
/// Verificarea statutului de plătitor de TVA al clientului (serviciul public ANAF de interogare TVA),
/// folosită la propunerea taxării inverse (REQ-FCT-02).
/// </summary>
public interface IVerificareTva
{
    Task<bool?> EstePlatitorTvaAsync(string cui, DateOnly data, CancellationToken ct = default);
}
