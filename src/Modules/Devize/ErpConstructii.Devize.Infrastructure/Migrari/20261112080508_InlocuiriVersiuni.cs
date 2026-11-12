using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class InlocuiriVersiuni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inlocuiri_versiuni",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_inlocuita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_noua_id = table.Column<Guid>(type: "uuid", nullable: false),
                    la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inlocuiri_versiuni", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inlocuiri_versiuni_versiune_inlocuita_id",
                schema: "devize",
                table: "inlocuiri_versiuni",
                column: "versiune_inlocuita_id");

            migrationBuilder.CreateIndex(
                name: "ix_inlocuiri_versiuni_versiune_noua_id",
                schema: "devize",
                table: "inlocuiri_versiuni",
                column: "versiune_noua_id");

            migrationBuilder.AplicaRls("devize", "inlocuiri_versiuni");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inlocuiri_versiuni",
                schema: "devize");
        }
    }
}
