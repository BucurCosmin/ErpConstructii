using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Avize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "avize",
                schema: "aprovizionare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    numar_extern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lucrare = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    furnizor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    furnizor_denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    furnizor_cui = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    observatii = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    nota_comanda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_comanda_numar = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nota_comanda_generata = table.Column<bool>(type: "boolean", nullable: false),
                    document_original_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inregistrat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inregistrat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avize", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avize_firma_id_an_numar",
                schema: "aprovizionare",
                table: "avize",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_avize_firma_id_furnizor_id_numar_extern",
                schema: "aprovizionare",
                table: "avize",
                columns: new[] { "firma_id", "furnizor_id", "numar_extern" });

            migrationBuilder.CreateIndex(
                name: "ix_avize_firma_id_nota_comanda_id",
                schema: "aprovizionare",
                table: "avize",
                columns: new[] { "firma_id", "nota_comanda_id" });

            migrationBuilder.CreateIndex(
                name: "ix_avize_firma_id_proiect_id_data",
                schema: "aprovizionare",
                table: "avize",
                columns: new[] { "firma_id", "proiect_id", "data" });

            // RLS: fiecare firmă își vede doar avizele (regula 3).
            migrationBuilder.AplicaRls("aprovizionare", "avize");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avize",
                schema: "aprovizionare");
        }
    }
}
