using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class ConsumRetur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "directie",
                schema: "stocuri",
                table: "transferuri",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SpreSantier");

            migrationBuilder.CreateTable(
                name: "bonuri_consum",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    gestiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    observatii = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    emis_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emis_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bonuri_consum", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bonuri_consum_firma_id_an_numar",
                schema: "stocuri",
                table: "bonuri_consum",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bonuri_consum_firma_id_proiect_id_data",
                schema: "stocuri",
                table: "bonuri_consum",
                columns: new[] { "firma_id", "proiect_id", "data" });

            migrationBuilder.AplicaRls("stocuri", "bonuri_consum");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bonuri_consum",
                schema: "stocuri");

            migrationBuilder.DropColumn(
                name: "directie",
                schema: "stocuri",
                table: "transferuri");
        }
    }
}
