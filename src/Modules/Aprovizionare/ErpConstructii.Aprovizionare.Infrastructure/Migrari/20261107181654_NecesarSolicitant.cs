using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class NecesarSolicitant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "compartiment_id",
                schema: "aprovizionare",
                table: "necesare_materiale",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "santier_id",
                schema: "aprovizionare",
                table: "necesare_materiale",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_necesare_materiale_firma_id_compartiment_id",
                schema: "aprovizionare",
                table: "necesare_materiale",
                columns: new[] { "firma_id", "compartiment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_necesare_materiale_firma_id_santier_id",
                schema: "aprovizionare",
                table: "necesare_materiale",
                columns: new[] { "firma_id", "santier_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_necesare_materiale_firma_id_compartiment_id",
                schema: "aprovizionare",
                table: "necesare_materiale");

            migrationBuilder.DropIndex(
                name: "ix_necesare_materiale_firma_id_santier_id",
                schema: "aprovizionare",
                table: "necesare_materiale");

            migrationBuilder.DropColumn(
                name: "compartiment_id",
                schema: "aprovizionare",
                table: "necesare_materiale");

            migrationBuilder.DropColumn(
                name: "santier_id",
                schema: "aprovizionare",
                table: "necesare_materiale");
        }
    }
}
