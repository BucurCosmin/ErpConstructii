using Microsoft.EntityFrameworkCore.Migrations;

namespace ErpConstructii.Infrastructure.Persistenta;

/// <summary>
/// Aplică Row-Level Security pe un tabel de business, din migrarea care îl creează
/// (regula 3 din CLAUDE.md, șablonul din db/rls-template.sql).
/// </summary>
public static class MigrariRls
{
    public static void AplicaRls(this MigrationBuilder migrare, string schema, string tabel)
    {
        migrare.Sql($"""
            ALTER TABLE {schema}.{tabel} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {schema}.{tabel} FORCE ROW LEVEL SECURITY;
            CREATE POLICY izolare_firma ON {schema}.{tabel}
              USING      (firma_id = NULLIF(current_setting('app.firma_id', true), '')::uuid)
              WITH CHECK (firma_id = NULLIF(current_setting('app.firma_id', true), '')::uuid);
            """);
    }
}
