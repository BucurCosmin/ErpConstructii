using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Formulare.Infrastructure.Migrari
{
    /// <summary>
    /// Tipul de formular „comanda-furnizor” era de fapt necesarul de materiale → „necesar-materiale”.
    /// Tabelele au FORCE RLS, iar migrarea rulează fără firmă activă: ridic temporar forțarea pentru proprietar,
    /// actualizez toate firmele, apoi o pun la loc (în aceeași tranzacție).
    /// </summary>
    public partial class NecesarMaterialeCodTip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => SchimbaCod(migrationBuilder, "comanda-furnizor", "necesar-materiale");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => SchimbaCod(migrationBuilder, "necesar-materiale", "comanda-furnizor");

        private static void SchimbaCod(MigrationBuilder migrationBuilder, string vechi, string nou)
        {
            foreach (var tabel in new[] { "sabloane", "importuri" })
            {
                migrationBuilder.Sql($"ALTER TABLE formulare.{tabel} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"UPDATE formulare.{tabel} SET cod_tip = '{nou}' WHERE cod_tip = '{vechi}';");
                migrationBuilder.Sql($"ALTER TABLE formulare.{tabel} FORCE ROW LEVEL SECURITY;");
            }
        }
    }
}
