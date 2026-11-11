using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class NumereHartie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "numar_extern",
                schema: "stocuri",
                table: "bonuri_consum",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "renumerotari",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_numar = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    numar_vechi = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    numar_nou = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    motiv = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    utilizator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    utilizator_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renumerotari", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_renumerotari_firma_id_document_id",
                schema: "stocuri",
                table: "renumerotari",
                columns: new[] { "firma_id", "document_id" });

            migrationBuilder.AplicaRls("stocuri", "renumerotari");

            // Bonurile importate înainte de D-48 au numărul de hârtie doar în observații: „Bon de consum nr. 12 din 03.09.2026 (import)…”.
            // RLS-ul forțat ascunde rândurile și proprietarului, deci se oprește cât ține completarea (toate firmele).
            migrationBuilder.Sql("""
                ALTER TABLE stocuri.bonuri_consum NO FORCE ROW LEVEL SECURITY;
                UPDATE stocuri.bonuri_consum
                   SET numar_extern = left(substring(observatii from '^Bon de consum nr\. (.+?) din [0-9]{2}\.[0-9]{2}\.[0-9]{4} \(import\)'), 50)
                 WHERE numar_extern IS NULL
                   AND observatii ~ '^Bon de consum nr\. .+? din [0-9]{2}\.[0-9]{2}\.[0-9]{4} \(import\)';
                ALTER TABLE stocuri.bonuri_consum FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "renumerotari",
                schema: "stocuri");

            migrationBuilder.DropColumn(
                name: "numar_extern",
                schema: "stocuri",
                table: "bonuri_consum");
        }
    }
}
