using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Facturi.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class FacturiNotaComanda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "nota_comanda_id",
                schema: "facturi",
                table: "facturi_primite",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nota_comanda_numar",
                schema: "facturi",
                table: "facturi_primite",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturi_primite_firma_id_nota_comanda_id",
                schema: "facturi",
                table: "facturi_primite",
                columns: new[] { "firma_id", "nota_comanda_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_facturi_primite_firma_id_nota_comanda_id",
                schema: "facturi",
                table: "facturi_primite");

            migrationBuilder.DropColumn(
                name: "nota_comanda_id",
                schema: "facturi",
                table: "facturi_primite");

            migrationBuilder.DropColumn(
                name: "nota_comanda_numar",
                schema: "facturi",
                table: "facturi_primite");
        }
    }
}
