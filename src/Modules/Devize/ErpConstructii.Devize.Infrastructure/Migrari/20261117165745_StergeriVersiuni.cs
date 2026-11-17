using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class StergeriVersiuni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stergeri_versiuni",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obiectiv_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_stearsa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_stearsa = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    stare_stearsa = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    versiune_relegata_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    facuta_de = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stergeri_versiuni", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stergeri_versiuni_firma_id_proiect_id",
                schema: "devize",
                table: "stergeri_versiuni",
                columns: new[] { "firma_id", "proiect_id" });

            migrationBuilder.AplicaRls("devize", "stergeri_versiuni");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stergeri_versiuni",
                schema: "devize");
        }
    }
}
