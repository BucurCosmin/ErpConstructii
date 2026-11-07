using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class NoteComandaDinNecesar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "acceptata_de_id",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acceptata_de_nume",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "acceptata_la",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motiv_respingere",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "nota_sursa_id",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stare",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Emisa"); // notele existente sunt comenzi emise / importate

            migrationBuilder.CreateIndex(
                name: "ix_note_comanda_firma_id_nota_sursa_id",
                schema: "aprovizionare",
                table: "note_comanda",
                columns: new[] { "firma_id", "nota_sursa_id" });

            migrationBuilder.CreateIndex(
                name: "ix_note_comanda_firma_id_stare",
                schema: "aprovizionare",
                table: "note_comanda",
                columns: new[] { "firma_id", "stare" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_note_comanda_firma_id_nota_sursa_id",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropIndex(
                name: "ix_note_comanda_firma_id_stare",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "acceptata_de_id",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "acceptata_de_nume",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "acceptata_la",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "motiv_respingere",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "nota_sursa_id",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "stare",
                schema: "aprovizionare",
                table: "note_comanda");
        }
    }
}
