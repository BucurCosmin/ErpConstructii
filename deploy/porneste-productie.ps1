# Pornește instanța de producție pregătită cu deploy\pregateste-productie.ps1: aplicația (API + interfața web) ascultă
# direct în rețea, pe portul HTTPS ales. Jurnalele: productie\loguri\. Oprire: deploy\opreste-productie.ps1.
#
#   powershell -ExecutionPolicy Bypass -File deploy\porneste-productie.ps1

$ErrorActionPreference = "Stop"
$productie = Join-Path (Split-Path -Parent $PSScriptRoot) "productie"
$api = Join-Path $productie "api"
$loguri = Join-Path $productie "loguri"
if (-not (Test-Path (Join-Path $api "ErpConstructii.Api.dll"))) { throw "Rulează întâi deploy\pregateste-productie.ps1." }
$setari = Get-Content (Join-Path $productie "setari.json") -Raw | ConvertFrom-Json

& (Join-Path $PSScriptRoot "opreste-productie.ps1") -Liniste

# Portul trebuie să fie liber (ex. 443 e ocupat des de IIS, Skype, VMware sau alt server web).
$ocupat = Get-NetTCPConnection -LocalPort ([int]$setari.Port) -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($ocupat) {
    $program = (Get-Process -Id $ocupat.OwningProcess -ErrorAction SilentlyContinue).ProcessName
    throw ("Portul $($setari.Port) e ocupat de alt program ($program, PID $($ocupat.OwningProcess)). " +
        "Alege alt port: powershell -ExecutionPolicy Bypass -File deploy\reporneste-productie.ps1 -Actualizeaza -Port 8443")
}

$env:ASPNETCORE_ENVIRONMENT = "Production"
Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue # adresa vine din configurare (Kestrel:Endpoints)
$proces = Start-Process -FilePath "dotnet" -ArgumentList "ErpConstructii.Api.dll" -WorkingDirectory $api -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $loguri "api.log") -RedirectStandardError (Join-Path $loguri "api-erori.log")

$pornit = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    if ($proces.HasExited) { break }
    $cod = & curl.exe -s -k -o NUL -w "%{http_code}" --max-time 2 "https://127.0.0.1:$($setari.Port)/sanatate" 2>$null
    if ($cod -eq "200") { $pornit = $true; break }
}
if (-not $pornit) {
    Get-Content (Join-Path $loguri "api-erori.log") -Tail 30 -ErrorAction SilentlyContinue
    Get-Content (Join-Path $loguri "api.log") -Tail 30 -ErrorAction SilentlyContinue
    throw "Aplicația nu a pornit pe portul $($setari.Port) (port ocupat? vezi productie\loguri\api.log)."
}

@{ Api = $proces.Id } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $productie "procese.json")
$sufix = if ($setari.Port -eq 443) { "" } else { ":$($setari.Port)" }
Write-Host "Producția rulează (PID $($proces.Id)): https://$($setari.Domeniu)$sufix/" -ForegroundColor Green
