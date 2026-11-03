using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Infrastructure;

public static class InregistrareInfrastructura
{
    /// <summary>
    /// Înregistrează DbContext-ul unui modul: PostgreSQL, snake_case, istoric de migrări în
    /// schema modulului, interceptorii de RLS și audit.
    /// Connection string-ul: <c>ConnectionStrings:Erp</c>; folosește rolul <c>erp_app</c>,
    /// care NU are BYPASSRLS și nu e proprietarul tabelelor.
    /// </summary>
    public static IServiceCollection AdaugaDbContextModul<TContext>(
        this IServiceCollection servicii,
        IConfiguration configurare,
        string schema)
        where TContext : DbContextModul
    {
        var connectionString = configurare.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Lipsește ConnectionStrings:Erp.");

        servicii.AddDbContext<TContext>((sp, optiuni) =>
        {
            optiuni
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrari", schema))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    sp.GetRequiredService<InterceptorFirmaRls>(),
                    sp.GetRequiredService<InterceptorAudit>());
        });

        return servicii;
    }

    /// <summary>Servicii comune tuturor modulelor. Apelat o singură dată din Host.</summary>
    public static IServiceCollection AdaugaInfrastructuraComuna(this IServiceCollection servicii)
    {
        servicii.AddScoped<InterceptorFirmaRls>();
        servicii.AddScoped<InterceptorAudit>();
        return servicii;
    }
}
