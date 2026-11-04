# Creează (sau repară) baza pe un PostgreSQL instalat (fără Docker) și rulează migrările.
#
#   powershell -ExecutionPolicy Bypass -File db\creeaza-baza-locala.ps1
#
# Cere o singură dată parola superuser-ului „postgres”. Pașii, toți idempotenți (scriptul se poate rula din nou oricând):
#   1. CREATE DATABASE erp (dacă nu există)
#   2. db/init/01-roluri.sql: rolurile erp_owner / erp_app (create dacă lipsesc) și schemele modulelor
#   3. parolele rolurilor, setate la valorile date (repară „password authentication failed for user erp_app”)
#   4. migrările EF ale fiecărui modul, rulate ca erp_owner
#
# Pe un server, alege parole proprii și folosește-le apoi și la pregătirea producției:
#   powershell -ExecutionPolicy Bypass -File db\creeaza-baza-locala.ps1 -ParolaApp "..." -ParolaOwner "..."
#   powershell -ExecutionPolicy Bypass -File deploy\pregateste-productie.ps1 -Domeniu ... -ParolaBazaAplicatie "..."

param(
    [string]$PgBin = "C:\Program Files\PostgreSQL\18\bin",
    [string]$Gazda = "localhost",
    [int]$Port = 5432,
    [string]$ParolaApp = "erp_app_dev",
    [string]$ParolaOwner = "erp_owner_dev"
)

$ErrorActionPreference = "Stop"
$radacina = Split-Path -Parent $PSScriptRoot
$psql = Join-Path $PgBin "psql.exe"
if (-not (Test-Path $psql)) { throw "Nu găsesc psql.exe în $PgBin (dă calea cu -PgBin, ex. -PgBin 'C:\Program Files\PostgreSQL\17\bin')." }

$parola = Read-Host "Parola utilizatorului postgres" -AsSecureString
$env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($parola))

try {
    $exista = & $psql -h $Gazda -p $Port -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'erp'"
    if ($LASTEXITCODE -ne 0) { throw "Nu mă pot conecta ca postgres la ${Gazda}:${Port} (parola greșită sau serviciul PostgreSQL oprit)." }
    if ($exista -ne "1") {
        Write-Host "Creez baza erp..."
        & $psql -h $Gazda -p $Port -U postgres -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE erp"
        if ($LASTEXITCODE -ne 0) { throw "CREATE DATABASE erp a eșuat." }
    }
    else {
        Write-Host "Baza erp există deja."
    }

    # Rolurile și schemele se verifică la fiecare rulare: baza poate exista fără roluri (instalare întreruptă).
    Write-Host "Verific rolurile și schemele..."
    & $psql -h $Gazda -p $Port -U postgres -d erp -v ON_ERROR_STOP=1 -q -f (Join-Path $radacina "db\init\01-roluri.sql")
    if ($LASTEXITCODE -ne 0) { throw "01-roluri.sql a eșuat." }

    # Parolele rolurilor = cele din configurarea aplicației. Apostroful se dublează pentru SQL.
    $pApp = $ParolaApp.Replace("'", "''")
    $pOwner = $ParolaOwner.Replace("'", "''")
    & $psql -h $Gazda -p $Port -U postgres -d erp -v ON_ERROR_STOP=1 -q -c "ALTER ROLE erp_app PASSWORD '$pApp'; ALTER ROLE erp_owner PASSWORD '$pOwner';"
    if ($LASTEXITCODE -ne 0) { throw "Setarea parolelor rolurilor a eșuat." }
    Write-Host "Rolurile erp_owner și erp_app sunt pregătite." -ForegroundColor Green
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

# Instrumentul de migrări (dotnet-ef) e declarat în .config\dotnet-tools.json; se instalează local dacă lipsește.
Push-Location $radacina
try { dotnet tool restore | Out-Null; if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore a eșuat (e instalat .NET SDK 10?)." } }
finally { Pop-Location }

# Migrările rulează cu rolul proprietar, NU cu erp_app.
$conexiune = "Host=$Gazda;Port=$Port;Database=erp;Username=erp_owner;Password=$ParolaOwner"
# (modul, DbContext): un modul poate avea mai multe contexte (Identitate: business + autentificare).
$contexte = @(
    @("Identitate", "IdentitateDbContext"),
    @("Identitate", "AutentificareDbContext"),
    @("Proiecte", "ProiecteDbContext"),
    @("Devize", "DevizeDbContext"),
    @("Formulare", "FormulareDbContext"),
    @("Parteneri", "ParteneriDbContext"),
    @("Aprovizionare", "AprovizionareDbContext"),
    @("Facturi", "FacturiDbContext"),
    @("Stocuri", "StocuriDbContext")
)
Push-Location $radacina
foreach ($c in $contexte) {
    $m = $c[0]; $context = $c[1]
    Write-Host "Migrări $context..."
    dotnet ef database update `
        -p (Join-Path $radacina "src\Modules\$m\ErpConstructii.$m.Infrastructure") `
        -s (Join-Path $radacina "src\Host\ErpConstructii.Api") `
        -c $context --connection $conexiune
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Migrările $context au eșuat." }
}
Pop-Location

Write-Host "Gata. Pornește API-ul: dotnet run --project src/Host/ErpConstructii.Api" -ForegroundColor Green
