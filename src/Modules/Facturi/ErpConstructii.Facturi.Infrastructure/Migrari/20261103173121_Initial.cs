using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Facturi.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "facturi");

            migrationBuilder.CreateTable(
                name: "documente",
                schema: "facturi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nume_fisier = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    tip_continut = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hash_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    dimensiune_octeti = table.Column<long>(type: "bigint", nullable: false),
                    continut = table.Column<byte[]>(type: "bytea", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facturi_primite",
                schema: "facturi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numar = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    data_emitere = table.Column<DateOnly>(type: "date", nullable: false),
                    data_scadenta = table.Column<DateOnly>(type: "date", nullable: true),
                    moneda = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    furnizor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    furnizor_denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    furnizor_cui = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    total_fara_tva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_tva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_de_plata = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    referinta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    observatii = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sursa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_xml_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_pdf_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inregistrata_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inregistrata_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturi_primite", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "anulari_facturi_primite",
                schema: "facturi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    anulata_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anulata_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    anulata_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anulari_facturi_primite", x => x.id);
                    table.ForeignKey(
                        name: "fk_anulari_facturi_primite_facturi_primite_factura_id",
                        column: x => x.factura_id,
                        principalSchema: "facturi",
                        principalTable: "facturi_primite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anulari_facturi_primite_factura_id",
                schema: "facturi",
                table: "anulari_facturi_primite",
                column: "factura_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documente_firma_id_hash_sha256",
                schema: "facturi",
                table: "documente",
                columns: new[] { "firma_id", "hash_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturi_primite_firma_id_furnizor_cui_numar",
                schema: "facturi",
                table: "facturi_primite",
                columns: new[] { "firma_id", "furnizor_cui", "numar" });

            migrationBuilder.CreateIndex(
                name: "ix_facturi_primite_firma_id_proiect_id_data_emitere",
                schema: "facturi",
                table: "facturi_primite",
                columns: new[] { "firma_id", "proiect_id", "data_emitere" });

            // RLS pe firmă (regula 3) și drepturile rolului aplicației pe schema nouă (rolul erp_app nu are BYPASSRLS).
            migrationBuilder.AplicaRls("facturi", "facturi_primite");
            migrationBuilder.AplicaRls("facturi", "documente");
            migrationBuilder.AplicaRls("facturi", "anulari_facturi_primite");
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA facturi TO erp_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA facturi TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA facturi
                  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA facturi
                  GRANT USAGE, SELECT ON SEQUENCES TO erp_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anulari_facturi_primite",
                schema: "facturi");

            migrationBuilder.DropTable(
                name: "documente",
                schema: "facturi");

            migrationBuilder.DropTable(
                name: "facturi_primite",
                schema: "facturi");
        }
    }
}
