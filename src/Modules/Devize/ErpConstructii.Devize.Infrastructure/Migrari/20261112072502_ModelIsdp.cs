using ErpConstructii.Infrastructure.Persistenta;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class ModelIsdp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "categorie_id",
                schema: "devize",
                table: "articole",
                newName: "capitol_id");

            migrationBuilder.RenameIndex(
                name: "ix_articole_categorie_id",
                schema: "devize",
                table: "articole",
                newName: "ix_articole_capitol_id");

            migrationBuilder.AddColumn<decimal>(
                name: "curs",
                schema: "devize",
                table: "versiuni",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "moneda",
                schema: "devize",
                table: "versiuni",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RON");

            migrationBuilder.AddColumn<string>(
                name: "obiect",
                schema: "devize",
                table: "versiuni",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "obiectiv",
                schema: "devize",
                table: "versiuni",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "politica_rotunjire",
                schema: "devize",
                table: "versiuni",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "stadiu_fizic",
                schema: "devize",
                table: "versiuni",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "totaluri_declarate_json",
                schema: "devize",
                table: "versiuni",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "pret_unitar",
                schema: "devize",
                table: "consum_normat",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "consum_unitar",
                schema: "devize",
                table: "consum_normat",
                type: "numeric(24,12)",
                precision: 24,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AddColumn<decimal>(
                name: "cantitate_totala",
                schema: "devize",
                table: "consum_normat",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ordine",
                schema: "devize",
                table: "consum_normat",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "resursa_catalog_id",
                schema: "devize",
                table: "consum_normat",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "tip_dedus",
                schema: "devize",
                table: "consum_normat",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<decimal>(
                name: "pret_utilaj",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "pret_transport",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "pret_material",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "pret_manopera",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "cantitate",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<string>(
                name: "cod_wbs_sursa",
                schema: "devize",
                table: "articole",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pret_echipament",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pret_oferta_echipament",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pret_oferta_manopera",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pret_oferta_material",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pret_oferta_transport",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pret_oferta_utilaj",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "simbol_baza",
                schema: "devize",
                table: "articole",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tip_articol",
                schema: "devize",
                table: "articole",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Normat");

            migrationBuilder.AddColumn<decimal>(
                name: "valoare_oferta_echipament",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "valoare_oferta_manopera",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "valoare_oferta_material",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "valoare_oferta_transport",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "valoare_oferta_utilaj",
                schema: "devize",
                table: "articole",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "capitole",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parinte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cod_wbs = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    denumire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subcontractat = table.Column<bool>(type: "boolean", nullable: false),
                    ordine = table.Column<int>(type: "integer", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capitole", x => x.id);
                    table.ForeignKey(
                        name: "fk_capitole_capitole_parinte_id",
                        column: x => x.parinte_id,
                        principalSchema: "devize",
                        principalTable: "capitole",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_capitole_versiuni_versiune_id",
                        column: x => x.versiune_id,
                        principalSchema: "devize",
                        principalTable: "versiuni",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consum_normat_resursa_catalog_id",
                schema: "devize",
                table: "consum_normat",
                column: "resursa_catalog_id");

            migrationBuilder.CreateIndex(
                name: "ix_capitole_parinte_id",
                schema: "devize",
                table: "capitole",
                column: "parinte_id");

            migrationBuilder.CreateIndex(
                name: "ix_capitole_versiune_id_parinte_id_ordine",
                schema: "devize",
                table: "capitole",
                columns: new[] { "versiune_id", "parinte_id", "ordine" });

            migrationBuilder.AplicaRls("devize", "capitole");

            // Datele existente: fiecare obiect devine capitol de primul nivel (tip Obiect), fiecare categorie de lucrări un
            // capitol sub obiectul ei (tip Capitol). Id-urile se păstrează, deci articolele (capitol_id = fostul categorie_id)
            // rămân legate. RLS-ul forțat ascunde rândurile și proprietarului: se oprește cât ține copierea (toate firmele).
            migrationBuilder.Sql("""
                ALTER TABLE devize.obiect_deviz NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE devize.categorie_lucrari NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE devize.capitole NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE devize.articole NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE devize.consum_normat NO FORCE ROW LEVEL SECURITY;

                INSERT INTO devize.capitole (id, versiune_id, parinte_id, cod, cod_wbs, denumire, tip, subcontractat, ordine,
                                             firma_id, creat_la, creat_de, modificat_la, modificat_de)
                SELECT o.id, o.versiune_id, NULL, o.cod, NULL, o.denumire, 'Obiect', false, o.ordine,
                       o.firma_id, o.creat_la, o.creat_de, o.modificat_la, o.modificat_de
                  FROM devize.obiect_deviz o;

                INSERT INTO devize.capitole (id, versiune_id, parinte_id, cod, cod_wbs, denumire, tip, subcontractat, ordine,
                                             firma_id, creat_la, creat_de, modificat_la, modificat_de)
                SELECT c.id, o.versiune_id, o.id, c.cod, NULL, c.denumire, 'Capitol', c.subcontractata, c.ordine,
                       c.firma_id, c.creat_la, c.creat_de, c.modificat_la, c.modificat_de
                  FROM devize.categorie_lucrari c JOIN devize.obiect_deviz o ON o.id = c.obiect_id;

                -- Articolele existente: simbolul de bază = simbolul; consumurile: cantitatea pe tot articolul.
                UPDATE devize.articole SET simbol_baza = simbol WHERE simbol_baza = '';
                UPDATE devize.consum_normat c SET cantitate_totala = c.consum_unitar * a.cantitate
                  FROM devize.articole a WHERE a.id = c.articol_id;

                ALTER TABLE devize.capitole FORCE ROW LEVEL SECURITY;
                ALTER TABLE devize.articole FORCE ROW LEVEL SECURITY;
                ALTER TABLE devize.consum_normat FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_articole_categorie_lucrari_categorie_id",
                schema: "devize",
                table: "articole");

            migrationBuilder.DropTable(
                name: "categorie_lucrari",
                schema: "devize");

            migrationBuilder.DropTable(
                name: "obiect_deviz",
                schema: "devize");

            migrationBuilder.AddForeignKey(
                name: "fk_articole_capitole_capitol_id",
                schema: "devize",
                table: "articole",
                column: "capitol_id",
                principalSchema: "devize",
                principalTable: "capitole",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Arborele de capitole (oricâte niveluri) nu încape înapoi în obiect → categorie fără pierderi:
            // revenirea se face din backup-ul bazei (db\restaureaza-baza.ps1), nu prin această migrare.
            throw new NotSupportedException("Migrarea ModelIsdp nu se poate anula: restaurează baza din backup.");
        }
    }
}
