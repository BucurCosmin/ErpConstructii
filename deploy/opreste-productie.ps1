# Oprește instanța de producție pornită cu deploy\porneste-productie.ps1.
param([switch] $Liniste)

$productie = Join-Path (Split-Path -Parent $PSScriptRoot) "productie"
$fisier = Join-Path $productie "procese.json"
if (Test-Path $fisier) {
    $procese = Get-Content $fisier -Raw | ConvertFrom-Json
    foreach ($id in @($procese.Caddy, $procese.Api)) {
        if ($id) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    }
    Remove-Item $fisier
    if (-not $Liniste) { Write-Host "Producția a fost oprită." }
}
elseif (-not $Liniste) {
    Write-Host "Producția nu rulează (nu există productie\procese.json)."
}
