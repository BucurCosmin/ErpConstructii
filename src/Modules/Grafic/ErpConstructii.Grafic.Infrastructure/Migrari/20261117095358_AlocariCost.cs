using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Grafic.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class AlocariCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alocari_cost",
                schema: "grafic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grafic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sursa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_numar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    linie_index = table.Column<int>(type: "integer", nullable: false),
                    linie_denumire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tip_tinta = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cheie = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    denumire_tinta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    procent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    creat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alocari_cost", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alocari_cost_grafic_id_sursa_document_id",
                schema: "grafic",
                table: "alocari_cost",
                columns: new[] { "grafic_id", "sursa", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_alocari_cost_grafic_id_sursa_document_id_linie_index_tip_ti",
                schema: "grafic",
                table: "alocari_cost",
                columns: new[] { "grafic_id", "sursa", "document_id", "linie_index", "tip_tinta", "cheie" },
                unique: true);

            migrationBuilder.AplicaRls("grafic", "alocari_cost");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alocari_cost",
                schema: "grafic");
        }
    }
}
