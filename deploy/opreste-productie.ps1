# Oprește instanța de producție pornită cu deploy\porneste-productie.ps1 (și un Caddy rămas de la versiunea veche).
param([switch] $Liniste)

$productie = Join-Path (Split-Path -Parent $PSScriptRoot) "productie"
$fisier = Join-Path $productie "procese.json"
$oprit = $false
if (Test-Path $fisier) {
    $procese = Get-Content $fisier -Raw | ConvertFrom-Json
    foreach ($nume in $procese.PSObject.Properties.Name) {
        Stop-Process -Id $procese.$nume -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $fisier
    $oprit = $true
}
Get-Process caddy -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (-not $Liniste) { Write-Host $(if ($oprit) { "Producția a fost oprită." } else { "Producția nu rula." }) }
