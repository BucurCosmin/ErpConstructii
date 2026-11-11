using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class SolduriInitialeAnulate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "solduri_initiale_anulate",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sold_initial_id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    gestiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestiune_denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    categorie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    nume_fisier = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    anulat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anulat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    anulat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solduri_initiale_anulate", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_solduri_initiale_anulate_firma_id_an_numar",
                schema: "stocuri",
                table: "solduri_initiale_anulate",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.AplicaRls("stocuri", "solduri_initiale_anulate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solduri_initiale_anulate",
                schema: "stocuri");
        }
    }
}
