# Decizii de arhitectură

Format scurt: decizia, motivul, consecința. O decizie se schimbă doar cu o intrare nouă care o înlocuiește (nu se rescrie istoria).

| ID | Decizie | Motiv | Consecință |
|---|---|---|---|
| D-01 | **Monolit modular în .NET 10** (ASP.NET Core), un proces, module cu granițe clare. | Echipă mică; microserviciile nu se justifică la început. | Granițele de modul permit extragerea ulterioară a unui serviciu, dacă devine necesar. |
| D-02 | **PostgreSQL** (decis). | Fără licență, tranzacții solide, JSONB, RLS, colecții native. | EF Core + Npgsql; Dapper pentru rapoartele grele. |
| D-03 | **Schemă și DbContext per modul**; istoricul migrărilor în schema modulului. | Modulele evoluează independent. | Fără navigări EF între module: referințe doar prin Id. |
| D-04 | **Row-Level Security pe `firma_id`**, setat la deschiderea conexiunii (`app.firma_id`). | O greșeală în cod nu poate expune datele altei firme. | Rolul aplicației fără BYPASSRLS; migrările rulează cu alt rol. Schema `identitate` este excepție (login pe mai multe firme). |
| D-05 | **Articolul de deviz este pivotul**; versiunile de deviz aprobate sunt imuabile. | Controlul costurilor funcționează doar dacă totul se leagă de articol. | Cheie stabilă pe articol între versiuni; legăturile din alte module referă articolul. |
| D-06 | **Importatori ca module** (plugin) pentru devize, grafice, formulare: contract `IImportator<T>` + model canonic. | Formatele externe se înmulțesc; nucleul nu trebuie atins. | Un format nou = un proiect nou + o linie de înregistrare. Importatorii nu ating baza de date. |
| D-07 | **Autentificare: ASP.NET Core Identity + OpenIddict** (OAuth2/OIDC) în aplicație. Alternativa: Keycloak, dacă SSO/federarea devin necesare devreme. | Fără dependență externă la început. | Până la implementare, Host-ul pornește doar în Development (autentificare prin headere). |
| D-08 | **Fără portal pentru externi** în această etapă. | Decizia clientului. | Aprobările beneficiarului/dirigintelui se fac pe PDF semnat, atașat de managerul de proiect. Modelul de drepturi permite portalul mai târziu. |
| D-09 | **Formulare: șabloane Word ale firmei cu controale de conținut etichetate**; Open XML SDK. | Același șablon servește generării și importului exact al formularelor completate manual. | Convenția de etichete este contract cu firmele (documentată în `OpenXmlUtil`). |
| D-10 | **PDF gratuit: LibreOffice headless prin Gotenberg**, serviciu separat. | Cerință explicită: fără licențe comerciale. | Fidelitatea față de Word poate varia la șabloane complexe; se testează pe șabloanele pilotului. |
| D-11 | **e-Factura canal principal; XML UBL CIUS-RO = formatul intern al facturii.** | O singură sursă de adevăr. | PDF / export contabil sunt convertoare; integrarea ANAF stă într-un proiect separat. |
| D-12 | **Grafice: Primavera P6 și Excel primele**, MS Project la cerere; MPXJ pentru citire/scriere. | Ce folosesc firmele-țintă. | De verificat licența MPXJ (LGPL) la integrare. Excel fără dependențe → prognoză liniară. |
| D-13 | **Până în faza 3, contabilitatea rămâne în programul clientului**; exportatori contabili ca module. | Livrare mai rapidă a valorii din construcții. | Import al stării plăților din contabilitate pentru scadențar și cash-flow. |
| D-14 | **Clienți țintă: firme medii (20–250 angajați)**, arhitectură pregătită pentru firme mari. | Decizia clientului. | Multi-firmă, audit, RLS, API intern din prima zi; grupuri, workflow complex, SSO mai târziu. |
| D-15 | **Identificatorii de domeniu și comentariile în română** (fără diacritice în identificatori); termenii tehnici rămân în engleză. | Domeniul (deviz, situație, NIR) nu are traduceri fidele; codul se citește alături de documentele firmei. | Convenție în CLAUDE.md. |
| D-16 | **Sume `decimal`, rotunjire explicită „jumătate departe de zero”, cote nehardcodate.** | Devizele se verifică la ban față de programul sursă; cotele legale se schimbă. | `Rotunjire`, `ParametriRecapitulatie`; teste cu valori calculate de mână. |
| D-17 | **Frontend: React + TypeScript cu un grid serios (AG Grid)**; mobil offline-first (.NET MAUI sau React Native — de ales în faza 2). | Devizele și situațiile sunt tabele mari editabile. | Frontend-ul nu e încă început. |
