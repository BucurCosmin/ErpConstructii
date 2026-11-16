using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Contracte.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class SurseFisa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "surse",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: true),
                    observatii = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordine = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_surse", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_surse_firma_id_contract_id",
                schema: "contracte",
                table: "surse",
                columns: new[] { "firma_id", "contract_id" });

            // RLS pe firmă (regula 3): sursele fișei (D-56).
            migrationBuilder.AplicaRls("contracte", "surse");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "surse",
                schema: "contracte");
        }
    }
}
