# Diagnostic pentru accesul din exterior: verifică pe rând API-ul, Caddy, certificatul, firewall-ul, DNS-ul și portul public.
#
#   powershell -ExecutionPolicy Bypass -File deploy\verifica-productie.ps1 -Domeniu sdaq.home.ro
#
# Se rulează pe calculatorul pe care rulează producția. Trimite rezultatul dacă ceva e „PROBLEMĂ”.

param(
    [Parameter(Mandatory = $true)] [string] $Domeniu,
    [int] $PortApi = 5090
)

$productie = Join-Path (Split-Path -Parent $PSScriptRoot) "productie"
function Ok($m) { Write-Host "OK        $m" -ForegroundColor Green }
function Problema($m, $sfat) { Write-Host "PROBLEMĂ  $m" -ForegroundColor Red; if ($sfat) { Write-Host "          → $sfat" -ForegroundColor Yellow } }
function Info($m) { Write-Host "INFO      $m" -ForegroundColor Cyan }

# 1. API-ul de producție
try { Invoke-WebRequest "http://127.0.0.1:$PortApi/sanatate" -UseBasicParsing -TimeoutSec 5 | Out-Null; Ok "API-ul răspunde pe 127.0.0.1:$PortApi" }
catch { Problema "API-ul nu răspunde pe 127.0.0.1:$PortApi" "pornește producția: deploy\porneste-productie.ps1; vezi productie\loguri\api-erori.log" }

# 2. Caddy ascultă pe 80 / 443
foreach ($port in 80, 443) {
    $c = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($c) {
        $proces = (Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue).ProcessName
        if ($proces -eq "caddy") { Ok "portul $port e ascultat de Caddy ($($c.LocalAddress))" }
        else { Problema "portul $port e ocupat de „$proces”, nu de Caddy" "oprește programul respectiv (ex. IIS, Skype, alt server web) sau schimbă-i portul" }
    }
    else { Problema "nimeni nu ascultă pe portul $port" "Caddy nu rulează: vezi productie\loguri\caddy-erori.log" }
}

# 3. Caddy servește aplicația local (ocolind DNS-ul și routerul)
$raspuns = & curl.exe -s -o NUL -w "%{http_code}" --max-time 15 --resolve "${Domeniu}:443:127.0.0.1" "https://$Domeniu/sanatate" 2>$null
if ($raspuns -eq "200") { Ok "Caddy servește https://$Domeniu local, cu certificat valid" }
else {
    $nesigur = & curl.exe -s -k -o NUL -w "%{http_code}" --max-time 15 --resolve "${Domeniu}:443:127.0.0.1" "https://$Domeniu/sanatate" 2>$null
    if ($nesigur -eq "200") { Problema "Caddy răspunde, dar certificatul HTTPS nu e valid încă" "Let's Encrypt nu a putut verifica domeniul: porturile 80 / 443 nu ajung din internet la acest calculator (vezi punctele 5–7)" }
    else { Problema "Caddy nu servește https://$Domeniu local (cod: $raspuns)" "vezi productie\loguri\caddy-erori.log" }
}

# 4. Certificatul în jurnalul Caddy
$jurnal = Join-Path $productie "loguri\caddy-erori.log"
if (Test-Path $jurnal) {
    $text = Get-Content $jurnal -Raw
    if ($text -match "certificate obtained successfully") { Ok "jurnalul Caddy: certificatul a fost obținut" }
    elseif ($text -match "(?i)tls-alpn-01.*(error|fail)|(error|fail).*tls-alpn-01") {
        Problema "jurnalul Caddy: verificarea pe portul 443 (TLS-ALPN) a eșuat" "portul 443 din internet nu ajunge la Caddy: redirecționarea 443 din router lipsește sau routerul ține 443 pentru administrarea HTTPS (mut-o pe alt port, ex. 8443)"
    }
    elseif ($text -match "(?i)(challenge failed|timeout during connect|connection refused|unauthorized|could not get certificate)") {
        Problema "jurnalul Caddy: obținerea certificatului a eșuat" "ultimele erori de mai jos; de regulă portul 443 nu ajunge din internet la acest calculator"
        Get-Content $jurnal | Select-String -Pattern "(?i)error|failed" | Select-Object -Last 5 | ForEach-Object { Write-Host "          $($_.Line.Substring(0, [Math]::Min(220, $_.Line.Length)))" }
    }
    else { Info "jurnalul Caddy nu arată încă rezultatul certificatului (se obține la prima pornire, poate dura un minut)" }
}

# 5. Firewall-ul Windows
foreach ($port in 80, 443) {
    if (Get-NetFirewallRule -DisplayName "ERP Construcții HTTP $port" -ErrorAction SilentlyContinue | Where-Object Enabled -eq "True") { Ok "firewall: regula pentru portul $port există" }
    else { Problema "firewall: lipsește regula pentru portul $port" "PowerShell ca administrator: New-NetFirewallRule -DisplayName 'ERP Construcții HTTP $port' -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow" }
}

# 6. DNS și IP-ul public
$ipDns = (Resolve-DnsName $Domeniu -Type A -ErrorAction SilentlyContinue | Where-Object Type -eq "A" | Select-Object -First 1).IPAddress
$ipPublic = try { (Invoke-RestMethod "https://api.ipify.org" -TimeoutSec 10).ToString().Trim() } catch { $null }
$ipLocal = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" -and $_.InterfaceAlias -notlike "*VMware*" -and $_.InterfaceAlias -notlike "*vEthernet*" }).IPAddress -join ", "
Info "IP-ul local al acestui calculator: $ipLocal (redirecționarea din router trebuie să meargă AICI)"
if (-not $ipDns) { Problema "$Domeniu nu se rezolvă în DNS" "verifică înregistrarea la furnizorul DNS" }
elseif ($ipPublic -and $ipDns -ne $ipPublic) { Problema "$Domeniu arată spre $ipDns, dar IP-ul public al acestei rețele e $ipPublic" "actualizează înregistrarea DNS (sau DNS-ul dinamic) la $ipPublic" }
else { Ok "$Domeniu arată spre IP-ul public al acestei rețele ($ipDns)" }

# 7. Portul public, văzut din internet (prin adresa publică; unele routere nu permit testul din interior)
if ($ipPublic) {
    $din = Test-NetConnection $ipPublic -Port 443 -WarningAction SilentlyContinue
    if ($din.TcpTestSucceeded) { Ok "portul 443 al IP-ului public răspunde" }
    else {
        Problema "portul 443 al IP-ului public ($ipPublic) nu răspunde din această rețea" "fie redirecționarea din router lipsește / merge la alt calculator (trebuie la $ipLocal), fie routerul nu permite testul din interior (NAT loopback): testează https://$Domeniu de pe telefon, pe date mobile"
    }
}

Write-Host ""
Write-Host "Testul decisiv: deschide https://$Domeniu de pe telefon, cu Wi-Fi OPRIT (date mobile)." -ForegroundColor Cyan
