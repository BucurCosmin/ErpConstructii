using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <summary>
    /// Reorganizarea documentelor de aprovizionare: fostele „note de comandă” sunt de fapt necesare de materiale
    /// (cererea șantierului) → tabelul se redenumește, cu date, politica RLS și indecși; „note_comanda” devine tabelul
    /// notelor de comandă către furnizori (formularul GC 007).
    /// </summary>
    public partial class ReorganizareDocumente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "note_comanda", schema: "aprovizionare", newName: "necesare_materiale", newSchema: "aprovizionare");
            migrationBuilder.Sql("ALTER TABLE aprovizionare.necesare_materiale RENAME CONSTRAINT pk_note_comanda TO pk_necesare_materiale;");
            migrationBuilder.RenameIndex(name: "ix_note_comanda_firma_id_an_numar", schema: "aprovizionare", table: "necesare_materiale", newName: "ix_necesare_materiale_firma_id_an_numar");
            migrationBuilder.RenameIndex(name: "ix_note_comanda_firma_id_proiect_id_data", schema: "aprovizionare", table: "necesare_materiale", newName: "ix_necesare_materiale_firma_id_proiect_id_data");

            migrationBuilder.CreateTable(
                name: "note_comanda",
                schema: "aprovizionare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    numar_extern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lucrare = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    furnizor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    furnizor_denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    furnizor_cui = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    observatii = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sursa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_original_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inregistrata_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inregistrata_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table => table.PrimaryKey("pk_note_comanda", x => x.id));

            migrationBuilder.CreateIndex(name: "ix_note_comanda_firma_id_an_numar", schema: "aprovizionare", table: "note_comanda",
                columns: new[] { "firma_id", "an", "numar" }, unique: true);
            migrationBuilder.CreateIndex(name: "ix_note_comanda_firma_id_proiect_id_data", schema: "aprovizionare", table: "note_comanda",
                columns: new[] { "firma_id", "proiect_id", "data" });
            migrationBuilder.CreateIndex(name: "ix_note_comanda_firma_id_furnizor_id", schema: "aprovizionare", table: "note_comanda",
                columns: new[] { "firma_id", "furnizor_id" });

            migrationBuilder.AplicaRls("aprovizionare", "note_comanda");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "note_comanda", schema: "aprovizionare");
            migrationBuilder.RenameIndex(name: "ix_necesare_materiale_firma_id_an_numar", schema: "aprovizionare", table: "necesare_materiale", newName: "ix_note_comanda_firma_id_an_numar");
            migrationBuilder.RenameIndex(name: "ix_necesare_materiale_firma_id_proiect_id_data", schema: "aprovizionare", table: "necesare_materiale", newName: "ix_note_comanda_firma_id_proiect_id_data");
            migrationBuilder.Sql("ALTER TABLE aprovizionare.necesare_materiale RENAME CONSTRAINT pk_necesare_materiale TO pk_note_comanda;");
            migrationBuilder.RenameTable(name: "necesare_materiale", schema: "aprovizionare", newName: "note_comanda", newSchema: "aprovizionare");
        }
    }
}
