# Repornește instanța de producție.
#
#   powershell -ExecutionPolicy Bypass -File deploy\reporneste-productie.ps1               # doar repornire (aceeași versiune)
#   powershell -ExecutionPolicy Bypass -File deploy\reporneste-productie.ps1 -Actualizeaza # recompilează codul curent, apoi pornește
#
# -Actualizeaza rulează din nou deploy\pregateste-productie.ps1 cu domeniul, portul și parola bazei din instalarea
# existentă (productie\setari.json și productie\api\appsettings.Production.local.json), deci nu le mai ceri.
# Certificatele existente se păstrează. Codul folosit e cel din folderul curent (fă `git pull` înainte, dacă e cazul).

param([switch] $Actualizeaza)

$ErrorActionPreference = "Stop"
$productie = Join-Path (Split-Path -Parent $PSScriptRoot) "productie"
$fisierSetari = Join-Path $productie "setari.json"
if (-not (Test-Path $fisierSetari)) {
    throw "Producția nu e instalată pe acest calculator. Rulează întâi: deploy\pregateste-productie.ps1 -Domeniu <domeniu>"
}

if ($Actualizeaza) {
    $setari = Get-Content $fisierSetari -Raw | ConvertFrom-Json
    $argumente = @{ Domeniu = $setari.Domeniu; Port = [int]$setari.Port }

    # Parola rolului erp_app: din configurarea existentă (altfel pregătirea ar pune valoarea implicită).
    $configurare = Join-Path $productie "api\appsettings.Production.local.json"
    if (Test-Path $configurare) {
        $conexiune = (Get-Content $configurare -Raw | ConvertFrom-Json).ConnectionStrings.Erp
        if ($conexiune -match '(?:^|;)Password=([^;]*)') { $argumente.ParolaBazaAplicatie = $Matches[1] }
    }

    Write-Host "Actualizez producția ($($setari.Domeniu), portul $($setari.Port))..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "pregateste-productie.ps1") @argumente
}

# porneste-productie.ps1 oprește întâi instanța care rulează.
& (Join-Path $PSScriptRoot "porneste-productie.ps1")
