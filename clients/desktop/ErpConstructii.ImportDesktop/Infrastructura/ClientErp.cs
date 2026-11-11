using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace ErpConstructii.ImportDesktop.Infrastructura;

/// <summary>O eroare raportată de server (mesajul e deja pentru utilizator, în română).</summary>
public sealed class EroareServer(string mesaj, HttpStatusCode cod) : Exception(mesaj)
{
    public HttpStatusCode Cod { get; } = cod;
}

/// <summary>
/// Clientul API-ului aplicației principale: aceleași endpoint-uri ca aplicația web (centrul de import, interpretarea și
/// confirmarea fiecărui tip de document). Reînnoiește singur access token-ul (10 min) cu refresh token-ul.
/// Certificatul autosemnat al producției (D-26) e acceptat doar dacă utilizatorul i-a confirmat amprenta.
/// </summary>
public sealed class ClientErp : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ClientOAuth _oauth;
    private readonly SemaphoreSlim _reinnoire = new(1, 1);
    private Tokenuri? _tokenuri;

    /// <param name="confirmaCertificat">Întreabă utilizatorul dacă acceptă un certificat necunoscut (gazdă, amprentă).</param>
    public ClientErp(Uri server, Setari setari, Func<string, string, bool> confirmaCertificat)
    {
        Server = server;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, erori) =>
                erori == SslPolicyErrors.None || cert is not null && CertificatAcceptat(server.Host, cert, setari, confirmaCertificat),
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) }; // OCR-ul unui fișier mare durează
        _oauth = new ClientOAuth(_http);
    }

    public Uri Server { get; }

    public bool Autentificat => _tokenuri is not null;

    public async Task AutentificaAsync(CancellationToken ct) => _tokenuri = await _oauth.AutentificaAsync(Server, ct);

    public async Task SchimbaFirmaAsync(Guid firmaId, CancellationToken ct)
    {
        var refresh = _tokenuri?.RefreshToken ?? throw new InvalidOperationException("Autentifică-te întâi.");
        _tokenuri = await _oauth.ReinnoiesteAsync(Server, refresh, firmaId, ct);
    }

    // ------------------------------------------------------------------ cereri

    public Task<Profil> ProfilAsync(CancellationToken ct) => GetAsync<Profil>("/api/identitate/eu", ct);

    public Task<List<string>> PermisiuniAsync(CancellationToken ct) => GetAsync<List<string>>("/api/identitate/eu/permisiuni", ct);

    public Task<List<Proiect>> ProiecteAsync(CancellationToken ct) => GetAsync<List<Proiect>>("/api/proiecte", ct);

    public Task<AnalizaImport> AnalizeazaAsync(string cale, CancellationToken ct) =>
        TrimiteAsync<AnalizaImport>(() => CuFisier("/api/import-documente/analiza", File.ReadAllBytes(cale), Path.GetFileName(cale), null), ct);

    /// <summary>Paginile unui document, tăiate pe server într-un PDF (cu textul OCR deja citit).</summary>
    public async Task<byte[]> DocumentAsync(string cheie, IReadOnlyList<int> pagini, CancellationToken ct)
    {
        using var raspuns = await CuTokenAsync(() => new HttpRequestMessage(HttpMethod.Post, $"/api/import-documente/{cheie}/document")
        {
            Content = JsonContent.Create(new { pagini }, options: Json),
        }, ct);
        await VerificaAsync(raspuns, ct);
        return await raspuns.Content.ReadAsByteArrayAsync(ct);
    }

    public Task<InterpretareFactura> InterpreteazaFacturaAsync(byte[] fisier, string nume, CancellationToken ct) =>
        TrimiteAsync<InterpretareFactura>(() => CuFisier("/api/facturi/primite/import/interpretare", fisier, nume, null), ct);

    public Task<FacturaInregistrata> ImportaFacturaAsync(Guid proiectId, byte[] fisier, string nume, ConfirmaFactura date, CancellationToken ct) =>
        TrimiteAsync<FacturaInregistrata>(() => CuFisier($"/api/facturi/proiecte/{proiectId}/primite/import", fisier, nume, date), ct);

    public Task<InterpretareNotaComanda> InterpreteazaNotaAsync(byte[] fisier, string nume, CancellationToken ct) =>
        TrimiteAsync<InterpretareNotaComanda>(() => CuFisier("/api/aprovizionare/note-comanda/import/interpretare", fisier, nume, null), ct);

    public Task<DocumentInregistrat> ImportaNotaAsync(Guid proiectId, byte[] fisier, string nume, ConfirmaNotaComanda date, CancellationToken ct) =>
        TrimiteAsync<DocumentInregistrat>(() => CuFisier($"/api/aprovizionare/proiecte/{proiectId}/note-comanda/import", fisier, nume, date), ct);

    public Task<InterpretareNecesar> InterpreteazaNecesarAsync(byte[] fisier, string nume, CancellationToken ct) =>
        TrimiteAsync<InterpretareNecesar>(() => CuFisier("/api/aprovizionare/necesare-materiale/import/interpretare", fisier, nume, null), ct);

    public Task<DocumentInregistrat> ImportaNecesarAsync(Guid proiectId, byte[] fisier, string nume, ConfirmaNecesar date, CancellationToken ct) =>
        TrimiteAsync<DocumentInregistrat>(() => CuFisier($"/api/aprovizionare/proiecte/{proiectId}/necesare-materiale/import", fisier, nume, date), ct);

    public Task<InterpretareBonConsum> InterpreteazaBonAsync(byte[] fisier, string nume, int zona, CancellationToken ct) =>
        TrimiteAsync<InterpretareBonConsum>(() => CuFisier($"/api/stocuri/bonuri-consum/import/interpretare?zona={zona}", fisier, nume, null), ct);

    /// <summary>Șantierele active ale proiectului: cine poate cere materialele (necesarul se emite pe un șantier al proiectului).</summary>
    public async Task<List<LocSolicitant>> LocuriSolicitantAsync(Guid proiectId, CancellationToken ct)
    {
        var santiere = await GetAsync<List<Santier>>($"/api/proiecte/{proiectId}/santiere", ct);
        return [.. santiere.Where(s => s.Activ).Select(s => new LocSolicitant(s.Id, true, s.Denumire, s.Adresa))];
    }


    /// <summary>Materialele din stocul șantierului proiectului, cu propunerea pentru fiecare linie a bonului.</summary>
    public Task<PotrivireBon> PotrivesteBonAsync(Guid proiectId, IEnumerable<(string Denumire, string? Um)> linii, CancellationToken ct) =>
        TrimiteAsync<PotrivireBon>(() => CuJson($"/api/stocuri/proiecte/{proiectId}/bonuri-consum/import/potrivire",
            new { linii = linii.Select(l => new { denumire = l.Denumire, unitateMasura = l.Um }) }), ct);

    /// <summary>Devizul de referință al proiectului (articolele pe care se consumă); null dacă proiectul n-are deviz.</summary>
    public async Task<DevizReferinta?> DevizAsync(Guid proiectId, CancellationToken ct)
    {
        try
        {
            return await GetAsync<DevizReferinta>($"/api/stocuri/proiecte/{proiectId}/deviz", ct);
        }
        catch (EroareServer e) when (e.Cod == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Bonul de consum din aplicație (număr de sistem BC/AAAA/NNNN, evaluat la CMP).</summary>
    public Task<DocumentInregistrat> ImportaBonAsync(Guid proiectId, ConsumCerere cerere, CancellationToken ct) =>
        TrimiteAsync<DocumentInregistrat>(() => CuJson($"/api/stocuri/proiecte/{proiectId}/consumuri", cerere), ct);

    // ------------------------------------------------------------------ transport

    private static HttpRequestMessage CuJson(string url, object corp) =>
        new(HttpMethod.Post, url) { Content = JsonContent.Create(corp, options: Json) };

    private Task<T> GetAsync<T>(string url, CancellationToken ct) => TrimiteAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, url), ct);

    private static HttpRequestMessage CuFisier(string url, byte[] continut, string nume, object? date)
    {
        var formular = new MultipartFormDataContent();
        var fisier = new ByteArrayContent(continut);
        fisier.Headers.ContentType = new MediaTypeHeaderValue(TipContinut(nume));
        formular.Add(fisier, "fisier", nume);
        if (date is not null)
        {
            formular.Add(new StringContent(JsonSerializer.Serialize(date, Json)), "date");
        }

        return new HttpRequestMessage(HttpMethod.Post, url) { Content = formular };
    }

    private async Task<T> TrimiteAsync<T>(Func<HttpRequestMessage> cerere, CancellationToken ct)
    {
        using var raspuns = await CuTokenAsync(cerere, ct);
        await VerificaAsync(raspuns, ct);
        return (await raspuns.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    /// <summary>Cererea cu access token-ul; la 401 sau token expirat, o reînnoire și încă o încercare.</summary>
    private async Task<HttpResponseMessage> CuTokenAsync(Func<HttpRequestMessage> cerere, CancellationToken ct)
    {
        if (_tokenuri is null)
        {
            throw new EroareServer("Nu ești autentificat. Apasă „Conectare”.", HttpStatusCode.Unauthorized);
        }

        if (_tokenuri.Expira <= DateTimeOffset.UtcNow)
        {
            await ReinnoiesteAsync(ct);
        }

        var r = await TrimiteOdataAsync(cerere, ct);
        if (r.StatusCode != HttpStatusCode.Unauthorized)
        {
            return r;
        }

        r.Dispose();
        await ReinnoiesteAsync(ct);
        return await TrimiteOdataAsync(cerere, ct);
    }

    private Task<HttpResponseMessage> TrimiteOdataAsync(Func<HttpRequestMessage> cerere, CancellationToken ct)
    {
        var m = cerere();
        m.RequestUri = new Uri(Server, m.RequestUri!.ToString());
        m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenuri!.AccessToken);
        return _http.SendAsync(m, ct);
    }

    private async Task ReinnoiesteAsync(CancellationToken ct)
    {
        var inainte = _tokenuri;
        await _reinnoire.WaitAsync(ct);
        try
        {
            if (!ReferenceEquals(_tokenuri, inainte) && _tokenuri is { } t && t.Expira > DateTimeOffset.UtcNow)
            {
                return; // altă cerere a reînnoit între timp
            }

            if (_tokenuri?.RefreshToken is not { } refresh)
            {
                throw new EroareServer("Sesiunea a expirat. Apasă „Conectare”.", HttpStatusCode.Unauthorized);
            }

            try
            {
                _tokenuri = await _oauth.ReinnoiesteAsync(Server, refresh, null, ct);
            }
            catch (InvalidOperationException)
            {
                _tokenuri = null;
                throw new EroareServer("Sesiunea a expirat. Apasă „Conectare”.", HttpStatusCode.Unauthorized);
            }
        }
        finally
        {
            _reinnoire.Release();
        }
    }

    private static async Task VerificaAsync(HttpResponseMessage r, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode)
        {
            return;
        }

        var text = await r.Content.ReadAsStringAsync(ct);
        string? mesaj = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            mesaj = doc.RootElement.TryGetProperty("mesaj", out var m) ? m.GetString()
                : doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            // corp fără JSON
        }

        throw new EroareServer(mesaj ?? r.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Nu ești autentificat.",
            HttpStatusCode.Forbidden => "Nu ai drept pentru această operație.",
            HttpStatusCode.NotFound => "Înregistrarea nu a fost găsită.",
            _ => $"Eroare de server ({(int)r.StatusCode}).",
        }, r.StatusCode);
    }

    private static bool CertificatAcceptat(string gazda, X509Certificate2 cert, Setari setari, Func<string, string, bool> confirma)
    {
        var amprenta = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
        if (setari.CertificateAcceptate.TryGetValue(gazda, out var acceptata))
        {
            return string.Equals(acceptata, amprenta, StringComparison.OrdinalIgnoreCase);
        }

        if (!confirma(gazda, amprenta))
        {
            return false;
        }

        setari.CertificateAcceptate[gazda] = amprenta;
        setari.Salveaza();
        return true;
    }

    private static string TipContinut(string nume) => Path.GetExtension(nume).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tif" or ".tiff" => "image/tiff",
        ".xml" => "application/xml",
        ".zip" => "application/zip",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream",
    };

    public void Dispose()
    {
        _http.Dispose();
        _reinnoire.Dispose();
    }
}
