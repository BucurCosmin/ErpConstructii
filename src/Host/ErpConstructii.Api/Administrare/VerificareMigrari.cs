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
                // erp_app nu are mereu drept de citire pe istoricul migrărilor (tabelele create înaintea drepturilor implicite):
                // se verifică înainte, ca jurnalul să nu primească erori de la o interogare refuzată.
                if (!await PoateCitiIstoriculAsync(db))
                {
                    logger.LogDebug("Migrările {Context} nu pot fi verificate cu rolul aplicației.", tip.Name);
                    continue;
                }

                var neaplicate = (await db.Database.GetPendingMigrationsAsync()).ToList();
                if (neaplicate.Count > 0) lipsa.Add($"{tip.Name}: {string.Join(", ", neaplicate)}");
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.InsufficientPrivilege)
            {
                // erp_app nu are mereu drept de citire pe istoricul migrărilor: nu se poate verifica, dar nu e o problemă.
                logger.LogDebug("Migrările {Context} nu pot fi verificate cu rolul aplicației.", tip.Name);
            }
            catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
            {
                lipsa.Add($"{tip.Name}: nu pot citi migrările ({ex.Message})");
            }
        }

        Lipsa = lipsa;
        LogheazaLipsa(lipsa, logger);
    }

    /// <summary>Tabela de istoric lipsă = nicio migrare aplicată (se raportează); fără drept de citire = nu se poate verifica.</summary>
    private static async Task<bool> PoateCitiIstoriculAsync(DbContext db)
    {
        var optiuni = Microsoft.EntityFrameworkCore.Infrastructure.RelationalOptionsExtension.Extract(
            Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>(db));
        var tabela = optiuni.MigrationsHistoryTableName ?? "__EFMigrationsHistory";
        var schema = optiuni.MigrationsHistoryTableSchema ?? "public";
        var conexiune = db.Database.GetDbConnection();
        var deschisa = conexiune.State == System.Data.ConnectionState.Open;
        if (!deschisa) await conexiune.OpenAsync();
        try
        {
            await using var comanda = conexiune.CreateCommand();
            comanda.CommandText = "SELECT CASE WHEN to_regclass(@t) IS NULL THEN true ELSE has_table_privilege(@t, 'SELECT') END";
            var p = comanda.CreateParameter();
            p.ParameterName = "t";
            p.Value = $"\"{schema}\".\"{tabela}\"";
            comanda.Parameters.Add(p);
            return await comanda.ExecuteScalarAsync() is true;
        }
        finally
        {
            if (!deschisa) await conexiune.CloseAsync();
        }
    }

    private static void LogheazaLipsa(List<string> lipsa, ILogger logger)
    {
        if (lipsa.Count > 0)
        {
            logger.LogError(
                "Baza de date nu e la zi: lipsesc migrări, paginile modulelor afectate vor da erori. " +
                "Rulează db\\creeaza-baza-locala.ps1 (fără -ParolaApp), apoi repornește aplicația.\n  {Lipsa}",
                string.Join("\n  ", lipsa));
        }
    }
}
