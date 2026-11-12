# Editorul de deviz, biblioteca de norme și catalogul de prețuri

Continuarea specificației `docs/import-isdp.md`. Decizia de bază (D-19): **după semnarea contractului, sursa de adevăr pentru deviz este ERP-ul**. ISDP rămâne unealta devizierilor pentru ofertele mari de licitație. Nu există sincronizare în ambele sensuri cu ISDP.

## 1. Ce construim și de ce

| Componentă | Ce rezolvă |
|---|---|
| **Editorul de deviz** | Modificări în execuție (cantități, lucrări suplimentare, renunțări), acte adiționale, buget revizuit, oferte mici — fără întoarcere în ISDP. |
| **Biblioteca de norme a firmei** | Articole gata descompuse pe resurse, adăugate din bibliotecă în orice deviz. Se populează **automat din fiecare import ISDP**; nu cumpărăm și nu reconstruim indicatoarele. |
| **Catalogul de prețuri al resurselor** | Prețuri reale (comenzi, facturi, oferte furnizori) în locul prețurilor generice: bugetul reflectă costul adevărat al firmei. |
| **Compararea versiunilor** | Actul adițional = diferența dintre contract și versiunea nouă, calculată automat. |
| **Exporturile** | F3, deviz-ofertă cu resurse, extras de resurse — ca firma să poată trimite beneficiarului ce a modificat în ERP. |

Ce **nu** facem: indicatoarele oficiale complete de norme și bazele de prețuri ale furnizorilor de programe de devize (date licențiate, întreținute de ei). Biblioteca noastră conține doar ce a folosit firma.

## 2. Ciclul de viață al versiunilor

| Versiune | De unde vine | Editabilă | Cine aprobă | După aprobare |
|---|---|---|---|---|
| **Ofertă** | import ISDP sau creată în ERP (din bibliotecă) | da, cât e ciornă | director / ofertant (permisiunea `ofertare.aprobare`) | imuabilă; se trimite la beneficiar |
| **Contract** | copie a ofertei câștigate (eventual negociată) | da, până la semnare | director | imuabilă; **baza situațiilor** (prețuri de ofertă) |
| **Buget de execuție** | copie a contractului, cu costuri directe actualizate la prețurile firmei | da | manager de proiect | imuabilă; baza controlului costurilor și a aprovizionării |
| **Act adițional** | copie a contractului curent + modificări | da | director | devine noul „contract curent”; generează diferențele pentru beneficiar |
| **Buget revizuit** | copie a bugetului curent + modificări | da | manager de proiect | devine noul „buget curent” |

Reguli:
- Doar **ciornele** se editează. O versiune aprobată nu se modifică niciodată: se creează una nouă, cu `VersiuneAnterioaraId`.
- Pe proiect există în orice moment un **contract curent** și un **buget curent** (indicatori pe proiect, schimbați la aprobare).
- **Reimport ISDP după contract** (devizierul a refăcut ceva în ISDP): se importă ca versiune nouă de tip Ofertă sau Act adițional, **comparată** cu contractul curent; nu suprascrie nimic.
- Toate versiunile păstrează `CheieStabila` pe articolele potrivite (§7), ca graficul, comenzile, cantitățile executate și situațiile să rămână legate.

## 3. Editorul de deviz

### 3.1 Operații

Pe o ciornă, toate operațiile sunt comenzi către server (§3.3):

| Grup | Operații |
|---|---|
| Structură | adaugă / redenumește / mută / șterge capitol; mută articole între capitole; renumerotare WBS automată |
| Articole | adaugă din bibliotecă (alegând varianta); adaugă manual (simbol, denumire, UM, resurse); **asimilează** (copie a unui articol existent, cu sufixul „asim”, consumuri editabile); duplică; șterge; modifică cantitatea; modifică denumirea |
| Resursele articolului | modifică consumul unitar; înlocuiește o resursă cu alta (ex. alt sortiment de beton); adaugă / elimină resursă; modifică prețul doar în acest articol (excepție, marcată) |
| Global pe versiune | **actualizează prețurile** din catalog după o politică (§5.3); modifică prețul unei resurse în tot devizul; modifică procentele (CAM, indirecte, profit), moneda și cursul ofertei |
| Între versiuni | copiază capitole / articole dintr-o altă versiune sau alt proiect |

Validări pe fiecare operație: cantitate > 0, UM obligatorie, consum ≥ 0, resursă cu tip, simbol unic în capitol (avertisment), versiune în stare Ciornă, permisiune.

### 3.2 Recalcul

- Același motor ca la import (`CalculatorDeviz` / regula din `import-isdp.md` §2.5), cu politica de rotunjire a versiunii: `Fara` pentru versiunile din ISDP și cele derivate din ele, ca totalurile să rămână comparabile la ban.
- **Incremental**: o operație recalculează articolul atins, apoi totalurile capitolelor părinte și recapitulația. Totalurile pe capitol se păstrează denormalizat, actualizat în aceeași tranzacție. Un deviz mare (5.000 de articole × 10 resurse) nu se recalculează integral la fiecare tastă.
- Prețul de ofertă pe articol se recalculează după aceeași regulă ca F3 (`PretOferta`), la cursul și procentele versiunii.

### 3.3 API și concurență

- `POST /api/proiecte/{proiectId}/devize/{versiuneId}/operatii` cu o listă de operații (ex. `{"tip": "modifica-cantitate", "articol": "<id>", "cantitate": 12.5}`), aplicate **atomic**; răspunsul conține articolele și capitolele modificate plus recapitulația nouă.
- **Jurnal de operații** pe versiune (cine, când, operația, valorile vechi și noi), folosit pentru audit și pentru **anulare** (undo = operația inversă din jurnal), cât timp versiunea e ciornă.
- **Concurență optimistă**: fiecare cerere trimite versiunea rândului (`xmin` în PostgreSQL / token de concurență EF); la conflict, clientul reîncarcă articolele atinse. Doi utilizatori pot lucra pe capitole diferite ale aceleiași ciorne.
- Permisiuni: `ofertare.editare` pentru oferte și contracte, `buget.editare` pentru bugete; aprobarea cu `*.aprobare` și separarea atribuțiilor (cine a editat ultimul nu aprobă).

### 3.4 Interfața

- Grila arată ca în ISDP (devizierii o recunosc): WBS, Simbol, Denumire, UM, Cantitate, prețuri unitare pe 5 componente, valori, total; resursele se desfac sub articol. Editare din tastatură ca în Excel; panou jos cu recapitulația live; panou lateral cu căutarea în bibliotecă și inserare „după rândul curent”.
- **Decizie de luat (D-20): componenta de grilă.** Afișarea arborescentă (Tree Data), copy/paste și exportul Excel din AG Grid sunt **doar în AG Grid Enterprise** (licență comercială); AG Grid Community e gratuită (MIT), dar fără ele ([comparație oficială](https://github.com/ag-grid/ag-grid), [Tree Data — enterprise](https://www.ag-grid.com/react-data-grid/tree-data.md)). Variante:
  1. **AG Grid Enterprise** — cel mai rapid de implementat, cost de licență per dezvoltator;
  2. **TanStack Table (MIT) + virtualizare** — gratuit; arborele, editarea și copy/paste le scriem noi (câteva săptămâni în plus);
  3. altă grilă comercială, după evaluare.
  Recomandare: un prototip de o zi cu devizul real (15 articole, apoi unul sintetic de 50.000 de rânduri) pe varianta 2; dacă nu e fluent, varianta 1.

## 4. Biblioteca de norme a firmei

### 4.1 Model

```
Norma            (FirmaId, Simbol, SimbolBaza, TipArticol: Normat|Asimilat|Manual, Denumire, UnitateMasura,
                  CapitolIndicator?, Sursa: Import|Manual, Implicita: bool, Amprenta, NrUtilizari,
                  PrimaUtilizare, UltimaUtilizare, Activa)
ConsumNormaBib   (NormaId, ResursaCatalogId, CodResursa, ConsumUnitar numeric(24,12), Ordine)
ProvenientaNorma (NormaId, VersiuneDevizId, WbsSursa, DataImport)
```

- O normă = un **simbol + o variantă de consumuri** (amprenta = simbol + UM + consumuri sortate, fără prețuri; vezi `NormaExtrasa.Amprenta`). Același simbol poate avea mai multe variante; una e **implicită** (cea mai folosită, sau aleasă de un ofertant).
- **Prețurile nu stau în normă**: norma dă consumurile, catalogul de resurse dă prețurile (§5). Așa o normă rămâne valabilă când se schimbă prețurile.
- `CapitolIndicator` se deduce din prefixul simbolului (TS…, AC…, RP…, TR…) — informativ, pentru filtrare.

### 4.2 Populare

- La **fiecare import ISDP confirmat**, `ExtractorNorme.Extrage` produce normele devizului (pe devizul real: 11 norme din 15 articole). Pentru fiecare: dacă amprenta există → crește `NrUtilizari` + proveniență; altfel → variantă nouă. Avertismentul „același simbol, consumuri diferite în același deviz” se arată la import.
- Articolele **manuale** și **asimilate** create în editor pot fi „salvate în bibliotecă” explicit (nu automat, ca biblioteca să nu se umple de experimente).
- Resursele din consumuri trec prin **nomenclatorul firmei** (`ResursaCatalog`, din `import-isdp.md` §6.3): biblioteca nu creează resurse paralele.

### 4.3 Căutare și folosire

- Căutare după simbol (prefix) și denumire (full-text, fără diacritice: `unaccent` + `pg_trgm` în PostgreSQL), filtre pe capitol de indicator, UM, „folosite în ultimele 12 luni”.
- La adăugarea în deviz: se copiază consumurile variantei alese, iar prețurile resurselor vin din catalog după politica versiunii. Articolul ține `NormaId` (de unde provine), dar e o **copie**: modificarea ulterioară a bibliotecii nu schimbă devizele existente.
- Etichetare clară în interfață: „normă din devizele firmei”, nu „indicator oficial”.

## 5. Catalogul de prețuri al resurselor

### 5.1 Model

```
ResursaCatalog   (din import-isdp.md §6.3: cod ISDP, denumire, UM, tip, mapare la stoc / meserie / utilaj)
PretResursa      (ResursaCatalogId, Pret, Moneda, Data, Sursa: DevizIsdp|OfertaFurnizor|Comanda|Factura|NIR|Manual,
                  FurnizorId?, DocumentId?, ProiectId?)
TarifManopera    (Meserie/Categorie → tarif orar, valabil de la)       ex. 111 lei/oră în devizul real
TarifUtilaj      (Utilaj → tarif orar intern, valabil de la; din modulul Utilaje)
```

- Prețurile sunt **istoric**, nu o singură valoare: fiecare import ISDP, ofertă de furnizor, comandă și factură (din modulul Facturi, după potrivirea în 3 puncte) adaugă un preț datat.

### 5.2 Surse automate

| Eveniment | Preț adăugat |
|---|---|
| import ISDP confirmat | prețurile unitare ale resurselor din deviz (sursa „DevizIsdp”) |
| ofertă furnizor înregistrată | prețul ofertat |
| comandă aprobată | prețul din comandă |
| factură primită aprobată / NIR | prețul real de achiziție (cel mai valoros pentru buget) |

### 5.3 Politica de preț la „actualizează prețurile”

Setare per firmă, alegere la operație: **ultimul preț de achiziție**; **medie ponderată pe ultimele N luni**; **cel mai mic preț de ofertă valabil**; **prețul din devizul sursă** (nu schimbă nimic). Rezultatul este un raport înainte de aplicare: resursă, preț vechi, preț nou, sursa, impactul pe total. Resursele fără preț în catalog rămân la prețul din deviz și sunt semnalate.

## 6. Exporturi din ERP

| Export | Regula | Format |
|---|---|---|
| **F3 — Lista cu cantități** | exact algoritmul verificat (`import-isdp.md` §3.1): componente rotunjite separat, total = suma componentelor | Excel + PDF (șablon Word al firmei, modulul Formulare) |
| **Deviz ofertă cu resurse** | structura din `import-isdp.md` §2 (WBS, simbol, resurse, recapitulație) | Excel |
| **Extras de resurse** | `ExtrasResurse` | Excel; și ca necesar pentru aprovizionare |
| **Act adițional — lista diferențelor** | §7: lucrări suplimentare și renunțări, la prețurile contractului | F3 cu două secțiuni + PDF |

Exporturile poartă în proprietăți ascunse ID-ul versiunii (ca la formulare), ca un fișier reîncărcat să fie recunoscut.

## 7. Compararea versiunilor

- Potrivirea articolelor: `CheieStabila`; pentru versiuni din importuri diferite, cheile din `import-isdp.md` §7.1.
- Rezultat pe articol: **nou**, **eliminat**, **cantitate modificată**, **preț modificat**, **consumuri modificate**, **neschimbat** — cu valorile vechi/noi și diferența valorică (cost direct și preț de ofertă).
- **Actul adițional** = comparația „contract curent → ciorna de act adițional”: articolele noi și cantitățile în plus = lucrări suplimentare; eliminările și cantitățile în minus = renunțări; totalul = valoarea actului adițional. Exportul în §6.

## 8. Efectul asupra celorlalte module

| Eveniment | Efect |
|---|---|
| buget curent nou aprobat | aprovizionarea recalculează necesarul pe resursă (extras); comenzile deja făcute rămân legate de articol prin `CheieStabila`; diferențe necesar vs. angajat semnalate |
| act adițional aprobat | situațiile următoare folosesc noul contract curent (cantități și prețuri); graficul primește articolele noi nelegate (de legat de activități) |
| cantitate executată > cantitate din contractul curent | alertă „depășire de cantitate” pe articol → propunere de act adițional |
| preț real de achiziție mult peste bugetul curent (prag configurabil) | alertă în controlul costurilor; propunere de buget revizuit |
| articol eliminat din buget care are deja consumuri / pontaj | operația e permisă, dar articolul rămâne „închis” cu costurile reale vizibile (nu se pierd costuri) |

## 9. Ordinea de implementare

| Pas | Conținut | Criteriu de acceptare |
|---|---|---|
| 1 | Ciclul de versiuni (§2): copiere versiune, contract curent / buget curent pe proiect, aprobări | se poate crea Contract din Ofertă și Buget din Contract; aprobatele sunt imuabile (test) |
| 2 | Operațiile de editare pe server + jurnal + recalcul incremental (§3.1–3.3) | pe devizul real: modificarea cantității la 1.1 recalculează T3 identic cu un recalcul complet; undo readuce valorile la ban |
| 3 | Biblioteca de norme (§4) + populare la import | după importul devizului real: 11 norme; al doilea import identic nu creează variante noi |
| 4 | Catalogul de prețuri (§5) + „actualizează prețurile” | raport de impact corect; resursele fără preț semnalate |
| 5 | Compararea versiunilor + act adițional (§7) | diferențele pe un act adițional de test, verificate de mână |
| 6 | Exporturi (§6) | F3 exportat din versiunea importată = F3-ul original din ISDP, celulă cu celulă pe valori |
| 7 | Interfața editorului (§3.4), după decizia D-20 | devizul real editabil din tastatură; recapitulația se actualizează sub 300 ms |
| 8 | Efectele din §8 (după ce modulele respective există) | — |

## 10. Întrebări deschise

1. **D-20**: buget pentru AG Grid Enterprise, sau varianta gratuită cu efort în plus?
2. Cine aprobă fiecare tip de versiune la pilot (tabelul din §2 este o propunere)?
3. Politica de preț implicită pentru buget (§5.3): ultimul preț de achiziție sau medie?
4. Tarifele orare de manoperă (111 lei/oră în devizul real): unic pe firmă sau pe meserie/categorie?
5. Ofertele mici: vor să le facă în ERP (din bibliotecă) de la început, sau doar după ce biblioteca are suficiente norme?
