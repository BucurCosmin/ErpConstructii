using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class ImportDocumenteExterne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "document_original_id",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numar_extern",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sursa",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sistem");

            migrationBuilder.CreateTable(
                name: "documente_originale",
                schema: "aprovizionare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nume_fisier = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    tip_continut = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hash_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    dimensiune_octeti = table.Column<long>(type: "bigint", nullable: false),
                    continut = table.Column<byte[]>(type: "bytea", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documente_originale", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documente_originale_firma_id_hash_sha256",
                schema: "aprovizionare",
                table: "documente_originale",
                columns: new[] { "firma_id", "hash_sha256" },
                unique: true);

            // RLS pe firmă (regula 3, db/rls-template.sql).
            migrationBuilder.AplicaRls("aprovizionare", "documente_originale");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documente_originale",
                schema: "aprovizionare");

            migrationBuilder.DropColumn(
                name: "document_original_id",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "numar_extern",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "sursa",
                schema: "aprovizionare",
                table: "note_comanda");
        }
    }
}
