# Prețurile resurselor: din devize, recepții, comenzi și oferte

> Completează `editor-deviz.md` §5 (catalogul de prețuri) cu organizarea pe module. Decizii: D-54 (catalogul din devize), D-55 (sursele de preț și actualizarea devizului).

## 1. Ce există și de unde vin prețurile

| Sursă | Unde e | Ce are | Folosire |
|---|---|---|---|
| Devize importate | Devize (`consum_normat`, `articole`) | cod resursă ISDP, UM, preț unitar, cantitate | catalogul din devize (D-54) |
| Recepții (NIR) | Stocuri (`LinieNir`) | material din stoc, preț unitar, cantitate, data, furnizor (prin factură), proiect | **prețul real de achiziție**; prețul NIR = prețul facturii, deci facturile sunt acoperite prin NIR |
| Facturi primite | Facturi (`LinieFactura`) | doar text (fără material din stoc) | indirect, prin NIR; serviciile (fără stoc) nu intră deocamdată |
| Note de comandă | Aprovizionare (`LinieNotaComanda`) | text, UM, cantitate, preț opțional | sursă mai slabă: materialul se potrivește după denumire |
| Oferte de furnizori | — (nu există) | — | modul nou, etapa 5 |

**Puntea dintre deviz și stoc** e legătura resursei din nomenclator (`ResursaCatalog.ArticolStocId`) cu materialul din stoc, cu factorul de conversie a UM (`FactorConversie`: 1 UM resursă = factor × UM stoc; ex. „to” → „kg” = 1000; mc → to depinde de densitate și îl dă utilizatorul).

## 2. Reguli de organizare (ca să nu se rupă)

1. **Un singur contract, definit de Devize:** `ISursaPreturi` — „prețurile observate pentru materialele din stoc X, Y, Z, în intervalul dat”. Fiecare modul-sursă îl implementează într-o singură clasă, în proiectul lui `.Infrastructure` (ca `IArticoleStocPentruMapare`). Devize nu citește tabelele altor module; modulele-sursă nu știu de catalog.
2. **Citire la cerere, fără copii:** fără tabele sincronizate și fără evenimente. Un NIR corectat / stornat se vede imediat; o sursă nouă = o clasă + o înregistrare; un modul lipsă = o sursă mai puțin.
3. **Calculul e pur** (Devize): alegerea prețului după regulă (`PoliticaPret`), raportul de impact, recalculul articolelor din resurse. Infrastructura adună datele și salvează.
4. **Devizul nu se modifică pe loc:** actualizarea prețurilor scrie în ciorna de lucru (D-53); modificările se văd marcate și se aprobă sau se aruncă.

## 3. Etape

Fiecare etapă se livrează și se folosește singură.

### Etapa 1 — legarea resurselor de ERP ✅
- Ecranul Ofertare și devize → „Nomenclator resurse”: resursele nelegate / legate, filtre pe tip, căutare; fereastra de legare: materialul din stoc (propuneri după denumire + căutare liberă, `GET /api/devize/resurse/{id}/propuneri?cauta=`), factorul de conversie, meseria / categoria, utilajul, „fără corespondent”, confirmarea tipului dedus.
- Factorul: dat de utilizator, altfel propus (`ConversieUm.Propune`: aceeași unitate scrisă altfel = 1, perechi fixe to/kg, kg/g, mc/l, km/m), altfel legarea e refuzată cu mesajul care cere factorul.
- Dreptul: `ofertare.editare` sau `aprovizionare.editare` (ofertantul sau achizitorul).

### Etapa 2 — prețuri din recepții ✅
- `ISursaPreturi` (Devize) implementat de Stocuri: liniile NIR neanulate, cu corecturile, prețul convertit în UM-ul resursei (preț stoc × factor), data, furnizorul, proiectul, numărul NIR / facturii.
- Catalogul (Prețuri resurse): la resursele legate, alături de prețurile din devize: ultimul preț de achiziție, media ponderată pe ultimele N luni, furnizorul; rândul desfăcut arată și recepțiile, cu link spre NIR / factură.

- Implementat: `ISursaPreturi` (`SurseCosturi.cs`, Devize) ← `SursaPreturiReceptii` (Stocuri: NIR-urile nestornate, prețul > 0); statisticile pure `CatalogPreturi.Achizitii` (prețul convertit cu factorul, media ponderată pe ultimele 6 luni); `GET /api/devize/catalog/resurse/achizitii` și `/achizitii/detalii?cod=&um=`; în „Prețuri resurse”, coloanele „Achiziție: ultimul” și „medie 6 luni” (cu diferența față de media din devize) și recepțiile la desfacerea rândului.

### Etapa 2b — bugetul proiectului ✅ (primul pas)
- Același tipar, pentru costuri: `ISursaCosturi` (Devize) ← `SursaCosturiConsum` (Stocuri: liniile bonurilor de consum, pe articolul de deviz sau fără articol) și `SursaCosturiFacturi` (Facturi: facturile neanulate în lei pe proiect; liniile de stoc = materiale cumpărate, celelalte = servicii nerepartizate).
- Calculul pur `CalculBuget` (`BugetProiect.cs`): fiecare articol al devizului de referință (aceeași regulă ca pentru consumuri, fără ciornele de lucru) cu bugetul pe resurse și costurile lui; costurile pe articole scoase din deviz și cele fără articol separat; rezumatul pe proiect (consum, servicii, materiale cumpărate și neconsumate ≈ cumpărate − consumate, procentul din buget).
- `GET /api/proiecte/{id}/buget` și `/buget/costuri?articol=<cheie>|generale|servicii|materiale` (drepturile `buget.vizualizare` + `date.preturi_cost`); fișa proiectului → tab-ul „Buget”.
- **Rămân:** repartizarea facturilor de servicii / subantrepriză pe articole (contractul → articolele lui), manopera din pontaj și utilajele pe articole, angajatul (comenzi și contracte neexecutate), facturile în altă monedă (curs).

### Etapa 3 — „Actualizează prețurile” în deviz ✅
- Regula (setare pe firmă, aleasă la operație): **media ponderată pe ultimele 6 luni din recepții** (implicit); ultimul preț de achiziție; cel mai mic preț recent; prețurile din alt deviz. Fără preț în sursă: rămâne prețul din deviz și e semnalat.
- Raportul înainte de aplicare: resursă, preț vechi → nou, sursa, impactul pe articol și pe total; resursele fără preț; **articolele cu prețul modificat manual** (suma resurselor ≠ prețul articolului) sunt separate și implicit nu se schimbă.
- Aplicarea: liniile bifate se scriu în ciorna de lucru (creată automat, D-53); prețul unitar al articolelor se recalculează din resurse, iar prețul de ofertă după regula F3.
- Implementat: în deviz, butonul „Prețuri de achiziție” (cere `date.preturi_cost`): pentru fiecare resursă a devizului, prețul din deviz, ultima achiziție (data, NIR / factura, furnizorul), media pe 6 luni, prețul nou după politica aleasă (medie 6 luni / ultimul / cel mai mic în 6 luni), diferența și impactul pe costul direct; resursele nelegate / fără achiziții sunt semnalate, iar prețul nou se poate corecta înainte de aplicare.
- Calculul: `ActualizarePreturi.Propune` (pur, raportul) și `VersiuneDeviz.ActualizeazaPreturiResurse` (aplicarea: fiecare articol primește doar diferența consum unitar × (preț nou − preț vechi) pe componenta resursei, deci modificările manuale rămân; prețul de ofertă se recalculează după regula F3).
- `GET …/devize/{v}/preturi-achizitie?politica=` și `POST …/devize/{v}/preturi-achizitie` (aplică în ciorna de lucru, D-53; fără niciun preț efectiv nou nu creează ciornă).

### Etapa 4 — prețuri din notele de comandă
- Aprovizionare implementează `ISursaPreturi` pentru liniile cu preț; materialul se potrivește după denumire (aliasurile de la recepții, `PotrivireArticol`), eventual memorat pe linie la emitere. Sursa apare ca „comandă” (încredere mai mică decât recepția).

### Etapa 5 — oferte de la furnizori
- În Aprovizionare: cerere de ofertă → oferte primite (furnizor, valabilitate, linii cu material și preț), comparația ofertelor; implementează același `ISursaPreturi` („ofertă”, doar cele valabile).

## 4. Întrebări deschise
- Prețurile manoperei și utilajelor: din pontaj / tarife interne (modulele HR și Utilaje, încă neîncepute).
- Moneda: recepțiile sunt în lei; devizul în altă monedă păstrează costul direct în lei (D-50), deci nu e nevoie de curs la actualizare.
