using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Identitate.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Firme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "firme",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cui = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firme", x => x.id);
                });

            // Rolurile și atribuirile create înainte de tabelul firme primesc o firmă (redenumibilă),
            // ca cheile străine de mai jos să poată fi adăugate.
            migrationBuilder.Sql("""
                INSERT INTO identitate.firme (id, denumire, cui, activa)
                SELECT firma_id, 'Firmă ' || left(firma_id::text, 8), NULL, true
                FROM (SELECT firma_id FROM identitate.roluri UNION SELECT firma_id FROM identitate.atribuiri) f
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_atribuiri_firma_id",
                schema: "identitate",
                table: "atribuiri",
                column: "firma_id");

            migrationBuilder.AddForeignKey(
                name: "fk_atribuiri_firme_firma_id",
                schema: "identitate",
                table: "atribuiri",
                column: "firma_id",
                principalSchema: "identitate",
                principalTable: "firme",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_roluri_firme_firma_id",
                schema: "identitate",
                table: "roluri",
                column: "firma_id",
                principalSchema: "identitate",
                principalTable: "firme",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_atribuiri_firme_firma_id",
                schema: "identitate",
                table: "atribuiri");

            migrationBuilder.DropForeignKey(
                name: "fk_roluri_firme_firma_id",
                schema: "identitate",
                table: "roluri");

            migrationBuilder.DropTable(
                name: "firme",
                schema: "identitate");

            migrationBuilder.DropIndex(
                name: "ix_atribuiri_firma_id",
                schema: "identitate",
                table: "atribuiri");
        }
    }
}
