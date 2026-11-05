# Resetează parola unui cont (parolă temporară nouă, schimbată obligatoriu la primul login), îl deblochează și îi închide
# sesiunile. Opțional dezactivează MFA (telefon pierdut / schimbat).
#
#   powershell -ExecutionPolicy Bypass -File deploy\reseteaza-parola.ps1 -Email demo@erpconstructii.ro
#   powershell -ExecutionPolicy Bypass -File deploy\reseteaza-parola.ps1 -Email ion.pop@firma.ro -ReseteazaMfa
#
# Pe calculatorul cu producția (există productie\api) folosește configurarea de producție; altfel pe cea de dezvoltare
# (src\Host\ErpConstructii.Api, baza din appsettings.json). Aplicația poate rula în timpul resetării.

param(
    [Parameter(Mandatory = $true)] [string] $Email,
    [switch] $ReseteazaMfa
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8 # diacriticele din mesajele aplicației
$radacina = Split-Path -Parent $PSScriptRoot
$argumente = @("--reseteaza-parola", $Email)
if ($ReseteazaMfa) { $argumente += "--reseteaza-mfa" }

$productie = Join-Path $radacina "productie\api"
if (Test-Path (Join-Path $productie "ErpConstructii.Api.dll")) {
    Write-Host "Folosesc configurarea de producție (productie\api)..."
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    Push-Location $productie
    try { dotnet ErpConstructii.Api.dll @argumente; $cod = $LASTEXITCODE }
    finally { Pop-Location }
}
else {
    Write-Host "Folosesc configurarea de dezvoltare (src\Host\ErpConstructii.Api)..."
    $proiect = Join-Path $radacina "src\Host\ErpConstructii.Api"
    # Compilare separată (Release), ca să nu se lovească de fișierele ținute ocupate de API-ul de dezvoltare pornit.
    $iesire = Join-Path $radacina "src\Host\ErpConstructii.Api\bin\Administrare"
    dotnet build $proiect -c Release -o $iesire --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Compilarea a eșuat." }
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    Push-Location $proiect # appsettings*.json se citesc din folderul proiectului
    try { dotnet (Join-Path $iesire "ErpConstructii.Api.dll") @argumente; $cod = $LASTEXITCODE }
    finally { Pop-Location }
}

if ($cod -ne 0) { throw "Resetarea nu s-a făcut (vezi mesajul de mai sus)." }
