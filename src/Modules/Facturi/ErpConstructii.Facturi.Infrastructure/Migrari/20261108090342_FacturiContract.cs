using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Facturi.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class FacturiContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "contract_id",
                schema: "facturi",
                table: "facturi_primite",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract_numar",
                schema: "facturi",
                table: "facturi_primite",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturi_primite_firma_id_contract_id",
                schema: "facturi",
                table: "facturi_primite",
                columns: new[] { "firma_id", "contract_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_facturi_primite_firma_id_contract_id",
                schema: "facturi",
                table: "facturi_primite");

            migrationBuilder.DropColumn(
                name: "contract_id",
                schema: "facturi",
                table: "facturi_primite");

            migrationBuilder.DropColumn(
                name: "contract_numar",
                schema: "facturi",
                table: "facturi_primite");
        }
    }
}
