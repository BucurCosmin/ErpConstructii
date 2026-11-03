# ERP pentru construcții

ERP pentru firme de construcții din România (după modelul Charisma ERP): devize, contracte, situații de lucrări, grafice de execuție, șantier, formulare Word, e-Factura.

- **Stack:** .NET 10, ASP.NET Core, EF Core, PostgreSQL, Open XML SDK, Gotenberg (PDF).
- **Arhitectură:** monolit modular; schemă PostgreSQL per modul; Row-Level Security pe firmă.

## Pornire rapidă

```bash
docker compose up -d
dotnet restore && dotnet build && dotnet test
dotnet run --project src/Host/ErpConstructii.Api
```

## Documentație

- [CLAUDE.md](CLAUDE.md): starea proiectului, reguli, convenții, backlog
- [docs/cerinte.md](docs/cerinte.md): cerințele cu ID-uri și faze
- [docs/decizii.md](docs/decizii.md): deciziile de arhitectură
