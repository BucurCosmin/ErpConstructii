using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Grafic.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "grafic");

            migrationBuilder.CreateTable(
                name: "activitati",
                schema: "grafic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    denumire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cheie_wbs = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stadiu = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    procent = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    start_planificat = table.Column<DateOnly>(type: "date", nullable: false),
                    sfarsit_planificat = table.Column<DateOnly>(type: "date", nullable: false),
                    start = table.Column<DateOnly>(type: "date", nullable: true),
                    sfarsit = table.Column<DateOnly>(type: "date", nullable: true),
                    start_real = table.Column<DateOnly>(type: "date", nullable: true),
                    sfarsit_real = table.Column<DateOnly>(type: "date", nullable: true),
                    start_tarziu = table.Column<DateOnly>(type: "date", nullable: true),
                    sfarsit_tarziu = table.Column<DateOnly>(type: "date", nullable: true),
                    durata_zile = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    durata_ramasa_zile = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    rezerva_totala_zile = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    critica = table.Column<bool>(type: "boolean", nullable: false),
                    calendar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    constrangere = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cost_planificat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_realizat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_ramas = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    dependente_json = table.Column<string>(type: "jsonb", nullable: false),
                    coduri_json = table.Column<string>(type: "jsonb", nullable: false),
                    resurse_json = table.Column<string>(type: "jsonb", nullable: false),
                    pasi_json = table.Column<string>(type: "jsonb", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activitati", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fisiere",
                schema: "grafic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    continut = table.Column<byte[]>(type: "bytea", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fisiere", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "grafice",
                schema: "grafic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cod_sursa = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    format = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    numar_versiuni = table.Column<int>(type: "integer", nullable: false),
                    versiune_curenta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grafice", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "noduri_wbs",
                schema: "grafic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cheie = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cod = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    denumire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cheie_parinte = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ordine = table.Column<int>(type: "integer", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_noduri_wbs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "versiuni",
                schema: "grafic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grafic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    data_actualizare = table.Column<DateOnly>(type: "date", nullable: true),
                    start = table.Column<DateOnly>(type: "date", nullable: true),
                    sfarsit = table.Column<DateOnly>(type: "date", nullable: true),
                    nume_fisier = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dimensiune = table.Column<long>(type: "bigint", nullable: false),
                    importat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    activitati = table.Column<int>(type: "integer", nullable: false),
                    finalizate = table.Column<int>(type: "integer", nullable: false),
                    in_curs = table.Column<int>(type: "integer", nullable: false),
                    critice = table.Column<int>(type: "integer", nullable: false),
                    procent = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    cost_planificat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_realizat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_ramas = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    coduri_json = table.Column<string>(type: "jsonb", nullable: false),
                    comparatie_json = table.Column<string>(type: "jsonb", nullable: true),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_versiuni", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activitati_versiune_id_cod",
                schema: "grafic",
                table: "activitati",
                columns: new[] { "versiune_id", "cod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fisiere_versiune_id",
                schema: "grafic",
                table: "fisiere",
                column: "versiune_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grafice_firma_id_proiect_id",
                schema: "grafic",
                table: "grafice",
                columns: new[] { "firma_id", "proiect_id" });

            migrationBuilder.CreateIndex(
                name: "ix_noduri_wbs_versiune_id_cheie",
                schema: "grafic",
                table: "noduri_wbs",
                columns: new[] { "versiune_id", "cheie" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_versiuni_grafic_id_numar",
                schema: "grafic",
                table: "versiuni",
                columns: new[] { "grafic_id", "numar" },
                unique: true);

            // RLS pe firma (regula 3) si drepturile rolului aplicatiei pe schema noua.
            foreach (var tabel in new[] { "grafice", "versiuni", "fisiere", "noduri_wbs", "activitati" })
                migrationBuilder.AplicaRls("grafic", tabel);
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA grafic TO erp_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA grafic TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA grafic
                  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA grafic
                  GRANT USAGE, SELECT ON SEQUENCES TO erp_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activitati",
                schema: "grafic");

            migrationBuilder.DropTable(
                name: "fisiere",
                schema: "grafic");

            migrationBuilder.DropTable(
                name: "grafice",
                schema: "grafic");

            migrationBuilder.DropTable(
                name: "noduri_wbs",
                schema: "grafic");

            migrationBuilder.DropTable(
                name: "versiuni",
                schema: "grafic");
        }
    }
}
