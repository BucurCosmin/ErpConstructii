# ERP pentru construcții

ERP pentru firme de construcții din România (după modelul Charisma ERP): devize, contracte, situații de lucrări, grafice de execuție, șantier, formulare Word, e-Factura.

- **Stack:** .NET 10, ASP.NET Core, EF Core, PostgreSQL, Open XML SDK, Gotenberg (PDF).
- **Interfață web:** React + TypeScript + Ant Design (Vite), în `clients/web`. Aplicația mobilă va sta în `clients/mobile` (React Native).
- **Arhitectură:** monolit modular; schemă PostgreSQL per modul; Row-Level Security pe firmă.

## Pornire rapidă

**Zilnic** (după ce baza de date există): din folderul proiectului rulezi

```powershell
powershell -ExecutionPolicy Bypass -File porneste.ps1
```

Scriptul pornește API-ul și interfața web în câte o fereastră și deschide http://localhost:5173.
Atenție: `dotnet run` simplu, dat din rădăcină, nu merge (acolo e soluția, nu API-ul); varianta manuală e
`dotnet run --project src/Host/ErpConstructii.Api` plus `npm run dev` în `clients/web`.

**Prima instalare:**

Pentru importul notelor scanate (OCR) trebuie modelele de limbă Tesseract: `scripts/descarca-tessdata.ps1`
(rulat automat de `porneste.ps1`). Pe Windows, Tesseract are nevoie de Microsoft Visual C++ Redistributable 2015–2022;
pe Linux, de pachetele `libtesseract5` și `libleptonica`.


Cerințe: .NET SDK 10, Node.js 22+, PostgreSQL 17/18 (local sau prin Docker), `dotnet tool install -g dotnet-ef`.

```bash
# 1. Baza de date
#    a) PostgreSQL instalat local (cere parola utilizatorului postgres):
powershell -ExecutionPolicy Bypass -File db/creeaza-baza-locala.ps1
#    b) sau Docker: docker compose up -d, apoi aceleași migrări (vezi scriptul)

# 2. Backend: http://localhost:5080 (în Development creează date demo la prima pornire)
dotnet build && dotnet test
dotnet run --project src/Host/ErpConstructii.Api

# 3. Interfața web: http://localhost:5173
cd clients/web
npm install
npm run dev
```

**Cont demo (doar Development):** `demo@erpconstructii.ro` / `Demo-Constructii-2026`. La primul login se cere activarea
verificării în doi pași (cod QR pentru Google/Microsoft Authenticator). Contul are acces la două firme demo
(„Construct Demo SRL” și „Construct Demo Sud SRL”), cu selectorul de firmă din antet, și este administrator de sistem
(poate crea firme). Colegii demo (Maria Ionescu, Radu Marin etc.) au parole temporare: din Administrare → Utilizatori →
fișa utilizatorului → „Resetează parola” obții una pe care o poți folosi la login.

## Actualizarea producției

```powershell
git pull
powershell -ExecutionPolicy Bypass -File db\creeaza-baza-locala.ps1     # migrările noi (cere parola lui postgres)
powershell -ExecutionPolicy Bypass -File deploy\reporneste-productie.ps1 -Actualizeaza
```

Pe serverul de producție, scriptul bazei ia parola lui `erp_app` din `productie\api\appsettings.Production.local.json`
(nu o da cu `-ParolaApp`). Dacă aplicația nu pornește cu „password authentication failed for user erp_app”, rulează
din nou `db\creeaza-baza-locala.ps1` fără `-ParolaApp`: pune înapoi parola din configurare.

Aplicația nu aplică singură migrările (rulează cu `erp_app`; migrările cer `erp_owner`). Fără ele, paginile modulelor
noi dau „error processing your request”. La pornire, lipsa lor apare în `productie\loguri\api.log`, în `/sanatate`
(`"stare": "migrari-lipsa"`) și în `deploy\verifica-productie.ps1`. Parolele trebuie să fie cele cu care a fost creată
baza pe acel server (altfel scriptul le schimbă și aplicația nu se mai conectează).

## Cont blocat, parolă uitată, codul din Authenticator nu mai merge

Pe calculatorul cu aplicația (producție sau dezvoltare), dintr-un PowerShell în rădăcina repo-ului:

```powershell
# parolă temporară nouă + deblocare + sesiuni închise
powershell -ExecutionPolicy Bypass -File deploy\reseteaza-parola.ps1 -Email demo@erpconstructii.ro

# la fel, plus dezactivarea verificării în doi pași (telefon pierdut / schimbat, codurile sunt refuzate)
powershell -ExecutionPolicy Bypass -File deploy\reseteaza-parola.ps1 -Email demo@erpconstructii.ro -ReseteazaMfa
```

- Scriptul afișează parola temporară. La login se cere o parolă nouă; cu `-ReseteazaMfa` se scanează din nou codul QR
  (șterge mai întâi intrarea veche din Authenticator).
- Dacă există `productie\api` (creat de `deploy\pregateste-productie.ps1`), lucrează pe baza de producție; altfel
  compilează și folosește configurarea de dezvoltare. Aplicația poate rula în timpul resetării.
- Comanda din spate: `ErpConstructii.Api --reseteaza-parola <email> [--reseteaza-mfa]` (`Administrare/ComandaCont.cs`).
- **Codurile refuzate repetat = aproape mereu ceasul serverului.** Codurile din Authenticator depind de oră, iar aplicația
  acceptă doar ±1 minut față de telefon. Când codul e corect dar ceasul diferă, ecranul de login spune acum direct asta
  (și `productie\loguri\api.log` scrie decalajul): atunci **nu** reseta MFA, sincronizează ora.
  `deploy\verifica-productie.ps1` compară ceasul cu ora de pe internet. Sincronizarea permanentă (PowerShell ca administrator):

  ```powershell
  sc.exe config w32time start= auto
  net start w32time
  w32tm /config /manualpeerlist:"time.windows.com,0x9 pool.ntp.org,0x9" /syncfromflags:manual /update
  w32tm /resync /force
  ```

  Pe telefon: data și ora setate automat.
- **Fiecare server are altă cheie MFA.** Intrarea din Authenticator se numește acum după server („ERP Construcții
  (dezvoltare)”, „ERP Construcții (sdaq.home.ro)”; configurabil cu `Autentificare:EmitentMfa`). Intrările vechi, numite
  doar „ERP Construcții”, se puteau înlocui una pe alta la scanarea unui cod nou (dezvoltare ↔ producție). Codul refuzat
  care nu se potrivește nici cu decalaj de ceas e semnalat pe ecran ca „cod pentru alt server”.

## Documentație

- [CLAUDE.md](CLAUDE.md): starea proiectului, reguli, convenții, backlog
- [docs/cerinte.md](docs/cerinte.md): cerințele cu ID-uri și faze
- [docs/decizii.md](docs/decizii.md): deciziile de arhitectură
