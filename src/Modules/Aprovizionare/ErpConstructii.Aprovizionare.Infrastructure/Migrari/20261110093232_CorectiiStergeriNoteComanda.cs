using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class CorectiiStergeriNoteComanda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corectii_note_comanda",
                schema: "aprovizionare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_comanda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tip = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    date_vechi_json = table.Column<string>(type: "jsonb", nullable: false),
                    date_noi_json = table.Column<string>(type: "jsonb", nullable: false),
                    rezumat = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    corectat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corectat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    corectat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corectii_note_comanda", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stergeri_note_comanda",
                schema: "aprovizionare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_comanda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numar = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    numar_extern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    furnizor_denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    efect = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    date_json = table.Column<string>(type: "jsonb", nullable: false),
                    stearsa_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stearsa_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stearsa_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stergeri_note_comanda", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corectii_note_comanda_firma_id_nota_comanda_id",
                schema: "aprovizionare",
                table: "corectii_note_comanda",
                columns: new[] { "firma_id", "nota_comanda_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stergeri_note_comanda_firma_id_stearsa_la",
                schema: "aprovizionare",
                table: "stergeri_note_comanda",
                columns: new[] { "firma_id", "stearsa_la" });

            migrationBuilder.AplicaRls("aprovizionare", "corectii_note_comanda");
            migrationBuilder.AplicaRls("aprovizionare", "stergeri_note_comanda");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corectii_note_comanda",
                schema: "aprovizionare");

            migrationBuilder.DropTable(
                name: "stergeri_note_comanda",
                schema: "aprovizionare");
        }
    }
}
