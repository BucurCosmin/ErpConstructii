using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Proiecte.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "proiecte");

            migrationBuilder.CreateTable(
                name: "proiecte",
                schema: "proiecte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    beneficiar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cui_beneficiar = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    adresa_santier = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    stare = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    manager_proiect_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data_inceput = table.Column<DateOnly>(type: "date", nullable: true),
                    termen_contractual = table.Column<DateOnly>(type: "date", nullable: true),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proiecte", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_proiecte_firma_id_cod",
                schema: "proiecte",
                table: "proiecte",
                columns: new[] { "firma_id", "cod" },
                unique: true);

            // RLS pe firmă (regula 3, db/rls-template.sql).
            migrationBuilder.AplicaRls("proiecte", "proiecte");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proiecte",
                schema: "proiecte");
        }
    }
}
