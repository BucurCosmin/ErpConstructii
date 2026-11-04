using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Stocuri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "stocuri");

            migrationBuilder.CreateTable(
                name: "aliasuri_articole",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    furnizor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cheie = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    articol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aliasuri_articole", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "articole",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    unitate_masura = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    activ = table.Column<bool>(type: "boolean", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_articole", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gestiuni",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gestiuni", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "miscari",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    tip = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cantitate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_numar = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_miscari", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nir",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    gestiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestiune_denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_numar = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    factura_data = table.Column<DateOnly>(type: "date", nullable: false),
                    furnizor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    furnizor_denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    observatii = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    receptionat_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receptionat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nir", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "solduri",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestiune_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantitate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solduri", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transferuri",
                schema: "stocuri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    gestiune_sursa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestiune_sursa_denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gestiune_destinatie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gestiune_destinatie_denumire = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    observatii = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    emis_de_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emis_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transferuri", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aliasuri_articole_firma_id_furnizor_id_cheie",
                schema: "stocuri",
                table: "aliasuri_articole",
                columns: new[] { "firma_id", "furnizor_id", "cheie" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_articole_firma_id_cod",
                schema: "stocuri",
                table: "articole",
                columns: new[] { "firma_id", "cod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gestiuni_firma_id_cod",
                schema: "stocuri",
                table: "gestiuni",
                columns: new[] { "firma_id", "cod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gestiuni_firma_id_proiect_id",
                schema: "stocuri",
                table: "gestiuni",
                columns: new[] { "firma_id", "proiect_id" },
                unique: true,
                filter: "proiect_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_miscari_firma_id_gestiune_id_articol_id_data",
                schema: "stocuri",
                table: "miscari",
                columns: new[] { "firma_id", "gestiune_id", "articol_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_nir_firma_id_an_numar",
                schema: "stocuri",
                table: "nir",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nir_firma_id_factura_id",
                schema: "stocuri",
                table: "nir",
                columns: new[] { "firma_id", "factura_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_solduri_firma_id_gestiune_id_articol_id",
                schema: "stocuri",
                table: "solduri",
                columns: new[] { "firma_id", "gestiune_id", "articol_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transferuri_firma_id_an_numar",
                schema: "stocuri",
                table: "transferuri",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transferuri_firma_id_proiect_id_data",
                schema: "stocuri",
                table: "transferuri",
                columns: new[] { "firma_id", "proiect_id", "data" });

            // RLS pe firmă (regula 3) și drepturile rolului aplicației pe schema nouă.
            migrationBuilder.AplicaRls("stocuri", "gestiuni");
            migrationBuilder.AplicaRls("stocuri", "articole");
            migrationBuilder.AplicaRls("stocuri", "aliasuri_articole");
            migrationBuilder.AplicaRls("stocuri", "nir");
            migrationBuilder.AplicaRls("stocuri", "transferuri");
            migrationBuilder.AplicaRls("stocuri", "miscari");
            migrationBuilder.AplicaRls("stocuri", "solduri");
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA stocuri TO erp_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA stocuri TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA stocuri
                  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA stocuri
                  GRANT USAGE, SELECT ON SEQUENCES TO erp_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aliasuri_articole",
                schema: "stocuri");

            migrationBuilder.DropTable(
                name: "articole",
                schema: "stocuri");

            migrationBuilder.DropTable(
                name: "gestiuni",
                schema: "stocuri");

            migrationBuilder.DropTable(
                name: "miscari",
                schema: "stocuri");

            migrationBuilder.DropTable(
                name: "nir",
                schema: "stocuri");

            migrationBuilder.DropTable(
                name: "solduri",
                schema: "stocuri");

            migrationBuilder.DropTable(
                name: "transferuri",
                schema: "stocuri");
        }
    }
}
