using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class FisiereSursa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fisiere_sursa",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nume_fisier = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    tip_continut = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    marime = table.Column<long>(type: "bigint", nullable: false),
                    amprenta = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    principal = table.Column<bool>(type: "boolean", nullable: false),
                    continut = table.Column<byte[]>(type: "bytea", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fisiere_sursa", x => x.id);
                    table.ForeignKey(
                        name: "fk_fisiere_sursa_versiuni_versiune_id",
                        column: x => x.versiune_id,
                        principalSchema: "devize",
                        principalTable: "versiuni",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fisiere_sursa_firma_id_amprenta",
                schema: "devize",
                table: "fisiere_sursa",
                columns: new[] { "firma_id", "amprenta" });

            migrationBuilder.CreateIndex(
                name: "ix_fisiere_sursa_versiune_id",
                schema: "devize",
                table: "fisiere_sursa",
                column: "versiune_id");

            migrationBuilder.AplicaRls("devize", "fisiere_sursa");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fisiere_sursa",
                schema: "devize");
        }
    }
}
