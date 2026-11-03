using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Identitate.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class DateContactFirma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "adresa_cod_postal",
                schema: "identitate",
                table: "firme",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "adresa_judet",
                schema: "identitate",
                table: "firme",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "adresa_localitate",
                schema: "identitate",
                table: "firme",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "adresa_strada",
                schema: "identitate",
                table: "firme",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "adresa_tara",
                schema: "identitate",
                table: "firme",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "RO");

            migrationBuilder.AddColumn<string>(
                name: "conturi_bancare",
                schema: "identitate",
                table: "firme",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "email",
                schema: "identitate",
                table: "firme",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fax",
                schema: "identitate",
                table: "firme",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nr_reg_com",
                schema: "identitate",
                table: "firme",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telefon",
                schema: "identitate",
                table: "firme",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                schema: "identitate",
                table: "firme",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adresa_cod_postal",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "adresa_judet",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "adresa_localitate",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "adresa_strada",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "adresa_tara",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "conturi_bancare",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "email",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "fax",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "nr_reg_com",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "telefon",
                schema: "identitate",
                table: "firme");

            migrationBuilder.DropColumn(
                name: "website",
                schema: "identitate",
                table: "firme");
        }
    }
}
