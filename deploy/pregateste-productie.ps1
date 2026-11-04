# Pregătește instanța de producție pe acest calculator: aplicația (API + interfața web) ascultă direct în rețea,
# pe un singur port HTTPS, fără reverse proxy și fără Let's Encrypt.
#
#   powershell -ExecutionPolicy Bypass -File deploy\pregateste-productie.ps1 -Domeniu sdaq.home.ro
#   powershell -ExecutionPolicy Bypass -File deploy\pregateste-productie.ps1 -Domeniu sdaq.home.ro -Port 8443 -ParolaBazaAplicatie "..."
#
# Pe router: redirecționează portul TCP ales (implicit 443) către acest calculator. Atât.
# Adrese: https://<domeniu>[:port]/ din internet, https://<IP-ul din rețea>[:port]/ din rețeaua locală.
#
# HTTPS e obligatoriu (login-ul folosește criptografia browserului, disponibilă doar pe https; parolele nu circulă în clar).
# Certificatul e generat aici (autosemnat, valabil 5 ani): la prima deschidere browserul cere confirmarea
# („Conexiunea nu este privată” → Avansat → Continuă). Un certificat cumpărat / Let's Encrypt se poate pune oricând
# în locul lui (Kestrel:Endpoints:Https:Certificate în productie\api\appsettings.Production.local.json).
#
# Ce face (idempotent; se rulează din nou la fiecare versiune nouă a aplicației):
#   1. folderul productie\ (ignorat de git): api\, web\, certificate\, loguri\
#   2. certificatele: HTTPS (pentru domeniu + IP-urile din rețea) și semnarea / criptarea token-urilor (o singură dată)
#   3. compilează interfața web și publică API-ul (Release)
#   4. scrie configurarea cu secretele în productie\api\appsettings.Production.local.json
#   5. regula de firewall pentru port (cere drepturi de administrator; altfel afișează comanda)

param(
    [Parameter(Mandatory = $true)] [string] $Domeniu,
    [int] $Port = 443,
    [string] $ParolaBazaAplicatie = "erp_app_dev"
)

$ErrorActionPreference = "Stop"
$radacina = Split-Path -Parent $PSScriptRoot
$productie = Join-Path $radacina "productie"
foreach ($d in @("api", "web", "certificate", "loguri")) { New-Item -ItemType Directory -Force (Join-Path $productie $d) | Out-Null }

function ParolaAleatoare { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ }) }

$ipuriLocale = @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
        $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" -and $_.InterfaceAlias -notlike "*VMware*" -and $_.InterfaceAlias -notlike "*vEthernet*"
    } | ForEach-Object { $_.IPAddress })
$sufixPort = if ($Port -eq 443) { "" } else { ":$Port" }

# ---------------------------------------------------------------- 2. certificate
$secrete = Join-Path $productie "certificate\secrete.json"
$info = if (Test-Path $secrete) { Get-Content $secrete -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }

function CertificatNou([string] $subiect, [string[]] $extensii, [string[]] $utilizare, [string] $fisier) {
    $parola = ParolaAleatoare
    $argumente = @{
        Subject = $subiect; KeyAlgorithm = "RSA"; KeyLength = 2048; KeyExportPolicy = "Exportable"; KeyUsage = $utilizare
        NotAfter = (Get-Date).AddYears(5); CertStoreLocation = "Cert:\CurrentUser\My"
    }
    if ($extensii.Count -gt 0) { $argumente.TextExtension = $extensii }
    $cert = New-SelfSignedCertificate @argumente
    $cale = Join-Path $productie "certificate\$fisier"
    Export-PfxCertificate -Cert $cert -FilePath $cale -Password (ConvertTo-SecureString $parola -AsPlainText -Force) | Out-Null
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
    return [pscustomobject]@{ Cale = $cale; Parola = $parola }
}

foreach ($tip in @("Semnare", "Criptare")) {
    if (-not $info.PSObject.Properties[$tip]) {
        Write-Host "Generez certificatul de $($tip.ToLower()) a token-urilor..."
        $utilizare = if ($tip -eq "Semnare") { "DigitalSignature" } else { "KeyEncipherment" }
        $info | Add-Member -NotePropertyName $tip -NotePropertyValue (CertificatNou "CN=ERP Construcții $tip" @() $utilizare "$($tip.ToLower()).pfx")
    }
}

# Certificatul HTTPS acoperă domeniul, localhost și IP-urile din rețea; se reface dacă s-a schimbat domeniul sau IP-ul.
$numeHttps = (@("DNS=$Domeniu", "DNS=localhost", "IPAddress=127.0.0.1") + ($ipuriLocale | ForEach-Object { "IPAddress=$_" })) -join "&"
if (-not $info.PSObject.Properties["Https"] -or $info.HttpsNume -ne $numeHttps) {
    Write-Host "Generez certificatul HTTPS pentru $Domeniu și $($ipuriLocale -join ', ')..."
    $https = CertificatNou "CN=$Domeniu" @("2.5.29.17={text}$numeHttps", "2.5.29.37={text}1.3.6.1.5.5.7.3.1") @("DigitalSignature", "KeyEncipherment") "https.pfx"
    $info | Add-Member -Force -NotePropertyName Https -NotePropertyValue $https
    $info | Add-Member -Force -NotePropertyName HttpsNume -NotePropertyValue $numeHttps
}
$info | ConvertTo-Json | Set-Content -Encoding utf8 $secrete

# ---------------------------------------------------------------- 3. compilare
Write-Host "Compilez interfața web..."
Push-Location (Join-Path $radacina "clients\web")
try {
    if (-not (Test-Path "node_modules")) { npm ci; if ($LASTEXITCODE -ne 0) { throw "npm ci a eșuat." } }
    npm run build; if ($LASTEXITCODE -ne 0) { throw "Compilarea interfeței web a eșuat." }
}
finally { Pop-Location }
$web = Join-Path $productie "web"
Get-ChildItem $web | Remove-Item -Recurse -Force
Copy-Item (Join-Path $radacina "clients\web\dist\*") $web -Recurse

Write-Host "Public API-ul (Release)..."
& (Join-Path $PSScriptRoot "opreste-productie.ps1") -Liniste
$api = Join-Path $productie "api"
dotnet publish (Join-Path $radacina "src\Host\ErpConstructii.Api") -c Release -o $api --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Publicarea API-ului a eșuat." }

# ---------------------------------------------------------------- 4. configurarea
$adrese = @("https://$Domeniu$sufixPort") + ($ipuriLocale | ForEach-Object { "https://$_$sufixPort" }) + @("https://localhost$sufixPort")
$config = [ordered]@{
    AllowedHosts      = (@($Domeniu, "localhost", "127.0.0.1") + $ipuriLocale) -join ";"
    ConnectionStrings = @{ Erp = "Host=localhost;Port=5432;Database=erp;Username=erp_app;Password=$ParolaBazaAplicatie" }
    Ocr               = @{ CaleTessdata = (Join-Path $radacina "tessdata") }
    Web               = @{ Radacina = $web }
    Kestrel           = @{
        Endpoints = @{
            Https = @{ Url = "https://0.0.0.0:$Port"; Certificate = @{ Path = $info.Https.Cale; Password = $info.Https.Parola } }
        }
    }
    Autentificare     = [ordered]@{
        # Fără emitent fix: aplicația merge și pe domeniu, și pe IP-ul din rețea (emitentul = adresa folosită).
        Certificate = @{
            Semnare  = @{ Cale = $info.Semnare.Cale; Parola = $info.Semnare.Parola }
            Criptare = @{ Cale = $info.Criptare.Cale; Parola = $info.Criptare.Parola }
        }
        Clienti     = @{
            "erp-web" = @{
                Denumire               = "ERP Construcții – aplicația web"
                RedirectUris           = @($adrese | ForEach-Object { "$_/autentificare/callback" })
                PostLogoutRedirectUris = @($adrese | ForEach-Object { "$_/" })
            }
        }
    }
}
$config | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $api "appsettings.Production.local.json")
@{ Port = $Port; Domeniu = $Domeniu } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $productie "setari.json")

# ---------------------------------------------------------------- 5. firewall
$regula = "ERP Construcții HTTPS $Port"
try {
    if (-not (Get-NetFirewallRule -DisplayName $regula -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $regula -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
    }
    Write-Host "Firewall: portul $Port e deschis."
}
catch {
    Write-Warning "Nu am putut deschide portul $Port în firewall. Rulează o dată, ca administrator:"
    Write-Host "  New-NetFirewallRule -DisplayName '$regula' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow"
}

Write-Host ""
Write-Host "Gata. Pornește: powershell -ExecutionPolicy Bypass -File deploy\porneste-productie.ps1" -ForegroundColor Green
Write-Host "Adrese: $($adrese -join '  ')"
Write-Host "Pe router: portul TCP $Port → acest calculator ($($ipuriLocale -join ', '))."
