using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class DocumenteDinUrma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "din_urma",
                schema: "aprovizionare",
                table: "note_comanda",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "din_urma",
                schema: "aprovizionare",
                table: "necesare_materiale",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "nota_comanda_numar",
                schema: "aprovizionare",
                table: "avize",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<Guid>(
                name: "nota_comanda_id",
                schema: "aprovizionare",
                table: "avize",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "din_urma",
                schema: "aprovizionare",
                table: "avize",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "din_urma",
                schema: "aprovizionare",
                table: "note_comanda");

            migrationBuilder.DropColumn(
                name: "din_urma",
                schema: "aprovizionare",
                table: "necesare_materiale");

            migrationBuilder.DropColumn(
                name: "din_urma",
                schema: "aprovizionare",
                table: "avize");

            migrationBuilder.AlterColumn<string>(
                name: "nota_comanda_numar",
                schema: "aprovizionare",
                table: "avize",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "nota_comanda_id",
                schema: "aprovizionare",
                table: "avize",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
