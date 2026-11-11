using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class MutariSantier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mutari_santier",
                schema: "aprovizionare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    necesar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    necesar_numar = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    santier_vechi_id = table.Column<Guid>(type: "uuid", nullable: true),
                    santier_vechi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    santier_nou_id = table.Column<Guid>(type: "uuid", nullable: false),
                    santier_nou = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mutari_santier", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mutari_santier_firma_id_necesar_id",
                schema: "aprovizionare",
                table: "mutari_santier",
                columns: new[] { "firma_id", "necesar_id" });

            migrationBuilder.AplicaRls("aprovizionare", "mutari_santier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mutari_santier",
                schema: "aprovizionare");
        }
    }
}
