using System;
using ErpConstructii.Infrastructure.Persistenta;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "aprovizionare");

            migrationBuilder.CreateTable(
                name: "note_comanda",
                schema: "aprovizionare",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    an = table.Column<int>(type: "integer", nullable: false),
                    numar = table.Column<int>(type: "integer", nullable: false),
                    proiect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proiect_cod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lucrare = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    emitent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sablon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    versiune_sablon = table.Column<int>(type: "integer", nullable: false),
                    linii_json = table.Column<string>(type: "jsonb", nullable: false),
                    stare = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    decisa_de_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decisa_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motiv_respingere = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    firma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creat_de = table.Column<Guid>(type: "uuid", nullable: true),
                    modificat_la = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modificat_de = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note_comanda", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_note_comanda_firma_id_an_numar",
                schema: "aprovizionare",
                table: "note_comanda",
                columns: new[] { "firma_id", "an", "numar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_note_comanda_firma_id_proiect_id_data",
                schema: "aprovizionare",
                table: "note_comanda",
                columns: new[] { "firma_id", "proiect_id", "data" });

            migrationBuilder.AplicaRls("aprovizionare", "note_comanda");
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA aprovizionare TO erp_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE aprovizionare.note_comanda TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA aprovizionare
                  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA aprovizionare
                  GRANT USAGE, SELECT ON SEQUENCES TO erp_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "note_comanda",
                schema: "aprovizionare");
        }
    }
}
