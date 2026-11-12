using System.Text.Json;
using ErpConstructii.Devize.Import.Isdp;

namespace ErpConstructii.Teste.Isdp;

/// <summary>Încarcă o foaie salvată ca JSON: { "nume": "...", "randuri": [[null, "text", 12.5], ...] }.</summary>
internal static class Fixturi
{
    public const string DevizResurse = "deviz-resurse-sant-chilleri.json";
    public const string ListaF3 = "f3-sant-chilleri.json";

    public static FoaieCalcul Incarca(string fisier) => Incarca(fisier, (_, _, v) => v);

    /// <summary>Încarcă fixture-ul modificând celulele (pentru testele de fișiere corupte / fără prețuri).</summary>
    public static FoaieCalcul Incarca(string fisier, Func<int, int, object?, object?> transforma)
    {
        var cale = Cauta(fisier);
        using var doc = JsonDocument.Parse(File.ReadAllText(cale));
        var nume = doc.RootElement.GetProperty("nume").GetString() ?? fisier;
        var randuri = doc.RootElement.GetProperty("randuri").EnumerateArray()
            .Select((r, i) => (IReadOnlyList<object?>)r.EnumerateArray().Select((c, j) => transforma(i, j, Valoare(c))).ToList())
            .ToList();
        return new FoaieCalcul(nume, randuri);
    }

    // Numerele ca double, exact cum le dă biblioteca de citire Excel.
    private static object? Valoare(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String => e.GetString(),
        _ => null,
    };

    private static string Cauta(string fisier)
    {
        foreach (var baza in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(baza);
            while (dir is not null)
            {
                foreach (var candidat in new[]
                         {
                             Path.Combine(dir.FullName, "samples", fisier),
                             Path.Combine(dir.FullName, "tests", "samples", "isdp", fisier),
                         })
                {
                    if (File.Exists(candidat))
                    {
                        return candidat;
                    }
                }

                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException($"Nu găsesc fixture-ul {fisier}.");
    }
}
