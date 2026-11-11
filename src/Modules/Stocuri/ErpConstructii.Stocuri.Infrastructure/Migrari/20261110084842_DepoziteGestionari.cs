using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class DepoziteGestionari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "depozit_id",
                schema: "stocuri",
                table: "gestiuni",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "gestionar_id",
                schema: "stocuri",
                table: "gestiuni",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gestionar_nume",
                schema: "stocuri",
                table: "gestiuni",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "depozite",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    adresa = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    activ = table.Column<bool>(type: "boolean", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depozite", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gestiuni_firma_id_depozit_id",
                schema: "stocuri",
                table: "gestiuni",
                columns: new[] { "firma_id", "depozit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_depozite_firma_id_cod",
                schema: "stocuri",
                table: "depozite",
                columns: new[] { "firma_id", "cod" },
                unique: true);

            migrationBuilder.AplicaRls("stocuri", "depozite");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "depozite",
                schema: "stocuri");

            migrationBuilder.DropIndex(
                name: "ix_gestiuni_firma_id_depozit_id",
                schema: "stocuri",
                table: "gestiuni");

            migrationBuilder.DropColumn(
                name: "depozit_id",
                schema: "stocuri",
                table: "gestiuni");

            migrationBuilder.DropColumn(
                name: "gestionar_id",
                schema: "stocuri",
                table: "gestiuni");

            migrationBuilder.DropColumn(
                name: "gestionar_nume",
                schema: "stocuri",
                table: "gestiuni");
        }
    }
}
