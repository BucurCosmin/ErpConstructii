# Backup al bazei de date (toate firmele, toate modulele, inclusiv documentele originale arhivate).
#
#   powershell -ExecutionPolicy Bypass -File db\backup-baza.ps1
#   powershell -ExecutionPolicy Bypass -File db\backup-baza.ps1 -Folder D:\Backup
#
# Rezultatul: backup\erp_AAAA-LL-ZZ_HHmm.dump (format „custom” al PostgreSQL, comprimat), de restaurat cu
# db\restaureaza-baza.ps1 pe același calculator sau pe altul (ex. producția).
#
# Cere parola superuser-ului „postgres”: tabelele au Row-Level Security forțat (D-04), care se aplică și proprietarului
# erp_owner, deci doar un superuser vede rândurile tuturor firmelor.
# Fișierul conține date personale și hash-urile parolelor utilizatorilor: păstrează-l în loc sigur (folderul backup\ nu
# intră în git).

param(
    [string]$PgBin = "C:\Program Files\PostgreSQL\18\bin",
    [string]$Gazda = "localhost",
    [int]$Port = 5432,
    [string]$Baza = "erp",
    [string]$Folder = (Join-Path (Split-Path -Parent $PSScriptRoot) "backup")
)

$ErrorActionPreference = "Stop"
$pgDump = Join-Path $PgBin "pg_dump.exe"
$pgRestore = Join-Path $PgBin "pg_restore.exe"
if (-not (Test-Path $pgDump)) { throw "Nu găsesc pg_dump.exe în $PgBin (dă calea cu -PgBin)." }

New-Item -ItemType Directory -Force $Folder | Out-Null
$fisier = Join-Path $Folder ("{0}_{1}.dump" -f $Baza, (Get-Date -Format "yyyy-MM-dd_HHmm"))

$parola = Read-Host "Parola utilizatorului postgres" -AsSecureString
$env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($parola))
try {
    Write-Host "Fac backup-ul bazei $Baza de pe ${Gazda}:${Port}..."
    & $pgDump -h $Gazda -p $Port -U postgres -d $Baza --format=custom --compress=9 --no-password --file $fisier
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $fisier -ErrorAction SilentlyContinue
        throw "pg_dump a eșuat (parola greșită, serviciul PostgreSQL oprit sau baza $Baza nu există)."
    }

    # Verificare: arhiva se poate citi și conține tabelele modulelor.
    $continut = & $pgRestore --list $fisier
    if ($LASTEXITCODE -ne 0) { throw "Arhiva $fisier nu se poate citi." }
    $tabele = @($continut | Where-Object { $_ -match ' TABLE DATA ' }).Count
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

$marime = "{0:N1} MB" -f ((Get-Item $fisier).Length / 1MB)
Write-Host "Backup gata: $fisier ($marime, $tabele tabele cu date)." -ForegroundColor Green
Write-Host "Restaurare (ex. pe producție): powershell -ExecutionPolicy Bypass -File db\restaureaza-baza.ps1 -Fisier `"$fisier`""
