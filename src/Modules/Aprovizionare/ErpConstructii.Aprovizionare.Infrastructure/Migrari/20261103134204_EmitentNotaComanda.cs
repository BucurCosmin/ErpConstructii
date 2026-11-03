using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class EmitentNotaComanda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "emitent_nume",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "Necunoscut");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "emitent_nume",
                schema: "aprovizionare",
                table: "note_comanda");
        }
    }
}
