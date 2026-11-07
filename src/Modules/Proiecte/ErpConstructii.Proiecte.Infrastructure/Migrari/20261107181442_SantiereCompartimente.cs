using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Proiecte.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class SantiereCompartimente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compartimente",
                schema: "proiecte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    activ = table.Column<bool>(type: "boolean", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compartimente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "santiere",
                schema: "proiecte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    adresa = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    activ = table.Column<bool>(type: "boolean", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_santiere", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_compartimente_firma_id_denumire",
                schema: "proiecte",
                table: "compartimente",
                columns: new[] { "firma_id", "denumire" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_santiere_firma_id_proiect_id",
                schema: "proiecte",
                table: "santiere",
                columns: new[] { "firma_id", "proiect_id" });

            migrationBuilder.AplicaRls("proiecte", "santiere");
            migrationBuilder.AplicaRls("proiecte", "compartimente");

            // D-38: fiecare proiect existent primește „Șantier principal”, la adresa lui (RLS forțat: pe rând, pe firmă).
            migrationBuilder.Sql("""
                DO $$
                DECLARE f uuid;
                BEGIN
                    FOR f IN SELECT id FROM identitate.firme LOOP
                        PERFORM set_config('app.firma_id', f::text, true);
                        INSERT INTO proiecte.santiere (id, proiect_id, denumire, adresa, activ, firma_id, creat_la)
                        SELECT gen_random_uuid(), p.id, 'Șantier principal', nullif(trim(p.adresa_santier), ''), true, f, now()
                          FROM proiecte.proiecte p
                         WHERE NOT EXISTS (SELECT 1 FROM proiecte.santiere s WHERE s.proiect_id = p.id);
                    END LOOP;
                    PERFORM set_config('app.firma_id', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compartimente",
                schema: "proiecte");

            migrationBuilder.DropTable(
                name: "santiere",
                schema: "proiecte");
        }
    }
}
