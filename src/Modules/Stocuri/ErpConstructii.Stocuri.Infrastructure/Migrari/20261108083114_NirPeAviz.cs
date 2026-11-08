using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class NirPeAviz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "factura_id",
                schema: "stocuri",
                table: "storno_nir",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "factura_numar",
                schema: "stocuri",
                table: "nir",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<Guid>(
                name: "factura_id",
                schema: "stocuri",
                table: "nir",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "factura_data",
                schema: "stocuri",
                table: "nir",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<DateOnly>(
                name: "aviz_data",
                schema: "stocuri",
                table: "nir",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "aviz_id",
                schema: "stocuri",
                table: "nir",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "aviz_numar",
                schema: "stocuri",
                table: "nir",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "factura_id",
                schema: "stocuri",
                table: "corectii_nir",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "ix_nir_firma_id_aviz_id",
                schema: "stocuri",
                table: "nir",
                columns: new[] { "firma_id", "aviz_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nir_firma_id_aviz_id",
                schema: "stocuri",
                table: "nir");

            migrationBuilder.DropColumn(
                name: "aviz_data",
                schema: "stocuri",
                table: "nir");

            migrationBuilder.DropColumn(
                name: "aviz_id",
                schema: "stocuri",
                table: "nir");

            migrationBuilder.DropColumn(
                name: "aviz_numar",
                schema: "stocuri",
                table: "nir");

            migrationBuilder.AlterColumn<Guid>(
                name: "factura_id",
                schema: "stocuri",
                table: "storno_nir",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "factura_numar",
                schema: "stocuri",
                table: "nir",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "factura_id",
                schema: "stocuri",
                table: "nir",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "factura_data",
                schema: "stocuri",
                table: "nir",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "factura_id",
                schema: "stocuri",
                table: "corectii_nir",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
