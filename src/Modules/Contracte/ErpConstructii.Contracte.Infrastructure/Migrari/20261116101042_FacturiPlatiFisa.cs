using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Contracte.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class FacturiPlatiFisa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "facturi_fisa",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    membru = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    numar = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    tip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transa_avans = table.Column<int>(type: "integer", nullable: true),
                    situatie_plata = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    luna_executie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    storno_avans = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    curs = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    net_ron = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tva_ron = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_ron = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    scadenta = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("pk_facturi_fisa", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plati",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    suma_ron = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    instrument = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    numar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scadenta = table.Column<DateOnly>(type: "date", nullable: true),
                    confirmata = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_plati", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_facturi_fisa_firma_id_contract_id",
                schema: "contracte",
                table: "facturi_fisa",
                columns: new[] { "firma_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_facturi_fisa_firma_id_contract_id_factura_id",
                schema: "contracte",
                table: "facturi_fisa",
                columns: new[] { "firma_id", "contract_id", "factura_id" },
                unique: true,
                filter: "factura_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plati_firma_id_contract_id",
                schema: "contracte",
                table: "plati",
                columns: new[] { "firma_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_plati_firma_id_factura_contract_id",
                schema: "contracte",
                table: "plati",
                columns: new[] { "firma_id", "factura_contract_id" });

            // RLS pe firmă (regula 3): facturile din fișă și plățile (D-56, etapa 2).
            migrationBuilder.AplicaRls("contracte", "facturi_fisa");
            migrationBuilder.AplicaRls("contracte", "plati");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "facturi_fisa",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "plati",
                schema: "contracte");
        }
    }
}
