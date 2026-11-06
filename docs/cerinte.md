# Cerințe — ERP pentru construcții

Sursa: documentul de proiect „ERP pentru construcții — schiță de proiect” (claude.ai). Fiecare cerință are un ID stabil, folosit în comentariile din cod și în teste. **Faza** arată când se livrează (vezi roadmap-ul de la final).

Legendă stare: ✅ implementat (cod + teste) · 🟡 schelet / parțial · ⬜ neînceput.

## GEN — Principii generale

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-GEN-01 | Proiectul (șantierul) este axa costurilor: orice document de cost sau venit are obligatoriu un proiect. | 0 | 🟡 |
| REQ-GEN-02 | Articolul de deviz este entitatea pivot: buget, consumuri, pontaj, cantități executate, situații și facturi se leagă de el. | 0 | 🟡 |
| REQ-GEN-03 | Multi-firmă din prima zi: `firma_id` pe fiecare tabel de business, izolare prin Row-Level Security în PostgreSQL. | 0 | 🟡 |
| REQ-GEN-04 | Toate sumele sunt `decimal`, cu rotunjire explicită pe fiecare nivel; cotele legale (TVA, contribuții) nu se hardcodează. | 0 | ✅ |
| REQ-GEN-05 | Audit complet pe documentele financiare: cine, când, valori vechi/noi; jurnal append-only. | 0 | 🟡 |
| REQ-GEN-06 | Documentele aprobate sau facturate sunt needitabile; corecția prin storno sau versiune nouă. | 0 | ✅ |

## UTL / DRP — Utilizatori și drepturi

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-UTL-01 | Tipuri de utilizatori: administrator sistem, administrator firmă, intern (birou), șantier (mobil), auditor, cont de serviciu. Fără conturi pentru externi (subantreprenori, beneficiar/diriginte) în această etapă. | 0 | ✅ |
| REQ-UTL-02 | Autentificare: ASP.NET Core Identity + OpenIddict; access token 5–15 min, refresh token rotit; MFA (TOTP) obligatoriu pentru administratori și rolurile financiare. | 0 | ✅ |
| REQ-UTL-03 | Mobil: dispozitiv înregistrat și aprobat, PIN/biometrie, date offline criptate (SQLCipher), revocare la distanță cu ștergerea datelor locale. | 2 | ⬜ |
| REQ-UTL-04 | Auditor: cont doar-citire cu dată de expirare. | 1 | ✅ |
| REQ-UTL-05 | Firme mari: SSO (Entra ID / LDAP) și provizionare SCIM. | ulterior | ⬜ |
| REQ-DRP-01 | Permisiune = resursă + acțiune (vizualizare, creare, editare, anulare, aprobare, export). Fiecare endpoint de business cere o permisiune. | 0 | ✅ |
| REQ-DRP-02 | Roluri predefinite (matricea rol × modul din document), copiate în firmă și ajustabile de administratorul firmei. | 0 | ✅ |
| REQ-DRP-03 | Atribuire = utilizator + rol + domeniu (firmă / proiect / gestiune) + valabilitate. | 0 | ✅ |
| REQ-DRP-04 | Câmpuri sensibile (prețuri de cost și marje, salarii, date personale) cu permisiuni proprii; fără permisiune, API-ul le trimite mascate. | 1 | 🟡 |
| REQ-DRP-05 | Praguri valorice la aprobare, configurabile per firmă (ex. șef de șantier sub o limită, peste → manager de proiect). | 2 | ⬜ |
| REQ-DRP-06 | Separarea atribuțiilor: cine creează o comandă, plată sau situație nu o poate aproba. | 1 | ✅ (regula) |
| REQ-DRP-07 | Starea documentului bate rolul: aprobat/facturat = needitabil pentru oricine. | 0 | ✅ |
| REQ-DRP-08 | Izolarea firmelor impusă de PostgreSQL (RLS); rolul aplicației nu are BYPASSRLS și nu e proprietar pe tabele. | 0 | 🟡 |
| REQ-DRP-09 | Permisiunile efective se calculează la login, se țin în cache, cache invalidat la schimbarea atribuirilor. | 0 | ✅ |

## DEV / IMP — Devize și import

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-DEV-01 | Structura: versiune → obiect → categorie de lucrări → articol → consumuri normate pe 4 resurse (material, manoperă, utilaj, transport). | 0 | ✅ |
| REQ-DEV-02 | Recapitulația devizului-ofertă: directe pe resurse, contribuții pe manoperă, indirecte %, profit %, TVA. | 0 | ✅ |
| REQ-DEV-03 | Versiuni de deviz: ofertă, contract, buget de execuție, act adițional; comparare între versiuni. | 1 | 🟡 |
| REQ-DEV-04 | Formulare de ofertă pentru licitații (liste de cantități, deviz-ofertă pe categorii, liste de consumuri), din șabloane editabile. | 1 | ⬜ |
| REQ-DEV-05 | Articolul se urmărește între versiuni printr-o cheie stabilă. | 1 | 🟡 |
| REQ-DEV-06 | Versiunea aprobată este imuabilă; orice modificare creează o versiune nouă legată de cea anterioară. | 0 | ✅ |
| REQ-IMP-01 | Fiecare format de deviz este un importator separat (plugin); nucleul nu cunoaște formatele. Primul: **WinDoc**. | 0 | 🟡 |
| REQ-IMP-02 | Model canonic comun (`DevizCanonic`), folosit și la export. | 0 | ✅ |
| REQ-IMP-03 | Pașii importului: detectare → citire → validare (totaluri recalculate vs. declarate, UM, prețuri, duplicate) → mapare nomenclatoare → previzualizare cu diferențe la reimport → confirmare ca versiune nouă, cu fișierul original arhivat. | 0 | 🟡 |
| REQ-IMP-04 | Maparea codurilor de resurse la articole de stoc / meserii / utilaje, memorată per firmă și per sursă. | 1 | ⬜ |
| REQ-IMP-05 | Fiecare importator are fișiere de test și totaluri așteptate; recalculul trebuie să iasă la ban. | 0 | ⬜ |
| REQ-IMP-06 | Importator Excel generic cu șablon și mapare de coloane salvată. | 1 | ⬜ |

## CTR / SIT — Contracte și situații de lucrări

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-CTR-01 | Contract cu beneficiarul: valoare, termene, avans, garanție de bună execuție (reținere sau scrisoare), penalități. | 1 | ⬜ |
| REQ-CTR-02 | Acte adiționale cu deviz propriu (lucrări suplimentare / renunțări). | 1 | ⬜ |
| REQ-CTR-03 | Formulă de ajustare a prețului pe indici INS, per contract (parte fixă + ponderi); înghețată după aprobare. | 1 | ✅ (calcul) |
| REQ-CTR-04 | Contracte cu subantreprenorii, legate de categoriile subcontractate. | 2 | ⬜ |
| REQ-SIT-01 | Situație lunară: cantități pe articole, cumulat, rest de executat; atașamente și PV de lucrări ascunse. | 1 | ⬜ |
| REQ-SIT-02 | Aprobarea dirigintelui / beneficiarului în afara sistemului (PDF semnat), înregistrată de managerul de proiect cu documentul atașat. | 1 | ⬜ |
| REQ-SIT-03 | Calcul: valoare, ajustare, recuperare avans, reținere garanție, TVA sau taxare inversă, de încasat. | 1 | ✅ (calcul) |
| REQ-SIT-04 | Situațiile subantreprenorilor introduse intern și verificate față de contractul lor. | 2 | ⬜ |

## GRF — Grafic de execuție

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-GRF-01 | Import grafic: **Primavera P6** (.xer, PMXML) și **Excel** cu șablon primele; MS Project la cerere. Model canonic `GraficCanonic`. | 1 | 🟡 |
| REQ-GRF-02 | Legarea activităților de articolele de deviz (N:M, cu cantități alocate); propunere automată după coduri, corecții memorate la reimport. | 1 | 🟡 |
| REQ-GRF-03 | Actualizare la data de raportare: progres ponderat valoric din cantitățile raportate, date reale, prognoză (ritm sau plan) cu recalcul pe drumul critic, aprobare de managerul de proiect ca „Actualizarea nr. N”. | 2 | ✅ (calcul) |
| REQ-GRF-04 | Linia de bază aprobată la contract rămâne înghețată; rebugetarea creează o linie nouă. | 1 | ⬜ |
| REQ-GRF-05 | Export al graficului actualizat în XER / PMXML și PDF / Excel pentru beneficiar. | 2 | ⬜ |
| REQ-GRF-06 | Graficele Excel fără dependențe: prognoză liniară, fără drum critic (limită comunicată utilizatorului). | 1 | ⬜ |

## SNT — Execuție șantier (aplicația mobilă)

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-SNT-01 | Jurnal zilnic: cantități executate pe articole, vreme, efectiv, incidente, poze geolocalizate. | 2 | ⬜ |
| REQ-SNT-02 | Pontaj pe echipă și pe articol de deviz (alimentează costul de manoperă și salarizarea). | 2 | ⬜ |
| REQ-SNT-03 | Bonuri de consum pe articol; cereri de materiale către aprovizionare. | 2 | ⬜ |
| REQ-SNT-04 | Ore de funcționare utilaje, consum de combustibil. | 2 | ⬜ |
| REQ-SNT-05 | Funcționare offline, sincronizare la reconectare. | 2 | ⬜ |

## APV — Aprovizionare

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-APV-01 | Necesarele de materiale (cererile șantierului, NM/AAAA/NNNN) sunt emise pe proiect, fără furnizor; numărul și data se generează automat, lucrarea se preia din proiect, iar compartimentul / șantierul se introduce manual. | 1 | ✅ |
| REQ-APV-02 | Necesarele emise au status și intră în lista de aprobare; aprobarea cere permisiunea `aprovizionare.aprobare`, emitentul nu își poate aproba propriul necesar, iar aprobatorul și momentul aprobării se înregistrează. | 1 | ✅ |
| REQ-APV-03 | Necesarul se poate genera în Word pentru arhivare indiferent de status, folosind versiunea șablonului de la emitere și numele emitentului capturat la emitere. | 1 | ✅ |
| REQ-APV-04 | Necesarele din proiectele accesibile se pot filtra după proiect/lucrare, material, interval de dată, status, număr, emitent și aprobator. | 1 | ✅ |
| REQ-APV-05 | Șablonul „Necesar materiale” acceptă placeholderele documentate și le mapează la controalele Word ale aplicației. | 1 | ✅ |
| REQ-APV-06 | Notele de comandă către furnizori (formularul GC 007: furnizor, lucrare, materiale cu UM / cantitate / termen) au număr de sistem NC/AAAA/NNNN; cele emise pe hârtie se importă (PDF / scanare / Word) cu numărul original păstrat și originalul arhivat. Furnizorul se caută în nomenclator după CUI sau denumire și se adaugă dacă lipsește. | 1 | ✅ (import; emiterea din sistem rămâne) |
| REQ-APV-07 | Dosarele scanate (factură + e-Factura + notă de comandă + aviz + certificate în același PDF) se împart pe pagini: fiecare document se citește doar din paginile lui, iar din importul facturii se pot importa nota de comandă / necesarul din același fișier. | 1 | ✅ |
| REQ-APV-08 | Un fișier cu mai multe documente (mai multe facturi cu dosarele lor, note de comandă, necesare, bonuri de consum) se importă integral din „Import documente”: documentele propuse automat, corectabile (tip, unire, despărțire), fiecare importat separat cu originalul lui (doar paginile documentului). | 1 | ✅ (bonurile de consum: doar recunoscute) |
| REQ-APV-09 | Aplicație Windows de sine stătătoare pentru importul fișierelor cu mai multe documente (pornită din web sau direct): verificare pe rând lângă imaginea paginilor, import în aplicația principală prin API-ul ei, cu login-ul și drepturile utilizatorului. | 1 | ✅ |

## STC — Stocuri

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-STC-01 | Liniile facturilor primite se marchează „intră în stoc” (implicit toate; serviciile se debifează). Gestionarul recepționează factura într-un depozit (NIR/AAAA/NNNN): confirmă cantitățile primite (diferențele cer observație), leagă fiecare linie de un material din nomenclator (propus după alegerile anterioare pe furnizor) și materialele intră în stoc la prețul facturii. O singură recepție pe factură. | 2 | ✅ |
| REQ-STC-02 | Gestiuni: depozite configurabile (implicit „Depozit central”) și o gestiune „Materiale în șantier” pe proiect, creată la primul transfer. Bonul de transfer (BT/AAAA/NNNN) mută materiale din depozit în șantier la cost mediu ponderat; stocul nu devine negativ. | 2 | ✅ |
| REQ-STC-03 | Soldul pe gestiune (cantitate, CMP, valoare) și fișa de magazie; valorile se văd doar cu dreptul „prețuri de cost”. | 2 | ✅ |
| REQ-STC-04 | Bonul de consum (BC/AAAA/NNNN): materialele din șantier se consumă la CMP pe articolele devizului de referință (referite prin cheia stabilă) sau pe cheltuieli generale de șantier; returul nefolosit în depozit (bon de transfer în sens invers). | 2 | ✅ |
| REQ-STC-05 | Raportul consum real de materiale pe articol față de bugetul de materiale din deviz (cantitate × preț material), cu articolele neîncepute și consumurile fără articol separat. | 2 | ✅ |
| REQ-STC-06 | Inventarul unei gestiuni (INV/AAAA/NNNN): stocul scriptic preluat la deschidere, gestiunea blocată pentru alte mișcări până la finalizare, numărătoarea comisiei, finalizarea cu drept de aprobare: plusuri la CMP (sau la prețul dat, dacă materialul nu are CMP), minusuri la CMP. | 2 | ✅ |
| REQ-STC-07 | Stornarea NIR-ului (document separat): materialele ies la valoarea de intrare, doar dacă sunt încă în gestiune; după storno factura neanulată se poate recepționa din nou; NIR-urile facturilor anulate sunt semnalate. | 2 | ✅ |
| REQ-STC-08 | e-Transport pentru transferurile de materiale cu risc fiscal; proces-verbal de inventar tipărit din șablonul firmei. | 2 | ⬜ |
| REQ-STC-09 | NIR-ul (14-3-1A), bonul de consum (14-3-4A) și bonul de predare-transfer-restituire (14-3-3A) se descarcă în PDF: din șablonul Word al firmei, dacă e încărcat, altfel în formatul standard; prețurile apar doar cu dreptul „prețuri de cost”. | 2 | ✅ |

## FCT — Facturi

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-FCT-01 | Formatul intern al facturii este XML-ul UBL CIUS-RO; PDF, hârtie și exportul contabil sunt convertoare. | 1 | ⬜ |
| REQ-FCT-02 | Taxare inversă propusă automat din tipul lucrării și statutul de plătitor de TVA al clientului (serviciul ANAF). | 1 | 🟡 (contract) |
| REQ-FCT-03 | Validare locală (schemă UBL + reguli CIUS-RO), apoi trimitere automată în SPV; urmărirea stării (în prelucrare / validată / eroare). | 1 | 🟡 (contract) |
| REQ-FCT-04 | Termen: 5 zile lucrătoare de la emitere (facturile din 2026); alertă după 3 zile lucrătoare. | 1 | ✅ |
| REQ-FCT-05 | PDF pentru client din șablonul Word al firmei; arhivare XML + mesaj semnat ANAF + PDF. | 1 | ⬜ |
| REQ-FCT-06 | Surse de facturi emise: situații, avansuri, refacturări, vânzări de materiale, storno (cu referință la factura inițială). | 1 | ⬜ |
| REQ-FCT-07 | Descărcare automată periodică a facturilor primite din SPV; fără dublări. | 1 | 🟡 (import manual XML / ZIP din SPV; descărcarea automată rămâne) |
| REQ-FCT-08 | Facturi din afara e-Factura: PDF/scan atașat și introducere manuală. Din scanări se citește de preferință pagina e-Factura (vizualizarea SPV, același format la toți furnizorii), apoi factura tipărită de furnizor. | 1 | ✅ |
| REQ-FCT-09 | Potrivire în 3 puncte comandă–NIR–factură, cu toleranțe configurabile; diferențele merg la achizitor. | 2 | ✅ (regula) |
| REQ-FCT-10 | Alocare pe proiect și articol moștenită din comandă; reguli per furnizor pentru facturile fără comandă. | 2 | ⬜ |
| REQ-FCT-11 | Arhiva originală din SPV păstrată neschimbată. | 1 | ✅ |
| REQ-FCT-12 | Până la faza 3: export către programul contabil existent (exportatori ca module + export generic) și import al stării plăților. | 1 | ⬜ |

## FRM — Formulare și documente Word

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-FRM-01 | Catalogul de formulare din document (comandă furnizor, referat, NIR, bon de consum, aviz, PV-uri, dispoziție de șantier, foaie utilaj etc.). | 1 | 🟡 |
| REQ-FRM-02 | Catalog extensibil: un tip nou = schemă de câmpuri + șablon, fără cod nou. | 1 | ✅ |
| REQ-FRM-03 | Generare .docx din șabloanele firmei (controale de conținut etichetate, rânduri de tabel repetate), fără Word pe server. | 1 | ✅ |
| REQ-FRM-04 | Șabloane încărcate de administratorul firmei, verificate (câmpuri obligatorii) și versionate. | 1 | ✅ |
| REQ-FRM-05 | Proprietăți ascunse în fișierul generat: tip formular, versiune șablon, ID document, număr rezervat. | 1 | ✅ |
| REQ-FRM-06 | PDF gratuit: LibreOffice headless prin Gotenberg, serviciu separat. | 1 | ✅ |
| REQ-FRM-07 | Arhivare imuabilă cu SHA-256; corecție = versiune nouă; varianta semnată atașată lângă cea generată. | 1 | ⬜ |
| REQ-FRM-08 | Import al formularelor completate manual: recunoaștere tip/versiune, citirea câmpurilor, validare. | 1 | ✅ |
| REQ-FRM-09 | Potrivire la import: furnizor după CUI, proiect după cod, materiale după cod/denumire, cu propuneri. | 2 | 🟡 (proiect) |
| REQ-FRM-10 | Documentul importat intră ca ciornă „de verificat” și trece prin același flux de aprobare; originalul se arhivează. | 2 | 🟡 |
| REQ-FRM-11 | Documentele fără șablon: atașate ca fișier + introducere manuală. | 1 | 🟡 |
| REQ-FRM-12 | Numerotare pe serii (firmă, tip, an); numere rezervate pentru formulare goale, anulate la expirare. | 1 | 🟡 |

În implementarea curentă, necesarele de materiale folosesc șabloane Word versionate pe firmă și sunt păstrate cu status de aprobare; numărul, data și lucrarea sunt completate de sistem, iar compartimentul / șantierul se introduce manual. Șablonul „Necesar materiale” este normalizat la încărcare, iar facturile primite se importă ca ciorne „De verificat”, cu originalul păstrat și datele introduse manual; potrivirea automată și aprobarea facturilor rămân de completat.

## RPT — Rapoarte

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-RPT-01 | Grafic fizic actualizat, curba S, raport lunar de progres (PDF), lookahead 2–4 săptămâni, activități întârziate. | 2 | ⬜ |
| REQ-RPT-02 | Situații vs. grafic valoric; buget vs. angajat vs. realizat; EVM (CPI, SPI, EAC); marjă și WIP; cash-flow previzionat. | 2–3 | ⬜ |
| REQ-RPT-03 | Consum materiale normat vs. real; productivitate manoperă; utilizare utilaje. | 2 | ⬜ |
| REQ-RPT-04 | Livrare: dashboard, PDF cu layout fix, Excel, trimitere programată, view-uri pentru BI extern; respectă drepturile utilizatorului. | 2 | ⬜ |

## CNF — Conformitate România

| ID | Cerință | Faza | Stare |
|---|---|---|---|
| REQ-CNF-01 | e-Factura (B2B), SAF-T (D406), e-Transport pentru transferurile de materiale cu risc fiscal. | 1–3 | ⬜ |
| REQ-CNF-02 | Cote TVA configurabile cu dată de valabilitate (din 1.08.2025: 21% standard, 11% redusă). | 1 | ⬜ |
| REQ-CNF-03 | REGES-Online pentru contractele de muncă (de urmărit situația juridică a HG 295/2025). | 3 | ⬜ |
| REQ-CNF-04 | D112, salarizare cu facilitățile din construcții, parametrizate. | 3 | ⬜ |
| REQ-CNF-05 | GDPR: roluri, jurnal de acces, retenție. | 0 | 🟡 |

## Roadmap (din documentul de proiect)

| Fază | Conținut | Poarta de ieșire |
|---|---|---|
| 0 — Fundament (1–2 luni, estimare) | Client pilot, import WinDoc (plugin), model de date + calcul, autentificare, roluri, audit, multi-firmă | Un deviz real importat și recalculat identic |
| 1 — Proiect (4–6 luni) | Ofertare și versiuni, contracte, buget, import grafic, situații de lucrări, facturi + e-Factura, formulare Word | Pilotul emite situații lunare din sistem |
| 2 — Șantier (4–5 luni) | Aplicația mobilă, gestiuni, e-Transport, aprovizionare, pontaj, utilaje, progres grafic, cost real | Cost real pe articol, fără Excel paralel |
| 3 — Financiar (6+ luni) | Contabilitate, SAF-T, trezorerie, salarizare, REGES, mijloace fixe, EVM și BI | Al doilea client în producție |
