using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class NomenclatorResurse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resurse_catalog",
                schema: "devize",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sursa = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    denumire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unitate_masura = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tip_dedus = table.Column<bool>(type: "boolean", nullable: false),
                    articol_stoc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    articol_stoc_denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    meserie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    categorie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    utilaj = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fara_mapare = table.Column<bool>(type: "boolean", nullable: false),
                    mapata_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    mapata_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resurse_catalog", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_resurse_catalog_firma_id_sursa_cod",
                schema: "devize",
                table: "resurse_catalog",
                columns: new[] { "firma_id", "sursa", "cod" },
                unique: true);

            migrationBuilder.AplicaRls("devize", "resurse_catalog");

            migrationBuilder.AddForeignKey(
                name: "fk_consum_normat_resurse_resursa_catalog_id",
                schema: "devize",
                table: "consum_normat",
                column: "resursa_catalog_id",
                principalSchema: "devize",
                principalTable: "resurse_catalog",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_consum_normat_resurse_resursa_catalog_id",
                schema: "devize",
                table: "consum_normat");

            migrationBuilder.DropTable(
                name: "resurse_catalog",
                schema: "devize");
        }
    }
}
