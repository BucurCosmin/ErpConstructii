using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Proiecte.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class TipProiect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tip",
                schema: "proiecte",
                table: "proiecte",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Lucrare");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tip",
                schema: "proiecte",
                table: "proiecte");
        }
    }
}
