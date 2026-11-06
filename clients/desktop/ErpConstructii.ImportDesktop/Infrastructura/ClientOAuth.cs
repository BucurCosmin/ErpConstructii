using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ErpConstructii.ImportDesktop.Infrastructura;

public sealed record Tokenuri(string AccessToken, string? RefreshToken, DateTimeOffset Expira);

/// <summary>
/// Login pentru aplicațiile desktop (RFC 8252): Authorization Code + PKCE prin browserul sistemului, cu întoarcerea pe
/// o adresă locală (http://127.0.0.1:47615/callback/). Pagina de login e cea a aplicației web (cu MFA); aplicația nu
/// vede niciodată parola. Clientul OAuth „erp-import” e înregistrat pe server (appsettings.json, Autentificare:Clienti).
/// </summary>
public sealed class ClientOAuth(HttpClient http)
{
    public const string ClientId = "erp-import";

    private static readonly int[] Porturi = [47615, 47616, 47617];

    public async Task<Tokenuri> AutentificaAsync(Uri server, CancellationToken ct)
    {
        var (ascultator, port) = Asculta();
        using var _ = ascultator;
        var redirect = $"http://127.0.0.1:{port}/callback/";
        var verificator = Base64Url(RandomNumberGenerator.GetBytes(32));
        var provocare = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificator)));
        var stare = Base64Url(RandomNumberGenerator.GetBytes(16));

        var url = new Uri(server, "/connect/authorize?" + Interogare(new()
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirect,
            ["response_type"] = "code",
            ["scope"] = "openid profile email offline_access",
            ["code_challenge"] = provocare,
            ["code_challenge_method"] = "S256",
            ["state"] = stare,
        }));
        // Browserul implicit; ERP_IMPORT_BROWSER = alt program (ex. un browser anume sau testele automate), primește adresa.
        var browser = Environment.GetEnvironmentVariable("ERP_IMPORT_BROWSER");
        Process.Start(string.IsNullOrWhiteSpace(browser)
            ? new ProcessStartInfo(url.ToString()) { UseShellExecute = true }
            : new ProcessStartInfo(browser) { ArgumentList = { url.ToString() }, UseShellExecute = false });

        var parametri = await AsteaptaRaspunsAsync(ascultator, ct);
        if (parametri.TryGetValue("error", out var eroare))
        {
            throw new InvalidOperationException($"Autentificarea a fost refuzată: {parametri.GetValueOrDefault("error_description") ?? eroare}.");
        }

        if (parametri.GetValueOrDefault("state") != stare || !parametri.TryGetValue("code", out var cod))
        {
            throw new InvalidOperationException("Răspunsul de autentificare nu este valid. Încearcă din nou.");
        }

        return await TokenAsync(server, new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = cod,
            ["redirect_uri"] = redirect,
            ["client_id"] = ClientId,
            ["code_verifier"] = verificator,
        }, ct);
    }

    /// <summary>Token-uri noi cu refresh token-ul (rotit de server); cu <paramref name="firmaId"/> se schimbă firma activă.</summary>
    public Task<Tokenuri> ReinnoiesteAsync(Uri server, string refreshToken, Guid? firmaId, CancellationToken ct)
    {
        var date = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        };
        if (firmaId is { } f)
        {
            date["firma_id"] = f.ToString();
        }

        return TokenAsync(server, date, ct);
    }

    private async Task<Tokenuri> TokenAsync(Uri server, Dictionary<string, string> date, CancellationToken ct)
    {
        using var raspuns = await http.PostAsync(new Uri(server, "/connect/token"), new FormUrlEncodedContent(date), ct);
        var json = await raspuns.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var r = doc.RootElement;
        if (!raspuns.IsSuccessStatusCode || !r.TryGetProperty("access_token", out var acces))
        {
            var mesaj = r.TryGetProperty("error_description", out var d) ? d.GetString() : $"cod {(int)raspuns.StatusCode}";
            throw new InvalidOperationException($"Serverul nu a emis token-ul: {mesaj}");
        }

        var expira = r.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 600;
        return new Tokenuri(acces.GetString()!, r.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            DateTimeOffset.UtcNow.AddSeconds(expira - 30));
    }

    private static (TcpListener Ascultator, int Port) Asculta()
    {
        foreach (var port in Porturi)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                return (l, port);
            }
            catch (SocketException)
            {
                // portul e ocupat: următorul
            }
        }

        throw new InvalidOperationException($"Porturile locale {string.Join(", ", Porturi)} sunt ocupate. Închide aplicația care le folosește.");
    }

    /// <summary>Primește redirecționarea browserului (GET /callback/?code=…&amp;state=…) și răspunde cu o pagină scurtă.</summary>
    private static async Task<Dictionary<string, string>> AsteaptaRaspunsAsync(TcpListener ascultator, CancellationToken ct)
    {
        while (true)
        {
            using var client = await ascultator.AcceptTcpClientAsync(ct);
            await using var flux = client.GetStream();
            using var cititor = new StreamReader(flux, Encoding.ASCII, leaveOpen: true);
            var primaLinie = await cititor.ReadLineAsync(ct) ?? string.Empty; // GET /callback/?code=... HTTP/1.1
            var parti = primaLinie.Split(' ');
            if (parti.Length < 2 || !parti[1].StartsWith("/callback/", StringComparison.Ordinal))
            {
                await RaspundeAsync(flux, 404, "Nu există.", ct); // ex. /favicon.ico
                continue;
            }

            var interogare = parti[1].Contains('?', StringComparison.Ordinal) ? parti[1][(parti[1].IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
            var parametri = interogare.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty);
            var ok = parametri.ContainsKey("code");
            await RaspundeAsync(flux, 200, ok
                ? "<h2>Autentificare reușită</h2><p>Te poți întoarce în aplicația de import. Poți închide această pagină.</p>"
                : "<h2>Autentificarea nu a reușit</h2><p>Întoarce-te în aplicația de import și încearcă din nou.</p>", ct);
            return parametri;
        }
    }

    private static async Task RaspundeAsync(Stream flux, int cod, string continut, CancellationToken ct)
    {
        var html = $"<!DOCTYPE html><html lang=\"ro\"><head><meta charset=\"utf-8\"><title>ERP Construcții</title></head>" +
                   $"<body style=\"font-family:Segoe UI,sans-serif;margin:48px\">{continut}</body></html>";
        var corp = Encoding.UTF8.GetBytes(html);
        var antet = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {cod} {(cod == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {corp.Length}\r\nConnection: close\r\n\r\n");
        await flux.WriteAsync(antet, ct);
        await flux.WriteAsync(corp, ct);
    }

    private static string Interogare(Dictionary<string, string> p) =>
        string.Join('&', p.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private static string Base64Url(byte[] date) => Convert.ToBase64String(date).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
