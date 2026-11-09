# Diagnostic pentru accesul la producție: aplicația, rețeaua locală (firewall), DNS-ul și portul public.
#
#   powershell -ExecutionPolicy Bypass -File deploy\verifica-productie.ps1
#
# Se rulează pe calculatorul pe care rulează producția. Trimite rezultatul dacă ceva e „PROBLEMĂ”.

param([string] $Domeniu = "") # ignorat: domeniul și portul se citesc din productie\setari.json

$productie = Join-Path (Split-Path -Parent $PSScriptRoot) "productie"
function Ok($m) { Write-Host "OK        $m" -ForegroundColor Green }
function Problema($m, $sfat) { Write-Host "PROBLEMĂ  $m" -ForegroundColor Red; if ($sfat) { Write-Host "          → $sfat" -ForegroundColor Yellow } }
function Info($m) { Write-Host "INFO      $m" -ForegroundColor Cyan }

$fisierSetari = Join-Path $productie "setari.json"
if (-not (Test-Path $fisierSetari)) { Problema "producția nu e pregătită (versiunea nouă)" "rulează deploy\pregateste-productie.ps1 -Domeniu sdaq.home.ro"; return }
$setari = Get-Content $fisierSetari -Raw | ConvertFrom-Json
$port = [int]$setari.Port
$domeniu = $setari.Domeniu
$sufix = if ($port -eq 443) { "" } else { ":$port" }

function Raspunde([string] $gazda) {
    (& curl.exe -s -k -o NUL -w "%{http_code}" --max-time 6 "https://${gazda}:$port/sanatate" 2>$null) -eq "200"
}

# 1. Aplicația, pe calculator
if (Raspunde "127.0.0.1") { Ok "aplicația răspunde pe https://127.0.0.1:$port" }
else { Problema "aplicația nu răspunde pe portul $port" "pornește-o: deploy\porneste-productie.ps1; vezi productie\loguri\api.log"; return }

$sanatate = try { & curl.exe -s -k --max-time 6 "https://127.0.0.1:$port/sanatate" | ConvertFrom-Json } catch { $null }
if ($sanatate.stare -eq "migrari-lipsa") {
    Problema "baza de date nu e la zi (de aici vin erorile „error processing your request”): $($sanatate.migrariLipsa -join '; ')" `
        "rulează db\creeaza-baza-locala.ps1 (fără -ParolaApp: o ia din configurarea producției), apoi deploy\reporneste-productie.ps1"
}
elseif ($sanatate.stare -eq "ok") { Ok "baza de date are toate migrările" }

$asculta = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
if ($asculta.LocalAddress -in "0.0.0.0", "::") { Ok "ascultă pe toate interfețele (0.0.0.0:$port)" }
else { Problema "ascultă doar pe $($asculta.LocalAddress)" "rulează din nou deploy\pregateste-productie.ps1 (versiunea nouă ascultă pe 0.0.0.0)" }

# 2. Rețeaua locală: aici se vede firewall-ul Windows
$ipuri = @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
        $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" -and $_.InterfaceAlias -notlike "*VMware*" -and $_.InterfaceAlias -notlike "*vEthernet*"
    } | ForEach-Object { $_.IPAddress })
$regula = Get-NetFirewallRule -DisplayName "ERP Construcții HTTPS $port" -ErrorAction SilentlyContinue | Where-Object Enabled -eq "True"
if ($regula) { Ok "firewall: regula pentru portul $port există" }
else {
    Problema "firewall: lipsește regula pentru portul $port (Windows blochează conexiunile din rețea și din internet)" `
        "deschide PowerShell CA ADMINISTRATOR și rulează: New-NetFirewallRule -DisplayName 'ERP Construcții HTTPS $port' -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow"
}
foreach ($ip in $ipuri) {
    if (Raspunde $ip) { Ok "răspunde în rețea: https://${ip}$sufix/  (redirecționarea din router trebuie să meargă la $ip)" }
    else { Problema "nu răspunde pe https://${ip}$sufix/" "firewall-ul blochează portul $port (vezi regula de mai sus) sau un antivirus cu firewall propriu" }
}

# 3. DNS și IP-ul public
$ipDns = (Resolve-DnsName $domeniu -Type A -ErrorAction SilentlyContinue | Where-Object Type -eq "A" | Select-Object -First 1).IPAddress
$ipPublic = try { (Invoke-RestMethod "https://api.ipify.org" -TimeoutSec 10).ToString().Trim() } catch { $null }
if (-not $ipDns) { Problema "$domeniu nu se rezolvă în DNS" "verifică numele (atenție la litere: sdaq ≠ sdag)" }
elseif ($ipPublic -and $ipDns -ne $ipPublic) { Problema "$domeniu arată spre $ipDns, dar IP-ul public al acestei rețele e $ipPublic" "actualizează înregistrarea DNS la $ipPublic" }
else { Ok "$domeniu arată spre IP-ul public al acestei rețele ($ipDns)" }

# 4. Portul public (din interiorul rețelei unele routere nu permit testul: verificarea finală e de pe telefon, pe date mobile)
if ($ipPublic) {
    if (Raspunde $ipPublic) { Ok "aplicația răspunde pe IP-ul public: https://$domeniu$sufix/ ar trebui să meargă" }
    else {
        Info "pe IP-ul public ($ipPublic, portul $port) aplicația nu răspunde din această rețea: fie redirecționarea portului $port din router nu merge la $($ipuri -join ' / '), fie routerul nu permite testul din interior"
    }
}

Write-Host ""
Write-Host "Testul decisiv: https://$domeniu$sufix/ de pe telefon, cu Wi-Fi OPRIT (date mobile). La avertismentul de certificat: Avansat → Continuă." -ForegroundColor Cyan
