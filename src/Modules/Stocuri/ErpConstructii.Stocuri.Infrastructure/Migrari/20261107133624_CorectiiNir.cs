using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class CorectiiNir : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corectii_nir",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nir_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nir_numar = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    rezumat = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    diferenta_valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    corectat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corectat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corectii_nir", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corectii_nir_firma_id_factura_id",
                schema: "stocuri",
                table: "corectii_nir",
                columns: new[] { "firma_id", "factura_id" });

            migrationBuilder.CreateIndex(
                name: "ix_corectii_nir_firma_id_nir_id",
                schema: "stocuri",
                table: "corectii_nir",
                columns: new[] { "firma_id", "nir_id" });

            migrationBuilder.AplicaRls("stocuri", "corectii_nir");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corectii_nir",
                schema: "stocuri");
        }
    }
}
