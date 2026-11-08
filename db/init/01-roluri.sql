-- Rolurile bazei de date. Idempotent: se poate rula din nou oricând (db\creeaza-baza-locala.ps1 îl rulează la fiecare
-- execuție; în docker rulează la prima pornire).
--
--   erp_owner  proprietarul schemelor și tabelelor; rulează migrările EF. NU îl folosește aplicația.
--   erp_app    rolul aplicației: drepturi DML, fără BYPASSRLS, nu e proprietar → RLS se aplică.
--
-- Parolele de mai jos sunt DOAR pentru dezvoltare locală.

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_owner') THEN
    CREATE ROLE erp_owner LOGIN PASSWORD 'erp_owner_dev';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
    CREATE ROLE erp_app LOGIN PASSWORD 'erp_app_dev' NOBYPASSRLS;
  END IF;
END $$;
-- Rolurile pot exista dinainte (alt proiect, o instalare întreruptă): drepturile de bază se impun oricum.
ALTER ROLE erp_owner LOGIN;
ALTER ROLE erp_app LOGIN NOBYPASSRLS;

GRANT CONNECT ON DATABASE erp TO erp_owner, erp_app;
GRANT CREATE ON DATABASE erp TO erp_owner;

-- Schemele modulelor (D-03). Un modul nou = o schemă nouă aici + în ModulX.Nume.
DO $$
DECLARE s text;
BEGIN
  FOREACH s IN ARRAY ARRAY['identitate', 'proiecte', 'devize', 'formulare', 'parteneri', 'aprovizionare', 'facturi', 'stocuri', 'audit', 'contracte'] LOOP
    EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION erp_owner', s);
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO erp_app', s);
    -- Tabelele create de erp_owner în viitor primesc automat drepturile pentru erp_app.
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO erp_app', s);
  END LOOP;
END $$;

-- Jurnalul de audit: aplicația poate doar adăuga (append-only).
ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner IN SCHEMA audit REVOKE UPDATE, DELETE ON TABLES FROM erp_app;
