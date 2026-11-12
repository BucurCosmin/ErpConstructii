# Restaurează baza de date dintr-un backup făcut cu db\backup-baza.ps1 (ex. baza de dezvoltare trecută pe producție).
#
#   powershell -ExecutionPolicy Bypass -File db\restaureaza-baza.ps1 -Fisier backup\erp_2026-11-11_1830.dump
#   powershell -ExecutionPolicy Bypass -File db\restaureaza-baza.ps1 -Fisier ... -Baza erp_proba   # probă, fără să atingi baza erp
#
# ATENȚIE: baza țintă se ÎNLOCUIEȘTE complet cu conținutul backup-ului. Înainte, scriptul face automat un backup al
# bazei existente (backup\<baza>_inainte-de-restaurare_….dump), ca să poți reveni.
#
# Pașii:
#   1. backup de siguranță al bazei existente (dacă există);
#   2. oprește instanța de producție de pe acest calculator (dacă e instalată), ca să nu scrie în baza în curs de înlocuire;
#   3. rolurile erp_owner / erp_app: create dacă lipsesc (fără parolă; parolele existente rămân neschimbate — ele NU sunt
#      în backup); pe un server nou rulează apoi db\creeaza-baza-locala.ps1 cu parolele producției;
#   4. baza se șterge și se creează din nou, apoi se restaurează (o singură tranzacție: la eroare nu rămâne pe jumătate);
#   5. db\init\01-roluri.sql (drepturile), apoi pornește din nou producția.
#
# Versiunea PostgreSQL de pe țintă trebuie să fie cel puțin cea pe care s-a făcut backup-ul (ex. 18 → 18).

param(
    [Parameter(Mandatory = $true)][string]$Fisier,
    [string]$PgBin = "C:\Program Files\PostgreSQL\18\bin",
    [string]$Gazda = "localhost",
    [int]$Port = 5432,
    [string]$Baza = "erp",
    [switch]$FaraBackupSiguranta
)

$ErrorActionPreference = "Stop"
$radacina = Split-Path -Parent $PSScriptRoot
$psql = Join-Path $PgBin "psql.exe"
$pgDump = Join-Path $PgBin "pg_dump.exe"
$pgRestore = Join-Path $PgBin "pg_restore.exe"
if (-not (Test-Path $pgRestore)) { throw "Nu găsesc pg_restore.exe în $PgBin (dă calea cu -PgBin)." }
if (-not (Test-Path $Fisier)) { throw "Nu găsesc fișierul $Fisier." }
$Fisier = (Resolve-Path $Fisier).Path
if ($Baza -notmatch '^[a-z_][a-z0-9_]*$') { throw "Numele bazei poate avea doar litere mici, cifre și _." }

& $pgRestore --list $Fisier | Out-Null
if ($LASTEXITCODE -ne 0) { throw "$Fisier nu e un backup valid (format custom PostgreSQL)." }

Write-Host "Baza $Baza de pe ${Gazda}:${Port} va fi ÎNLOCUITĂ cu conținutul din $Fisier." -ForegroundColor Yellow
$confirmare = Read-Host "Scrie numele bazei ($Baza) ca să continui"
if ($confirmare -ne $Baza) { Write-Host "Anulat."; return }

$parola = Read-Host "Parola utilizatorului postgres" -AsSecureString
$env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($parola))
$productieOprita = $false
try {
    $exista = & $psql -h $Gazda -p $Port -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$Baza'"
    if ($LASTEXITCODE -ne 0) { throw "Nu mă pot conecta ca postgres la ${Gazda}:${Port} (parola greșită sau serviciul PostgreSQL oprit)." }

    # 1. Backup de siguranță al bazei existente.
    if ($exista -eq "1" -and -not $FaraBackupSiguranta) {
        $folder = Join-Path $radacina "backup"
        New-Item -ItemType Directory -Force $folder | Out-Null
        $siguranta = Join-Path $folder ("{0}_inainte-de-restaurare_{1}.dump" -f $Baza, (Get-Date -Format "yyyy-MM-dd_HHmm"))
        Write-Host "Backup de siguranță al bazei existente: $siguranta"
        & $pgDump -h $Gazda -p $Port -U postgres -d $Baza --format=custom --compress=9 --no-password --file $siguranta
        if ($LASTEXITCODE -ne 0) { throw "Backup-ul de siguranță a eșuat; nu am atins baza." }
    }

    # 2. Producția de pe acest calculator se oprește cât timp se înlocuiește baza.
    $opreste = Join-Path $radacina "deploy\opreste-productie.ps1"
    if ($Baza -eq "erp" -and (Test-Path (Join-Path $radacina "productie\setari.json")) -and (Test-Path $opreste)) {
        Write-Host "Opresc producția..."
        & $opreste
        $productieOprita = $true
    }

    # 3. Rolurile (globale, nu sunt în backup): create fără parolă dacă lipsesc.
    & $psql -h $Gazda -p $Port -U postgres -d postgres -v ON_ERROR_STOP=1 -q -c @"
DO `$`$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_owner') THEN CREATE ROLE erp_owner LOGIN; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN CREATE ROLE erp_app LOGIN NOBYPASSRLS; END IF;
END `$`$;
"@
    if ($LASTEXITCODE -ne 0) { throw "Crearea rolurilor a eșuat." }

    # 4. Baza goală, apoi restaurarea într-o singură tranzacție.
    Write-Host "Recreez baza $Baza..."
    & $psql -h $Gazda -p $Port -U postgres -d postgres -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS $Baza WITH (FORCE)"
    if ($LASTEXITCODE -ne 0) { throw "Ștergerea bazei $Baza a eșuat." }
    & $psql -h $Gazda -p $Port -U postgres -d postgres -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE $Baza"
    if ($LASTEXITCODE -ne 0) { throw "Crearea bazei $Baza a eșuat." }

    Write-Host "Restaurez datele..."
    & $pgRestore -h $Gazda -p $Port -U postgres -d $Baza --no-password --single-transaction --exit-on-error $Fisier
    if ($LASTEXITCODE -ne 0) {
        throw "Restaurarea a eșuat (nimic nu s-a scris în $Baza). Baza veche e în backup-ul de siguranță: restaureaz-o cu același script."
    }

    # 5. Drepturile pe bază și pe scheme (idempotent).
    & $psql -h $Gazda -p $Port -U postgres -d $Baza -v ON_ERROR_STOP=1 -q -c "GRANT CONNECT ON DATABASE $Baza TO erp_owner, erp_app; GRANT CREATE ON DATABASE $Baza TO erp_owner;"
    if ($Baza -eq "erp") {
        & $psql -h $Gazda -p $Port -U postgres -d $Baza -v ON_ERROR_STOP=1 -q -f (Join-Path $radacina "db\init\01-roluri.sql")
        if ($LASTEXITCODE -ne 0) { throw "01-roluri.sql a eșuat." }
    }

    # Parola lui erp_app = cea din configurarea producției (altfel: „password authentication failed for user erp_app”).
    # Fără producție pe acest calculator, parolele se pun cu db\creeaza-baza-locala.ps1.
    $configProductie = Join-Path $radacina "productie\api\appsettings.Production.local.json"
    if (Test-Path $configProductie) {
        $conexiune = (Get-Content $configProductie -Raw | ConvertFrom-Json).ConnectionStrings.Erp
        if ($conexiune -match '(?:^|;)Password=([^;]*)') {
            $pApp = $Matches[1].Replace("'", "''")
            & $psql -h $Gazda -p $Port -U postgres -d postgres -v ON_ERROR_STOP=1 -q -c "ALTER ROLE erp_app PASSWORD '$pApp'"
            if ($LASTEXITCODE -ne 0) { throw "Setarea parolei lui erp_app a eșuat." }
            Write-Host "Parola lui erp_app: cea din productie\api\appsettings.Production.local.json."
        }
    }
    else {
        Write-Warning "Nu există configurarea producției pe acest calculator: verifică parolele rolurilor cu db\creeaza-baza-locala.ps1 (-ParolaApp / -ParolaOwner)."
    }

    $sumar = & $psql -h $Gazda -p $Port -U postgres -d $Baza -tAc "SELECT (SELECT count(*) FROM identitate.firme) || ' firme, ' || (SELECT count(*) FROM proiecte.proiecte) || ' proiecte'" 2>$null
    Write-Host "Baza $Baza a fost restaurată: $sumar." -ForegroundColor Green
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

if ($productieOprita) {
    Write-Host "Pornesc din nou producția..."
    & (Join-Path $radacina "deploy\porneste-productie.ps1")
}
