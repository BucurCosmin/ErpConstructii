using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class NoteComandaEmise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "conditii_json",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "furnizor_adresa",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "furnizor_email",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "furnizor_telefon",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "necesar_id",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sablon_id",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "versiune_sablon",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_note_comanda_firma_id_necesar_id",
                schema: "aprovizionare",
                table: "note_comanda",
                columns: new[] { "firma_id", "necesar_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_note_comanda_firma_id_necesar_id",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "conditii_json",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "furnizor_adresa",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "furnizor_email",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "furnizor_telefon",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "necesar_id",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "sablon_id",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "versiune_sablon",
                schema: "aprovizionare",
                table: "note_comanda");
        }
    }
}
