# Contractele cu subantreprenorii: fișa de urmărire, importul și exportul ei

> Plan (D-56). Pornește de la fișele de urmărire folosite azi (`Sabloane/contracte/`: Züblin, HT Clima, AGER-ROM — fișiere reale, nu intră în git) și completează modulul Contracte (D-41). Răspunsurile utilizatorului: ambele cote ale asocierii una lângă alta; cursul BNR de pe factură, introdus / propus la legare; plățile se țin în aplicație; contractele în curs se citesc din Excel, iar un contract importat se poate edita.

## 1. Ce conțin fișele

Aceeași structură în toate trei (Züblin are o variantă mai scurtă), aceeași formatare (titlu bleumarin `#183153` cu text alb, antete `#DCEAF5`, rânduri „nesemnat / propunere” pe `#FFF1D6`, sume `#,##0.00 "EUR"`):

| Foaie | Conținut |
|---|---|
| 1. Date generale | contract (nr. / dată), subantreprenor (CUI), antreprenori (asocierea **MGJV = MELSAM–GCC Joint Venture**), proiect, obiect, clădiri, tip preț (global / prețuri unitare × cantități real executate), monedă, valoare, durată, stare, avans, data fișei |
| 2. Valoare contract | contract inițial + acte adiționale (modificare, cumulat, **semnat / nesemnat**), pe antreprenor (MELSAM / GCC / total), tranșele de avans |
| 3. Obligații și termene | obligație, temei (TCS art. x), termen / condiție, stare, responsabil |
| 4. Facturi / situații / plăți | lună, partener facturat (MELSAM / GCC), nr. și dată factură, **situația de plată**, tip (avans tranșa n / lucrări / livrare), lucrări în EUR, **storno avans**, curs BNR, RON net / TVA / total, plată, stare, observații |
| 5. Garanții și riscuri | avans pe tranșe și pe antreprenor, recuperarea pro rata pe situații; garanții de avans / bună execuție / tehnică (procente, durată de la PVRTL); instrumente (SGB, bilet la ordin: emitent, nr., beneficiar, valoare, valabilitate); penalități (%/zi, plafon); aspecte de clarificat |
| 6. Acțiuni | acțiune, prioritate, stare, responsabil |
| 7. Surse | documentele (contract, acte adiționale, facturi, SGB, BO) |
| 8. Termene de execuție | faze cu start / final contractual, condiții, stare |

Reguli care reies din ele: actul adițional nesemnat nu schimbă valoarea; avansul ≠ lucrare executată (stornarea avansului nu se scade din lucrări); biletul la ordin emis ≠ plată; un câmp de plată gol nu confirmă neplata; contractele sunt în EUR, facturile în RON la cursul scris pe factură.

## 2. Modelul (modulul Contracte)

Contractul rămâne `ContractPartener` (D-41), extins; registrele fișei sunt entități ale contractului, în schema `contracte` (RLS).

- **Asocierea:** `MembruContract` (denumire, CUI, cotă, `EsteFirmaActiva`) — antreprenorii care semnează contractul și cota fiecăruia (MELSAM 50% / GCC 50%). Valorile, avansul și garanțiile se arată pe fiecare membru și total.
- **Contractul:** tipul prețului, moneda, valoarea inițială, perioada / durata (text, când nu e o dată), stare; **avansul pe tranșe** (`TransaAvans`: procent, descriere); **garanții** (avans, bună execuție, tehnică: procent + durată în luni de la PVRTL); **penalități** (execuție / plată: %/zi + plafon); termenul de plată în zile de la comunicarea facturii; PVRTL (data).
- **Acte adiționale** cu stare **Propus / Semnat** (doar cele semnate intră în valoare; cele propuse apar ca „valoare propusă”).
- **Facturile din fișă** (`FacturaContract`): membrul facturat, nr., dată, tip (avans tranșa n / lucrări / livrare / altele), nr. situației de plată, lucrări în moneda contractului, storno avans (pe tranșă), curs, RON net / TVA / total, scadență, observații. Facturile adresate firmei active se **leagă de factura din modulul Facturi** (`FacturaId`, prin contractul public existent; câmpurile se propun din factură: liniile negative = storno avans); cele adresate celuilalt membru al asocierii se introduc / importă în fișă (nu sunt facturile firmei active, deci nu intră în modulul Facturi).
- **Plăți** (`PlataContract`): factura din fișă, data, suma RON, instrumentul (OP / bilet la ordin / CEC / compensare), numărul, scadența instrumentului, **confirmată / doar emisă**. În spatele unei interfețe pe care modulul Financiar o va putea înlocui.
- **Instrumente de garanție** (`InstrumentGarantie`): tip (SGB avans / bună execuție / tehnică, bilet la ordin, altul), emitent, nr., membrul beneficiar, valoare, monedă, emitere, expirare / scadență, stare, documentul atașat.
- **Registre:** obligații și termene; acțiuni (prioritate, stare, responsabil = utilizator din aplicație, termen); faze de execuție; aspecte de clarificat (riscuri). Sursele = documentele atașate (există), cu dată și observații.

**Editarea (și a contractelor importate):** contractul importat intră ca **ciornă** — se modifică liber până la „Activează” (semnat). După activare, datele de bază (valoare, avans, garanții, termene) se corectează cu motiv și istoric (`CorectieContract`, ca la notele de comandă, D-44; regula 6). Registrele (facturi din fișă, plăți, instrumente, obligații, acțiuni, faze, riscuri) se editează oricând; modificările rămân în jurnalul de audit.

## 3. Calculul fișei (pur, `FisaContract`)

Pe fiecare membru și total: valoarea actualizată (semnate) și propusă (cu nesemnate); avansul contractual pe tranșe, facturat, recuperat (storno), rămas; lucrările facturate (brut, în moneda contractului), procentul și restul de executat; facturat RON net / TVA / total, plătit confirmat, emis neconfirmat, sold; garanțiile datorate față de instrumentele constituite; facturi scadente / depășite; termen depășit → penalitatea posibilă (zile × %/zi × valoare, cu plafonul). Teste cu valorile din fișe (calculate de mână).

## 4. Importul fișelor din Excel

- Cititor comun de .xlsx cu **toate foile** (nume, celule cu valoare numerică, nu text formatat) în `BuildingBlocks` (cel din Stocuri citește o singură foaie; îl va putea folosi și el).
- `InterpretorFisaContract` (pur, Contracte): foile se recunosc după nume („Date Generale”, „Valoare contract”, „Obligatii”, „Situatii de lucrari” / „Facturi Plati”, „Garantii”, „Actiuni”, „Surse”, „Termene executie”), câmpurile după eticheta din coloana A (variantele din cele trei fișe: „Valoare contract EUR fără TVA” / „Valoare inițială” / „Valoare contractuală”), tabelele după antet (coloanele diferă între fișe). Fiecare câmp are certitudine (Sigur / Nesigur / Lipsă), ca la facturi; textele de tip „De completat” / „De verificat” rămân text sau stare, nu devin valori.
- Ecranul de import: previzualizarea pe tab-urile fișei, corectarea, alegerea proiectului și a subantreprenorului (căutat după CUI, adăugat dacă lipsește), confirmarea → contract **ciornă**. Fișierul original se atașează la contract.
- Verificare pe cele trei fișe reale, local (ca la facturi, cu aplicația de test; fișierele nu intră în git).

## 5. Exportul „Fișă de urmărire contract” (.xlsx)

Generat din aplicație (OpenXml), în formatul fișelor de azi: aceleași foi, coloane, culori și formate, cu formulele de total (SUM) și rândurile nesemnate pe galben; pe fiecare membru al asocierii și total. Data fișei = data exportului. Testul: exportul unui contract, reimportat, dă același contract.

## 6. Etape

1. ✅ **Modelul și fișa în aplicație:** entitățile de mai sus, migrarea, ecranul contractului cu tab-urile fișei, editarea (ciornă liber, corecturi după activare), registrele.
2. ✅ **Facturi, avans și plăți:** facturile din fișă (legate de Facturi sau introduse pentru celălalt membru), cursul, stornarea avansului, plățile; calculul `FisaContract`; alertele (scadențe, expirări de garanții, termene depășite).
3. ✅ **Importul fișelor** existente (cele trei fișe ca verificare).
4. ✅ **Exportul** în formatul fișei.

**Etapa 1, implementat:** condițiile (`ConditiiContract`: tip preț, durată, amplasament, stadiu, predare amplasament, PVRTL, membrii asocierii cu cotele, tranșele de avans, garanțiile de avans / tehnică, penalitățile) pe `ContractPartener`; registrele `ObligatieContract`, `ActiuneContract`, `FazaExecutie`, `RiscContract`, `InstrumentGarantie` (`FisaContract.cs`) cu endpointurile `…/{id}/fisa/{obligatii|actiuni|faze|riscuri|instrumente}` (`EndpointuriFisaContract`); actele adiționale propuse / semnate (`ActAditional.Semnat`, `…/acte-aditionale/{actId}/semnare`, `SituatieContract.ValoarePropusa`); corectura contractului semnat (`ContractPartener.Corecteaza` + `CorectieContract`, `PUT` cu `motiv`). Ecranul: fișa contractului cu tab-urile foilor (1–8 + corecturi), editorul cu asocierea, tranșele, garanțiile și penalitățile; registrele prin `RegistreFisa.tsx`.

**Etapa 2, implementat:** `FacturaContract` (membrul facturat, legată de factura din Facturi sau introdusă, tip avans / lucrări / livrare, tranșa, situația de plată, valoarea și stornarea avansului în moneda contractului, cursul, RON net / TVA / total, scadența) și `PlataContract` (OP / bilet la ordin / CEC / compensare, confirmată sau doar emisă) în `FacturiPlatiContract.cs`; propunerea rândului din liniile facturii (`PropunereFacturaFisa`, prin `IInterogareFacturiContract.PentruFisaAsync`); calculul pur `CalculFisaContract` (pe membru și total: avans pe tranșe facturat / recuperat / rămas, lucrări facturate și rest de executat, RON, plătit confirmat / emis, sold, garanții datorate; alertele: facturi scadente, instrumente care expiră, termen depășit cu penalitatea plafonată, acte propuse, acțiuni depășite); endpointurile `…/facturi-fisa`, `…/plati` (`EndpointuriFacturiPlati`). „Facturat” din situația contractului = lucrările din fișă + facturile legate încă neadăugate. Ecranul: tab-ul „4. Facturi și plăți”, alertele în capul fișei.

**Etapa 3, implementat:** cititorul comun `CititorFoiXlsx` (BuildingBlocks, toate foile) → `FoaieTabel` + `ValoriFoaie` (SharedKernel: numere în ambele scrieri, date, procente); interpretorul pur `InterpretorFisaContract` (`Contracte/Import`): date generale (număr / dată, subantreprenor + CUI, antreprenori → asocierea cu firma activă recunoscută, valoare, monedă, perioadă, stadiu; rândurile nerecunoscute → observații), acte adiționale semnate / propuse, tranșele avansului („5% + 10% + 5%”, „două tranșe a câte 25%”), obligațiile (termenul de plată), facturile (coloanele după antet; EUR din RON la curs când lipsesc), plățile („Achitat …” = confirmată, „BO emis” = bilet la ordin), garanțiile și penalitățile, instrumentele, acțiunile, sursele (registru nou `SursaContract`), fazele. Endpointurile `POST /api/contracte/import/analiza` și `/proiecte/{id}/import` (`EndpointuriImportFisa`): contractul ciornă cu tot conținutul și fișierul original atașat; rândurile invalide sunt sărite cu avertisment. Ecranul: „Importă fișă (Excel)” în lista contractelor. Verificat pe cele trei fișe reale (`Teste.Formulare/FisaContractImportTeste`, doar local) și pe foi sintetice (`Teste.Unitare`).

**Etapa 4, implementat:** `ExportFisaContract` (Contracte.Infrastructure, OpenXml) din detaliile contractului (aceleași date ca fișa din aplicație): foile 1–8 în formatul fișelor (titlu `#183153`, antete `#DCEAF5`, nesemnate `#FFF1D6`, `#,##0.00 "EUR"`, totaluri SUM cu valoarea salvată, coloane pe fiecare membru), plus „9. Reconciliere” (situația pe antreprenor, alertele) și „10. Corecturi”. `GET /api/contracte/proiecte/{proiectId}/{id}/fisa.xlsx`; butonul „Fișa Excel” în fișa contractului. Controlul: fișele reale importate → exportate → reimportate dau aceleași date (contract, asociere, tranșe, acte, facturi, plăți, registre, garanții, penalități); singura diferență, CUI-ul fără „RO” (aplicația îl păstrează normalizat).

Mai târziu: legătura contractului cu articolele subcontractate din deviz (bugetul proiectului, D-55), situațiile subantreprenorului pe cantități (REQ-SIT-04), contractul cu beneficiarul (același model, direcția „beneficiar”), cursul BNR automat.

## 7. Întrebări deschise
- Firma activă în aplicație e unul dintre membri (ex. GCC); facturile celuilalt membru (MELSAM) se introduc în fișă. Dacă MELSAM va lucra și ea în aplicație (ca firmă separată), fișa comună cere o regulă de partajare între firme (azi RLS separă firmele) — de discutat atunci.
