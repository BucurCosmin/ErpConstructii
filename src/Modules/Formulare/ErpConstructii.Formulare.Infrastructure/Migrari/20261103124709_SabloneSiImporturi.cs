using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Formulare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class SabloneSiImporturi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "formulare");

            migrationBuilder.CreateTable(
                name: "importuri",
                schema: "formulare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod_tip = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nume_fisier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    hash_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original = table.Column<byte[]>(type: "bytea", nullable: false),
                    date_json = table.Column<string>(type: "jsonb", nullable: false),
                    stare = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_importuri", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sabloane",
                schema: "formulare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod_tip = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    versiune = table.Column<int>(type: "integer", nullable: false),
                    nume_fisier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    hash_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    continut = table.Column<byte[]>(type: "bytea", nullable: false),
                    activ = table.Column<bool>(type: "boolean", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sabloane", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_importuri_firma_id_cod_tip_creat_la",
                schema: "formulare",
                table: "importuri",
                columns: new[] { "firma_id", "cod_tip", "creat_la" });

            migrationBuilder.CreateIndex(
                name: "ix_sabloane_firma_id_cod_tip",
                schema: "formulare",
                table: "sabloane",
                columns: new[] { "firma_id", "cod_tip" },
                unique: true,
                filter: "activ");

            migrationBuilder.CreateIndex(
                name: "ix_sabloane_firma_id_cod_tip_versiune",
                schema: "formulare",
                table: "sabloane",
                columns: new[] { "firma_id", "cod_tip", "versiune" },
                unique: true);

            migrationBuilder.AplicaRls("formulare", "importuri");
            migrationBuilder.AplicaRls("formulare", "sabloane");
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA formulare TO erp_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE formulare.importuri TO erp_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE formulare.sabloane TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA formulare
                  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA formulare
                  GRANT USAGE, SELECT ON SEQUENCES TO erp_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "importuri",
                schema: "formulare");

            migrationBuilder.DropTable(
                name: "sabloane",
                schema: "formulare");
        }
    }
}
