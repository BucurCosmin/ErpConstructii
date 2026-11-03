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

## Documentație

- [CLAUDE.md](CLAUDE.md): starea proiectului, reguli, convenții, backlog
- [docs/cerinte.md](docs/cerinte.md): cerințele cu ID-uri și faze
- [docs/decizii.md](docs/decizii.md): deciziile de arhitectură
