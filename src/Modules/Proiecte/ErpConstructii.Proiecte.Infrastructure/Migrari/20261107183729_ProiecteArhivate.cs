using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Proiecte.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class ProiecteArhivate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "arhivat",
                schema: "proiecte",
                table: "proiecte",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "arhivat_de_nume",
                schema: "proiecte",
                table: "proiecte",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "arhivat_la",
                schema: "proiecte",
                table: "proiecte",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "arhivat",
                schema: "proiecte",
                table: "proiecte");

            migrationBuilder.DropColumn(
                name: "arhivat_de_nume",
                schema: "proiecte",
                table: "proiecte");

            migrationBuilder.DropColumn(
                name: "arhivat_la",
                schema: "proiecte",
                table: "proiecte");
        }
    }
}
