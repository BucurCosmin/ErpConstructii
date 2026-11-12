# Analize automate pe deviz: plan de implementare

> Document de plan, fără cod. Completează `import-isdp.md` și `editor-deviz.md`.
> Scop: algoritmi proprii, rulați în aplicație, care găsesc greșeli și riscuri într-un deviz și, unde se poate, propun corectura.

## Stadiul implementării

- Există nucleul pur `Devize/Analize`: contract și registru de analizori, constatări explicabile cu amprentă și un dicționar inițial de unități. În detaliile versiunii, „Verifică devizul” rulează la cerere A2 și A5 prin `POST /api/proiecte/{proiectId}/devize/{versiuneId}/analize/previzualizare`; cere `ofertare.vizualizare` și `date.preturi_cost` pe proiect. Rezultatul este experimental și nesalvat; nu modifică devizul și nu blochează aprobarea. Încă nu există stocarea rapoartelor sau aplicarea corecturilor.
- „Descarcă PDF” descarcă rezultatele, dovezile și limitările pe dispozitiv, fără a stoca raportul pe server. `POST .../analize/raport/pdf` cere aceleași drepturi și amprenta previzualizării; dacă datele afișate sau rezultatele s-au schimbat, întoarce 409 și verificarea trebuie rulată din nou.
- A2 acoperă deocamdată doar egalități volum–masă și lanțurile cu densități cunoscute; verificarea zecimalelor și celelalte perechi de dimensiuni lipsesc. A5 acoperă doar prețul zero și tariful orar uniform, fără partea statistică. Pragurile de precizie și recall nu au fost încă măsurate; implementarea nu este validată pentru producție.
- A2 compară și capitole din același stadiu fizic: în fixture-ul chilleri săpătura și umpluturile sunt în capitole diferite. Constatările sunt informative; suspiciunile chilleri nu sunt confirmate ca greșeli.
- Analiza va avertiza, nu va bloca aprobarea. Validarea familiilor și relațiilor, aplicarea pe F3 de la beneficiar și alegerea unui model local fără cost de licențiere rămân de stabilit.

## 1. Principii

1. **Analizorii sunt module** (ca importatorii, D-06). Fiecare analizor e independent, are un cod (ex. `A2.AMPRENTA`), o versiune și se poate activa sau dezactiva per firmă.
2. **Rulează local.** Nicio dată de deviz nu pleacă în afara serverului firmei. Modelele (embeddings, ML) rulează în proces, prin ONNX Runtime și ML.NET. Trimiterea către un AI extern cere acordul explicit al firmei și e în afara acestui plan.
3. **Constatările nu modifică devizul.** O corectură propusă devine operație de editor (`editor-deviz.md`) doar dacă utilizatorul o aplică, deci intră în jurnal și se poate anula.
4. **Fiecare constatare se explică.** Utilizatorul vede de ce: numerele implicate, regula, comparația. Nu există „scor magic” fără explicație.
5. **Calculele sunt deterministe.** La același deviz și același context, rezultatul e același. Simulările Monte Carlo folosesc o sămânță fixă salvată în raport.
6. **Pragurile sunt configurabile per firmă** și se calibrează din feedback (secțiunea 6).
7. **Ordine de valoare:** întâi algoritmii care merg de la primul deviz, fără istoric (A2, A3, A1, A4, A8). Cei statistici și de învățare (A5–A7, A9) vin pe măsură ce se adună date.

## 2. Model comun

### 2.1 Contextul unei analize

Ce primește fiecare analizor:

| Element | Sursă | Obligatoriu |
|---|---|---|
| Versiunea de deviz (arbore, articole, consumuri, prețuri, recapitulație) | modulul Devize | da |
| Dicționarul de unități de măsură (dimensiune, factori de conversie) | date de referință (2.4) | da |
| Dicționarul de familii de articole și relațiile dintre ele | date de referință (2.4) | pentru A1 |
| Biblioteca de norme (variante, amprente) | modulul Devize | pentru A4, A3b |
| Catalogul de prețuri cu istoric | modulul Devize / Aprovizionare | pentru A5 |
| Istoricul de devize ale firmei | modulul Devize | pentru A5–A7 |
| Graficul de execuție | modulul Grafic | pentru A10 |
| Realizări (normat vs. real) | Execuție / Contracte | pentru A9 |
| Indici INS de preț | date de referință, actualizate lunar | pentru A5 |
| Configurația firmei (praguri, analizori activi) | Setări | da |

Un analizor își declară dependențele. Dacă lipsește o dependență opțională, analizorul nu rulează și raportul spune de ce, de exemplu „A5 nu a rulat: sub 20 de observații de preț”.

### 2.2 Constatarea

| Câmp | Descriere |
|---|---|
| Cod regulă | ex. `A2.UNITATE_COPIATA`, stabil între versiuni |
| Analizor + versiune | pentru reproductibilitate |
| Severitate | `Eroare` (aproape sigur greșit), `Avertisment` (probabil greșit), `Info` (de verificat), `Risc` (corect, dar periculos comercial) |
| Încredere | 0–1; cât de sigur e algoritmul |
| Ținte | articole, resurse sau capitole implicate, prin `CheieStabila`, ca să supraviețuiască reimportului |
| Explicație | text generat din șablon, cu numerele concrete |
| Dovezi | valorile folosite: cantități, rapoarte, mediane, vecini |
| Impact estimat | diferența în lei la cost direct și la preț de ofertă, dacă se aplică corectura; poate lipsi |
| Corectură propusă | listă de operații de editor (ex. „cantitate articol X: 577,47 → 320,82”); poate lipsi |
| Amprentă | hash din cod + ținte + valori cheie; folosit ca să nu reapară o constatare deja marcată „justificată” |
| Stare | `Nouă`, `Confirmată` (greșeală reală), `Justificată` (corect, cu motiv), `Corectată`, `Ignorată` |

Constatările se ordonează după severitate × încredere × impact.

### 2.3 Raportul de analiză

- Se salvează per versiune de deviz, cu data, analizorii rulați, versiunile lor și pragurile folosite.
- Se poate compara cu raportul versiunii anterioare: constatări noi, rezolvate, rămase.
- Se exportă PDF (prin Gotenberg, ca formularele) și Excel.

### 2.4 Date de referință (de construit înainte de cod)

1. **Unități de măsură**: fiecare UM din ISDP are o dimensiune (volum, arie, lungime, masă, timp, bucată, procent, forfetar) și factor față de unitatea de bază (ex. `100 mc` → volum × 100, `kg` → masă × 0,001 t). Lista de pornire vine din UM-urile găsite în devizele importate.
2. **Factori de conversie plauzibili** între dimensiuni: densități pământ (1,6–2,0 t/mc), beton (2,4), oțel (7,85), afânare (1,1–1,4), grosimi tipice etc. Fiecare cu interval, nu valoare fixă.
3. **Familii de articole**: săpătură, umplutură, compactare, încărcare, transport, sprijinire, demontare sprijinire, cofraj, decofrare, armătură, turnare beton, epuismente etc. Articolul intră într-o familie după prefixul simbolului (ex. `TS*` terasamente) plus cuvinte cheie din denumire. Clasificarea se poate corecta manual și se reține per normă în bibliotecă.
4. **Relații între familii** (pentru A1), fiecare cu toleranță și severitate. Exemple:
   - `umplutură ≤ săpătură × afânare_max`
   - `compactare ≈ umplutură` (aceeași dimensiune)
   - `transport_t ≈ (săpătură − umplutură) × densitate`, dacă excedentul se transportă
   - `demontare_sprijiniri = sprijiniri`
   - `decofrare = cofraj`
   - `armătură_kg / beton_mc` în intervalul tipic al elementului
5. **Cine validează:** un deviziere al firmei (întrebare deschisă Q1). Fără validare, relațiile pornesc cu severitate `Info`.

## 3. Algoritmii

Pentru fiecare: scop, intrări, metodă, ieșiri, criterii de acceptare. Criteriile de recall și precizie se măsoară pe corpusul cu erori injectate (secțiunea 5).

### A1. Graf de constrângeri între cantități

- **Scop:** contradicții între cantități legate și cel mai mic set de corecturi care le rezolvă.
- **Intrări:** cantitățile articolelor, familiile lor, relațiile din 2.4, dimensiunile UM.
- **Metodă:**
  1. Construiește graful: noduri = cantități, muchii = relațiile aplicabile în același obiect sau capitol (nu peste tot devizul).
  2. **Propagare de intervale:** fiecare relație restrânge intervalul vecinilor; se repetă până la stabilizare. Un interval vid înseamnă contradicție.
  3. **Corecție minimă:** problemă liniară mixtă. Variabile: cantitatea nouă și o variabilă binară „schimbată” per nod. Obiectiv: minimul de noduri schimbate, apoi minimul de abatere relativă. Rezolvitor: Google OR-Tools (CP-SAT sau MIP), cu timp limită de 2 s.
  4. Dacă există mai multe soluții la fel de bune, se raportează toate (maxim 3), ca alternative.
- **Ieșiri:** `A1.CONTRADICTIE` (cu lista relațiilor încălcate) și corectura propusă, cu impactul ei în lei.
- **Criterii de acceptare:**
  - pe devizul chilleri propune exact: împrăștiere și compactare 577,47 → ≤ 320,82 mc; sprijiniri / demontare: aliniere la una din cele două valori;
  - recall ≥ 90% pe erorile injectate „cantitate copiată” și „unitate confundată” între articole legate prin relații;
  - fără constatări pe devizele de control fără erori (precizie ≥ 95%);
  - timp ≤ 2 s pentru un deviz de 2.000 de articole.

### A2. Analiză dimensională și amprenta numerelor

- **Scop:** cantități copiate între articole cu dimensiuni diferite, unități confundate, zecimale mutate. Fără cunoaștere de meserie.
- **Intrări:** cantitățile, UM-urile și dimensiunile lor, lista de factori de conversie plauzibili.
- **Metodă:**
  1. Indexează toate cantitățile nenule (rotunjite la 2 zecimale) într-un dicționar valoare → articole.
  2. **Egalitate peste dimensiuni:** aceeași valoare la articole cu dimensiuni diferite (t și mc, mp și mc). Ignoră valorile „rotunde” frecvente (1, 10, 100) și cantitățile sub un prag.
  3. **Raport de conversie:** pentru perechi de articole din același stadiu fizic (sau același capitol când nu există stadiu), raportul cantităților e (în toleranță 0,1%) un factor de conversie cunoscut (1,8; 2,4; 1,6; 7,85; 1.000). Raport exact = încredere mare; comparațiile între capitole sunt informative.
  4. **Lanț:** dacă A = B × factor și C = A, dar C are dimensiunea lui B, atunci C a fost probabil copiat din A în loc de B. Corectura propusă: C = B.
  5. **Zecimale:** cantitate ≈ 10ⁿ × cantitatea unui articol legat (aceeași familie sau relație din A1), n ≠ 0.
- **Ieșiri:** `A2.UNITATE_COPIATA`, `A2.FACTOR_CONVERSIE`, `A2.ZECIMALA`, cu corectura propusă unde lanțul e clar.
- **Criterii de acceptare:**
  - pe chilleri: detectează 577,47 la compactare / împrăștiere cu raportul 1,8000 față de săpătură și propune 320,82; impact direct ≈ 37.035 lei, la preț de ofertă ≈ 41.734 lei;
  - recall ≥ 95% pe „unitate confundată” și ≥ 90% pe „zecimală mutată”;
  - precizie ≥ 90% (coincidențele numerice sunt principala sursă de alarme false; se filtrează după capitol și dimensiune).

### A3. Informații extrase din texte

**A3a. Dimensiuni din denumiri și titluri**
- **Metodă:** gramatică de unități (expresii regulate + reguli) pentru forme ca `25M X 2M X1M`, `L=25m`, `D=6mm`, `h=1,5 m`, `dist. 20 km`, `gR = 50mm`, `C16/20`. Valorile extrase se convertesc în volum, arie, lungime și se compară cu cantitățile din capitol.
- **Ieșiri:** `A3.DIMENSIUNE_TITLU` (ex. „titlul indică 50 mc, săpătura are 320,82 mc, raport 6,4×”), `A3.CLASA_BETON` (clasa din denumire diferă de resursa de beton din consumuri), `A3.DISTANTA_TRANSPORT`.
- **Criterii:** pe chilleri raportează abaterea 50 vs. 320,82 mc; pe un set de 200 de denumiri adnotate manual, extrage corect ≥ 95% din dimensiuni.

**A3b. Potrivire text → normă din bibliotecă**
- **Scop principal:** pentru un F3 primit de la beneficiar (fără simboluri), propune normele potrivite pentru fiecare linie.
- **Metodă:** în două etape: TF-IDF pe cuvinte și pe secvențe de 3 caractere (rapid, fără model) pentru candidați; apoi reordonare cu embeddings dintr-un model multilingv mic, rulat local (ONNX Runtime). Se combină cu potrivirea UM (dimensiunea trebuie să coincidă).
- **Ieșiri:** top 5 norme per linie, cu scor; nu se aplică automat.
- **Criterii:** pe liniile F3 din devizele istorice (unde știm norma aleasă), norma corectă e în top 5 la ≥ 85% din linii și pe primul loc la ≥ 60%.

### A4. Consumuri comparate cu biblioteca

- **Scop:** articole ale căror consumuri diferă de varianta din bibliotecă a aceleiași norme.
- **Intrări:** vectorul de consumuri unitare al articolului; variantele normei din bibliotecă (amprentele din `ExtractorNorme`).
- **Metodă:** pentru fiecare articol, cea mai apropiată variantă, apoi clasificarea diferenței:
  - `IDENTIC`;
  - `SCALAT` (toate consumurile × k, cu abatere ≤ 0,5%; raportează k);
  - `RESURSA_ELIMINATA` (consum 0 sau lipsă);
  - `RESURSA_ADAUGATA`;
  - `SUBSTITUIT` (resursă lipsă înlocuită cu una din aceeași clasă și dimensiune, denumire asemănătoare);
  - `MODIFICAT_PUNCTUAL`.
- **Ieșiri:** `A4.*` cu severitate după tip și pondere valorică. Eliminarea unei resurse de material cu pondere mare e `Avertisment`; scalarea uniformă e `Info`.
- **Bonus:** caută dacă resursa eliminată apare ca articol separat în același capitol (dublă sau mutată).
- **Criterii:** pe chilleri raportează dulapii cu consum 0 în TSF01C1 și legătura cu articolul separat de cherestea de 5 mc; recall ≥ 95% pe „consum modificat” când biblioteca are norma.

### A5. Statistică robustă pe prețuri și consumuri

- **Scop:** prețuri unitare de resurse și consumuri anormale față de istoricul firmei.
- **Metodă:**
  1. Prețurile istorice se aduc la data devizului cu indicii INS potriviți clasei de resursă.
  2. Per resursă (sau clasă): mediana și MAD; scor z modificat = 0,6745 × (x − mediană) / MAD; prag implicit |z| > 3,5.
  3. **Micșorare bayesiană** spre mediana clasei când resursa are puține observații (sub 10), ca să nu apară alarme din 2–3 valori.
  4. Cazuri speciale: preț 0 la resursă cu consum nenul (`A5.PRET_ZERO`, chiar fără istoric); tarif orar identic la toate categoriile de manoperă (`A5.TARIF_UNIFORM`).
- **Ieșiri:** `A5.PRET_ANORMAL`, `A5.CONSUM_ANORMAL`, `A5.PRET_ZERO`, `A5.TARIF_UNIFORM`, cu mediana, intervalul și numărul de observații.
- **Prag de activare:** ≥ 20 de observații per clasă. Sub prag rulează doar cazurile speciale.
- **Criterii:** pe chilleri raportează apa industrială la 0 și tariful uniform de 111 lei/h; recall ≥ 90% pe „preț × 10” și „preț 0” injectate.

### A6. Devize similare

- **Metodă:** fiecare deviz devine un vector de simboluri de normă, ponderat cu valoarea lor (TF-IDF); similaritate cosinus; cei mai apropiați k = 5 vecini din istoric.
- **Ieșiri:**
  - comparație de structură (manoperă / material / utilaj / transport) cu vecinii;
  - `A6.ARTICOL_LIPSA`: articol prezent în ≥ 60% din vecini, absent aici;
  - prețuri unitare pe normă comparate cu vecinii.
- **Prag de activare:** ≥ 30 de devize în istoric.
- **Criterii:** la ștergerea injectată a unui articol frecvent, îl semnalează în ≥ 70% din cazuri.

### A7. Reguli de asociere (lucrări care lipsesc)

- **Metodă:** FP-Growth pe familii de articole (nu pe simboluri, ca să fie destule date); reguli cu suport ≥ 5% și încredere ≥ 85%.
- **Ieșiri:** `A7.LUCRARE_LIPSA` („92% din devizele cu săpătură > 1,5 m au sprijiniri; acesta nu are”).
- **Până la sute de devize:** aceleași reguli se scriu manual în dicționar (2.4) și rulează ca reguli fixe.
- **Prag de activare automată:** ≥ 200 de devize.

### A8. Monte Carlo pe marja ofertei

- **Intrări:** devizul cu prețul de ofertă; distribuții per clasă de cost (materiale, tarif manoperă, productivitate, utilaj, cantități). Pornim cu distribuții triunghiulare configurabile; ulterior estimate din A5 și A9.
- **Metodă:** 10.000 de iterații cu sămânță fixă; corelații între clase (ex. toate materialele cu indicele INS) prin matrice de corelație simplă.
- **Ieșiri:** distribuția marjei (P10 / P50 / P90), probabilitatea de pierdere, contribuția fiecărei clase la varianță (diagramă tornado).
- **Criterii:** rezultate identice la aceeași sămânță; pe chilleri, manopera iese principalul contributor la varianță; ≤ 3 s pentru 2.000 de articole.

### A9. Predicție din execuție

- **Date:** perechi normat / realizat per articol, din proiecte închise (consum real de manoperă, material, cost real).
- **Metodă:** regresie cu arbori de decizie cu gradient boosting (ML.NET, LightGBM) pe trăsături: familia, UM, cantitatea, ponderile pe clase, tipul de proiect, sezonul; regresie pe cuantile pentru interval.
- **Ieșiri:** `A9.RISC_DEPASIRE` per articol, cu depășirea estimată (P50 și P90).
- **Prag de activare:** ≥ 500 de perechi din ≥ 10 proiecte. Validare pe proiecte lăsate deoparte (nu pe articole amestecate), ca să nu se scurgă informație între seturi.
- **Criterii:** pe proiectele de validare, eroare mai mică decât a referinței naive „depășirea medie a familiei”.

### A10. Ofertă dezechilibrată

- **Intrări:** prețul de ofertă și costul pe articol; graficul de execuție (când se execută fiecare articol); opțional, F3-ul beneficiarului.
- **Metodă:** marja per articol vs. momentul execuției; curba de încasare vs. curba de cost; articole cu marjă mare și cantitate probabil subestimată în F3 (din A1–A3 aplicate pe F3).
- **Ieșiri:** `A10.FRONT_LOADING`, `A10.MARJA_NEGATIVA_TARZIE`, `A10.CANTITATE_F3_SUBESTIMATA`; curba cash-flow.
- **Dependențe:** graficul legat de articolele devizului (modulul Grafic).

## 4. Rezultatele așteptate pe devizul chilleri

Devizul real (`tests/samples/isdp/deviz-resurse-sant-chilleri.json`) devine testul de acceptanță principal.

| # | Constatare | Analizor | Severitate | Corectură propusă |
|---|---|---|---|---|
| 1 | Împrăștiere și compactare 577,47 mc = tone copiate ca volum (320,82 × 1,8) | A2, A1 | Eroare | 577,47 → 320,82 mc; impact ≈ 37.035 lei direct, ≈ 41.734 lei la ofertă |
| 2 | Titlul indică 25 × 2 × 1 = 50 mc, săpătura are 320,82 mc | A3a | Avertisment | — (de verificat cu proiectul) |
| 3 | Sprijiniri 329,92 mp vs. demontare 328,82 mp | A1 | Avertisment | aliniere la una din valori |
| 4 | Apă industrială cu preț 0 | A5 | Avertisment | preț din catalog |
| 5 | Dulapi cu consum 0 în TSF01C1 + articol separat cherestea 5 mc | A4 | Info | — (de confirmat de deviziere) |
| 6 | Tarif 111 lei/h identic la toate categoriile de manoperă | A5 | Info | — |
| 7 | Manopera = 97,8% din costul direct | A8 | Risc | — |

Constatările 1 și 2 nu sunt încă confirmate de utilizator ca greșeli reale (Q4).

## 5. Evaluare: injecție sintetică de erori

### 5.1 Corpus

- **Devize de control:** devize considerate corecte (după revizia unui deviziere), inclusiv chilleri corectat. Țintă: ≥ 20 la început. Fișierele reale stau în `tests/samples-private/` (ignorat de git); în repo intră doar devize anonimizate sau sintetice.
- **Generator de devize sintetice** din bibliotecă, pentru volum, cu structuri realiste (obiecte, capitole, familii legate).

### 5.2 Operatori de injectare

| Operator | Ce face | Analizori vizați |
|---|---|---|
| `UNITATE` | înlocuiește o cantitate cu cantitatea × factor de conversie (t ↔ mc etc.) | A2, A1 |
| `COPIE` | copiază cantitatea unui articol în alt articol din capitol | A2, A1 |
| `ZECIMALA` | × 10, × 100, ÷ 10 | A2, A1 |
| `STERGE` | elimină un articol | A6, A7, A1 |
| `PRET_ZERO` | preț 0 la o resursă | A5 |
| `PRET_X10` | preț × 10 sau ÷ 10 | A5 |
| `CONSUM` | modifică un consum cu ± 20–80% sau îl pune pe 0 | A4 |
| `SUBSTITUIE` | înlocuiește o resursă cu alta din altă clasă | A4 |
| `DIMENSIUNE_TITLU` | schimbă dimensiunile din titlu | A3a |

Fiecare injecție se salvează (operator, țintă, valoare veche și nouă), ca adevăr de referință.

### 5.3 Metrici

- **Recall** per operator și analizor: erori injectate găsite / erori injectate.
- **Precizie:** constatări corecte / toate constatările, măsurată pe devizele de control cu și fără injecții.
- **Corectitudinea corecturii:** procentul de corecturi propuse care readuc valoarea originală (pentru A1, A2).
- Raport rulat în CI la fiecare modificare de analizor; o scădere a recall-ului sau a preciziei peste 2 puncte procentuale pică build-ul.

## 6. Feedback și calibrare

- Utilizatorul marchează fiecare constatare: `Confirmată`, `Justificată` (cu motiv obligatoriu, text scurt) sau `Ignorată`.
- O constatare `Justificată` nu mai apare la reanaliza aceleiași versiuni și nici la versiunile următoare, cât timp amprenta ei (2.2) rămâne aceeași.
- Lunar, per regulă și per firmă: rata de confirmare. Regulile sub 20% confirmare își cresc automat pragul sau scad în severitate (cu jurnal al schimbării și posibilitate de revenire).
- Constatările confirmate și justificate devin date etichetate pentru A5–A9.
- Permisiuni (modelul din `decizii-arhitectura.md`): `devize.analiza.rulare`, `devize.analiza.marcare`, `devize.analiza.configurare`.

## 7. Integrare în aplicație

- **Când rulează:**
  - automat după import (A2, A3a, A1, A4, A5 cazuri speciale), în fundal;
  - la cerere, din editor;
  - obligatoriu la trecerea unei versiuni în stare `Contract` sau `Buget` (dacă se decide că blochează; Q5).
- **Incremental:** după o operație de editor, se reanalizează doar capitolul afectat pentru A1–A4. A5–A10 rulează doar la cerere sau la salvarea versiunii.
- **Afișare în editor:** marcaj pe rândul articolului, panou de constatări filtrabil, buton „Aplică corectura” care trimite operațiile prin API-ul de operații (`POST /api/proiecte/{proiectId}/devize/{versiuneId}/operatii`).
- **API propus:**
  - `POST /api/proiecte/{proiectId}/devize/{versiuneId}/analize` – pornește analiza, întoarce id-ul raportului;
  - `GET .../analize/{raportId}` – raportul cu constatările;
  - `PUT .../analize/{raportId}/constatari/{id}/stare` – marcare.
- **Stocare:** schema `devize`, tabele `raport_analiza` și `constatare` (cu `firma_id` și RLS, ca restul). Dovezile ca `jsonb`.
- **Performanță țintă:** analiza automată după import ≤ 5 s pentru un deviz de 2.000 de articole.

## 8. Ordinea de implementare

Continuă pașii 1–13 din `PROMPT-CLAUDE-CODE.md`.

| Pas | Conținut | Criteriu de terminare |
|---|---|---|
| 14 | Date de referință: dimensiuni UM, factori de conversie, familii de articole (fără relații încă) | toate UM-urile din devizele de test au dimensiune; ≥ 90% din articole au familie |
| 15 | Cadrul: contract de analizor, context, constatare, raport, stocare, API, registru de analizori | un analizor de test rulează și raportul se salvează și se citește |
| 16 | Injecția sintetică de erori + raportul de metrici în CI | rulează pe chilleri și pe devizele sintetice |
| 17 | A2 amprenta numerelor | criteriile A2 + constatarea 1 pe chilleri |
| 18 | A3a dimensiuni din texte | criteriile A3a + constatarea 2 |
| 19 | Relațiile din 2.4 + A1 graf de constrângeri cu OR-Tools | criteriile A1 + constatările 1 și 3 |
| 20 | A4 consumuri vs. bibliotecă + A5 cazuri speciale (preț 0, tarif uniform) | constatările 4, 5, 6 |
| 21 | Integrare în editor: panou, marcaje, „Aplică corectura”, feedback | o corectură aplicată apare în jurnal și se poate anula |
| 22 | A8 Monte Carlo | criteriile A8 + constatarea 7 |
| 23 | A3b potrivire F3 → normă (TF-IDF, apoi embeddings locale) | criteriile A3b |
| 24+ | A5 complet, A6, A7, A9, A10, pe măsură ce pragurile de date sunt atinse | criteriile fiecăruia |

Pașii 14–20 nu depind de decizia D-20 (grila); pasul 21 da.

## 9. Biblioteci propuse

| Bibliotecă | Folosire | Notă |
|---|---|---|
| Google OR-Tools (.NET) | A1 corecție minimă | Apache 2.0; verificat de versiune și pachete native pe Linux |
| Math.NET Numerics | A5, A8 statistică, distribuții | MIT |
| ML.NET (+ LightGBM) | A9, eventual A5 | MIT |
| Microsoft.ML.OnnxRuntime | A3b embeddings locale | MIT; modelul ales separat, cu licență compatibilă |
| Implementare proprie | A2, A3a, A4, A6, A7 (FP-Growth e simplu) | fără dependențe |

Versiunile nu sunt verificate (nuget.org a fost blocat în sesiunea de plan); se verifică în Claude Code.

## 10. Decizii propuse

- **D-21:** analizorii de deviz sunt module cu contract comun, înregistrați ca importatorii; rulează exclusiv local.
- **D-22:** constatările nu modifică devizul; corecturile trec prin API-ul de operații al editorului.
- **D-23:** fiecare analizor intră în producție doar cu metrici de recall și precizie din injecția sintetică, rulate în CI.

## 11. Întrebări deschise

1. **Q1.** Cine din firmă validează dicționarul de familii și relațiile (A1, A7 manual)? Fără un deviziere, relațiile rămân `Info`.
2. **Q2.** Câte devize istorice are firma și în ce format (pentru A5–A7)? Se pot importa în masă din ISDP?
3. **Q3.** Există date de execuție reale (consumuri de manoperă, bonuri de consum) legabile de articole, pentru A9?
4. **Q4.** Constatările 1 și 2 de pe chilleri sunt greșeli reale? Răspunsul fixează testul de acceptanță.
5. **Q5.** Raportul blochează aprobarea unei versiuni când are constatări `Eroare` nerezolvate, sau doar avertizează?
6. **Q6.** Analiza se aplică și pe F3-uri de la beneficiar înainte de ofertare (A3b, A10)? Dacă da, pasul 23 urcă în prioritate.
7. **Q7.** Ce model de embeddings e acceptabil ca dimensiune și licență pentru rulare pe serverul clientului?
