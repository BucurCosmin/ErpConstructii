# Repornește instanța de producție.
#
#   powershell -ExecutionPolicy Bypass -File deploy\reporneste-productie.ps1               # doar repornire (aceeași versiune)
#   powershell -ExecutionPolicy Bypass -File deploy\reporneste-productie.ps1 -Actualizeaza # recompilează codul curent, apoi pornește
#   powershell -ExecutionPolicy Bypass -File deploy\reporneste-productie.ps1 -Actualizeaza -Port 8443   # și schimbă portul
#
# -Actualizeaza rulează din nou deploy\pregateste-productie.ps1 cu domeniul, portul și parola bazei din instalarea
# existentă (productie\setari.json și productie\api\appsettings.Production.local.json), deci nu le mai ceri.
# Certificatele existente se păstrează. Codul folosit e cel din folderul curent (fă `git pull` înainte, dacă e cazul).

param([switch] $Actualizeaza, [int] $Port = 0)

$ErrorActionPreference = "Stop"
$productie = Join-Path (Split-Path -Parent $PSScriptRoot) "productie"
$fisierSetari = Join-Path $productie "setari.json"
if (-not (Test-Path $fisierSetari)) {
    throw "Producția nu e instalată pe acest calculator. Rulează întâi: deploy\pregateste-productie.ps1 -Domeniu <domeniu>"
}

if ($Port -ne 0 -and -not $Actualizeaza) {
    throw "Portul se schimbă împreună cu -Actualizeaza (configurarea se scrie din nou): reporneste-productie.ps1 -Actualizeaza -Port $Port"
}

if ($Actualizeaza) {
    $setari = Get-Content $fisierSetari -Raw | ConvertFrom-Json
    $argumente = @{ Domeniu = $setari.Domeniu; Port = $(if ($Port -ne 0) { $Port } else { [int]$setari.Port }) }

    # Parola rolului erp_app: din configurarea existentă (altfel pregătirea ar pune valoarea implicită).
    $configurare = Join-Path $productie "api\appsettings.Production.local.json"
    if (Test-Path $configurare) {
        $conexiune = (Get-Content $configurare -Raw | ConvertFrom-Json).ConnectionStrings.Erp
        if ($conexiune -match '(?:^|;)Password=([^;]*)') { $argumente.ParolaBazaAplicatie = $Matches[1] }
    }

    Write-Host "Actualizez producția ($($setari.Domeniu), portul $($argumente.Port))..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "pregateste-productie.ps1") @argumente
}

# porneste-productie.ps1 oprește întâi instanța care rulează.
& (Join-Path $PSScriptRoot "porneste-productie.ps1")

# Codul nou poate cere migrări noi, pe care aplicația nu le aplică singură (cer rolul erp_owner).
$port = [int](Get-Content $fisierSetari -Raw | ConvertFrom-Json).Port
$sanatate = try { & curl.exe -s -k --max-time 10 "https://127.0.0.1:$port/sanatate" | ConvertFrom-Json } catch { $null }
if ($sanatate.stare -eq "migrari-lipsa") {
    Write-Host ""
    Write-Host "ATENȚIE: baza de date nu e la zi, unele pagini vor da erori:" -ForegroundColor Red
    $sanatate.migrariLipsa | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host "Rulează: powershell -ExecutionPolicy Bypass -File db\creeaza-baza-locala.ps1" -ForegroundColor Yellow
    Write-Host "(fără -ParolaApp: o ia din configurarea producției), apoi din nou deploy\reporneste-productie.ps1" -ForegroundColor Yellow
}
