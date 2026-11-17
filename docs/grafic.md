# Graficul de execuție (D-57)

Graficul de execuție vine din Primavera P6 (export XER). Facturile și materialele consumate se leagă apoi de structura lui
(WBS / activități), ca să se vadă costurile pe obiectele graficului față de bugetul din grafic.

## Deciziile utilizatorului (17.11.2026)

- **Legarea** facturilor și consumurilor: pe o activitate **sau** pe orice nod WBS (când activitatea exactă nu se știe), cu totalizare în sus pe arbore.
- **Alocarea facturii:** pe linii sau procentual (o factură poate acoperi mai multe zone / activități).
- **Moneda:** bugetul din grafic e în moneda graficului (EUR în exportul pilot); facturile / consumurile în lei se convertesc la **cursul BNR din data facturii / bonului**, cu afișare în ambele monede.
- **Un XER nou** al aceleiași planificări (același „Project ID”) = **actualizare** (versiune nouă) a aceluiași grafic.

## Exportul pilot (`Sabloane/grafic executie/*.xer`, nu intră în git)

Primavera 15.2, Windows-1250, EUR, data actualizării 13.11.2026: 423 noduri WBS (fără nodul proiectului), 1.189 activități
(1.173 de lucru + 13 rezumat WBS + 3 LOE), 1.613 legături, 120 de activități cu resurse (TASKRSRC, 25,8 mil.) și 117 cu
cheltuieli (PROJCOST, 4,6 mil.), 26 tipuri de coduri de activitate (zonă, subantreprenor, disciplină, nivel), 554 pași.
Exportul nu conține linii de bază (doar datele planificate curente).

## Etapa 1 — importul și vizualizarea (făcută)

- `FisierXer` (citirea tabelelor; UTF-8, altfel Windows-1250) + `ConversieXer` (pur) → `GraficCanonic` extins: WBS cu chei-cale
  stabile („2INFRA.4.2”), activitățile cu tipul, stadiul, procentul după tipul lui (fizic / durată / unități), datele planificate /
  reale / prognozate / târzii, durata și rezerva în zile (orele calendarului activității), drum critic (rezerva ≤ pragul proiectului),
  constrângerea, legăturile, resursele și cheltuielile cu costurile, pașii, codurile de activitate.
- `AnalizaGrafic` (pur): totalurile pe WBS (date, costuri, procent ponderat cu costul, altfel cu durata; rezumatele și LOE nu intră în
  procent). `ComparareGrafice` (pur): activități noi / șterse / modificate, decalajul sfârșitului, procentul.
- Modulul `Grafic.Infrastructure`, schema `grafic` (RLS): `grafice`, `versiuni` (cu rezumatul comparației), `fisiere` (originalul),
  `noduri_wbs`, `activitati` (fără jurnal de modificări: sunt copia fișierului; versiunea e jurnalizată).
- Endpointurile `/api/proiecte/{proiectId}/grafice`: listă, previzualizare / import (actualizare a graficului cu același Project ID
  sau grafic nou), conținutul versiunii, activitatea (legături, resurse, pași, istoric pe actualizări), comparația, fișierul original,
  ștergerea ultimei actualizări. Drepturi: `proiecte.vizualizare` / `proiecte.editare`.
- Ecranul: fișa proiectului → Grafic (arbore WBS + activități, bare Gantt planificat / actual cu data actualizării, filtre după stadiu
  și coduri de activitate, fișa activității, „Ce s-a schimbat”).

## Etapa 2 — legăturile cu documentele (următoarea)

- Ținta legăturii: `(GraficId, cheie)` — codul activității sau cheia nodului WBS, stabile la reimport (nu Id-ul rândului, care e pe versiune).
- **Bonul de consum:** linia primește activitatea / nodul (opțional, alături de articolul de deviz); propus din articol după ce există
  legarea articol ↔ activitate (etapa 4).
- **Factura:** alocarea stă în modulul Grafic (`AlocareCost`: factura, linia sau procentul, ținta), modificabilă oricând, fără
  corectură pe factura blocată. Facturile legate de un contract pot moșteni nodul WBS al contractului.
- Grafic citește documentele prin contracte publice (ca `ISursaCosturi`), fără să depindă de infrastructura Facturi / Stocuri.
- Cursul BNR din data documentului: sursa de curs se stabilește atunci (cursul din factură, altfel serviciul de curs BNR).

## Etapa 3 — costurile pe WBS / activitate

Buget din grafic vs realizat (facturi + consumuri, în lei și în moneda graficului), totalizat pe arbore, abaterea, procentul fizic;
rapoarte PDF / Excel.

## Etapa 4 — articole de deviz și progres

Legarea activităților de articolele de deviz (REQ-GRF-02), progresul din cantități (REQ-GRF-03, `CalculatorProgres` există).
