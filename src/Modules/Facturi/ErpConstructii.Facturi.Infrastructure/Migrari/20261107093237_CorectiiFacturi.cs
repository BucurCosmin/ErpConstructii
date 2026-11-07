using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Facturi.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class CorectiiFacturi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corectii_facturi_primite",
                schema: "facturi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    date_vechi_json = table.Column<string>(type: "jsonb", nullable: false),
                    date_noi_json = table.Column<string>(type: "jsonb", nullable: false),
                    rezumat = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    corectat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corectat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    corectat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corectii_facturi_primite", x => x.id);
                    table.ForeignKey(
                        name: "fk_corectii_facturi_primite_facturi_primite_factura_id",
                        column: x => x.factura_id,
                        principalSchema: "facturi",
                        principalTable: "facturi_primite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corectii_facturi_primite_factura_id",
                schema: "facturi",
                table: "corectii_facturi_primite",
                column: "factura_id");

            migrationBuilder.CreateIndex(
                name: "ix_corectii_facturi_primite_firma_id_factura_id",
                schema: "facturi",
                table: "corectii_facturi_primite",
                columns: new[] { "firma_id", "factura_id" });

            // RLS pe firmă (regula 3); drepturile erp_app vin din privilegiile implicite ale schemei (migrarea inițială).
            migrationBuilder.AplicaRls("facturi", "corectii_facturi_primite");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corectii_facturi_primite",
                schema: "facturi");
        }
    }
}
