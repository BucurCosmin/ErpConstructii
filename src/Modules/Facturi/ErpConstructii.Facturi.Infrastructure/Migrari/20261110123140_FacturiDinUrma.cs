using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Facturi.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class FacturiDinUrma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "din_urma",
                schema: "facturi",
                table: "facturi_primite",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "din_urma",
                schema: "facturi",
                table: "facturi_primite");
        }
    }
}
