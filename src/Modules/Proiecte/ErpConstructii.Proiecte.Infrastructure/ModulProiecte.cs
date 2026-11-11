using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Proiecte.Infrastructure;

public sealed record ProiectDto(
    Guid Id,
    string Cod,
    string Denumire,
    string Beneficiar,
    string? CuiBeneficiar,
    string AdresaSantier,
    string Stare,
    DateOnly? DataInceput,
    DateOnly? TermenContractual,
    DateTimeOffset CreatLa,
    bool Arhivat = false,
    DateTimeOffset? ArhivatLa = null,
    string? ArhivatDe = null,
    string Tip = "Lucrare");

/// <param name="Tip">„Lucrare” (implicit) sau „Sediu” (D-47: centru de cost al firmei, fără beneficiar).</param>
public sealed record CreeazaProiectCerere(string Cod, string Denumire, string? Beneficiar, string? CuiBeneficiar, string AdresaSantier, string? Tip = null);

/// <summary>Editarea completă a proiectului (administrator): toate datele, inclusiv starea și datele contractuale.</summary>
public sealed record ModificaProiectCerere(
    string Cod, string Denumire, string? Beneficiar, string? CuiBeneficiar, string? AdresaSantier, string Stare, DateOnly? DataInceput, DateOnly? TermenContractual,
    string? Tip = null);

public sealed record SantierDto(Guid Id, Guid ProiectId, string Denumire, string? Adresa, bool Activ);

public sealed record SantierCerere(string Denumire, string? Adresa, bool Activ = true);

public sealed record CompartimentDto(Guid Id, string Denumire, bool Activ);

public sealed record CompartimentCerere(string Denumire, bool Activ = true);

public sealed class ModulProiecte : IModul
{
    private static ProiectDto Dto(Proiect p) => new(p.Id, p.Cod, p.Denumire, p.Beneficiar, p.CuiBeneficiar, p.AdresaSantier, p.Stare.ToString(),
        p.DataInceput, p.TermenContractual, p.CreatLa, p.Arhivat, p.ArhivatLa, p.ArhivatDeNume, p.Tip.ToString());

    /// <summary>„Lucrare” / „Sediu” (lipsă = lucrare); null dacă textul nu e un tip.</summary>
    private static TipProiect? TipDin(string? text) =>
        string.IsNullOrWhiteSpace(text) ? TipProiect.Lucrare
        : Enum.TryParse<TipProiect>(text, true, out var t) && Enum.IsDefined(t) ? t : null;

    public string Nume => ProiecteDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AdaugaDbContextModul<ProiecteDbContext>(configurare, Nume);
        servicii.AddScoped<IInterogareProiecte, InterogareProiecte>();
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var grup = rute.MapGroup("/proiecte").WithTags("Proiecte");
        var vizualizare = Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Creare);
        var editare = Permisiuni.Pentru(ModulErp.Proiecte, Actiune.Editare);
        var vizualizareAprovizionare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        var administrare = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Editare);

        // Lista: doar proiectele pe care utilizatorul are drept (firma vine deja filtrată prin RLS).
        // arhivate = true: doar proiectele arhivate (D-39); altfel doar cele active.
        grup.MapGet("/", async (
            bool? arhivate,
            ProiecteDbContext db,
            IServiciuPermisiuni permisiuni,
            IUtilizatorCurent utilizator,
            IFirmaCurenta firma,
            CancellationToken ct) =>
        {
            var evaluator = await permisiuni.PentruUtilizatorAsync(utilizator.UtilizatorId!.Value, ct);
            var accesibile = evaluator.ProiecteAccesibile(vizualizare, firma.FirmaId!.Value);

            var doarArhivate = arhivate == true;
            var interogare = db.Proiecte.AsNoTracking().Where(p => p.Arhivat == doarArhivate);
            if (accesibile is not null)
            {
                // Listă concretă: EF traduce List.Contains în „id = ANY(@lista)”.
                var lista = accesibile.ToList();
                interogare = interogare.Where(p => lista.Contains(p.Id));
            }

            return await interogare
                .OrderBy(p => p.Cod)
                .Select(p => Dto(p))
                .ToListAsync(ct);
        }).CerePermisiune(vizualizare);

        grup.MapGet("/{proiectId:guid}", async (Guid proiectId, ProiecteDbContext db, CancellationToken ct) =>
        {
            var p = await db.Proiecte.AsNoTracking().FirstOrDefaultAsync(x => x.Id == proiectId, ct);
            return p is null
                ? Results.NotFound()
                : Results.Ok(Dto(p));
        }).CerePermisiune(vizualizare);

        grup.MapPost("/", async (CreeazaProiectCerere cerere, ProiecteDbContext db, CancellationToken ct) =>
        {
            var cod = cerere.Cod.Trim().ToUpperInvariant();
            if (await db.Proiecte.AnyAsync(p => p.Cod == cod, ct))
            {
                return Results.Conflict(new { mesaj = $"Există deja un proiect cu codul {cod}." });
            }

            if (TipDin(cerere.Tip) is not { } tip)
            {
                return Results.BadRequest(new { mesaj = "Tipul proiectului nu este valid (Lucrare sau Sediu)." });
            }

            if (Proiect.Valideaza(tip, cerere.Cod, cerere.Denumire, cerere.Beneficiar) is { Count: > 0 } probleme)
            {
                return Results.BadRequest(new { mesaj = string.Join(" ", probleme) });
            }

            var proiect = new Proiect(cerere.Cod, cerere.Denumire, cerere.Beneficiar ?? string.Empty, cerere.CuiBeneficiar, cerere.AdresaSantier ?? string.Empty, tip);
            db.Proiecte.Add(proiect);
            // D-38: fiecare proiect pornește cu un șantier (la adresa lui); altele se adaugă din fișa proiectului.
            // Sediul (D-47) pornește cu locul lui principal; birourile, baza, depozitul se adaugă la fel.
            db.Santiere.Add(Santier.Creeaza(proiect.Id, tip == TipProiect.Sediu ? "Sediu" : "Șantier principal", proiect.AdresaSantier).Valoare);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/proiecte/{proiect.Id}", new { proiect.Id });
        }).CerePermisiune(creare);

        // Arhivarea / reactivarea (D-39): ca editarea, doar administratorul.
        grup.MapPost("/{proiectId:guid}/arhivare", async (Guid proiectId, ProiecteDbContext db, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct) =>
        {
            var p = await db.Proiecte.FirstOrDefaultAsync(x => x.Id == proiectId, ct);
            if (p is null)
                return Results.NotFound();
            var r = p.Arhiveaza(utilizator.Nume ?? string.Empty, ceas.Acum);
            if (r.Esec)
                return Results.Conflict(new { mesaj = r.Eroare.Mesaj });
            await db.SaveChangesAsync(ct);
            return Results.Ok(Dto(p));
        }).WithName("ArhiveazaProiect").CerePermisiunePeFirma(editare);

        grup.MapPost("/{proiectId:guid}/reactivare", async (Guid proiectId, ProiecteDbContext db, CancellationToken ct) =>
        {
            var p = await db.Proiecte.FirstOrDefaultAsync(x => x.Id == proiectId, ct);
            if (p is null)
                return Results.NotFound();
            var r = p.Reactiveaza();
            if (r.Esec)
                return Results.Conflict(new { mesaj = r.Eroare.Mesaj });
            await db.SaveChangesAsync(ct);
            return Results.Ok(Dto(p));
        }).WithName("ReactiveazaProiect").CerePermisiunePeFirma(editare);

        // Editarea completă: doar cine are dreptul de editare pe proiecte pe toată firma (administratorul).
        grup.MapPut("/{proiectId:guid}", async (Guid proiectId, ModificaProiectCerere cerere, ProiecteDbContext db, CancellationToken ct) =>
        {
            var p = await db.Proiecte.FirstOrDefaultAsync(x => x.Id == proiectId, ct);
            if (p is null)
                return Results.NotFound();
            if (!Enum.TryParse<StareProiect>(cerere.Stare, true, out var stare))
                return Results.BadRequest(new { mesaj = "Starea proiectului nu este validă." });
            if (TipDin(cerere.Tip ?? p.Tip.ToString()) is not { } tip)
                return Results.BadRequest(new { mesaj = "Tipul proiectului nu este valid (Lucrare sau Sediu)." });
            var cod = (cerere.Cod ?? string.Empty).Trim().ToUpperInvariant();
            if (await db.Proiecte.AnyAsync(x => x.Cod == cod && x.Id != proiectId, ct))
                return Results.Conflict(new { mesaj = $"Există deja un proiect cu codul {cod}." });
            var r = p.Modifica(cerere.Cod ?? string.Empty, cerere.Denumire ?? string.Empty, cerere.Beneficiar ?? string.Empty, cerere.CuiBeneficiar,
                cerere.AdresaSantier ?? string.Empty, stare, cerere.DataInceput, cerere.TermenContractual, tip);
            if (r.Esec)
                return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
            await db.SaveChangesAsync(ct);
            return Results.Ok(Dto(p));
        }).WithName("ModificaProiect").CerePermisiunePeFirma(editare);

        // Șantierele proiectului (D-38). Lista o folosesc și necesarele de materiale (aprovizionare).
        grup.MapGet("/{proiectId:guid}/santiere", async (Guid proiectId, bool? toate, ProiecteDbContext db, CancellationToken ct) =>
            await db.Santiere.AsNoTracking().Where(s => s.ProiectId == proiectId && (toate == true || s.Activ))
                .OrderByDescending(s => s.Activ).ThenBy(s => s.Denumire)
                .Select(s => new SantierDto(s.Id, s.ProiectId, s.Denumire, s.Adresa, s.Activ)).ToListAsync(ct))
            .WithName("SantiereProiect").CereOricarePermisiune(vizualizare, vizualizareAprovizionare);

        grup.MapPost("/{proiectId:guid}/santiere", async (Guid proiectId, SantierCerere cerere, ProiecteDbContext db, CancellationToken ct) =>
        {
            if (!await db.Proiecte.AnyAsync(x => x.Id == proiectId, ct))
                return Results.NotFound();
            var r = Santier.Creeaza(proiectId, cerere.Denumire ?? string.Empty, cerere.Adresa);
            if (r.Esec)
                return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
            var nume = r.Valoare.Denumire.ToLower();
            if (await db.Santiere.AnyAsync(s => s.ProiectId == proiectId && s.Denumire.ToLower() == nume, ct))
                return Results.Conflict(new { mesaj = $"Proiectul are deja șantierul „{r.Valoare.Denumire}”." });
            db.Santiere.Add(r.Valoare);
            await db.SaveChangesAsync(ct);
            var s = r.Valoare;
            return Results.Created($"/api/proiecte/{proiectId}/santiere/{s.Id}", new SantierDto(s.Id, s.ProiectId, s.Denumire, s.Adresa, s.Activ));
        }).WithName("AdaugaSantier").CerePermisiune(editare);

        grup.MapPut("/{proiectId:guid}/santiere/{id:guid}", async (Guid proiectId, Guid id, SantierCerere cerere, ProiecteDbContext db, CancellationToken ct) =>
        {
            var s = await db.Santiere.FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
            if (s is null)
                return Results.NotFound();
            if (s.Activ && !cerere.Activ &&
                StergereSantier.VerificaDezactivarea(s, await db.Santiere.Where(x => x.ProiectId == proiectId).ToListAsync(ct)) is { Esec: true } dezactivare)
                return Results.BadRequest(new { mesaj = dezactivare.Eroare.Mesaj });
            var r = s.Modifica(cerere.Denumire ?? string.Empty, cerere.Adresa, cerere.Activ);
            if (r.Esec)
                return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
            var nume = s.Denumire.ToLower();
            if (await db.Santiere.AnyAsync(x => x.ProiectId == proiectId && x.Id != id && x.Denumire.ToLower() == nume, ct))
                return Results.Conflict(new { mesaj = $"Proiectul are deja șantierul „{s.Denumire}”." });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new SantierDto(s.Id, s.ProiectId, s.Denumire, s.Adresa, s.Activ));
        }).WithName("ModificaSantier").CerePermisiune(editare);

        // Șantierul pus din greșeală se șterge; documentele lui (necesarele) se mută întâi pe alt șantier activ al proiectului.
        // Fără mutaPe și cu documente: 409, cu numărul documentelor, ca interfața să ceară șantierul.
        grup.MapDelete("/{proiectId:guid}/santiere/{id:guid}", async (
            Guid proiectId, Guid id, Guid? mutaPe, ProiecteDbContext db, IEnumerable<IUtilizariSantier> utilizari, CancellationToken ct) =>
        {
            var santiere = await db.Santiere.Where(x => x.ProiectId == proiectId).ToListAsync(ct);
            var s = santiere.FirstOrDefault(x => x.Id == id);
            if (s is null)
                return Results.NotFound();
            var documente = 0;
            foreach (var u in utilizari)
                documente += await u.NumaraAsync(id, ct);
            var r = StergereSantier.Verifica(s, santiere, documente, mutaPe);
            if (r.Esec)
                return r.Eroare.Cod == StergereSantier.CodDocumenteDeMutat
                    ? Results.Conflict(new { mesaj = r.Eroare.Mesaj, documente })
                    : Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
            if (r.Valoare is { } tinta)
            {
                var loc = new LocSolicitant(tinta.Id, true, tinta.Denumire, tinta.Activ);
                foreach (var u in utilizari)
                    await u.MutaAsync(id, loc, ct);
            }

            db.Santiere.Remove(s);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithName("StergeSantier").CerePermisiune(editare);

        // Compartimentele firmei (D-38): le administrează administratorul; le citesc și necesarele de materiale.
        grup.MapGet("/compartimente", async (bool? toate, ProiecteDbContext db, CancellationToken ct) =>
            await db.Compartimente.AsNoTracking().Where(c => toate == true || c.Activ)
                .OrderByDescending(c => c.Activ).ThenBy(c => c.Denumire)
                .Select(c => new CompartimentDto(c.Id, c.Denumire, c.Activ)).ToListAsync(ct))
            .WithName("CompartimenteFirma").CereOricarePermisiune(vizualizare, vizualizareAprovizionare, administrare);

        grup.MapPost("/compartimente", async (CompartimentCerere cerere, ProiecteDbContext db, CancellationToken ct) =>
        {
            var r = Compartiment.Creeaza(cerere.Denumire ?? string.Empty);
            if (r.Esec)
                return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
            var nume = r.Valoare.Denumire.ToLower();
            if (await db.Compartimente.AnyAsync(c => c.Denumire.ToLower() == nume, ct))
                return Results.Conflict(new { mesaj = $"Există deja compartimentul „{r.Valoare.Denumire}”." });
            db.Compartimente.Add(r.Valoare);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/proiecte/compartimente/{r.Valoare.Id}", new CompartimentDto(r.Valoare.Id, r.Valoare.Denumire, r.Valoare.Activ));
        }).WithName("AdaugaCompartiment").CerePermisiunePeFirma(administrare);

        grup.MapPut("/compartimente/{id:guid}", async (Guid id, CompartimentCerere cerere, ProiecteDbContext db, CancellationToken ct) =>
        {
            var c = await db.Compartimente.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null)
                return Results.NotFound();
            var r = c.Modifica(cerere.Denumire ?? string.Empty, cerere.Activ);
            if (r.Esec)
                return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
            var nume = c.Denumire.ToLower();
            if (await db.Compartimente.AnyAsync(x => x.Id != id && x.Denumire.ToLower() == nume, ct))
                return Results.Conflict(new { mesaj = $"Există deja compartimentul „{c.Denumire}”." });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new CompartimentDto(c.Id, c.Denumire, c.Activ));
        }).WithName("ModificaCompartiment").CerePermisiunePeFirma(administrare);
    }
}
