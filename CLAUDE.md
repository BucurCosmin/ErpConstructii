# CLAUDE.md — ERP pentru construcții

Instrucțiuni pentru Claude Code. Citește tot fișierul înainte de prima modificare.

## Context

ERP pentru firme de construcții din România, după modelul Charisma ERP (TotalSoft). Ținta inițială: **firme medii (20–250 angajați)**, cu arhitectură pregătită pentru firme mari. Ideea centrală: **proiectul (șantierul) este axa costurilor**, iar **articolul de deviz este entitatea pivot**. Oferta, bugetul, consumurile, pontajul, situațiile și facturile se leagă de articol.

- Cerințele, cu ID-uri: `docs/cerinte.md` (ID-urile `REQ-*` apar în comentariile din cod).
- Deciziile de arhitectură: `docs/decizii.md` (`D-01` … `D-55`).
- Prețurile resurselor din recepții / comenzi / oferte și actualizarea devizului: `docs/preturi-resurse.md` (D-55, pe etape).
- Devizele ISDP: `docs/import-isdp.md` (formatul, regulile verificate pe fișiere reale, modelul) și `docs/editor-deviz.md` (editorul, biblioteca de norme, catalogul de prețuri; D-51, D-52).
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
powershell -File scripts/reporneste-dezvoltare.ps1     # oprește API-ul de pe 5080, recompilează, pornește (+ Vite dacă nu rulează; -Web îl repornește)
powershell -File deploy/reporneste-productie.ps1       # repornește producția; -Actualizeaza = recompilează codul curent cu setările existente

# Migrări EF: una per modul, rulate cu rolul erp_owner (NU erp_app).
dotnet ef migrations add Initial \
  -p src/Modules/Proiecte/ErpConstructii.Proiecte.Infrastructure \
  -s src/Host/ErpConstructii.Api \
  -c ProiecteDbContext -o Migrari
```

Interfața web: `cd clients/web && npm run dev` (http://localhost:5173; proxy Vite spre API pentru `/api`, `/connect`, `/.well-known`). În Development, API-ul creează date demo (`Dezvoltare/DateDemo.cs`): contul `demo@erpconstructii.ro` / `Demo-Constructii-2026`, două firme.

**Autentificare (D-07, D-19):** server OAuth2/OIDC în aplicație (Identity + OpenIddict, `Identitate.Infrastructure/Autentificare`). Flux Authorization Code + PKCE; pagina de login e în aplicația web (`/autentificare`), care creează cookie-ul de sesiune prin `/api/cont/*`, apoi `/connect/authorize` emite codul. Access token 10 min, refresh token rotit. Claim-uri: `sub` (utilizator), `firma_id` (firma activă). Schimbarea firmei: refresh token + parametrul `firma_id`. MFA TOTP obligatoriu după `PoliticaMfa`. Producția cere certificatele `Autentificare:Certificate:Semnare|Criptare:Cale/Parola` și clienții în `Autentificare:Clienti`.

**Administrare:** pe firma activă (schema `identitate` nu are RLS, deci fiecare interogare filtrează explicit pe `FirmaId`). Drepturile `administrare.*` se cer pe toată firma (`CerePermisiunePeFirma`). Reguli: firma nu rămâne fără administrator activ; contul (parolă, MFA, activare) al unui utilizator cu acces și la alte firme îl modifică doar administratorul de sistem; atribuirile revocate se încheie (istoric), nu se șterg, cu excepția celor care încă nu au început. Utilizatorii noi primesc o parolă temporară, schimbată obligatoriu la primul login.

**Documentele aprovizionării (D-21):** necesar de materiale (cererea șantierului, `NecesarMateriale`, NM/AAAA/NNNN, șablonul „Necesar materiale”; emis pe un șantier al proiectului ales, D-38 / D-46, `SantierId`, entitatea `Santier` din Proiecte — necesarele vechi pe un compartiment al firmei, `CompartimentId`, îl păstrează la modificare; la creare generează automat nota de comandă „de acceptat”, fără furnizor, iar acceptarea ei aprobă necesarul, D-37; cât nu e aprobat, necesarul se modifică / șterge de emitent sau cu `aprovizionare.editare`, iar nota îl urmează; notele acceptate apar la achiziții în tab-ul „Necesar de achiziție”, unde primesc furnizorul — toată nota sau împărțită pe furnizori, `FluxNotaComanda`) → notă de comandă către furnizor (`NotaComanda`, NC/AAAA/NNNN, formularul GC 007: emisă în aplicație din tab-ul „Notă de comandă nouă”, D-34, cu Word din șablonul firmei `nota-comanda` și PDF, sau importată de pe hârtie; rapoarte pe lucrare: registru / detaliat / materiale comandate, `DocumenteNoteComanda`; nota emisă se modifică / șterge cu motiv și istoric, D-44, `CorectieNotaComanda` / `StergereNotaComanda`) → factură. La import, același document din alt fișier (altă scanare) se recunoaște după numărul de pe hârtie (D-46, `DocumentDublat`, pe cifre): necesar / notă = număr + dată, aviz = număr + furnizor; facturile după furnizor + număr. Documentele vechi se importă cu bifa „din urmă” (D-45, `DinUrma`): fără legăturile cerute celor curente (necesar, notă de comandă, contract), care se pun ulterior. Nu le confunda: în istoricul git, „notă de comandă” însemna necesarul. Numerele de pe hârtie (D-48, `NumerotareExterna` în SharedKernel): necesarele importate și bonurile de consum (`BonConsum.NumarExtern`) au numărul de sistem automat (NM/…, BC/…, nu se schimbă) și numărul de hârtie; seria de hârtie e pe proiect și an (același număr pe proiecte diferite e în regulă); fără număr pe document, interpretarea propune primul număr liber din seria proiectului propus, iar un număr folosit deja în serie e semnalat. „Verifică numerele” (`GET …/necesare-materiale/numere`, `/api/stocuri/consumuri/numere`) arată pe proiect și an suprapunerile, numerele sărite și documentele fără număr; renumerotarea cere motiv și lasă `RenumerotareDocument` (istoric, `aprovizionare.editare`, refuzată pe un număr ocupat pe același proiect, în același an). Rapoartele PDF ale documentelor filtrate (registru / detaliat / materiale adunate): `POST …/necesare-materiale/raport/pdf`, `/api/stocuri/consumuri/raport/pdf` (`componente/NumereHartie`).

**Contracte cu subantreprenori / parteneri (D-41):** `ContractPartener` (Contracte, CT/AAAA/NNNN, tab-ul „Contracte” din fișa proiectului și pagina `/contracte`): ciornă → activ (semnat, `contracte.aprobare`) → acte adiționale / închidere (entități separate) + documente atașate; situația (`SituatieContract`) din facturile legate (`IInterogareFacturiContract`, implementat de Facturi). Factura doar de servicii cere contractul (`FacturaPrimita.ContractId`, `IInterogareContracte`, `GET /api/facturi/contracte`), cea cu materiale cere nota de comandă (D-40).

**Avize de însoțire (D-40):** marfa venită înaintea facturii. `AvizInsotire` (Aprovizionare, AV/AAAA/NNNN, tab-ul „Avize”, import cu `InterpretorAviz` + `ImportAviz.tsx`, și din centrul de import, tipul `Aviz` doar în fișierele fără factură) se leagă de o notă de comandă emisă (pomenită pe aviz) sau de una generată din el. Tabelul avizului se citește pe rânduri fizice, ca la necesare: rândul cu „UM cantitate” e materialul, iar rândurile de text din celula lui (denumirea lungă, deasupra și / sau dedesubt; banda dintre liniile tabelului, altfel cel mai apropiat material, în coloana denumirii, fără etichete, zgomot OCR sau spații mari) se lipesc de el; rândurile de detaliu („REAVIZARE AVIZ …”, „Cod NC”) nu intră în denumire. Avizele reavizate din același fișier (către alt client, fără CUI-ul firmei) rămân anexele avizului către firmă (`ImpartireDocumente.Imparte(..., cuiFirma)`). Rapoartele avizelor afișate (cu filtrele din ecran): `POST /api/aprovizionare/avize/raport/pdf` (`RapoarteAvize`, `RaportAvize` pur): sintetic pe furnizor și lucrare (avize, poziții, de recepționat / recepționate fără factură / facturate), materiale primite adunate pe lucrare, registru, detaliat; plus CSV. Stocuri o citește prin `IInterogareAvize`: avizul e „de recepționat”, NIR-ul pe aviz (`Nir.EmiteDinAviz`, `AvizId`, liniile cu `IndexAviz`, `LinieNir.Cheie`; `FacturaId` null până vine factura) intră fără valoare. Factura cu materiale de stoc cere nota de comandă (`FacturaPrimita.NotaComandaId`, contractul `IInterogareNoteComanda`, `GET /api/facturi/note-comanda`); „Leagă de avize” (`EndpointuriFacturareAviz`, `Nir.Factureaza`) dă NIR-urilor pe aviz prețurile facturii. Contractele inverse: `IInterogareReceptiiAviz` (Stocuri), `IInterogareFacturiNotaComanda` (Facturi).

**Import documente externe (scanate / Word):** extractorii (`IExtractorText`, în `Formulare.Infrastructure/Extragere`) produc un `TextDocument` neutru: Word → paragrafe + tabele exacte; PDF / imagini → OCR local Tesseract (`ron+eng`, PDF randat cu PDFium la 300 DPI, liniile tabelelor șterse înainte de OCR, segmentare `SparseText`, paginile rotite îndreptate automat, cuvinte cu poziții regrupate pe rânduri; textul recunoscut stă 30 min în cache după SHA-256). Liniile orizontale ale tabelelor (`TextDocument.Linii`, `DetectareLinii`: din imaginea paginii înainte de ștergerea lor, sau din desenele PDF-ului digital) delimitează rândurile fizice: `InterpretorNecesarMateriale` face un material din tot textul dintre două linii (denumire / observații pe mai multe rânduri); fără linii, un rând doar cu denumire ține de rândul cu date cel mai apropiat. Dosarele scanate (factură + e-Factura + notă de comandă + aviz + certificate în același PDF) trec prin `ClasificarePagini` (SharedKernel): fiecare interpretor primește doar paginile documentului lui. `InterpretorNecesarMateriale` și `InterpretorNotaComanda` (logică pură, Aprovizionare) dau fiecărui câmp o `Certitudine` (Sigur / Nesigur / Lipsa); `PotrivireProiect` propune proiectul din textul „Lucrare”. Nota de comandă nu are CUI-ul furnizorului: furnizorul se caută după denumire (`DenumireFirma.Cheie`: „S.C TRITON S.R.L” = „Triton SRL”) și se adaugă dacă lipsește; factura ulterioară îi completează CUI-ul. Confirmarea creează `NecesarMateriale.Importa` / `NotaComanda.Importa`: număr de sistem, `NumarExtern`, originalul în `aprovizionare.documente_originale` (SHA-256 unic pe firmă), necesarul în așteptarea aprobării. Modelele OCR: `scripts/descarca-tessdata.ps1` (folderul `tessdata/`, ignorat de git; configurabil cu `Ocr:CaleTessdata`). Testele pe documente reale se fac doar local, cu fișierele din `Sabloane/` (nu în teste unitare), cu **aplicația de test** (D-28): `dotnet run --project tools/ErpConstructii.TestImport [-- <filtru>] [--confirma] [--fara-cache]` → `Sabloane/_rezultate/raport.html`; valorile confirmate în `Sabloane/_asteptat/`. **Rulează-o după orice schimbare a interpretorilor** (facturi, note de comandă, necesare, clasificare, OCR): o corectură pe un document nu are voie să strice altul.

**Import documente (D-27):** pagina `/import` (`CentruImport.tsx`) pentru fișiere cu mai multe documente. `POST /api/import-documente/analiza` (Formulare.Infrastructure, `EndpointuriImportDocumente`) citește fișierul, clasifică paginile și propune documentele (`ImpartireDocumente`, SharedKernel); `POST /api/import-documente/{cheie}/document` taie paginile unui document într-un PDF (PDFsharp) și pune textul lor în cache-ul OCR (`ExtractorTextOcr.Memoreaza`, cache pe pagină), apoi PDF-ul trece prin ecranul de import al tipului (`ImportFactura` / `ImportNotaComanda` / `ImportNecesarExtern`, cu `fisierInitial` + `onImportat`). Drept: `CereOricarePermisiune(aprovizionare.creare, financiar.creare)`.

**Aplicația Windows de import (D-29):** `clients/desktop/ErpConstructii.ImportDesktop` (WPF, `ErpImport.exe`): același flux, prin API-ul aplicației (nu direct în bază); login PKCE prin browser cu întoarcere pe `127.0.0.1:47615` (client OAuth `erp-import` în `appsettings.json`); pornire din web prin `erp-import://deschide?server=…`; descărcabilă din web (`/descarcari/ErpImport.exe`, publicată de `pregateste-productie.ps1`). Detalii: `clients/desktop/README.md`. Bonurile de consum de pe hârtie (D-30): `InterpretorBonConsum` (Stocuri) + `ZoneDocumente` (două bonuri pe pagină), `POST /api/stocuri/bonuri-consum/import/interpretare?zona=` și `.../proiecte/{id}/bonuri-consum/import/potrivire` (materialele din stocul șantierului), confirmarea prin `/consumuri`. Contractele JSON sunt copiate în `Infrastructura/Modele.cs`: **o schimbare a DTO-urilor de import se face și acolo**.

**Legături între pagini:** numărul facturii din NIR / stocuri / proveniență deschide `/facturi?factura=<id>` (`componente/LinkFactura`); corectura facturii trimite la `/stocuri?tab=receptii&nir=registru&factura=<id>`; butonul „Istoric” deschide `/jurnal?document=<număr>`.

**Facturi primite:** `POST /api/facturi/primite/import/interpretare` citește XML-ul e-Factura (`InterpretorEFactura`, UBL CIUS-RO, Invoice / CreditNote; arhiva ZIP din SPV despachetată, semnătura ignorată la citire dar arhivată) sau PDF-ul (`ExtractorTextPdf`: textul digital cu PdfPig, OCR doar pentru scanări; din dosarele scanate se citește de preferință pagina e-Factura din SPV cu `InterpretorVizualizareEFactura` (același format la toți furnizorii), altfel factura tipărită cu `InterpretorFacturaPdf`: CUI-ul diferit de al firmei = furnizorul, coloanele din antetul tabelului, cantitate × preț = valoare, totaluri încrucișate). Confirmarea (`/api/facturi/proiecte/{proiectId}/primite/import`) înregistrează `FacturaPrimita` (imuabilă, proiect obligatoriu); pentru XML serverul recitește XML-ul și ignoră liniile din formular; furnizorul se caută / adaugă prin `IServiciuParteneri`; XML-ul emis către alt CUI decât firma activă (`IInterogareFirma`) e refuzat. Anularea e entitate separată (`AnulareFacturaPrimita`, regula 6). Rapoartele (`EndpointuriRapoarteFacturi`): registrul facturilor și totalurile pe furnizor / proiect / lună / cotă TVA (`RaportFacturi`, pur; o factură ale cărei linii nu dau totalurile ei, `LiniiConcordante`, intră la cote cu totalurile ei și e semnalată „de verificat”), originalele PDF combinate pentru tipărire; în listă, totaluri pe monedă și export CSV (facturi / linii). Corectura (D-33, `EndpointuriCorectieFacturi`, `FacturaPrimita.Corecteaza`): factura blocată se modifică doar împreună cu o `CorectieFacturaPrimita` (istoric); orice câmp se corectează, și pe liniile intrate în stoc (NIR-ul rămâne cu valorile lui, răspunsul dă avertismente), dar cât există recepții liniile existente nu se șterg. Câmpurile numerice din web: `componente/NumarRo` (virgula = zecimale, punctul = mii). Drepturi: `financiar.*`.

**Stocuri (D-23):** factură (linii „intră în stoc”) → recepție de către gestionar în depozit (`Nir.Emite`, NIR/AAAA/NNNN; diferențe cu observație; materialul propus de `PotrivireArticol` după aliasurile pe furnizor, altfel creat MAT-NNNNN) → transfer în „Materiale în șantier” (`TransferStoc`, BT/AAAA/NNNN; `DirectieTransfer.Retur` pentru returul în depozit) → consum pe articolele devizului (`BonConsum`, BC/AAAA/NNNN; articolul referit prin `CheieStabila`, null = cheltuieli generale). Devizul de referință vine din `IInterogareDevize` (bugetul de execuție aprobat, altfel contractul, …, altfel ultima ciornă); `RaportConsum` compară consumul cu bugetul de materiale al articolului. O factură se poate recepționa în mai multe NIR-uri (D-31: liniile alese, „restul vine” = `LinieNir.Partiala`, starea în `StareReceptie`); bonul de consum se poate completa cu o factură întreagă (`GET /proiecte/{id}/consum/facturi`, transferul din depozit a ce lipsește în șantier), iar transferul în șantier poate lua o factură întreagă, doar dacă toate materialele ei sunt în depozitul recepției (`TabelFacturiReceptionate`, comun). Facturile primite se caută și după materialele din linii (`?material=`). Proveniența stocului (D-32): `TrasabilitateStoc` reconstituie din mișcări pe ce NIR / factură a intrat fiecare cantitate din gestiuni (ieșiri FIFO, transferul păstrează originea), raportul `GET /trasabilitate` (Recepții → Proveniență; în tabelele de stoc rândul se desface cu factura → NIR), raportul stocului `GET /raport-stoc/pdf` (sumar la CMP sau `detaliat=true` pe proveniență). Stornarea NIR-ului e entitate separată (`StornoNir`, iese valoarea de intrare prin `CalculStoc.Storno`); NIR-ul recepționat greșit se corectează (D-35, `Nir.Corecteaza` + `CorectieNir`, ✎ în Registru NIR): stocul primește diferența (`CalculStoc.Corectie`, mișcare `CorectieNir` pe același NIR), alături de factura actuală; inventarul (`Inventar`, INV/AAAA/NNNN) blochează gestiunea cât e deschis (`ModulStocuri.InventarDeschisAsync`, verificat de fiecare operație de stoc) și se finalizează cu `aprovizionare.aprobare`. Depozitul (`Depozit`, loc fizic) se împarte în gestiuni cu gestionar (D-43, `Gestiune.DepozitId` / `GestionarId`, `IInterogareUtilizatori`, ecranul `DepoziteGestiuni.tsx`); stocul e pe gestiune. Stocul de pornire (D-42): exportul din programul anterior (Excel Nexus, `Sabloane/gestiune/`): de preferință „Raport rapid stoc” (un rând pe lot de preț, gestiunea pe rând, cu valoare, fără U.M.: materialele găsite o au, cele noi o primesc în ecran), altfel „Balanță stoc” (doar cantități, valoare 0) → `InterpretorBalantaStoc` (recunoaște formatul după antet) + `CititorXlsx` → `SoldInitialStoc` (SI/AAAA/NNNN, câte unul pe fișier și gestiune; gestiunile din fișier sunt gestiuni ale depozitului ales, mișcarea `SoldInitial`; `EndpointuriSoldInitial`, ecranul `SoldInitial.tsx`). Soldul preluat greșit se anulează (`DELETE /solduri-initiale/{id}`, `AnulareSoldInitial`) cât timp materialele lui nu au alte mișcări în gestiune; rămâne urma `SoldInitialAnulat`, iar numărul nu se refolosește. PDF-urile (NIR, BC, BT): `DocumenteStocPdf` → `IServiciuDocumente` (Formulare): șablonul Word al firmei dacă există, altfel formatul standard `GeneratorPdfStandard` (PDFsharp-MigraDoc 6.2.4, MIT — nu actualiza: versiunile noi sunt AGPL). Evaluare CMP în `CalculStoc` (ieșirea întregii cantități ia toată valoarea rămasă). Stocuri citește facturile prin `IInterogareFacturiPrimite`; „de recepționat” = facturi neanulate cu linii de stoc fără NIR. Operațiile de stoc se serializează pe firmă (`pg_advisory_xact_lock`). Drepturi: `aprovizionare.*`; CMP / valori doar cu `date.preturi_cost`.

**Producție / acces extern (D-26):** `powershell -File deploy\pregateste-productie.ps1 -Domeniu sdaq.home.ro [-Port 443]` (compilează web + API, generează certificatul HTTPS autosemnat pentru domeniu + IP-urile din rețea și certificatele token-urilor, scrie secretele în `productie\api\appsettings.Production.local.json`, deschide portul în firewall — cere administrator), apoi `deploy\porneste-productie.ps1` / `opreste-productie.ps1`; diagnostic: `deploy\verifica-productie.ps1`. API-ul servește și aplicația web și ascultă direct pe `https://0.0.0.0:<port>`; pe router se redirecționează doar acel port.

**Cont blocat / parolă uitată:** `powershell -File deploy\reseteaza-parola.ps1 -Email <adresa> [-ReseteazaMfa]` (parolă temporară nouă, deblocare, sesiuni închise; folosește `productie\api` dacă există, altfel configurarea de dezvoltare). Comanda din spate: `ErpConstructii.Api --reseteaza-parola <email>` (`Administrare/ComandaCont.cs`).

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

Sediile firmei (D-47) sunt proiecte de tipul `TipProiect.Sediu` (birouri, bază, depozit): fără beneficiar, pornesc „în execuție”, primesc aceleași documente (necesare, note de comandă, facturi, bonuri de consum) și rapoarte ca o lucrare; locurile din ele sunt „șantierele” lor (tab-ul „Locații”); în alegeri apar cu „(sediu)” (`etichetaProiect`). Proiectul se editează complet și se arhivează / reactivează doar cu `proiecte.editare` pe toată firma (D-38, D-39: arhivat = ascuns din liste și alegeri, fără documente noi în afară de retur; endpointurile care creează documente verifică `DateProiectPentruDocument.Arhivat`); are mai multe șantiere (tab-ul „Șantiere” din fișa proiectului); pornește cu „Șantier principal” (redenumibil) și rămâne mereu cu cel puțin un șantier activ; șantierul pus din greșeală se șterge (D-49, `StergereSantier`, `DELETE …/santiere/{id}?mutaPe=`): necesarele lui se mută pe alt șantier activ prin `IUtilizariSantier` (implementat de Aprovizionare), cu `MutareSantierNecesar` ca istoric / corectură, iar compartimentele firmei se administrează în Administrare → Compartimente.

Module existente: Audit (jurnalul de modificări, D-36: scris de `InterceptorAudit`, citit în `/jurnal`), Identitate (+ server de autentificare), Proiecte, Parteneri (furnizori, clienți și subantreprenori), Devize (+ Import.Isdp: parserul pur al exporturilor ISDP; Import.Isdp.Excel: `CititorExcel` + importatorul `ImportatorIsdp` / `ConversieIsdp`, D-50 — devizul cu resurse .xls + F3 .xlsx opțional, prin `IImportatorDevizCuAnexe`; endpointurile `EndpointuriImportDeviz`: previzualizare cu recapitulația fișier vs. recalcul, extrasul de resurse cu starea în nomenclator și diferențele la reimport, confirmare cu cheile stabile păstrate (`PotrivireReimport`) și fișierele originale arhivate în `devize.fisiere_sursa`; modelul: capitole arborescente, 5 resurse, cost direct + preț de ofertă, rotunjirea `Fara`; lucrul pe versiune, D-53 (`EndpointuriEditareDeviz`): ciorna se redenumește / șterge / editează pe articole, „Modifică” = `CopieCiorna` (aceleași chei), aprobarea înlocuiește versiunea anterioară (`InlocuireVersiuneDeviz`, corectură), PDF prin `IServiciuDocumente` și Excel (`ExportDeviz`); aprobarea cere `ofertare.aprobare`; resursele fiecărui articol ca rânduri sub el (`GET …/{versiuneId}/resurse-articole`); catalogul de prețuri din devize și compararea între devize, D-54 (`CatalogPreturi`, `EndpointuriCatalogPreturi`, `/api/devize/catalog/*`, tab-urile din Ofertare și devize); legarea resurselor de ERP în tab-ul „Nomenclator resurse” (material din stoc cu `FactorConversie`, D-55); prețurile din recepții (`ISursaPreturi` ← Stocuri) și bugetul proiectului (`ISursaCosturi` ← Stocuri / Facturi, `CalculBuget`, `EndpointuriBuget`, tab-ul „Buget” din fișa proiectului); „Prețuri de achiziție” în deviz: diferențele deviz / recepții și aplicarea lor în ciorna de lucru (`ActualizarePreturi`, `EndpointuriActualizarePreturi`); nomenclatorul de resurse al firmei `ResursaCatalog`, REQ-IMP-04: la confirmarea importului `CatalogResurse.Potriveste` leagă consumurile de resurse și creează codurile necunoscute nemapate, `/api/devize/resurse` pentru listă / mapare / propuneri; articolele de stoc prin contractul `IArticoleStocPentruMapare`, implementat de Stocuri), Aprovizionare (necesare de materiale cu aprobare, note de comandă către furnizori, avize de însoțire a mărfii, import din scanări / Word), Contracte (contractele proiectului cu subantreprenori / parteneri, D-41; plus calculul situațiilor și al ajustării), Grafic (+ Import.Primavera, Import.Excel), Facturi (facturi primite: import e-Factura XML / ZIP SPV și PDF, înregistrare pe proiect), Stocuri (NIR, transfer / retur, consum pe articole de deviz, CMP), Formulare (+ Infrastructure Word/PDF).

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
5. ~~Jurnalul de audit append-only (schema `audit`), scris din `InterceptorAudit`.~~ (D-36, modulul Audit, pagina `/jurnal`.)
6. Modulul Documente (arhivă de fișiere: SHA-256, imuabil), folosit de importul de devize (`FisierSursaId`) și de formulare.
7. ~~**Importatorul de devize:** un deviz real importat și recalculat identic (poarta fazei 0).~~ Formatul real e ISDP, nu WinDoc (D-50): `ImportatorIsdp`, verificat pe exporturile reale (`Sabloane/devize/`). Rămân: extrasul de resurse pe versiune pentru aprovizionare, apoi editorul de deviz (`docs/editor-deviz.md`, D-51).
8. Setările firmei: cote implicite pentru recapitulație, cote TVA cu dată de valabilitate, formate de import active.
9. Teste de arhitectură (NetArchTest sau similar) pentru regula 1.

Fazele 1–3: vezi `docs/cerinte.md`, coloana „Faza”.

## Întrebări deschise (nu presupune răspunsul; întreabă)

- Devizele ISDP: întrebările din `docs/import-isdp.md` §10 (alte formate de export, fișiere cu mai multe stadii, echipamente, moneda situațiilor, cursul ofertei, catalogul de norme) și `docs/editor-deviz.md` §10.
- Exemple reale de grafice Primavera (.xer) și Excel din documentații de atribuire, pentru definirea șablonului.
- Ce program contabil folosește pilotul (pentru primul exportator contabil)?
- Contabilitate și salarizare proprii sau integrare pe termen lung?
- Cloud (SaaS) sau on-premise ca model principal?
- REGES: situația juridică a HG 295/2025, anulată în primă instanță în aprilie 2026.
- Tratamentul exact al avansului (storno separat?) și baza garanției de bună execuție. De validat cu contabilul pilotului înainte de faza 1.
