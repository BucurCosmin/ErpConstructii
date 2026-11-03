# Descarcă modelele de limbă pentru OCR (Tesseract, română + engleză) în folderul „tessdata” din rădăcină.
# Necesare pentru importul notelor scanate (PDF / imagini). Rulat automat de porneste.ps1 dacă lipsesc.
#
#   powershell -ExecutionPolicy Bypass -File scripts\descarca-tessdata.ps1
#
# Sursa: modelele oficiale „tessdata_best” (Apache 2.0). ~25 MB în total; nu intră în git.
# Pe server, calea se poate schimba din configurare: „Ocr:CaleTessdata”.

$ErrorActionPreference = "Stop"
$destinatie = Join-Path (Split-Path -Parent $PSScriptRoot) "tessdata"
New-Item -ItemType Directory -Force $destinatie | Out-Null

foreach ($limba in @("ron", "eng")) {
    $fisier = Join-Path $destinatie "$limba.traineddata"
    if (Test-Path $fisier) {
        Write-Host "$limba.traineddata există deja."
        continue
    }

    Write-Host "Descarc $limba.traineddata..."
    Invoke-WebRequest "https://github.com/tesseract-ocr/tessdata_best/raw/main/$limba.traineddata" -OutFile $fisier -UseBasicParsing
}

Write-Host "Modelele OCR sunt în $destinatie" -ForegroundColor Green
