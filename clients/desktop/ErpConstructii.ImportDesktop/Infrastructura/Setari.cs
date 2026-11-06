using System.IO;
using System.Text.Json;

namespace ErpConstructii.ImportDesktop.Infrastructura;

/// <summary>
/// Setările locale ale utilizatorului (%APPDATA%\ErpConstructii\Import\setari.json): ultimul server folosit și
/// certificatele autosemnate acceptate (amprentă per server). Token-urile NU se salvează: la fiecare pornire,
/// login prin browser (sesiunea serverului face ca, de obicei, să nu mai fie nevoie de parolă).
/// </summary>
public sealed class Setari
{
    private static readonly string Cale = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ErpConstructii", "Import", "setari.json");

    private static readonly JsonSerializerOptions Optiuni = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string? Server { get; set; }

    public string? UltimulFolder { get; set; }

    /// <summary>Gazdă → amprenta SHA-256 a certificatului acceptat de utilizator.</summary>
    public Dictionary<string, string> CertificateAcceptate { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static Setari Incarca()
    {
        try
        {
            return File.Exists(Cale) ? JsonSerializer.Deserialize<Setari>(File.ReadAllText(Cale), Optiuni) ?? new Setari() : new Setari();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Setari();
        }
    }

    public void Salveaza()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Cale)!);
        File.WriteAllText(Cale, JsonSerializer.Serialize(this, Optiuni));
    }
}
