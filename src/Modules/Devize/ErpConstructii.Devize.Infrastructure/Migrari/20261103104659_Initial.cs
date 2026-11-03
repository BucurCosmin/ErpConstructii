using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "devize");

            migrationBuilder.CreateTable(
                name: "versiuni",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tip = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    stare = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    versiune_anterioara_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sursa = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fisier_sursa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cota_contributii_manopera = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    cota_cheltuieli_indirecte = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    cota_profit = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    cota_tva = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_versiuni", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "obiect_deviz",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ordine = table.Column<int>(type: "integer", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_obiect_deviz", x => x.id);
                    table.ForeignKey(
                        name: "fk_obiect_deviz_versiuni_versiune_id",
                        column: x => x.versiune_id,
                        principalSchema: "devize",
                        principalTable: "versiuni",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "categorie_lucrari",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    obiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    subcontractata = table.Column<bool>(type: "boolean", nullable: false),
                    ordine = table.Column<int>(type: "integer", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categorie_lucrari", x => x.id);
                    table.ForeignKey(
                        name: "fk_categorie_lucrari_obiect_deviz_obiect_id",
                        column: x => x.obiect_id,
                        principalSchema: "devize",
                        principalTable: "obiect_deviz",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "articole",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    categorie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cheie_stabila = table.Column<Guid>(type: "uuid", nullable: false),
                    simbol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    denumire = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    unitate_masura = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cantitate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pret_material = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pret_manopera = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pret_utilaj = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pret_transport = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ordine = table.Column<int>(type: "integer", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_articole", x => x.id);
                    table.ForeignKey(
                        name: "fk_articole_categorie_lucrari_categorie_id",
                        column: x => x.categorie_id,
                        principalSchema: "devize",
                        principalTable: "categorie_lucrari",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consum_normat",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    articol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod_resursa = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    denumire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unitate_masura = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    consum_unitar = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    pret_unitar = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consum_normat", x => x.id);
                    table.ForeignKey(
                        name: "fk_consum_normat_articole_articol_id",
                        column: x => x.articol_id,
                        principalSchema: "devize",
                        principalTable: "articole",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_articole_categorie_id",
                schema: "devize",
                table: "articole",
                column: "categorie_id");

            migrationBuilder.CreateIndex(
                name: "ix_articole_cheie_stabila",
                schema: "devize",
                table: "articole",
                column: "cheie_stabila");

            migrationBuilder.CreateIndex(
                name: "ix_categorie_lucrari_obiect_id",
                schema: "devize",
                table: "categorie_lucrari",
                column: "obiect_id");

            migrationBuilder.CreateIndex(
                name: "ix_consum_normat_articol_id",
                schema: "devize",
                table: "consum_normat",
                column: "articol_id");

            migrationBuilder.CreateIndex(
                name: "ix_obiect_deviz_versiune_id",
                schema: "devize",
                table: "obiect_deviz",
                column: "versiune_id");

            migrationBuilder.CreateIndex(
                name: "ix_versiuni_firma_id_proiect_id_tip_numar",
                schema: "devize",
                table: "versiuni",
                columns: new[] { "firma_id", "proiect_id", "tip", "numar" },
                unique: true);

            // RLS pe firmă (regula 3, db/rls-template.sql).
            migrationBuilder.AplicaRls("devize", "versiuni");
            migrationBuilder.AplicaRls("devize", "obiect_deviz");
            migrationBuilder.AplicaRls("devize", "categorie_lucrari");
            migrationBuilder.AplicaRls("devize", "articole");
            migrationBuilder.AplicaRls("devize", "consum_normat");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consum_normat",
                schema: "devize");

            migrationBuilder.DropTable(
                name: "articole",
                schema: "devize");

            migrationBuilder.DropTable(
                name: "categorie_lucrari",
                schema: "devize");

            migrationBuilder.DropTable(
                name: "obiect_deviz",
                schema: "devize");

            migrationBuilder.DropTable(
                name: "versiuni",
                schema: "devize");
        }
    }
}
