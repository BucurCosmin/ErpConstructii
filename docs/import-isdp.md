# Importul devizelor ISDP (Intersoft)

Specificație pentru modulul Devize, scrisă pe baza a două exporturi reale: șanțul de investigații pentru utilități (chilleri), „UNITATEA 1 INVESTIGATII”. Toate regulile de mai jos au fost **verificate numeric pe aceste fișiere**: recalculul iese identic, iar prețurile din F3 se reproduc cu zero diferențe.

> **Corecție față de documentele anterioare:** programul de devizare este **ISDP de la Intersoft** (www.devize.ro), nu WinDoc. Importatorul se numește `isdp`. Vezi decizia D-18.

## 1. Rezumat

| Raport ISDP | Fișier | Ce conține | Rol în ERP |
|---|---|---|---|
| **Deviz ofertă cu resurse** („extras de resurse”) | `.xls` (BIFF) | Arbore WBS: stadiu fizic → capitole → articole (simbol normă) → resurse (cod, UM, cantitate, preț). Costuri directe în **lei**. Recapitulația: CAM, indirecte, profit, TVA. | **Sursa principală.** Din el se construiește versiunea de deviz, bugetul, necesarul de resurse. |
| **F3 – Lista cu cantități de lucrări** | `.xlsx` | Capitole și articole numerotate 1.1, 1.2…, cu UM, cantitate și **preț de ofertă** (cu indirecte și profit incluse) pe componente, în **EUR**. **Fără simboluri și fără resurse.** | **Secundar.** Verifică prețurile de ofertă și dă cursul. Ca listă de la beneficiar (fără prețuri), pornește o ofertă. |

Concluzii care schimbă modelul ERP (detalii în §6):

1. Devizul are **mai multe niveluri de grupare** decât obiect → categorie: obiectiv → obiect → stadiu fizic → capitole. Modelul trebuie să devină **arborescent** (capitole pe oricâte niveluri, cu cod WBS).
2. Pe fiecare articol trebuie păstrate **două prețuri**:
   - **costul direct** în lei, pe resurse, din deviz: alimentează bugetul și controlul costurilor;
   - **prețul de ofertă** în moneda ofertei, din F3: alimentează situațiile de lucrări și facturarea.
3. ISDP are o a **cincea resursă: Echipamente**.
4. ISDP **nu rotunjește** nimic pe parcurs. Precizia trebuie păstrată, iar rotunjirea se face doar la afișare și pe documentele fiscale.
5. Cantitatea unei resurse din export este **totală pe articol**. Consumul unitar se calculează: cantitate resursă / cantitate articol.

## 2. Formatul „Deviz ofertă cu resurse” (.xls)

### 2.1 Zone

| Zonă | Cum se recunoaște | Conținut |
|---|---|---|
| Antet | etichete `OBIECTIV:`, `OBIECTUL:`, `STADIUL FIZIC:`, `Beneficiar:`, `Proiectant:`, `Executant:` | Valoarea este prima celulă nevidă la dreapta. `______` = necompletat. |
| Titlu | text care începe cu `Deviz Oferta` | ex. „Deviz Oferta - SANT INVESTIGATII UTILITATI (CHILLERI)…” |
| Antet de coloane | rândul cu celulele `WBS` și `Simbol` | vezi 2.2; moneda din „- lei -” / „- euro -” |
| Rânduri de deviz | coloana WBS are formă ierarhică `0002.1.1.1.1` | vezi 2.3 |
| Recapitulație | etichete în coloana Denumire | vezi 2.5 |

O foaie = un stadiu fizic. Numele foii este stadiul, trunchiat la 31 de caractere. Un fișier cu mai multe foi se tratează ca mai multe devize.

### 2.2 Coloane

Coloanele se găsesc **după textul din antet, nu după literă**:

| Antet (normalizat) | Sens |
|---|---|
| `wbs` | cod ierarhic |
| `simbol` | simbolul normei (articol) sau codul resursei. La resurse este **indentat cu 3 spații**. |
| `denumire` | indentată după nivel (informativ) |
| `um`, `cantitate` | la resurse: cantitatea **totală** pe articol |
| `pret material` / `manopera` / `utilaj` / `transport` / `echipamente` | prețuri unitare pe componente |
| `pu` | preț unitar total |
| `valoare material` / `manopera` / `utilaj` / `transport` | valori = cantitate × preț. **Nu există „valoare echipamente”**: se deduce din Total − celelalte. |
| `total` | valoare totală |
| (coloane fără antet R–V) | repetă totalul și valorile pe componente **doar pe rândurile de articol**. Nu le folosim: clasificarea se face din arbore. |

### 2.3 Arborele WBS și tipurile de rânduri

- Părintele unui rând este WBS-ul fără ultimul segment (`0002.1.1.1.1` → `0002.1.1.1`). Rândurile al căror părinte nu există sunt rădăcini; aici rădăcina este stadiul fizic `0002.1.1`.
- Tipul rândului:

| Condiție | Tip | Exemplu |
|---|---|---|
| fără simbol | **Grup** (stadiu fizic, capitol) | `0002.1.1.1 SAPATURA SI EVACUARE` |
| cu simbol, părinte grup | **Articol** | `0002.1.1.1.1 TSA04G1 Sapatura manuala…` |
| cu simbol, părinte articol | **Resursă** | `0002.1.1.1.1.6 19621 Sapator categoria a II-a` |

- Rădăcina (stadiul) are UM `buc`, cantitate 1 și totalurile stadiului. Ele se folosesc la verificare.

### 2.4 Cazuri speciale găsite în fișier

| Caz | Exemplu | Tratament |
|---|---|---|
| Articol **asimilat** | `TSF01A1 asim` | `TipArticol = Asimilat`, `SimbolBaza = TSF01A1` |
| **Resursă introdusă direct** ca articol, fără descompunere | `20011961 Cherestea rasinoase`, 5 mc × 1.500 lei | `TipArticol = ResursaDirecta`. În extras devine o resursă cu consum unitar 1. |
| Resursă cu **același cod ca articolul** | articolul `TRA01A20P` are resursa `TRA01A20P` (transport 200 lei/t) | normal: resursa de transport a normei |
| Resursă cu **toate prețurile 0** | `6202806 Apa industriala`, 57,747 mc | Tipul se deduce din UM (`ora` → manoperă, altfel material), cu avertisment. Se confirmă la maparea în nomenclator. |
| Resurse cu **cantitate 0** | `2917685 Dulap brad` sub `TSF01C1` | se păstrează (fac parte din normă) |
| Consumuri foarte mici | `29931`: 0,000024 ore | precizie de cel puțin 8 zecimale la consumuri |
| Valori cu zgomot binar | `76.58888999999999` | Conversia double → decimal păstrează 15 cifre semnificative → `76.58889` |
| Aceeași resursă în mai multe articole | `19621 Sapator` în 3 articole | agregată în extras; prețuri sau UM diferite sunt semnalate |

### 2.5 Recapitulația și regula de calcul (verificată: diferență 0)

| Etichetă în fișier | Citire | Valoare în exemplu |
|---|---|---|
| `Total Valoare Directa:` (T0) | M / Mn / U / T în coloanele de preț, total = ultimul număr | 462.673,86 |
| `CAM` | procentul în coloana Cantitate | 2,25% → 10.181,68 |
| `Total inclusiv Cheltuieli directe :` (T1) | | 472.855,54 |
| `Cheltuieli indirecte` | procent | 7% → 33.099,89 |
| `Total inclusiv Cheltuieli indirecte :` (T2) | | 505.955,43 |
| `Profit` | procent | 3% → 15.178,66 |
| `Total inclusiv Beneficiu :` (T3) | | 521.134,09 |
| `Cota TVA:` / `Valoare TVA:` / `Valoare cu TVA:` | ultimul număr din rând | 21% / 109.438,16 / 630.572,25 |

```
T0 = Σ valori articole (pe componente)
CAM = T0.manoperă × %CAM            → se adaugă la manoperă
T1 = T0 + CAM
Indirecte = T1 × %ind               → pe fiecare componentă
T2 = T1 + Indirecte
Profit = T2 × %profit               → pe fiecare componentă
T3 = T2 + Profit
TVA = T3 × cota;  Total = T3 + TVA
```

**Fără rotunjiri intermediare.** Pentru versiunile din ISDP, calculul ERP trebuie să ruleze cu rotunjirea „Fara” pe linie și pe recapitulație. Altfel totalurile nu ies identic.

## 3. Formatul F3 (.xlsx)

- Antet ca la deviz, titlu `F3 - LISTA cu cantitati de lucrari…`, moneda din `- euro -`.
- Coloane, găsite după text: `Nr.`, `Capitolul de lucrari`, `U.M.`, `Cantitatea`, `Pretul unitar…`, `TOTALUL…`.
- Rânduri:
  - capitol: `1` + denumire;
  - articol: `1.1` + denumire, UM, cantitate, preț, total;
  - sub articol: `material:` / `manopera:` / `utilaj:` / `transport:`, cu prețul unitar și totalul componentei;
  - `TOTAL GENERAL (fara TVA)`.
- La fiecare pagină se repetă rândurile `Pag N`, `STADIUL FIZIC: …` și numerotarea coloanelor `0 | 1 | 2 | 3 | 4 | 5 = 3 x 4`. Parserul le ignoră.
- Numerele de articol sunt **text** („1.1”). Dacă vreodată vin ca numere, „1.10” devine „1.1”: de verificat la alte exporturi.

### 3.1 Prețul de ofertă (verificat: 60 de componente din 60 identice)

```
factor manoperă       = (1 + %CAM) × (1 + %ind) × (1 + %profit)
factor celelalte      = (1 + %ind) × (1 + %profit)
preț unitar comp.     = rotunjit₂( preț unitar lei × factor / curs )
total componentă      = rotunjit₂( valoare lei × factor / curs )     ← NU cantitate × preț rotunjit
preț/total articol    = suma componentelor rotunjite
rotunjire: jumătate departe de zero
```

- **Cursul** din exemplu este **5,23 lei/EUR**. Se deduce automat: valoarea de ofertă în lei / totalul general F3, rotunjit la 4 zecimale. Poate fi dat și explicit.
- Factorul pentru **echipamente** nu a putut fi verificat, pentru că în exemplu valoarea e zero. Presupunem că e ca la material.
- **F3 nu poate fi importat singur ca deviz**: nu are simbolurile normelor și nici resursele. Fără resurse nu există necesar de materiale, ore normate și nici control al costurilor.

### 3.2 Legarea F3 ↔ deviz

Legarea se face pozițional. Articolul `c.a` din F3 este al `a`-lea articol din al `c`-lea capitol, adică din grupurile de sub stadiul fizic, în ordinea WBS. Exemplu: `F3 1.1` ↔ `0002.1.1.1.1 TSA04G1`, `F3 3.1` ↔ `0002.1.1.3.1 TSF01A1 asim`. Pe fiecare pereche se verifică UM, cantitatea și denumirea. Orice nepotrivire înseamnă că rapoartele nu sunt din aceeași versiune.

## 4. Ce s-a verificat pe date

| Verificare | Rezultat |
|---|---|
| Articole / capitole / resurse distincte | 15 / 3 / 23 |
| Valoare articol = cantitate × preț unitar, pe fiecare componentă | OK pe toate |
| Valoare articol = Σ valori resurse | OK pe toate |
| Total stadiu = Σ articole | OK |
| T0…T3, TVA, total cu TVA recalculate | identice cu fișierul |
| Prețurile F3 reproduse din deviz (curs 5,23) | 60/60 componente identice; total F3 99.643,23 EUR |
| Extras agregat = valoarea directă | 462.673,86 lei |

## 5. Codul livrat

| Proiect | Stare | Conținut |
|---|---|---|
| `ErpConstructii.Devize.Import.Isdp` | compilat, **27 de teste verzi pe datele reale** | `FoaieCalcul` (grilă abstractă), `ParserDevizResurse`, `ParserListaF3`, `VerificatorDeviz`, `PretOferta`, `ReconciliereF3`, `ExtrasResurse`, `ExtractorNorme` (pentru biblioteca de norme, vezi `editor-deviz.md`), `DetectorIsdp`. Fără pachete NuGet și **fără dependențe de restul soluției**. |
| `ErpConstructii.Devize.Import.Isdp.Excel` | **necompilat** (ExcelDataReader) | `CititorExcel`: .xls/.xlsx → `FoaieCalcul` |
| `tests/ErpConstructii.Teste.Isdp` + `tests/samples/isdp/*.json` | verde | Fixture-uri JSON = cele două fișiere reale, convertite celulă cu celulă |

Parserul produce un model **fidel formatului** (`DevizIsdp`, `NodIsdp`, `ListaF3`), nu modelul ERP. Maparea la modelul canonic se scrie în importatorul modulului Devize (§7), pe codul actual al aplicației.

## 6. Schimbări necesare în modelul ERP

Le adaptezi la codul existent; numele de mai jos sunt orientative.

### 6.1 Structura devizului: arbore

```
VersiuneDeviz
 └─ CapitolDeviz (ParinteId?, CodWbs, Denumire, Tip: Obiect | StadiuFizic | Capitol, Ordine)   ← recursiv
     └─ ArticolDeviz
         └─ ConsumNormat
```

Înlocuiește nivelurile fixe Obiect → CategorieLucrari. Obiectivul și obiectul din antet se păstrează pe versiune, sau ca noduri de nivel superior dacă un import conține mai multe stadii fizice. **Toate nivelurile au `firma_id`** (RLS).

### 6.2 Câmpuri noi

| Entitate | Câmp | De ce |
|---|---|---|
| VersiuneDeviz | `Moneda`, `Curs` (lei / unitate) | oferta în EUR, costurile în lei |
| | `ProcentCam`, `ProcentIndirecte`, `ProcentProfit`, `CotaTva`. ISDP le dă în procente; în ERP ca fracții sau procente, dar consecvent. | recapitulație, preț de ofertă |
| | `PoliticaRotunjire` (`Fara` pentru ISDP) | totaluri identice cu sursa |
| | `Obiectiv`, `Obiect`, `StadiuFizic` (din antet) | afișare, potrivire la reimport |
| | totalurile declarate (T0–T3, TVA) ca JSON | audit: ce a spus fișierul |
| ArticolDeviz | `CodWbsSursa` | potrivirea la reimport și cu F3 |
| | `TipArticol` (Normat / Asimilat / ResursaDirecta), `SimbolBaza` | |
| | `PretUnitarEchipament` (a 5-a componentă) | ISDP are echipamente |
| | `PretOferta*` pe componente (unitar + total, în moneda ofertei) | **situațiile și facturile folosesc prețul de ofertă/contract** |
| ConsumNormat | `CantitateTotala` (din sursă) + `ConsumUnitar` (calculat) | aprovizionarea lucrează cu necesarul total |
| | `TipDedus` | resursele fără preț |
| | `ResursaCatalogId?` | maparea în nomenclatorul firmei (§6.3) |

**Precizie** în PostgreSQL:
- prețuri și valori: `numeric(19,6)`;
- cantități: `numeric(19,6)`;
- consumuri unitare: `numeric(24,12)`.

Rotunjirea la 2 zecimale se face doar la afișare, pe formularele de ofertă și pe documentele fiscale.

### 6.3 Nomenclatorul de resurse al firmei

`ResursaCatalog` are: FirmaId, Sursa (`isdp`), Cod (ex. `2917685`), Denumire, UM, Tip (5 tipuri) și maparea opțională spre un articol de stoc, o meserie și categorie, un utilaj sau un serviciu de transport.

- La primul import, codurile necunoscute se creează ca **nemapate**. Utilizatorul le leagă de nomenclatorul ERP o singură dată, iar la importurile următoare maparea e automată (REQ-IMP-04).
- Codurile ISDP: materialele au 7–8 cifre, iar meseriile și categoriile au coduri scurte (`19621` = săpător cat. II). Pentru transport și utilaj, codul poate fi chiar simbolul normei (`TRA01A20P`).

## 7. Fluxul de import în ERP

1. **Încărcare**: deviz cu resurse (obligatoriu), plus F3 (opțional), pentru un proiect.
2. **Citire**: `CititorExcel` → `FoaieCalcul`. `DetectorIsdp` stabilește tipul fiecărei foi.
3. **Parsare**: `ParserDevizResurse` (câte o foaie pentru fiecare stadiu fizic) și `ParserListaF3`.
4. **Verificare**: `VerificatorDeviz`, adică articole, resurse și recapitulație. Erorile blochează confirmarea.
5. **Reconciliere F3**, dacă există: `ReconciliereF3` dă cursul și prețurile de ofertă pe articol. Fără F3, prețul de ofertă se calculează cu `PretOferta` la cursul introdus de utilizator.
6. **Mapare resurse** în nomenclator: cele nemapate se afișează, dar nu blochează importul.
7. **Previzualizare**: arborele, recapitulația (fișier vs. recalcul), problemele, extrasul de resurse, iar la reimport diferențele față de versiunea existentă.
8. **Confirmare**: se creează `VersiuneDeviz` (Ofertă, ciornă), cu fișierele originale arhivate (SHA-256) și jurnalul importului.

Maparea `DevizIsdp` → model ERP:

| ISDP | ERP |
|---|---|
| antet (obiectiv, obiect, stadiu), moneda, recapitulația | `VersiuneDeviz` |
| `NodIsdp` Grup | `CapitolDeviz` (stadiul fizic = nodul rădăcină) |
| `NodIsdp` Articol | `ArticolDeviz` (Simbol, SimbolBaza, TipArticol, UM, Cantitate, PretUnitar pe 5 componente, CodWbsSursa) + preț de ofertă din reconciliere |
| `NodIsdp` Resursă | `ConsumNormat` (CodResursa, Denumire, Tip, UM, CantitateTotala, ConsumUnitar, PretUnitar, TipDedus) |
| articol `ResursaDirecta` | `ArticolDeviz` + un `ConsumNormat` cu codul = simbolul, consum 1 |

### 7.1 Reimport (o versiune nouă din ISDP)

Cheia de potrivire a articolului este `(CodWbsSursa, Simbol)`. Dacă WBS-ul s-a schimbat prin renumerotare, se folosește `(Simbol, Denumire normalizată, UM)`. Articolele potrivite **păstrează `CheieStabila`**, ca legăturile din grafic, din comenzi și din cantitățile executate să nu se rupă.

Previzualizarea arată:
- articole noi, eliminate, cu cantitate schimbată, cu preț schimbat;
- resurse schimbate.

## 8. Legătura cu celelalte module

Aici se văd consecințele modelului: totul pleacă de la **articol** (prin `CheieStabila`) și de la **resursă** (prin `ResursaCatalogId`).

| Modul | Ce ia din deviz | Calcul |
|---|---|---|
| **Buget de execuție** | costul direct pe articol × resursă | versiunea „Buget” se copiază din ofertă și se poate ajusta |
| **Aprovizionare** | extrasul de resurse de tip Material: cod, cantitate totală, articolele care o folosesc | necesar = Σ consum unitar × cantitate articol. Distribuit în timp prin grafic (activitate ↔ articol). Linia de referat sau comandă = (proiect, articol, resursă, cantitate) → **necesar vs. angajat vs. recepționat** |
| **Gestiuni** (bon de consum) | consumul normat pe articol | consum real (bonuri pe articol + resursă) vs. normat = cantitate executată × consum unitar |
| **Execuție / pontaj** | orele normate pe meserie (resurse Manoperă) | ore pontate pe articol și meserie vs. ore normate = cantitate executată × consum unitar → productivitate |
| **Utilaje** | orele normate de utilaj | ore raportate vs. normate |
| **Grafic de execuție** | articolele (CheieStabila), valorile pentru ponderare | legături activitate ↔ articol; progres ponderat valoric |
| **Situații de lucrări** | **prețul de ofertă / contract** pe articol | cantitate executată × preț de ofertă, în moneda contractului |
| **Control costuri** | costul direct (buget) vs. costul real (bonuri, pontaj, facturi) | pe articol × resursă |

## 9. Validări (în parser și verificator)

| Regulă | Severitate |
|---|---|
| lipsește antetul WBS/Simbol, sau nu există niciun rând WBS | Eroare (fișierul nu e deviz cu resurse) |
| WBS duplicat; resursă sub resursă; rând fără simbol sub articol | Eroare |
| cantitate articol ≤ 0; lipsă UM | Eroare |
| valoare ≠ cantitate × preț; articol ≠ Σ resurse; T0–T3 / TVA ≠ recalcul (toleranță 0,005 lei) | Eroare |
| resursă fără niciun preț (tip dedus); resursă cu preț pe mai multe coloane | Avertisment |
| aceeași resursă cu prețuri diferite în articole diferite | Avertisment |
| aceeași resursă cu UM diferite | Eroare |
| F3: total articol ≠ Σ componente; total general ≠ Σ articole | Eroare |
| F3 ↔ deviz: număr diferit de capitole sau articole; UM sau cantitate diferite | Eroare |
| F3 ↔ deviz: denumiri diferite | Avertisment |
| F3 ↔ deviz: preț diferit cu cel mult 1 ban | Avertisment; peste → Eroare |

## 10. Întrebări deschise

1. **Alte formate de export ISDP.** Exportul .xls de raport e fragil (formatarea se poate schimba între versiuni). Dacă ISDP exportă și XML sau fișierul de proiect nativ, merită un importator pe acela. De întrebat la Intersoft.
2. **Rapoartele exportate de rutină.** Exportați mereu „Deviz ofertă cu resurse”? Există un „Extras de resurse” agregat separat? Îl putem calcula noi, deci nu e necesar.
3. **Fișierele cu mai multe stadii fizice**, adică mai multe foi: avem un exemplu?
4. **Echipamentele**: un deviz cu valori la echipamente, ca să verificăm factorul de preț de ofertă și coloana de valoare.
5. **Moneda situațiilor**: contractele sunt în EUR? Facturarea se face în lei la cursul de la data facturii? De asta depinde modulul Situații și Facturi.
6. **Cursul ofertei** (5,23): îl stabilește ofertantul sau e BNR la o dată? Îl stocăm pe versiune.
7. **Indicatorul de norme**: păstrăm local catalogul de norme (simbol → denumire, consumuri) sau doar ce vine în devize?
