using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class SolduriInitiale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "categorie",
                schema: "stocuri",
                table: "articole",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cod_extern",
                schema: "stocuri",
                table: "articole",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "solduri_initiale",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    gestiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestiune_denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gestiune_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    categorie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    nume_fisier = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    fisier_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    importat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solduri_initiale", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_articole_firma_id_cod_extern",
                schema: "stocuri",
                table: "articole",
                columns: new[] { "firma_id", "cod_extern" });

            migrationBuilder.CreateIndex(
                name: "ix_solduri_initiale_firma_id_an_numar",
                schema: "stocuri",
                table: "solduri_initiale",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_solduri_initiale_firma_id_fisier_sha256_gestiune_externa",
                schema: "stocuri",
                table: "solduri_initiale",
                columns: new[] { "firma_id", "fisier_sha256", "gestiune_externa" },
                unique: true);

            migrationBuilder.AplicaRls("stocuri", "solduri_initiale");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solduri_initiale",
                schema: "stocuri");

            migrationBuilder.DropIndex(
                name: "ix_articole_firma_id_cod_extern",
                schema: "stocuri",
                table: "articole");

            migrationBuilder.DropColumn(
                name: "categorie",
                schema: "stocuri",
                table: "articole");

            migrationBuilder.DropColumn(
                name: "cod_extern",
                schema: "stocuri",
                table: "articole");
        }
    }
}
