# Aplicația Windows de import documente (D-29)

Aplicație auxiliară (WPF, .NET 10) pentru fișierele scanate cu mai multe documente: facturi (cu e-Factura, aviz,
certificate), note de comandă, necesare de materiale. Citește fișierul întreg, propune documentele, iar utilizatorul
le verifică pe rând lângă imaginea paginilor și le importă în aplicația principală.

**Lucrează doar prin API-ul aplicației principale** (aceleași endpoint-uri ca pagina web „Import documente”): aceleași
drepturi, verificări, potriviri de furnizori și proiecte, aceeași bază de date. Fără serverul aplicației instalat și
pornit, nu face nimic.

## Pornire

- **Din aplicația web:** Import documente → „Deschide în aplicația Windows” (linkul `erp-import://deschide?server=…`).
  Linkul merge după ce aplicația a fost pornită o dată (se înregistrează singură pentru utilizatorul Windows curent).
- **Direct:** `ErpImport.exe`; se scrie adresa aplicației web (ex. `https://sdaq.home.ro`, în dezvoltare
  `http://localhost:5173`) și se apasă „Conectare”. Opțional: `ErpImport.exe --server <adresă> <fișier>`.
- **Descărcare:** în producție, `deploy\pregateste-productie.ps1` publică `ErpImport.exe` (un singur fișier, fără .NET
  instalat) în aplicația web: Import documente → „Descarcă aplicația Windows”.

## Autentificare

Login prin browserul sistemului, pe pagina aplicației web (cu MFA): Authorization Code + PKCE, cu întoarcerea pe
`http://127.0.0.1:47615/callback/` (sau 47616 / 47617). Clientul OAuth `erp-import` e înregistrat pe server în
`appsettings.json` (`Autentificare:Clienti`). Aplicația nu vede parola și nu salvează token-urile.

Certificatul autosemnat al producției (D-26): la prima conectare aplicația arată amprenta SHA-256; după confirmare o
ține minte (`%APPDATA%\ErpConstructii\Import\setari.json`) și refuză alt certificat pe aceeași adresă.

## Dezvoltare

```bash
dotnet run --project clients/desktop/ErpConstructii.ImportDesktop -- --server http://localhost:5173
```

`ERP_IMPORT_BROWSER=<program>` înlocuiește browserul implicit la login (folosit de testele automate).
