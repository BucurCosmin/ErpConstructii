# Pornește aplicația local: API-ul (http://localhost:5080) și interfața web (http://localhost:5173),
# fiecare în fereastra lui, apoi deschide browserul.
#
#   powershell -ExecutionPolicy Bypass -File porneste.ps1
#
# Oprire: închide cele două ferestre (sau Ctrl+C în fiecare).
# Prima dată, baza de date se creează cu: powershell -ExecutionPolicy Bypass -File db\creeaza-baza-locala.ps1

$ErrorActionPreference = "Stop"
$radacina = $PSScriptRoot
$web = Join-Path $radacina "clients\web"

function PortOcupat([int]$port) {
    [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

# Modelele OCR (pentru importul documentelor scanate), descărcate o singură dată.
if (-not (Test-Path (Join-Path $radacina "tessdata\ron.traineddata"))) {
    & (Join-Path $radacina "scripts\descarca-tessdata.ps1")
}

if (PortOcupat 5080) {
    Write-Host "API-ul rulează deja pe portul 5080 (sau portul e ocupat de alt program)." -ForegroundColor Yellow
}
else {
    Write-Host "Pornesc API-ul..."
    Start-Process powershell -WorkingDirectory $radacina -ArgumentList @(
        "-NoExit", "-Command",
        "`$Host.UI.RawUI.WindowTitle = 'ERP - API (5080)'; dotnet run --project src/Host/ErpConstructii.Api"
    )
}

if (PortOcupat 5173) {
    Write-Host "Interfața web rulează deja pe portul 5173." -ForegroundColor Yellow
}
else {
    if (-not (Test-Path (Join-Path $web "node_modules"))) {
        Write-Host "Instalez dependențele interfeței web (doar prima dată)..."
        Push-Location $web
        npm install
        Pop-Location
    }

    Write-Host "Pornesc interfața web..."
    Start-Process powershell -WorkingDirectory $web -ArgumentList @(
        "-NoExit", "-Command",
        "`$Host.UI.RawUI.WindowTitle = 'ERP - Web (5173)'; npm run dev"
    )
}

# Așteaptă API-ul (prima compilare poate dura), apoi deschide aplicația.
Write-Host "Aștept pornirea API-ului..."
for ($i = 0; $i -lt 90; $i++) {
    try {
        Invoke-WebRequest "http://localhost:5080/sanatate" -UseBasicParsing -TimeoutSec 2 | Out-Null
        break
    }
    catch {
        Start-Sleep -Seconds 1
    }
}

Start-Process "http://localhost:5173"
Write-Host "Gata: http://localhost:5173  (cont demo: demo@erpconstructii.ro / Demo-Constructii-2026)" -ForegroundColor Green
