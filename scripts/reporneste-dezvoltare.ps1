# Repornește mediul de dezvoltare: oprește API-ul de pe portul 5080, îl recompilează și îl pornește din nou (într-o
# fereastră separată, cu jurnalul la vedere). Interfața web (Vite, portul 5173) se reîncarcă singură la modificări:
# e pornită doar dacă nu rulează deja, sau repornită cu -Web.
#
#   powershell -ExecutionPolicy Bypass -File scripts\reporneste-dezvoltare.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\reporneste-dezvoltare.ps1 -Web        # repornește și Vite
#   powershell -ExecutionPolicy Bypass -File scripts\reporneste-dezvoltare.ps1 -FaraWeb    # doar API-ul

param(
    [switch] $Web,
    [switch] $FaraWeb
)

$ErrorActionPreference = "Stop"
$radacina = Split-Path -Parent $PSScriptRoot
$proiectApi = Join-Path $radacina "src\Host\ErpConstructii.Api"

function OpresteDePePort([int] $port, [string[]] $procesePermise) {
    $pids = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
    foreach ($id in $pids) {
        $p = Get-Process -Id $id -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        if ($procesePermise -notcontains $p.ProcessName) {
            throw "Portul $port e ocupat de alt program ($($p.ProcessName), PID $id). Închide-l manual."
        }
        Write-Host "Opresc $($p.ProcessName) (PID $id) de pe portul $port..."
        Stop-Process -Id $id -Force
        $p.WaitForExit(10000) | Out-Null
    }
}

function AsteaptaPort([int] $port, [int] $secunde) {
    for ($i = 0; $i -lt $secunde; $i++) {
        if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

# ---------------------------------------------------------------- API
OpresteDePePort 5080 @("ErpConstructii.Api", "dotnet")

Write-Host "Compilez API-ul..."
dotnet build $proiectApi --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Compilarea a eșuat (vezi erorile de mai sus). API-ul a rămas oprit." }

Write-Host "Pornesc API-ul (http://localhost:5080)..."
Start-Process powershell -WorkingDirectory $radacina -ArgumentList @(
    "-NoExit", "-Command",
    "`$Host.UI.RawUI.WindowTitle = 'ERP API (dezvoltare)'; dotnet run --no-build --project `"$proiectApi`""
) | Out-Null

$pornit = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    $cod = & curl.exe -s -o NUL -w "%{http_code}" --max-time 2 "http://localhost:5080/sanatate" 2>$null
    if ($cod -eq "200") { $pornit = $true; break }
}
if (-not $pornit) { throw "API-ul nu răspunde pe portul 5080 după 60 s. Vezi fereastra 'ERP API (dezvoltare)'." }
Write-Host "API-ul rulează: http://localhost:5080" -ForegroundColor Green

# ---------------------------------------------------------------- interfața web (Vite)
if (-not $FaraWeb) {
    if ($Web) { OpresteDePePort 5173 @("node") }
    if (Get-NetTCPConnection -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue) {
        Write-Host "Interfața web rulează deja (se reîncarcă singură): http://localhost:5173" -ForegroundColor Green
    }
    else {
        Write-Host "Pornesc interfața web..."
        $web = Join-Path $radacina "clients\web"
        if (-not (Test-Path (Join-Path $web "node_modules"))) {
            Push-Location $web
            try { npm ci; if ($LASTEXITCODE -ne 0) { throw "npm ci a eșuat." } } finally { Pop-Location }
        }
        Start-Process powershell -WorkingDirectory $web -ArgumentList @(
            "-NoExit", "-Command", "`$Host.UI.RawUI.WindowTitle = 'ERP web (dezvoltare)'; npm run dev"
        ) | Out-Null
        if (AsteaptaPort 5173 60) { Write-Host "Interfața web rulează: http://localhost:5173" -ForegroundColor Green }
        else { Write-Warning "Vite nu a pornit pe portul 5173. Vezi fereastra 'ERP web (dezvoltare)'." }
    }
}
