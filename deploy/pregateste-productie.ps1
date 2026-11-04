# Pregătește instanța de producție pe acest calculator, expusă pe internet prin Caddy (HTTPS automat, Let's Encrypt).
#
#   powershell -ExecutionPolicy Bypass -File deploy\pregateste-productie.ps1 -Domeniu sdaq.home.ro
#
# Ce face (idempotent; se rulează din nou la fiecare versiune nouă a aplicației):
#   1. folderul productie\ (ignorat de git): api\, web\, caddy\, certificate\, loguri\
#   2. certificatele pentru semnarea / criptarea token-urilor OpenIddict (o singură dată; parole aleatoare)
#   3. compilează interfața web (npm run build) și publică API-ul (dotnet publish, Release)
#   4. scrie secretele în productie\api\appsettings.Production.local.json (nu intră în git)
#   5. descarcă Caddy (dacă lipsește) și scrie Caddyfile-ul
#   6. adaugă regulile de firewall pentru porturile 80 și 443 (cere drepturi de administrator)
#
# Pe router: redirecționează portul TCP 443 către acest calculator (obligatoriu: aplicația și certificatul Let's Encrypt,
# obținut prin verificarea TLS-ALPN pe 443). Portul 80 e opțional: dacă e redirecționat, http:// trimite spre https://;
# poate rămâne folosit de router (ex. administrarea de la distanță). Administrarea HTTPS a routerului NU trebuie să fie
# pe 443. NU redirecționa 5432 (PostgreSQL) sau 5090 (API).

param(
    [Parameter(Mandatory = $true)] [string] $Domeniu,
    [string] $EmailAcme = "",
    [int] $PortApi = 5090,
    [string] $ParolaBazaAplicatie = "erp_app_dev"
)

$ErrorActionPreference = "Stop"
$radacina = Split-Path -Parent $PSScriptRoot
$productie = Join-Path $radacina "productie"
foreach ($d in @("api", "web", "caddy", "certificate", "loguri")) { New-Item -ItemType Directory -Force (Join-Path $productie $d) | Out-Null }

function ParolaAleatoare { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ }) }

# ---------------------------------------------------------------- 2. certificatele token-urilor
$secrete = Join-Path $productie "certificate\secrete.json"
if (-not (Test-Path $secrete)) {
    Write-Host "Generez certificatele de semnare și criptare a token-urilor..."
    $info = @{}
    foreach ($tip in @("Semnare", "Criptare")) {
        $parola = ParolaAleatoare
        $utilizare = if ($tip -eq "Semnare") { "DigitalSignature" } else { "KeyEncipherment" }
        $cert = New-SelfSignedCertificate -Subject "CN=ERP Construcții $tip" -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable `
            -KeyUsage $utilizare -NotAfter (Get-Date).AddYears(5) -CertStoreLocation "Cert:\CurrentUser\My"
        $cale = Join-Path $productie "certificate\$($tip.ToLower()).pfx"
        Export-PfxCertificate -Cert $cert -FilePath $cale -Password (ConvertTo-SecureString $parola -AsPlainText -Force) | Out-Null
        Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
        $info[$tip] = @{ Cale = $cale; Parola = $parola }
    }
    $info | ConvertTo-Json | Set-Content -Encoding utf8 $secrete
}
$cert = Get-Content $secrete -Raw | ConvertFrom-Json

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
$api = Join-Path $productie "api"
dotnet publish (Join-Path $radacina "src\Host\ErpConstructii.Api") -c Release -o $api --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Publicarea API-ului a eșuat (oprește instanța de producție dacă rulează: deploy\opreste-productie.ps1)." }

# ---------------------------------------------------------------- 4. secretele API-ului
$tessdata = Join-Path $radacina "tessdata"
$config = [ordered]@{
    AllowedHosts      = "$Domeniu;localhost;127.0.0.1"
    ConnectionStrings = @{ Erp = "Host=localhost;Port=5432;Database=erp;Username=erp_app;Password=$ParolaBazaAplicatie" }
    Ocr               = @{ CaleTessdata = $tessdata }
    Autentificare     = [ordered]@{
        Emitent      = "https://$Domeniu/"
        Certificate  = @{
            Semnare  = @{ Cale = $cert.Semnare.Cale; Parola = $cert.Semnare.Parola }
            Criptare = @{ Cale = $cert.Criptare.Cale; Parola = $cert.Criptare.Parola }
        }
        Clienti      = @{
            "erp-web" = @{
                Denumire               = "ERP Construcții – aplicația web"
                RedirectUris           = @("https://$Domeniu/autentificare/callback")
                PostLogoutRedirectUris = @("https://$Domeniu/")
            }
        }
    }
}
$config | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 (Join-Path $api "appsettings.Production.local.json")

# ---------------------------------------------------------------- 5. Caddy
$caddy = Join-Path $productie "caddy\caddy.exe"
if (-not (Test-Path $caddy)) {
    Write-Host "Descarc Caddy..."
    Invoke-WebRequest "https://caddyserver.com/api/download?os=windows&arch=amd64" -OutFile $caddy -UseBasicParsing
}
$global = if ($EmailAcme) { "{`n    email $EmailAcme`n}`n`n" } else { "" }
$webCaddy = $web -replace '\\', '/'
$logCaddy = (Join-Path $productie "loguri\caddy-acces.log") -replace '\\', '/'
@"
$global$Domeniu {
    encode zstd gzip

    # Certificatul se verifică doar pe portul 443 (TLS-ALPN-01): portul 80 public poate rămâne al routerului.
    tls {
        issuer acme {
            disable_http_challenge
        }
    }

    # API-ul și serverul de autentificare (OAuth2 / OIDC), pe aceeași origine cu aplicația web (D-19).
    @api path /api/* /connect/* /.well-known/* /sanatate
    handle @api {
        reverse_proxy 127.0.0.1:$PortApi
    }

    # Aplicația web (React): fișiere statice; rutele aplicației cad pe index.html.
    handle {
        root * $webCaddy
        try_files {path} /index.html
        file_server
    }

    header {
        Strict-Transport-Security "max-age=31536000"
        X-Content-Type-Options "nosniff"
        X-Frame-Options "DENY"
        Referrer-Policy "strict-origin-when-cross-origin"
        -Server
    }

    log {
        output file $logCaddy {
            roll_size 20MiB
            roll_keep 5
        }
    }
}
"@ | Set-Content -Encoding utf8 (Join-Path $productie "caddy\Caddyfile")

# ---------------------------------------------------------------- 6. firewall
try {
    foreach ($port in @(80, 443)) {
        if (-not (Get-NetFirewallRule -DisplayName "ERP Construcții HTTP $port" -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName "ERP Construcții HTTP $port" -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow | Out-Null
        }
    }
    Write-Host "Regulile de firewall pentru 80 și 443 sunt active."
}
catch {
    Write-Warning "Nu am putut adăuga regulile de firewall (rulează scriptul o dată ca administrator): $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Gata. Pornește producția: powershell -File deploy\porneste-productie.ps1" -ForegroundColor Green
Write-Host "Adresa: https://$Domeniu/  (pe router: portul TCP 443 → acest calculator; 80 opțional)"
