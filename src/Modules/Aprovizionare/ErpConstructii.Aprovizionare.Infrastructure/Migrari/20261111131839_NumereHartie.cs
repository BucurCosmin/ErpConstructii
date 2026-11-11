using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class NumereHartie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "renumerotari",
                schema: "aprovizionare",
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
                schema: "aprovizionare",
                table: "renumerotari",
                columns: new[] { "firma_id", "document_id" });

            migrationBuilder.AplicaRls("aprovizionare", "renumerotari");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "renumerotari",
                schema: "aprovizionare");
        }
    }
}
