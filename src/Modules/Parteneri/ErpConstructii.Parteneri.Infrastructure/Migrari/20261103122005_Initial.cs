using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Parteneri.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "parteneri");

            migrationBuilder.CreateTable(
                name: "parteneri",
                schema: "parteneri",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cui = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nr_reg_com = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    platitor_tva = table.Column<bool>(type: "boolean", nullable: false),
                    telefon = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    persoana_contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefon_contact = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    observatii = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    este_furnizor = table.Column<bool>(type: "boolean", nullable: false),
                    este_client = table.Column<bool>(type: "boolean", nullable: false),
                    este_subantreprenor = table.Column<bool>(type: "boolean", nullable: false),
                    activ = table.Column<bool>(type: "boolean", nullable: false),
                    adresa_cod_postal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    adresa_judet = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    adresa_localitate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    adresa_strada = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    adresa_tara = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    conturi_bancare = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parteneri", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parteneri_firma_id_cui",
                schema: "parteneri",
                table: "parteneri",
                columns: new[] { "firma_id", "cui" },
                unique: true,
                filter: "cui IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_parteneri_firma_id_este_furnizor_denumire",
                schema: "parteneri",
                table: "parteneri",
                columns: new[] { "firma_id", "este_furnizor", "denumire" });

            migrationBuilder.AplicaRls("parteneri", "parteneri");
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA parteneri TO erp_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE parteneri.parteneri TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA parteneri
                  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA parteneri
                  GRANT USAGE, SELECT ON SEQUENCES TO erp_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parteneri",
                schema: "parteneri");
        }
    }
}
