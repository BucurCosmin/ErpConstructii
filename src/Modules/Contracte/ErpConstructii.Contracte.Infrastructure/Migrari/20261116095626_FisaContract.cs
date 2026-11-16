using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Contracte.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class FisaContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "amplasament",
                schema: "contracte",
                table: "contracte",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "data_predare_amplasament",
                schema: "contracte",
                table: "contracte",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "data_pvrtl",
                schema: "contracte",
                table: "contracte",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "durata",
                schema: "contracte",
                table: "contracte",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "luni_garantie_tehnica",
                schema: "contracte",
                table: "contracte",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "membri_json",
                schema: "contracte",
                table: "contracte",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<decimal>(
                name: "penalitate_executie_zi",
                schema: "contracte",
                table: "contracte",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "penalitate_plata_zi",
                schema: "contracte",
                table: "contracte",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "plafon_penalitate_executie",
                schema: "contracte",
                table: "contracte",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "plafon_penalitate_plata",
                schema: "contracte",
                table: "contracte",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "procent_garantie_avans",
                schema: "contracte",
                table: "contracte",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "procent_garantie_tehnica",
                schema: "contracte",
                table: "contracte",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "stare_text",
                schema: "contracte",
                table: "contracte",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tip_pret",
                schema: "contracte",
                table: "contracte",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transe_avans_json",
                schema: "contracte",
                table: "contracte",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "semnat",
                schema: "contracte",
                table: "acte_aditionale",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "semnat_de_nume",
                schema: "contracte",
                table: "acte_aditionale",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "actiuni",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    descriere = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    prioritate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stare = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    responsabil_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsabil_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    termen = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("pk_actiuni", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "corecturi",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motiv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    modificari = table.Column<string>(type: "text", nullable: false),
                    corectat_de_nume = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corecturi", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "faze_executie",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    denumire = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    start = table.Column<DateOnly>(type: "date", nullable: true),
                    final = table.Column<DateOnly>(type: "date", nullable: true),
                    conditii = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    stare = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_faze_executie", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instrumente",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tip = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    descriere = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    emitent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    numar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    beneficiar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    valoare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    data_emitere = table.Column<DateOnly>(type: "date", nullable: true),
                    data_expirare = table.Column<DateOnly>(type: "date", nullable: true),
                    stare = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_instrumente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "obligatii",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    denumire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    temei = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    termen = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    data_termen = table.Column<DateOnly>(type: "date", nullable: true),
                    stare = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    responsabil = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_obligatii", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "riscuri",
                schema: "contracte",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    element = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    prevedere = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    stare = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_riscuri", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actiuni_firma_id_contract_id",
                schema: "contracte",
                table: "actiuni",
                columns: new[] { "firma_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_corecturi_firma_id_contract_id",
                schema: "contracte",
                table: "corecturi",
                columns: new[] { "firma_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_faze_executie_firma_id_contract_id",
                schema: "contracte",
                table: "faze_executie",
                columns: new[] { "firma_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_instrumente_firma_id_contract_id",
                schema: "contracte",
                table: "instrumente",
                columns: new[] { "firma_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_instrumente_firma_id_data_expirare",
                schema: "contracte",
                table: "instrumente",
                columns: new[] { "firma_id", "data_expirare" });

            migrationBuilder.CreateIndex(
                name: "ix_obligatii_firma_id_contract_id",
                schema: "contracte",
                table: "obligatii",
                columns: new[] { "firma_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_riscuri_firma_id_contract_id",
                schema: "contracte",
                table: "riscuri",
                columns: new[] { "firma_id", "contract_id" });

            // RLS pe firmă (regula 3) pentru registrele fișei și corecturi (D-56).
            foreach (var tabel in new[] { "actiuni", "corecturi", "faze_executie", "instrumente", "obligatii", "riscuri" })
                migrationBuilder.AplicaRls("contracte", tabel);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actiuni",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "corecturi",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "faze_executie",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "instrumente",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "obligatii",
                schema: "contracte");

            migrationBuilder.DropTable(
                name: "riscuri",
                schema: "contracte");

            migrationBuilder.DropColumn(
                name: "amplasament",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "data_predare_amplasament",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "data_pvrtl",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "durata",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "luni_garantie_tehnica",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "membri_json",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "penalitate_executie_zi",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "penalitate_plata_zi",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "plafon_penalitate_executie",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "plafon_penalitate_plata",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "procent_garantie_avans",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "procent_garantie_tehnica",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "stare_text",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "tip_pret",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "transe_avans_json",
                schema: "contracte",
                table: "contracte");

            migrationBuilder.DropColumn(
                name: "semnat",
                schema: "contracte",
                table: "acte_aditionale");

            migrationBuilder.DropColumn(
                name: "semnat_de_nume",
                schema: "contracte",
                table: "acte_aditionale");
        }
    }
}
