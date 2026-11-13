using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Devize.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class ConversieResurse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "articol_stoc_unitate_masura",
                schema: "devize",
                table: "resurse_catalog",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "factor_conversie",
                schema: "devize",
                table: "resurse_catalog",
                type: "numeric(24,12)",
                precision: 24,
                scale: 12,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "articol_stoc_unitate_masura",
                schema: "devize",
                table: "resurse_catalog");

            migrationBuilder.DropColumn(
                name: "factor_conversie",
                schema: "devize",
                table: "resurse_catalog");
        }
    }
}
