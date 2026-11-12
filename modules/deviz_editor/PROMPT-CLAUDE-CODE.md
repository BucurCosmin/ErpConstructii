# Prompt pentru Claude Code: importul devizelor ISDP

Copiază conținutul acestei arhive în rădăcina repo-ului, păstrând căile. Apoi dă-i lui Claude Code textul de mai jos.

---

Citește `modules/deviz_editor/docs/import-isdp.md` în întregime înainte de orice modificare. Descrie formatul exporturilor ISDP (Intersoft), cu reguli verificate pe fișiere reale, plus schimbările de model necesare și legăturile cu celelalte module.

Am adăugat:
- `src/Modules/Devize/ErpConstructii.Devize.Import.Isdp`: parser pur, deja testat (27 de teste pe date reale);
- `src/Modules/Devize/ErpConstructii.Devize.Import.Isdp.Excel`: adaptor ExcelDataReader, **necompilat încă**;
- `tests/ErpConstructii.Teste.Isdp` și `tests/samples/isdp/*.json`.

Fă, în ordine, și oprește-te după fiecare pas cu build și teste verzi:

1. **Integrare.** Adaugă cele 3 proiecte în soluție. Pune `ExcelDataReader` în `Directory.Packages.props`, la ultima versiune stabilă. `dotnet build` și `dotnet test` trebuie să fie verzi. Nu modifica logica parserului ca să treacă testele; dacă un test pică, explică de ce.
2. **Test de acceptanță pentru adaptorul Excel.** Pun fișierele originale în `tests/samples-private/isdp/`: `extras_de_resurse.xls` și `sapare_chilleri.xlsx`. Scrie un test care le citește cu `CititorExcel` și verifică:
   - parserul dă același rezultat ca pe fixture-urile JSON (15 articole, T3 = 521.134,09, curs dedus 5,23, 60/60 componente F3 identice);
   - testul se sare dacă fișierele lipsesc.
3. **Modelul de deviz** (§6 din spec), adaptat la codul ACTUAL al aplicației:
   - capitole arborescente cu cod WBS;
   - a cincea resursă, Echipament;
   - costul direct și prețul de ofertă pe articol;
   - monedă și curs pe versiune;
   - `CantitateTotala` și `ConsumUnitar` pe consum;
   - politica de rotunjire „Fara” pentru sursele ISDP;
   - precizia coloanelor.

   Fă migrarea EF pentru toate acestea, cu RLS pe tabelele noi. Arată-mi planul de schimbări **înainte** să-l aplici, pentru că atinge entități existente.
4. **Nomenclatorul de resurse al firmei** (§6.3): entitatea, crearea automată a codurilor nemapate la import, endpoint-ul de mapare.
5. **Importatorul `isdp`** în modulul Devize (§7):
   - implementează contractul de importator existent;
   - acceptă unul sau două fișiere (deviz cu resurse, plus F3 opțional);
   - previzualizare, apoi confirmare;
   - reimport cu păstrarea `CheieStabila` (§7.1).

   Înlocuiește importatorul WinDoc: formatul real este ISDP.
6. **Extrasul de resurse** pe versiune (endpoint), ca bază pentru aprovizionare (§8). Nu implementa încă aprovizionarea; doar expune necesarul pe resursă, cu articolele din care provine.

Actualizează `docs/cerinte.md` și `docs/decizii.md` (D-18: ISDP în loc de WinDoc) și marchează ce s-a implementat.

## Partea a doua — editorul de deviz (după ce pașii 1–6 sunt verzi)

Citește `docs/editor-deviz.md`. Decizia D-19: după contract, **ERP-ul este sursa de adevăr pentru deviz**; ISDP rămâne pentru ofertele mari; fără sincronizare bidirecțională.

Implementează în ordinea din §9 a acelui document, câte un pas pe rând, cu criteriul de acceptare din tabel ca test:

7. Ciclul de versiuni (ofertă → contract → buget → act adițional / buget revizuit), contract curent și buget curent pe proiect.
8. Operațiile de editare pe server, jurnalul de operații cu undo, recalculul incremental, concurența optimistă.
9. Biblioteca de norme, populată la fiecare import cu `ExtractorNorme` (deja scris și testat: 11 norme din devizul real).
10. Catalogul de prețuri al resurselor și operația „actualizează prețurile”.
11. Compararea versiunilor și actul adițional.
12. Exporturile (F3, deviz-ofertă cu resurse, extras de resurse, lista actului adițional).
13. Interfața editorului — **NU începe până nu decid D-20** (AG Grid Enterprise sau grilă gratuită). Pregătește doar API-ul.

Adaugă D-19 și D-20 (deschisă) în `docs/decizii.md`.

---

Întrebările deschise sunt în §10 din `import-isdp.md` și din `editor-deviz.md`. Nu presupune răspunsurile.
