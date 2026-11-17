using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class ObiectiveDeviz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "obiectiv_id",
                schema: "devize",
                table: "versiuni",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "stare_anterioara",
                schema: "devize",
                table: "inlocuiri_versiuni",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Aprobata");

            migrationBuilder.CreateTable(
                name: "mutari_obiectiv",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obiectiv_vechi_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obiectiv_nou_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_mutari_obiectiv", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "obiective",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descriere = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ordine = table.Column<int>(type: "integer", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_obiective", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reveniri_versiuni",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_readusa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_stearsa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_stearsa = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    stare_readusa = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("pk_reveniri_versiuni", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_versiuni_obiectiv_id",
                schema: "devize",
                table: "versiuni",
                column: "obiectiv_id");

            migrationBuilder.CreateIndex(
                name: "ix_mutari_obiectiv_versiune_id",
                schema: "devize",
                table: "mutari_obiectiv",
                column: "versiune_id");

            migrationBuilder.CreateIndex(
                name: "ix_obiective_firma_id_proiect_id",
                schema: "devize",
                table: "obiective",
                columns: new[] { "firma_id", "proiect_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reveniri_versiuni_versiune_readusa_id",
                schema: "devize",
                table: "reveniri_versiuni",
                column: "versiune_readusa_id");

            // Devizele existente (D-59): câte un obiectiv pe proiect, numit după antetul primului deviz, cu toate versiunile lui.
            // RLS e forțat și pentru proprietar: se ridică doar pe durata mutării datelor.
            migrationBuilder.Sql("""
                ALTER TABLE devize.versiuni NO FORCE ROW LEVEL SECURITY;
                INSERT INTO devize.obiective (id, proiect_id, denumire, descriere, ordine, firma_id, creat_la)
                SELECT gen_random_uuid(), p.proiect_id,
                       COALESCE(NULLIF(left(btrim((SELECT v2.obiectiv FROM devize.versiuni v2
                                                   WHERE v2.proiect_id = p.proiect_id AND v2.firma_id = p.firma_id AND btrim(coalesce(v2.obiectiv, '')) <> ''
                                                   ORDER BY v2.creat_la LIMIT 1)), 200), ''), 'Deviz principal'),
                       NULL, 0, p.firma_id, now()
                FROM (SELECT DISTINCT proiect_id, firma_id FROM devize.versiuni) p;
                UPDATE devize.versiuni v SET obiectiv_id = o.id
                FROM devize.obiective o WHERE o.proiect_id = v.proiect_id AND o.firma_id = v.firma_id;
                ALTER TABLE devize.versiuni FORCE ROW LEVEL SECURITY;
                """);
            migrationBuilder.AplicaRls("devize", "obiective");
            migrationBuilder.AplicaRls("devize", "reveniri_versiuni");
            migrationBuilder.AplicaRls("devize", "mutari_obiectiv");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mutari_obiectiv",
                schema: "devize");

            migrationBuilder.DropTable(
                name: "obiective",
                schema: "devize");

            migrationBuilder.DropTable(
                name: "reveniri_versiuni",
                schema: "devize");

            migrationBuilder.DropIndex(
                name: "ix_versiuni_obiectiv_id",
                schema: "devize",
                table: "versiuni");

            migrationBuilder.DropColumn(
                name: "obiectiv_id",
                schema: "devize",
                table: "versiuni");

            migrationBuilder.DropColumn(
                name: "stare_anterioara",
                schema: "devize",
                table: "inlocuiri_versiuni");
        }
    }
}
