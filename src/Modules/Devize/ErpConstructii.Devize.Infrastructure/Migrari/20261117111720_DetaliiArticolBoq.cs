using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class DetaliiArticolBoq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "observatii",
                schema: "devize",
                table: "articole",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "specificatie",
                schema: "devize",
                table: "articole",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "observatii",
                schema: "devize",
                table: "articole");

            migrationBuilder.DropColumn(
                name: "specificatie",
                schema: "devize",
                table: "articole");
        }
    }
}
