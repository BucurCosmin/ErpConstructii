using System.Data.Common;
using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ErpConstructii.Infrastructure.Persistenta;

/// <summary>
/// Trimite firma curentă la PostgreSQL la fiecare deschidere de conexiune, ca variabilă de
/// sesiune <c>app.firma_id</c>. Politicile Row-Level Security (db/rls-template.sql) filtrează
/// după ea, deci o greșeală în cod (un WHERE uitat) nu poate expune datele altei firme.
///
/// De ce la deschiderea conexiunii și nu per tranzacție: interogările EF fără tranzacție
/// explicită ar rămâne altfel neacoperite. Npgsql resetează starea sesiunii (DISCARD ALL)
/// când conexiunea se întoarce în pool, deci valoarea nu „scapă” spre altă cerere.
/// NU seta <c>No Reset On Close=true</c> în connection string.
/// </summary>
public sealed class InterceptorFirmaRls(IFirmaCurenta firmaCurenta) : DbConnectionInterceptor
{
    private const string Sql = "SELECT set_config('app.firma_id', @firma, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var comanda = CreeazaComanda(connection);
        comanda.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var comanda = CreeazaComanda(connection);
        await comanda.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreeazaComanda(DbConnection connection)
    {
        var comanda = connection.CreateCommand();
        comanda.CommandText = Sql;
        var parametru = comanda.CreateParameter();
        parametru.ParameterName = "firma";
        // Șir gol = nicio firmă: politicile RLS nu vor returna niciun rând de business.
        parametru.Value = firmaCurenta.FirmaId?.ToString() ?? string.Empty;
        comanda.Parameters.Add(parametru);
        return comanda;
    }
}
