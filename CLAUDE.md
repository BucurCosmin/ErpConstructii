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

Interfața web: `cd clients/web && npm run dev` (http://localhost:5173; proxy Vite spre API pentru `/api`, `/connect`, `/.well-known`). În Development, API-ul creează date demo (`Dezvoltare/DateDemo.cs`): contul `demo@erpconstructii.ro` / `Demo-Constructii-2026`, două firme.

**Autentificare (D-07, D-19):** server OAuth2/OIDC în aplicație (Identity + OpenIddict, `Identitate.Infrastructure/Autentificare`). Flux Authorization Code + PKCE; pagina de login e în aplicația web (`/autentificare`), care creează cookie-ul de sesiune prin `/api/cont/*`, apoi `/connect/authorize` emite codul. Access token 10 min, refresh token rotit. Claim-uri: `sub` (utilizator), `firma_id` (firma activă). Schimbarea firmei: refresh token + parametrul `firma_id`. MFA TOTP obligatoriu după `PoliticaMfa`. Producția cere certificatele `Autentificare:Certificate:Semnare|Criptare:Cale/Parola` și clienții în `Autentificare:Clienti`.

**Administrare:** pe firma activă (schema `identitate` nu are RLS, deci fiecare interogare filtrează explicit pe `FirmaId`). Drepturile `administrare.*` se cer pe toată firma (`CerePermisiunePeFirma`). Reguli: firma nu rămâne fără administrator activ; contul (parolă, MFA, activare) al unui utilizator cu acces și la alte firme îl modifică doar administratorul de sistem; atribuirile revocate se încheie (istoric), nu se șterg, cu excepția celor care încă nu au început. Utilizatorii noi primesc o parolă temporară, schimbată obligatoriu la primul login.

**Documentele aprovizionării (D-21):** necesar de materiale (cererea șantierului, `NecesarMateriale`, NM/AAAA/NNNN, cu aprobare, șablonul „Necesar materiale”) → notă de comandă către furnizor (`NotaComanda`, NC/AAAA/NNNN, formularul GC 007) → factură. Nu le confunda: în istoricul git, „notă de comandă” însemna necesarul.

**Import documente externe (scanate / Word):** extractorii (`IExtractorText`, în `Formulare.Infrastructure/Extragere`) produc un `TextDocument` neutru: Word → paragrafe + tabele exacte; PDF / imagini → OCR local Tesseract (`ron+eng`, PDF randat cu PDFium la 300 DPI, liniile tabelelor șterse înainte de OCR, segmentare `SparseText`, paginile rotite îndreptate automat, cuvinte cu poziții regrupate pe rânduri; textul recunoscut stă 30 min în cache după SHA-256). Dosarele scanate (factură + e-Factura + notă de comandă + aviz + certificate în același PDF) trec prin `ClasificarePagini` (SharedKernel): fiecare interpretor primește doar paginile documentului lui. `InterpretorNecesarMateriale` și `InterpretorNotaComanda` (logică pură, Aprovizionare) dau fiecărui câmp o `Certitudine` (Sigur / Nesigur / Lipsa); `PotrivireProiect` propune proiectul din textul „Lucrare”. Nota de comandă nu are CUI-ul furnizorului: furnizorul se caută după denumire (`DenumireFirma.Cheie`: „S.C TRITON S.R.L” = „Triton SRL”) și se adaugă dacă lipsește; factura ulterioară îi completează CUI-ul. Confirmarea creează `NecesarMateriale.Importa` / `NotaComanda.Importa`: număr de sistem, `NumarExtern`, originalul în `aprovizionare.documente_originale` (SHA-256 unic pe firmă), necesarul în așteptarea aprobării. Modelele OCR: `scripts/descarca-tessdata.ps1` (folderul `tessdata/`, ignorat de git; configurabil cu `Ocr:CaleTessdata`). Testele pe documente reale se fac doar local, cu fișierele din `Sabloane/` (nu în teste unitare), cu **aplicația de test** (D-28): `dotnet run --project tools/ErpConstructii.TestImport [-- <filtru>] [--confirma] [--fara-cache]` → `Sabloane/_rezultate/raport.html`; valorile confirmate în `Sabloane/_asteptat/`. **Rulează-o după orice schimbare a interpretorilor** (facturi, note de comandă, necesare, clasificare, OCR): o corectură pe un document nu are voie să strice altul.

**Import documente (D-27):** pagina `/import` (`CentruImport.tsx`) pentru fișiere cu mai multe documente. `POST /api/import-documente/analiza` (Formulare.Infrastructure, `EndpointuriImportDocumente`) citește fișierul, clasifică paginile și propune documentele (`ImpartireDocumente`, SharedKernel); `POST /api/import-documente/{cheie}/document` taie paginile unui document într-un PDF (PDFsharp) și pune textul lor în cache-ul OCR (`ExtractorTextOcr.Memoreaza`, cache pe pagină), apoi PDF-ul trece prin ecranul de import al tipului (`ImportFactura` / `ImportNotaComanda` / `ImportNecesarExtern`, cu `fisierInitial` + `onImportat`). Drept: `CereOricarePermisiune(aprovizionare.creare, financiar.creare)`.

**Aplicația Windows de import (D-29):** `clients/desktop/ErpConstructii.ImportDesktop` (WPF, `ErpImport.exe`): același flux, prin API-ul aplicației (nu direct în bază); login PKCE prin browser cu întoarcere pe `127.0.0.1:47615` (client OAuth `erp-import` în `appsettings.json`); pornire din web prin `erp-import://deschide?server=…`; descărcabilă din web (`/descarcari/ErpImport.exe`, publicată de `pregateste-productie.ps1`). Detalii: `clients/desktop/README.md`. Contractele JSON sunt copiate în `Infrastructura/Modele.cs`: **o schimbare a DTO-urilor de import se face și acolo**.

**Facturi primite:** `POST /api/facturi/primite/import/interpretare` citește XML-ul e-Factura (`InterpretorEFactura`, UBL CIUS-RO, Invoice / CreditNote; arhiva ZIP din SPV despachetată, semnătura ignorată la citire dar arhivată) sau PDF-ul (`ExtractorTextPdf`: textul digital cu PdfPig, OCR doar pentru scanări; din dosarele scanate se citește de preferință pagina e-Factura din SPV cu `InterpretorVizualizareEFactura` (același format la toți furnizorii), altfel factura tipărită cu `InterpretorFacturaPdf`: CUI-ul diferit de al firmei = furnizorul, coloanele din antetul tabelului, cantitate × preț = valoare, totaluri încrucișate). Confirmarea (`/api/facturi/proiecte/{proiectId}/primite/import`) înregistrează `FacturaPrimita` (imuabilă, proiect obligatoriu); pentru XML serverul recitește XML-ul și ignoră liniile din formular; furnizorul se caută / adaugă prin `IServiciuParteneri`; XML-ul emis către alt CUI decât firma activă (`IInterogareFirma`) e refuzat. Anularea e entitate separată (`AnulareFacturaPrimita`, regula 6). Drepturi: `financiar.*`.

**Stocuri (D-23):** factură (linii „intră în stoc”) → recepție de către gestionar în depozit (`Nir.Emite`, NIR/AAAA/NNNN; diferențe cu observație; materialul propus de `PotrivireArticol` după aliasurile pe furnizor, altfel creat MAT-NNNNN) → transfer în „Materiale în șantier” (`TransferStoc`, BT/AAAA/NNNN; `DirectieTransfer.Retur` pentru returul în depozit) → consum pe articolele devizului (`BonConsum`, BC/AAAA/NNNN; articolul referit prin `CheieStabila`, null = cheltuieli generale). Devizul de referință vine din `IInterogareDevize` (bugetul de execuție aprobat, altfel contractul, …, altfel ultima ciornă); `RaportConsum` compară consumul cu bugetul de materiale al articolului. Stornarea NIR-ului e entitate separată (`StornoNir`, iese valoarea de intrare prin `CalculStoc.Storno`); inventarul (`Inventar`, INV/AAAA/NNNN) blochează gestiunea cât e deschis (`ModulStocuri.InventarDeschisAsync`, verificat de fiecare operație de stoc) și se finalizează cu `aprovizionare.aprobare`. PDF-urile (NIR, BC, BT): `DocumenteStocPdf` → `IServiciuDocumente` (Formulare): șablonul Word al firmei dacă există, altfel formatul standard `GeneratorPdfStandard` (PDFsharp-MigraDoc 6.2.4, MIT — nu actualiza: versiunile noi sunt AGPL). Evaluare CMP în `CalculStoc` (ieșirea întregii cantități ia toată valoarea rămasă). Stocuri citește facturile prin `IInterogareFacturiPrimite`; „de recepționat” = facturi neanulate cu linii de stoc fără NIR. Operațiile de stoc se serializează pe firmă (`pg_advisory_xact_lock`). Drepturi: `aprovizionare.*`; CMP / valori doar cu `date.preturi_cost`.

**Producție / acces extern (D-26):** `powershell -File deploy\pregateste-productie.ps1 -Domeniu sdaq.home.ro [-Port 443]` (compilează web + API, generează certificatul HTTPS autosemnat pentru domeniu + IP-urile din rețea și certificatele token-urilor, scrie secretele în `productiepippsettings.Production.local.json`, deschide portul în firewall — cere administrator), apoi `deploy\porneste-productie.ps1` / `opreste-productie.ps1`; diagnostic: `deployerifica-productie.ps1`. API-ul servește și aplicația web și ascultă direct pe `https://0.0.0.0:<port>`; pe router se redirecționează doar acel port.

**Cont blocat / parolă uitată:** `powershell -File deployeseteaza-parola.ps1 -Email <adresa> [-ReseteazaMfa]` (parolă temporară nouă, deblocare, sesiuni închise; folosește `productiepi` dacă există, altfel configurarea de dezvoltare). Comanda din spate: `ErpConstructii.Api --reseteaza-parola <email>` (`Administrare/ComandaCont.cs`).

Pentru teste cu curl, în Development API-ul acceptă și headerele `X-Dev-Utilizator: <guid>`, `X-Dev-Firma: <guid>` și opțional `X-Dev-Nume` (schema „Dev”, neînregistrată în alte medii). Pentru testele în browser se folosește un utilizator de test separat, nu contul demo (care are MFA înrolat pe telefonul utilizatorului).

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

Module existente: Identitate (+ server de autentificare), Proiecte, Parteneri (furnizori, clienți și subantreprenori), Devize (+ Import.WinDoc), Aprovizionare (necesare de materiale cu aprobare, note de comandă către furnizori, import din scanări / Word), Contracte (doar calcul), Grafic (+ Import.Primavera, Import.Excel), Facturi (facturi primite: import e-Factura XML / ZIP SPV și PDF, înregistrare pe proiect), Stocuri (NIR, transfer / retur, consum pe articole de deviz, CMP), Formulare (+ Infrastructure Word/PDF).

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
3. ~~Autentificare reală: Identity + OpenIddict, MFA TOTP, claim `firma_id`, schimbarea firmei active; rate limiting pe login.~~ Rămân: clientul OAuth pentru mobil (`erp-mobil`, redirect cu schemă proprie), curățarea periodică a token-urilor expirate (OpenIddict + Quartz), resetarea parolei (necesită serviciu de email).
4. ~~Administrarea utilizatorilor, rolurilor și atribuirilor, firme noi cu rolurile predefinite, invalidarea cache-ului de permisiuni.~~ (`EndpointuriAdministrare`, reguli în `RegulaAdministrare`; ecranul `/administrare`). Rămân: profilul propriu (schimbare parolă voluntară), invitație pe email în locul parolei temporare (după serviciul de email).
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
