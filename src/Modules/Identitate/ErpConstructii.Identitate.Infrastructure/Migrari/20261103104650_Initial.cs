using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Identitate.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identitate");

            migrationBuilder.CreateTable(
                name: "roluri",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    permisiuni = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roluri", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "utilizatori",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tip = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    activ = table.Column<bool>(type: "boolean", nullable: false),
                    expira_la = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utilizatori", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "atribuiri",
                schema: "identitate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    utilizator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gestiune_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valabil_de = table.Column<DateOnly>(type: "date", nullable: false),
                    valabil_pana = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_atribuiri", x => x.id);
                    table.ForeignKey(
                        name: "fk_atribuiri_roluri_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "identitate",
                        principalTable: "roluri",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_atribuiri_utilizatori_utilizator_id",
                        column: x => x.utilizator_id,
                        principalSchema: "identitate",
                        principalTable: "utilizatori",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_atribuiri_rol_id",
                schema: "identitate",
                table: "atribuiri",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "ix_atribuiri_utilizator_id_firma_id",
                schema: "identitate",
                table: "atribuiri",
                columns: new[] { "utilizator_id", "firma_id" });

            migrationBuilder.CreateIndex(
                name: "ix_roluri_firma_id_cod",
                schema: "identitate",
                table: "roluri",
                columns: new[] { "firma_id", "cod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_utilizatori_email",
                schema: "identitate",
                table: "utilizatori",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "atribuiri",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "roluri",
                schema: "identitate");

            migrationBuilder.DropTable(
                name: "utilizatori",
                schema: "identitate");
        }
    }
}
