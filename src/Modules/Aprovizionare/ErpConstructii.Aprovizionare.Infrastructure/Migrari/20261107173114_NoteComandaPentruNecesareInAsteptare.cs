using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpConstructii.Aprovizionare.Infrastructure.Migrari
{
    /// <summary>
    /// D-37: necesarele aflate deja în așteptarea aprobării primesc nota de comandă „de acceptat”, ca și cele noi.
    /// Tabelele au RLS forțat, deci inserarea se face pe rând, cu firma setată în sesiune.
    /// </summary>
    public partial class NoteComandaPentruNecesareInAsteptare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    f uuid;
                    an_curent int := extract(year from current_date)::int;
                BEGIN
                    FOR f IN SELECT id FROM identitate.firme LOOP
                        PERFORM set_config('app.firma_id', f::text, true);
                        INSERT INTO aprovizionare.note_comanda (
                            id, an, numar, data, proiect_id, proiect_cod, lucrare, furnizor_denumire, linii_json, observatii, sursa,
                            inregistrata_de_id, inregistrata_de_nume, firma_id, creat_la, necesar_id, stare)
                        SELECT gen_random_uuid(), an_curent,
                               coalesce((SELECT max(c.numar) FROM aprovizionare.note_comanda c WHERE c.an = an_curent), 0)
                                   + row_number() OVER (ORDER BY n.an, n.numar),
                               n.data, n.proiect_id, n.proiect_cod, n.lucrare, '',
                               (SELECT coalesce(jsonb_agg(jsonb_build_object(
                                           'Denumire', l.v ->> 'Denumire',
                                           'UnitateMasura', l.v ->> 'UnitateMasura',
                                           'Cantitate', l.v -> 'Cantitate',
                                           'Termen', null,
                                           'PretUnitar', null,
                                           'Observatii', nullif(trim(coalesce(l.v ->> 'Observatii', '')), '')) ORDER BY l.i), '[]'::jsonb)
                                  FROM jsonb_array_elements(n.linii_json) WITH ORDINALITY AS l(v, i)),
                               left('Din necesarul NM/' || n.an || '/' || lpad(n.numar::text, 4, '0') || ' (' || n.compartiment_santier || ')', 1000),
                               'Sistem', n.emitent_id, n.emitent_nume, f, now(), n.id, 'DeAcceptat'
                          FROM aprovizionare.necesare_materiale n
                         WHERE n.stare = 'InAsteptareAprobare'
                           AND NOT EXISTS (SELECT 1 FROM aprovizionare.note_comanda c WHERE c.necesar_id = n.id);
                    END LOOP;
                    PERFORM set_config('app.firma_id', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE f uuid;
                BEGIN
                    FOR f IN SELECT id FROM identitate.firme LOOP
                        PERFORM set_config('app.firma_id', f::text, true);
                        DELETE FROM aprovizionare.note_comanda WHERE stare = 'DeAcceptat' AND necesar_id IS NOT NULL;
                    END LOOP;
                    PERFORM set_config('app.firma_id', '', true);
                END $$;
                """);
        }
    }
}
