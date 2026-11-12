using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Identitate.Infrastructure.Migrari
{
    /// <summary>
    /// Aprobarea devizelor (D-53): dreptul <c>ofertare.aprobare</c> nu era în niciun rol predefinit. Îl primesc rolurile
    /// existente „director” (ca în rolurile predefinite noi) și rolurile cu acces complet: editare pe ofertare + administrare.
    /// </summary>
    public partial class AprobareDevize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE identitate.roluri
                SET permisiuni = array_append(permisiuni, 'ofertare.aprobare')
                WHERE NOT ('ofertare.aprobare' = ANY(permisiuni))
                  AND (cod = 'director'
                       OR ('ofertare.editare' = ANY(permisiuni) AND 'administrare.editare' = ANY(permisiuni)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dreptul dat poate fi fost confirmat între timp de administrator: nu se retrage automat.
        }
    }
}
