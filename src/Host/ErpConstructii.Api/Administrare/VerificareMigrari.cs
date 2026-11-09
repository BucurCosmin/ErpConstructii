using ErpConstructii.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Api.Administrare;

/// <summary>
/// La pornire, verifică dacă baza are toate migrările modulelor. API-ul nu le aplică singur (rulează cu erp_app, iar
/// migrările cer erp_owner), deci o bază rămasă în urmă dă erori 500 pe paginile modulelor noi. Aici devine un mesaj
/// clar în jurnal și în /sanatate.
/// </summary>
internal static class VerificareMigrari
{
    /// <summary>„DbContext: migrare1, migrare2” pentru fiecare context cu migrări neaplicate (gol = baza e la zi).</summary>
    public static IReadOnlyList<string> Lipsa { get; private set; } = [];

    public static async Task VerificaAsync(IServiceProvider servicii, IEnumerable<IModul> module, ILogger logger)
    {
        var contexte = module.Select(m => m.GetType().Assembly).Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(DbContext).IsAssignableFrom(t))
            .ToList();

        var lipsa = new List<string>();
        using var scope = servicii.CreateScope();
        foreach (var tip in contexte)
        {
            if (scope.ServiceProvider.GetService(tip) is not DbContext db) continue;
            try
            {
                var neaplicate = (await db.Database.GetPendingMigrationsAsync()).ToList();
                if (neaplicate.Count > 0) lipsa.Add($"{tip.Name}: {string.Join(", ", neaplicate)}");
            }
            catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
            {
                lipsa.Add($"{tip.Name}: nu pot citi migrările ({ex.Message})");
            }
        }

        Lipsa = lipsa;
        if (lipsa.Count > 0)
        {
            logger.LogError(
                "Baza de date nu e la zi: lipsesc migrări, paginile modulelor afectate vor da erori. " +
                "Rulează db\\creeaza-baza-locala.ps1 (fără -ParolaApp), apoi repornește aplicația.\n  {Lipsa}",
                string.Join("\n  ", lipsa));
        }
    }
}
