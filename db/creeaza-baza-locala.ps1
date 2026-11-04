# Creează baza locală de dezvoltare pe un PostgreSQL instalat (fără Docker) și rulează migrările.
#
#   powershell -ExecutionPolicy Bypass -File db\creeaza-baza-locala.ps1
#
# Cere o singură dată parola superuser-ului „postgres”. Pașii:
#   1. CREATE DATABASE erp (dacă nu există)
#   2. db/init/01-roluri.sql: rolurile erp_owner / erp_app și schemele modulelor
#   3. migrările EF ale fiecărui modul, rulate ca erp_owner

param(
    [string]$PgBin = "C:\Program Files\PostgreSQL\18\bin",
    [string]$Gazda = "localhost",
    [int]$Port = 5432
)

$ErrorActionPreference = "Stop"
$radacina = Split-Path -Parent $PSScriptRoot
$psql = Join-Path $PgBin "psql.exe"

$parola = Read-Host "Parola utilizatorului postgres" -AsSecureString
$env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($parola))

try {
    $exista = & $psql -h $Gazda -p $Port -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'erp'"
    if ($exista -ne "1") {
        Write-Host "Creez baza erp..."
        & $psql -h $Gazda -p $Port -U postgres -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE erp"
        Write-Host "Creez rolurile și schemele..."
        & $psql -h $Gazda -p $Port -U postgres -d erp -v ON_ERROR_STOP=1 -f (Join-Path $radacina "db\init\01-roluri.sql")
        if ($LASTEXITCODE -ne 0) { throw "01-roluri.sql a eșuat." }
    }
    else {
        Write-Host "Baza erp există deja; trec la migrări."
    }
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

# Migrările rulează cu rolul proprietar (parola de dezvoltare din 01-roluri.sql), NU cu erp_app.
$conexiune = "Host=$Gazda;Port=$Port;Database=erp;Username=erp_owner;Password=erp_owner_dev"
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
foreach ($c in $contexte) {
    $m = $c[0]; $context = $c[1]
    Write-Host "Migrări $context..."
    dotnet ef database update `
        -p (Join-Path $radacina "src\Modules\$m\ErpConstructii.$m.Infrastructure") `
        -s (Join-Path $radacina "src\Host\ErpConstructii.Api") `
        -c $context --connection $conexiune
    if ($LASTEXITCODE -ne 0) { throw "Migrările $context au eșuat." }
}

Write-Host "Gata. Pornește API-ul: dotnet run --project src/Host/ErpConstructii.Api" -ForegroundColor Green
