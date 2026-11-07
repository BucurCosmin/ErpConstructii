using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Audit.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "jurnal",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moment = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    utilizator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    utilizator_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    schema_modul = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    tabel = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    entitate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actiune = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    modificari = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jurnal", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_jurnal_firma_id_entitate_id",
                schema: "audit",
                table: "jurnal",
                columns: new[] { "firma_id", "entitate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_jurnal_firma_id_moment",
                schema: "audit",
                table: "jurnal",
                columns: new[] { "firma_id", "moment" });

            migrationBuilder.CreateIndex(
                name: "ix_jurnal_firma_id_schema_modul_tabel_moment",
                schema: "audit",
                table: "jurnal",
                columns: new[] { "firma_id", "schema_modul", "tabel", "moment" });

            // RLS pe firmă (regula 3). Jurnalul e append-only: aplicația doar citește și adaugă (fără UPDATE / DELETE).
            migrationBuilder.AplicaRls("audit", "jurnal");
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA audit TO erp_app;
                REVOKE ALL ON audit.jurnal FROM erp_app;
                GRANT SELECT, INSERT ON audit.jurnal TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA audit REVOKE UPDATE, DELETE ON TABLES FROM erp_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jurnal",
                schema: "audit");
        }
    }
}
