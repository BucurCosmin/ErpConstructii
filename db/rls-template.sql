-- Șablon Row-Level Security pentru un tabel de business (decizia D-04, REQ-DRP-08).
-- Se aplică în migrarea EF care creează tabelul (migrationBuilder.Sql(...)), pentru FIECARE
-- tabel cu coloana firma_id. Excepție: schema identitate.
--
-- Valoarea app.firma_id este setată de InterceptorFirmaRls la deschiderea conexiunii.
-- current_setting(..., true) întoarce NULL/'' dacă nu e setată → nicio linie vizibilă.

ALTER TABLE {schema}.{tabel} ENABLE ROW LEVEL SECURITY;
ALTER TABLE {schema}.{tabel} FORCE ROW LEVEL SECURITY;

CREATE POLICY izolare_firma ON {schema}.{tabel}
  USING      (firma_id = NULLIF(current_setting('app.firma_id', true), '')::uuid)
  WITH CHECK (firma_id = NULLIF(current_setting('app.firma_id', true), '')::uuid);

-- Test manual (ca erp_app):
--   SELECT set_config('app.firma_id', '<guid firma A>', false);
--   SELECT count(*) FROM {schema}.{tabel};   -- doar rândurile firmei A
--   SELECT set_config('app.firma_id', '', false);
--   SELECT count(*) FROM {schema}.{tabel};   -- 0
