# Devizele de ofertă BOQ pe obiecte (D-58)

Ofertele „Bill of Quantities” ale pachetului 703C1R (EPC, beneficiar coreean): câte un fișier Excel pe obiect (B5, B6, B10,
PCA16A), toate ale aceluiași deviz. Prețurile sunt de **ofertă** (vânzare), pe trei componente; costul direct rămâne gol.

## Formatul (`Sabloane/devize/*_OFFER_*.xlsx`, nu intră în git)

- **Foaia „BOQ”** (rezumatul): `Bill of Quantities - Building 5`, câte o linie pe secțiune (cod BSI + denumire + total) și
  `GRAND TOTAL`.
- **Câte o foaie pe secțiune** (20000 Architecture, 20200 Concrete, 50000 Electric…): antet `No / Work Items / Item Description /
  Specification / Unit / Quantity / Material (A) / Labour (B) / Expense (C) / TOTAL / Remarks`, sub el `Rate / Amount` pe fiecare
  componentă; rânduri de grup („1 EXTERIOR WALLS…”, fără cantitate), articole, rândul `TOTAL`.
- **Foile în coreeană** (cost direct, rate indirecte: același șablon în toate fișierele) și foile de lucru („sample_…”,
  „KHNP Comment”, „Sheet1”, 75130 gol) nu intră în deviz.
- **Neregularități tratate:** Description / Specification inversate; antetul componentelor lipsă (se iau perechile `Rate / Amount`
  rămase, în ordinea profilului); sume scrise ca text („750.000”); un `TOTAL` gol la mijlocul foii; numele din rezumat diferit de
  foaie (BOQ „14000” → foaia „14100”, „16100” → „20000”: potrivire după total); coduri cu greșeli („PCA16 C …”, „B5 C …” în B10,
  „C 20500 …” fără obiect) — semnalate; coduri repetate — sufix `~2`; electricele cu perechi Activity / Material.
- **Legătura cu graficul:** codul articolului fără spații („B5 C 20000 1 A” → `B5C200001A`) e cheia resurselor / costurilor din
  XER: Material = resursa material, Labour = resursa manoperă („…-1”), Expense = costul direct (PROJCOST), la cent. Pe pachetul
  pilot, 1.822 din 1.913 articole (95%) își găsesc codul în XER direct; restul sunt codurile cu greșeli de mai sus.

## Modelul (plugin `Devize.Import.Boq`)

- **`ProfilBoq`:** cum se recunosc antetul (prefixe / valori exacte ale antetelor), componentele de preț (cheie, etichetă, componenta
  devizului sau `null` = neclară), foaia de rezumat, rândurile de total, titlul, moneda implicită. Profilul livrat:
  `MaterialLabourExpense` (Material → material, Labour → manoperă, Expense → **ales la import**). Un format nou = un profil nou.
- **`CititorBoq`** (pur): foile (`CititorFoiXlsx`, mutat în BuildingBlocks `ErpConstructii.Import.Xlsx`, fără EF) → `BoqCitit`
  (obiect, secțiuni cu grupuri și articole, rezumat, componente, foi lăsate deoparte cu motivul, probleme).
- **`ConversieBoq`** (pur): obiectul pe primul nivel (`TipCapitol.Obiect`), secțiunile ca capitole, grupurile ca subcapitole;
  articolul: simbol = codul, cod WBS = codul fără spații, specificația și observațiile separate (coloane noi pe articol), prețul
  de ofertă pe componentele alese, costul direct zero; cotele recapitulației 0; `DevizCanonic.PeObiecte = true`.
- **`ImportatorBoq`** (`IImportatorDevizCuAnexe`, `boq`): detectează după antetul unui profil; moneda: cea aleasă, altfel EUR.

## Importul pe obiecte

- Primul fișier creează devizul (ofertă, ciornă). Următoarele se importă **în devizul ales** (propus automat: cel mai nou deviz
  BOQ în vigoare): obiectul intră în **ciorna de lucru** a devizului (creată la nevoie, ca la editare — D-53), deci devizul ales
  rămâne neschimbat, iar diferențele se văd și se aprobă.
- Obiectul cu același cod se **înlocuiește**: articolele potrivite (cod WBS + simbol, apoi simbol + denumire + UM) își păstrează
  cheile stabile (legăturile din alte module), noul obiect ia locul celui vechi. Fișierul se arhivează pe ciornă.
- Componentele neclare (Expense) blochează confirmarea până la alegere (mesaj în previzualizare).
- Afișarea: devizul fără cost direct arată oferta (componentele și totalurile în moneda versiunii), iar diferențele dintre versiuni
  se dau pe valoarea de ofertă. Modificarea cantității păstrează prețul de ofertă unitar (fără recalcul din costul direct gol).

## Etapele următoare

- **Etapa 3 (D-57 / REQ-GRF-02):** legătura articol ↔ activitate din grafic după cod (cu regula de rezervă pentru codurile fără
  obiect sau cu alt obiect: prefixul obiectului fișierului), cantitățile alocate din XER (6 articole sunt împărțite pe mai multe
  activități), raportul de diferențe deviz / grafic.
- **Etapa 4:** bonurile de consum (pe articole de deviz) ajung automat pe activitățile graficului; progresul din cantități.
