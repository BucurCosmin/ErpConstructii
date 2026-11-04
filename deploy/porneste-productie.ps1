# Pornește instanța de producție (API în mediul Production + Caddy), pregătită cu deploy\pregateste-productie.ps1.
#
#   powershell -ExecutionPolicy Bypass -File deploy\porneste-productie.ps1
#
# API-ul ascultă doar pe 127.0.0.1 (nu e accesibil direct din rețea); Caddy primește traficul pe 80 / 443.
# Jurnalele: productie\loguri\. Oprire: deploy\opreste-productie.ps1.

param([int] $PortApi = 5090)

$ErrorActionPreference = "Stop"
$productie = Join-Path (Split-Path -Parent $PSScriptRoot) "productie"
$api = Join-Path $productie "api"
$loguri = Join-Path $productie "loguri"
if (-not (Test-Path (Join-Path $api "ErpConstructii.Api.dll"))) { throw "Rulează întâi deploy\pregateste-productie.ps1." }

& (Join-Path $PSScriptRoot "opreste-productie.ps1") -Liniste

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$PortApi"
$procesApi = Start-Process -FilePath "dotnet" -ArgumentList "ErpConstructii.Api.dll" -WorkingDirectory $api -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $loguri "api.log") -RedirectStandardError (Join-Path $loguri "api-erori.log")

$pornit = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    if ($procesApi.HasExited) { break }
    try { Invoke-WebRequest "http://127.0.0.1:$PortApi/sanatate" -UseBasicParsing -TimeoutSec 2 | Out-Null; $pornit = $true; break } catch { }
}
if (-not $pornit) {
    Get-Content (Join-Path $loguri "api-erori.log") -Tail 30 -ErrorAction SilentlyContinue
    throw "API-ul nu a pornit; vezi productie\loguri\api.log și api-erori.log."
}

$caddyDir = Join-Path $productie "caddy"
$procesCaddy = Start-Process -FilePath (Join-Path $caddyDir "caddy.exe") -ArgumentList "run", "--config", "Caddyfile", "--adapter", "caddyfile" `
    -WorkingDirectory $caddyDir -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $loguri "caddy.log") -RedirectStandardError (Join-Path $loguri "caddy-erori.log")
Start-Sleep -Seconds 3
if ($procesCaddy.HasExited) {
    Get-Content (Join-Path $loguri "caddy-erori.log") -Tail 30 -ErrorAction SilentlyContinue
    throw "Caddy nu a pornit (porturile 80 / 443 ocupate sau fără drepturi?)."
}

@{ Api = $procesApi.Id; Caddy = $procesCaddy.Id } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $productie "procese.json")
Write-Host "Producția rulează: API (PID $($procesApi.Id)) pe 127.0.0.1:$PortApi, Caddy (PID $($procesCaddy.Id)) pe 80 / 443." -ForegroundColor Green
