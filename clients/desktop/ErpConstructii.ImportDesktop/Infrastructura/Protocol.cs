using System.IO;
using Microsoft.Win32;

namespace ErpConstructii.ImportDesktop.Infrastructura;

/// <summary>
/// Deschiderea din aplicația web: linkul <c>erp-import://deschide?server=https://sdaq.home.ro</c> pornește aplicația cu
/// serverul paginii. Schema se înregistrează la fiecare pornire pentru utilizatorul curent (HKCU, fără administrator),
/// ca linkul să pornească mereu versiunea folosită ultima dată.
/// </summary>
public static class Protocol
{
    public const string Schema = "erp-import";

    public static void Inregistreaza()
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !File.Exists(exe))
        {
            return;
        }

        try
        {
            using var cheie = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Schema}");
            cheie.SetValue(string.Empty, "URL:ERP Construcții – Import documente");
            cheie.SetValue("URL Protocol", string.Empty);
            using var icon = cheie.CreateSubKey("DefaultIcon");
            icon.SetValue(string.Empty, $"\"{exe}\",0");
            using var comanda = cheie.CreateSubKey(@"shell\open\command");
            comanda.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // fără înregistrare: aplicația merge și pornită direct
        }
    }

    /// <summary>Argumentele de pornire: linkul din web, „--server &lt;adresă&gt;” și/sau un fișier de deschis.</summary>
    public static (Uri? Server, string? Fisier) Citeste(string[] args)
    {
        Uri? server = null;
        string? fisier = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith(Schema + ":", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(a, UriKind.Absolute, out var link))
            {
                var parametri = link.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty, StringComparer.OrdinalIgnoreCase);
                server = ServerValid(parametri.GetValueOrDefault("server")) ?? server;
            }
            else if (a == "--server" && i + 1 < args.Length)
            {
                server = ServerValid(args[++i]) ?? server;
            }
            else if (File.Exists(a))
            {
                fisier = a;
            }
        }

        return (server, fisier);
    }

    /// <summary>Doar http(s), fără cale: adresa aplicației web (aceeași origine servește și API-ul).</summary>
    public static Uri? ServerValid(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? new Uri(u.GetLeftPart(UriPartial.Authority) + "/")
            : null;
}
