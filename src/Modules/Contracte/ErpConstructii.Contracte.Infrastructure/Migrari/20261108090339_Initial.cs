using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Contracte.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contracte");

            migrationBuilder.CreateTable(
                name: "acte_aditionale",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    numar_extern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    obiect = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    diferenta_valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    data_sfarsit_noua = table.Column<DateOnly>(type: "date", nullable: true),
                    creat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acte_aditionale", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contracte",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    numar_extern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    data_semnarii = table.Column<DateOnly>(type: "date", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    partener_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partener_denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    partener_cui = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tip = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    obiect = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    data_inceput = table.Column<DateOnly>(type: "date", nullable: true),
                    data_sfarsit = table.Column<DateOnly>(type: "date", nullable: true),
                    termen_plata_zile = table.Column<int>(type: "integer", nullable: true),
                    procent_garantie = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    avans = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxare_inversa = table.Column<bool>(type: "boolean", nullable: false),
                    observatii = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    stare = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    creat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    activat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracte", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documente",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    act_aditional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nume_fisier = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    tip_continut = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dimensiune = table.Column<long>(type: "bigint", nullable: false),
                    hash_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    continut = table.Column<byte[]>(type: "bytea", nullable: false),
                    incarcat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                name: "inchideri",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    inchis_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inchideri", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acte_aditionale_firma_id_contract_id_numar",
                schema: "contracte",
                table: "acte_aditionale",
                columns: new[] { "firma_id", "contract_id", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracte_firma_id_an_numar",
                schema: "contracte",
                table: "contracte",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracte_firma_id_partener_id",
                schema: "contracte",
                table: "contracte",
                columns: new[] { "firma_id", "partener_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contracte_firma_id_proiect_id",
                schema: "contracte",
                table: "contracte",
                columns: new[] { "firma_id", "proiect_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documente_firma_id_contract_id",
                schema: "contracte",
                table: "documente",
                columns: new[] { "firma_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inchideri_firma_id_contract_id",
                schema: "contracte",
                table: "inchideri",
                columns: new[] { "firma_id", "contract_id" },
                unique: true);

            // RLS pe firma (regula 3) si drepturile rolului aplicatiei pe schema noua.
            migrationBuilder.AplicaRls("contracte", "contracte");
            migrationBuilder.AplicaRls("contracte", "acte_aditionale");
            migrationBuilder.AplicaRls("contracte", "inchideri");
            migrationBuilder.AplicaRls("contracte", "documente");
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA contracte TO erp_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA contracte TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA contracte
                  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA contracte
                  GRANT USAGE, SELECT ON SEQUENCES TO erp_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acte_aditionale",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "contracte",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "documente",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "inchideri",
                schema: "contracte");
        }
    }
}
