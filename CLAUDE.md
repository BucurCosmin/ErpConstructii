# CLAUDE.md — ERP pentru construcții

Instrucțiuni pentru Claude Code. Citește tot fișierul înainte de prima modificare.

## Context

ERP pentru firme de construcții din România, după modelul Charisma ERP (TotalSoft). Ținta inițială: **firme medii (20–250 angajați)**, cu arhitectură pregătită pentru firme mari. Ideea centrală: **proiectul (șantierul) este axa costurilor**, iar **articolul de deviz este entitatea pivot**. Oferta, bugetul, consumurile, pontajul, situațiile și facturile se leagă de articol.

- Cerințele, cu ID-uri: `docs/cerinte.md` (ID-urile `REQ-*` apar în comentariile din cod).
- Deciziile de arhitectură: `docs/decizii.md` (`D-01` … `D-17`).
- Documentul de proiect complet (diagrame, rapoarte, formulare): în proiectul claude.ai „ERP pt constructii”.

## Starea curentă (generată în claude.ai, fără acces la nuget.org)

**Verificat (compilat și testat):** proiectele fără pachete NuGet: SharedKernel, nucleul modulelor Identitate, Proiecte, Devize, Contracte, Grafic, Facturi, Formulare, plus importatorii schelet. Cele 52 de teste din `tests/ErpConstructii.Teste.Unitare` au trecut, rulate cu un runner temporar care imita xUnit.

**Scris, dar NECOMPILAT** (depind de NuGet): `ErpConstructii.Infrastructure`, toate proiectele `*.Infrastructure` ale modulelor, `ErpConstructii.Api` și `tests/ErpConstructii.Teste.Formulare` (Open XML).

**Versiunile din `Directory.Packages.props` NU sunt verificate.**

### Primul pas obligatoriu

```bash
dotnet restore            # corectează versiunile din Directory.Packages.props dacă restore eșuează
dotnet build              # TreatWarningsAsErrors=true: repară toate erorile/avertismentele
dotnet test
```

Abia după ce build-ul și testele sunt verzi, treci la backlog-ul de mai jos.

## Comenzi

```bash
powershell -File db/creeaza-baza-locala.ps1           # PostgreSQL local: baza erp, roluri, migrări
docker compose up -d                                   # alternativ: PostgreSQL 17 + Gotenberg (PDF)
dotnet run --project src/Host/ErpConstructii.Api       # http://localhost:5080, OpenAPI la /openapi/v1.json

# Migrări EF: una per modul, rulate cu rolul erp_owner (NU erp_app).
dotnet ef migrations add Initial \
  -p src/Modules/Proiecte/ErpConstructii.Proiecte.Infrastructure \
  -s src/Host/ErpConstructii.Api \
  -c ProiecteDbContext -o Migrari
```

Interfața web: `cd clients/web && npm run dev` (http://localhost:5173, proxy spre API). În Development, API-ul creează date demo (`Dezvoltare/DateDemo.cs`), iar aplicația web își ia identitatea demo din `GET /dev/sesiune`.

În Development, autentificarea se face prin headere: `X-Dev-Utilizator: <guid>` și `X-Dev-Firma: <guid>`.

## Structura

```
src/
  BuildingBlocks/
    ErpConstructii.SharedKernel      entități de bază, Rezultat, Rotunjire, calendar, tipuri de import (fără NuGet)
    ErpConstructii.Infrastructure    IModul, DbContextModul, interceptori RLS + audit, CerePermisiune
  Host/ErpConstructii.Api            compunerea modulelor, autentificare, pipeline HTTP
  Modules/<Modul>/
    ErpConstructii.<Modul>           domeniu + logică pură (fără EF, fără ASP.NET) → testat unitar
    ErpConstructii.<Modul>.Infrastructure   EF (schema modulului), endpoints, servicii
    ErpConstructii.<Modul>.Import.<Format>  importatori (plugin)
tests/
  ErpConstructii.Teste.Unitare       logica pură, valori calculate de mână
  ErpConstructii.Teste.Formulare     generare + citire Word (Open XML)
clients/
  web                                React + TypeScript + Ant Design (Vite)
  mobile                             (viitor) React Native, partajează tipurile și clientul API
db/                                  roluri PostgreSQL, șablon RLS, script bază locală
docs/                                cerințe, decizii
```

Module existente: Identitate, Proiecte, Devize (+ Import.WinDoc), Contracte (doar calcul), Grafic (+ Import.Primavera, Import.Excel), Facturi (doar logică + contract ANAF), Formulare (+ Infrastructure Word/PDF).

## Reguli de arhitectură (nu le încălca)

1. **Modularitate.** Un modul nu referă proiectul `*.Infrastructure` al altui modul. Comunicarea trece prin proiectul de bază (contracte publice, ex. `IServiciuPermisiuni`) sau prin evenimente. Fără navigări EF între module: referințele sunt doar Id-uri.
2. **Schemă per modul.** Fiecare modul are propriul DbContext, derivat din `DbContextModul`. Schema nouă se adaugă și în `db/init/01-roluri.sql`.
3. **RLS pe fiecare tabel cu `firma_id`.** Migrarea care creează tabelul aplică `db/rls-template.sql`. Entitățile de business derivă din `EntitateFirma`; firma se completează automat la salvare. Excepție: schema `identitate`.
4. **Fiecare endpoint de business cere o permisiune:** `.CerePermisiune(Permisiuni.Pentru(ModulErp.X, Actiune.Y))`. Dacă ruta are `{proiectId}`, verificarea se face și pe proiect.
5. **Bani:** doar `decimal`, rotunjire explicită cu `Rotunjire`. Cotele legale (TVA, contribuții) vin din setări sau din document, niciodată din constante în cod.
6. **Documentele aprobate sunt imuabile** (`IDocumentCuStare`). Ce se schimbă după aprobare stă în entități separate.
7. **Importatorii** implementează `IImportator<T>` (`IImportatorDeviz`, `IImportatorGrafic`). Ei produc doar modelul canonic: nu ating baza de date, nu aruncă excepții pentru fișiere greșite, ci întorc probleme.
8. **Logica de calcul stă în proiectele de bază**, ca funcții pure cu teste. Infrastructura doar încarcă date, apelează calculul și salvează.

## Convenții de cod

- Identificatorii de domeniu și comentariile sunt în **română, fără diacritice în identificatori** (`VersiuneDeviz`, `CalculatorSituatie`). Termenii tehnici rămân în engleză (`DbContext`, `Handler`). Mesajele pentru utilizator sunt în română, cu diacritice.
- `namespace` la nivel de fișier, `record` pentru DTO-uri și valori, constructori privați pentru EF.
- Erorile așteptate se întorc ca `Rezultat` / `Rezultat<T>`. Excepțiile sunt doar pentru situații neprevăzute.
- Numele testelor sunt propoziții: `Totaluri_declarate_diferite_dau_eroare_cu_diferenta`. Valorile așteptate sunt calculate de mână, cu calculul în comentariu.
- Fișierele reale de la clienți (devize, grafice) **nu intră în git**. Ele stau în `tests/samples-private/` (ignorat). În `tests/samples/` intră doar fișiere anonimizate.

## Rețete

**Modul nou:** creezi `ErpConstructii.<Modul>` (domeniu) și `ErpConstructii.<Modul>.Infrastructure` (cu `IModul` și `DbContextModul`), îl adaugi în `module` din `Program.cs` și în `.csproj`-ul Host-ului, apoi adaugi schema în `01-roluri.sql`.

**Importator nou de deviz:** proiect `ErpConstructii.Devize.Import.<Format>` care referă doar `ErpConstructii.Devize`. Implementezi `IImportatorDeviz` și o extensie `Adauga…`, pe care o apelezi din `Program.cs`. Adaugi fișiere de test plus totalurile așteptate.

**Tip nou de formular:** adaugi schema în `CatalogFormulare` și un șablon .docx cu controale de conținut etichetate cu numele câmpurilor (tabele: `{tabel}.{coloana}`). Validezi șablonul cu `ValidatorSablonWord`.

## Backlog — faza 0 (în ordine)

1. Build și teste verzi (vezi „Primul pas”).
2. ~~Migrările inițiale pentru Identitate, Proiecte, Devize, cu RLS (`MigrariRls.AplicaRls`).~~ Rămâne: test de integrare (Testcontainers PostgreSQL): o firmă nu vede datele alteia, nici cu interogări fără `WHERE`.
3. Autentificare reală: ASP.NET Core Identity + OpenIddict (D-07), MFA TOTP, claim `firma_id` pentru firma activă și schimbarea firmei active. Elimini `throw`-ul din `Program.cs` pentru non-Development.
4. Administrarea utilizatorilor, rolurilor și atribuirilor: la crearea unei firme se copiază rolurile din `RoluriPredefinite`, iar cache-ul de permisiuni se invalidează la schimbări.
5. Jurnalul de audit append-only (schema `audit`), scris din `InterceptorAudit`.
6. Modulul Documente (arhivă de fișiere: SHA-256, imuabil), folosit de importul de devize (`FisierSursaId`) și de formulare.
7. **Importatorul WinDoc:** blocat până avem fișiere reale de la pilot. Pașii sunt în comentariul din `ImportatorWinDoc`. Poarta fazei 0: un deviz real importat și recalculat identic.
8. Setările firmei: cote implicite pentru recapitulație, cote TVA cu dată de valabilitate, formate de import active.
9. Teste de arhitectură (NetArchTest sau similar) pentru regula 1.

Fazele 1–3: vezi `docs/cerinte.md`, coloana „Faza”.

## Întrebări deschise (nu presupune răspunsul; întreabă)

- Ce format exportă WinDoc (fișier nativ, XML, Excel) și avem fișiere reale de test?
- Exemple reale de grafice Primavera (.xer) și Excel din documentații de atribuire, pentru definirea șablonului.
- Ce program contabil folosește pilotul (pentru primul exportator contabil)?
- Contabilitate și salarizare proprii sau integrare pe termen lung?
- Cloud (SaaS) sau on-premise ca model principal?
- REGES: situația juridică a HG 295/2025, anulată în primă instanță în aprilie 2026.
- Tratamentul exact al avansului (storno separat?) și baza garanției de bună execuție. De validat cu contabilul pilotului înainte de faza 1.
