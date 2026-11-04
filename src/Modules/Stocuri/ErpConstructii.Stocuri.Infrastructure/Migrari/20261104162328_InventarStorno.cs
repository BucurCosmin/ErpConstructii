using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class InventarStorno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nir_firma_id_factura_id",
                schema: "stocuri",
                table: "nir");

            migrationBuilder.CreateTable(
                name: "inventare",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    gestiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestiune_denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stare = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    comisie = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    deschis_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deschis_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    finalizat_de_id = table.Column<Guid>(type: "uuid", nullable: true),
                    finalizat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventare", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storno_nir",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nir_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nir_numar = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    stornat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stornat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storno_nir", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_nir_firma_id_factura_id",
                schema: "stocuri",
                table: "nir",
                columns: new[] { "firma_id", "factura_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventare_firma_id_an_numar",
                schema: "stocuri",
                table: "inventare",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventare_firma_id_gestiune_id",
                schema: "stocuri",
                table: "inventare",
                columns: new[] { "firma_id", "gestiune_id" },
                unique: true,
                filter: "stare = 'Deschis'");

            migrationBuilder.CreateIndex(
                name: "ix_storno_nir_firma_id_nir_id",
                schema: "stocuri",
                table: "storno_nir",
                columns: new[] { "firma_id", "nir_id" },
                unique: true);

            migrationBuilder.AplicaRls("stocuri", "storno_nir");
            migrationBuilder.AplicaRls("stocuri", "inventare");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventare",
                schema: "stocuri");

            migrationBuilder.DropTable(
                name: "storno_nir",
                schema: "stocuri");

            migrationBuilder.DropIndex(
                name: "ix_nir_firma_id_factura_id",
                schema: "stocuri",
                table: "nir");

            migrationBuilder.CreateIndex(
                name: "ix_nir_firma_id_factura_id",
                schema: "stocuri",
                table: "nir",
                columns: new[] { "firma_id", "factura_id" },
                unique: true);
        }
    }
}
