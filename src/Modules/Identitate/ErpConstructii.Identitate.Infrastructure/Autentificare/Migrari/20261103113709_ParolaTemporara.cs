using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Identitate.Infrastructure.Autentificare.Migrari
{
    /// <inheritdoc />
    public partial class ParolaTemporara : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "trebuie_schimbata_parola",
                schema: "identitate",
                table: "conturi",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trebuie_schimbata_parola",
                schema: "identitate",
                table: "conturi");
        }
    }
}
