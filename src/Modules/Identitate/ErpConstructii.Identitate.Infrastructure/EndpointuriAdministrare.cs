using ErpConstructii.Identitate.Infrastructure.Autentificare;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Identitate.Infrastructure;

// ---------------------------------------------------------------- DTO-uri

public sealed record CatalogPermisiuniDto(IReadOnlyList<ModulCatalog> Module, IReadOnlyList<PermisiuneCatalog> DateSensibile);

public sealed record UtilizatorListaDto(
    Guid Id,
    string Nume,
    string Email,
    string Tip,
    bool Activ,
    DateOnly? ExpiraLa,
    bool AreAcces,
    bool MfaActivat,
    bool Blocat,
    bool TrebuieSchimbataParola,
    IReadOnlyList<string> Roluri);

public sealed record AtribuireDto(Guid Id, Guid RolId, string Rol, Guid? ProiectId, DateOnly ValabilDe, DateOnly? ValabilPana, string Stare);

public sealed record UtilizatorDetaliiDto(
    UtilizatorListaDto Utilizator,
    IReadOnlyList<AtribuireDto> Atribuiri,
    bool PoateModificaContul,
    int AlteFirme);

public sealed record CreeazaUtilizatorCerere(string Email, string Nume, TipUtilizator Tip, Guid RolId, Guid? ProiectId, DateOnly? ValabilDe);

/// <param name="ParolaTemporara">Null când utilizatorul exista deja (a primit doar acces la firmă).</param>
public sealed record UtilizatorCreatDto(Guid Id, string? ParolaTemporara, bool Existent);

public sealed record ModificaUtilizatorCerere(string Nume, TipUtilizator Tip, DateOnly? ExpiraLa);

public sealed record AdaugaAtribuireCerere(Guid RolId, Guid? ProiectId, DateOnly ValabilDe, DateOnly? ValabilPana);

public sealed record ParolaTemporaraDto(string ParolaTemporara);

public sealed record RolDto(Guid Id, string Cod, string Denumire, IReadOnlyList<string> Permisiuni, bool Predefinit, int Utilizatori);

public sealed record CreeazaRolCerere(string Cod, string Denumire, IReadOnlyList<string> Permisiuni);

public sealed record ModificaRolCerere(string Denumire, IReadOnlyList<string> Permisiuni);

public sealed record SalveazaFirmaCerere(string Denumire, string? Cui);

public sealed record FirmaDetaliiDto(
    Guid Id,
    string Denumire,
    string? Cui,
    string? NrRegCom,
    Adresa Adresa,
    string? Telefon,
    string? Fax,
    string? Email,
    string? Website,
    IReadOnlyList<ContBancar> ConturiBancare);

public sealed record FirmaAdminDto(Guid Id, string Denumire, string? Cui, bool Activa, int Utilizatori);

/// <summary>
/// Administrarea utilizatorilor, rolurilor și atribuirilor pe firma activă (REQ-UTL-01, REQ-DRP-02/03/09),
/// plus firmele (doar administratorul de sistem).
///
/// Schema <c>identitate</c> nu are RLS, deci FIECARE interogare filtrează explicit pe firma activă.
/// Drepturile de administrare se cer pe toată firma (nu doar pe un proiect).
/// </summary>
internal static class EndpointuriAdministrare
{
    private static readonly string Vizualizare = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Vizualizare);
    private static readonly string Creare = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Creare);
    private static readonly string Editare = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Editare);
    private static readonly string Anulare = Permisiuni.Pentru(ModulErp.Administrare, Actiune.Anulare);

    private const string MesajFaraAdministrator =
        "Operația ar lăsa firma fără niciun administrator activ. Numește mai întâi alt administrator.";

    public static void Mapeaza(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/administrare").WithTags("Administrare");

        g.MapGet("/permisiuni", () => new CatalogPermisiuniDto(CatalogPermisiuni.Module, CatalogPermisiuni.DateSensibile))
            .CerePermisiunePeFirma(Vizualizare);

        g.MapGet("/utilizatori", ListaUtilizatori).CerePermisiunePeFirma(Vizualizare);
        g.MapGet("/utilizatori/{id:guid}", DetaliiUtilizator).CerePermisiunePeFirma(Vizualizare);
        g.MapPost("/utilizatori", CreeazaUtilizator).CerePermisiunePeFirma(Creare);
        g.MapPut("/utilizatori/{id:guid}", ModificaUtilizator).CerePermisiunePeFirma(Editare);
        g.MapPost("/utilizatori/{id:guid}/dezactivare", (Guid id, [AsParameters] Dependente d, CancellationToken ct) => SchimbaActivarea(id, false, d, ct))
            .CerePermisiunePeFirma(Anulare);
        g.MapPost("/utilizatori/{id:guid}/activare", (Guid id, [AsParameters] Dependente d, CancellationToken ct) => SchimbaActivarea(id, true, d, ct))
            .CerePermisiunePeFirma(Editare);
        g.MapPost("/utilizatori/{id:guid}/resetare-parola", ReseteazaParola).CerePermisiunePeFirma(Editare);
        g.MapPost("/utilizatori/{id:guid}/resetare-mfa", ReseteazaMfa).CerePermisiunePeFirma(Editare);
        g.MapPost("/utilizatori/{id:guid}/atribuiri", AdaugaAtribuire).CerePermisiunePeFirma(Editare);
        g.MapPost("/atribuiri/{id:guid}/revocare", RevocaAtribuire).CerePermisiunePeFirma(Editare);

        g.MapGet("/roluri", ListaRoluri).CerePermisiunePeFirma(Vizualizare);
        g.MapPost("/roluri", CreeazaRol).CerePermisiunePeFirma(Creare);
        g.MapPut("/roluri/{id:guid}", ModificaRol).CerePermisiunePeFirma(Editare);
        g.MapPost("/roluri/{id:guid}/restaurare", RestaureazaRol).CerePermisiunePeFirma(Editare);
        g.MapDelete("/roluri/{id:guid}", StergeRol).CerePermisiunePeFirma(Anulare);

        g.MapGet("/firma", CitesteFirma).CerePermisiunePeFirma(Vizualizare);
        g.MapPut("/firma", ModificaFirma).CerePermisiunePeFirma(Editare);

        // Firmele: doar administratorul de sistem (verificat în handler, pe tipul utilizatorului).
        g.MapGet("/firme", ListaFirme).CerePermisiunePeFirma(Vizualizare);
        g.MapPost("/firme", CreeazaFirma).CerePermisiunePeFirma(Creare);
    }

    /// <summary>Dependențele comune ale handler-elor (injectate de minimal API cu [AsParameters]).</summary>
    internal sealed class Dependente
    {
        public required IdentitateDbContext Db { get; init; }
        public required ServiciuConturi Conturi { get; init; }
        public required IServiciuPermisiuni Permisiuni { get; init; }
        public required IUtilizatorCurent Utilizator { get; init; }
        public required IFirmaCurenta Firma { get; init; }
        public required ICeas Ceas { get; init; }

        public Guid UtilizatorId => Utilizator.UtilizatorId ?? throw new InvalidOperationException("Utilizator neautentificat.");
        public Guid FirmaId => Firma.FirmaId ?? throw new InvalidOperationException("Nicio firmă activă.");

        public async Task<Utilizator> ActorAsync(CancellationToken ct) =>
            await Db.Utilizatori.AsNoTracking().FirstAsync(u => u.Id == UtilizatorId, ct);
    }

    // ---------------------------------------------------------------- Utilizatori

    private static async Task<IResult> ListaUtilizatori([AsParameters] Dependente d, CancellationToken ct)
    {
        var atribuiri = await d.Db.Atribuiri.AsNoTracking().Where(a => a.FirmaId == d.FirmaId).ToListAsync(ct);
        var idUtilizatori = atribuiri.Select(a => a.UtilizatorId).Distinct().ToList();
        var utilizatori = await d.Db.Utilizatori.AsNoTracking().Where(u => idUtilizatori.Contains(u.Id)).OrderBy(u => u.Nume).ToListAsync(ct);
        var roluri = await RoluriFirmeiAsync(d, ct);

        var lista = new List<UtilizatorListaDto>();
        foreach (var u in utilizatori)
        {
            lista.Add(await CaDtoAsync(d, u, atribuiri.Where(a => a.UtilizatorId == u.Id), roluri));
        }

        return Results.Ok(lista);
    }

    private static async Task<IResult> DetaliiUtilizator(Guid id, [AsParameters] Dependente d, CancellationToken ct)
    {
        var u = await d.Db.Utilizatori.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var toateAtribuirile = await d.Db.Atribuiri.AsNoTracking().Where(a => a.UtilizatorId == id).ToListAsync(ct);
        var dinFirma = toateAtribuirile.Where(a => a.FirmaId == d.FirmaId).ToList();
        if (u is null || dinFirma.Count == 0)
        {
            return Results.NotFound();
        }

        var roluri = await RoluriFirmeiAsync(d, ct);
        var azi = d.Ceas.Azi;
        var atribuiri = dinFirma
            .OrderByDescending(a => a.EsteValabila(azi)).ThenByDescending(a => a.ValabilDe)
            .Select(a => new AtribuireDto(
                a.Id, a.RolId, roluri.TryGetValue(a.RolId, out var r) ? r.Denumire : "(rol șters)",
                a.ProiectId, a.ValabilDe, a.ValabilPana,
                a.EsteValabila(azi) ? "activa" : a.ValabilDe > azi ? "viitoare" : "incheiata"))
            .ToList();
        var actor = await d.ActorAsync(ct);
        var firme = toateAtribuirile.Select(a => a.FirmaId).Distinct().ToList();

        return Results.Ok(new UtilizatorDetaliiDto(
            await CaDtoAsync(d, u, dinFirma, roluri),
            atribuiri,
            RegulaAdministrare.PoateModificaContul(actor.Tip, d.FirmaId, firme) && RegulaAdministrare.PoateGestionaTipul(actor.Tip, u.Tip),
            firme.Count(f => f != d.FirmaId)));
    }

    private static async Task<IResult> CreeazaUtilizator(CreeazaUtilizatorCerere cerere, [AsParameters] Dependente d, CancellationToken ct)
    {
        var actor = await d.ActorAsync(ct);
        if (!RegulaAdministrare.PoateGestionaTipul(actor.Tip, cerere.Tip))
        {
            return Eroare("Doar un administrator de sistem poate crea administratori de sistem.");
        }

        if (string.IsNullOrWhiteSpace(cerere.Nume) || string.IsNullOrWhiteSpace(cerere.Email) || !cerere.Email.Contains('@'))
        {
            return Eroare("Numele și o adresă de email validă sunt obligatorii.");
        }

        var rol = await d.Db.Roluri.FirstOrDefaultAsync(r => r.Id == cerere.RolId && r.FirmaId == d.FirmaId, ct);
        if (rol is null)
        {
            return Eroare("Rolul ales nu există în această firmă.");
        }

        var email = cerere.Email.Trim().ToLowerInvariant();
        var existent = await d.Db.Utilizatori.FirstOrDefaultAsync(u => u.Email == email, ct);
        string? parola = null;

        if (existent is not null)
        {
            // Utilizatorul lucrează deja pentru altă firmă: primește doar acces la aceasta (nu i se schimbă contul).
            if (await d.Db.Atribuiri.AnyAsync(a => a.UtilizatorId == existent.Id && a.FirmaId == d.FirmaId, ct))
            {
                return Results.Conflict(new { mesaj = $"{existent.Nume} are deja acces la această firmă. Adaugă-i roluri din fișa lui." });
            }
        }
        else
        {
            existent = new Utilizator(email, cerere.Nume, cerere.Tip);
            d.Db.Utilizatori.Add(existent);
        }

        d.Db.Atribuiri.Add(new Atribuire(existent.Id, rol.Id, new Domeniu(d.FirmaId, cerere.ProiectId), cerere.ValabilDe ?? d.Ceas.Azi));

        // Utilizatorul (business) și contul (autentificare) sunt în contexte diferite: dacă al doilea pas
        // eșuează, ștergem utilizatorul abia creat, ca să nu rămână un utilizator fără cont.
        var nou = d.Db.Entry(existent).State == EntityState.Added;
        await d.Db.SaveChangesAsync(ct);
        if (nou)
        {
            try
            {
                parola = await d.Conturi.CreeazaAsync(existent);
            }
            catch (InvalidOperationException ex)
            {
                d.Db.Atribuiri.RemoveRange(d.Db.Atribuiri.Where(a => a.UtilizatorId == existent.Id));
                d.Db.Utilizatori.Remove(existent);
                await d.Db.SaveChangesAsync(ct);
                return Eroare("Contul nu a putut fi creat: " + ex.Message);
            }
        }

        d.Permisiuni.Invalideaza(existent.Id);
        return Results.Created($"/api/administrare/utilizatori/{existent.Id}", new UtilizatorCreatDto(existent.Id, parola, !nou));
    }

    private static async Task<IResult> ModificaUtilizator(Guid id, ModificaUtilizatorCerere cerere, [AsParameters] Dependente d, CancellationToken ct)
    {
        var (u, eroare) = await UtilizatorCuDrepturiPeContAsync(id, d, ct);
        if (u is null)
        {
            return eroare!;
        }

        var actor = await d.ActorAsync(ct);
        if (!RegulaAdministrare.PoateGestionaTipul(actor.Tip, cerere.Tip))
        {
            return Eroare("Doar un administrator de sistem poate numi administratori de sistem.");
        }

        if (string.IsNullOrWhiteSpace(cerere.Nume))
        {
            return Eroare("Numele este obligatoriu.");
        }

        u.Modifica(cerere.Nume, cerere.Tip);
        u.SeteazaExpirare(cerere.ExpiraLa);
        await d.Db.SaveChangesAsync(ct);
        d.Permisiuni.Invalideaza(id);
        return Results.NoContent();
    }

    private static async Task<IResult> SchimbaActivarea(Guid id, bool activ, Dependente d, CancellationToken ct)
    {
        if (id == d.UtilizatorId && !activ)
        {
            return Eroare("Nu îți poți dezactiva propriul cont.");
        }

        var (u, eroare) = await UtilizatorCuDrepturiPeContAsync(id, d, ct);
        if (u is null)
        {
            return eroare!;
        }

        if (activ)
        {
            u.Activeaza();
        }
        else
        {
            u.Dezactiveaza();
            if (!await RamaneAdministratorAsync(d, ct, utilizatorDezactivat: id))
            {
                return Eroare(MesajFaraAdministrator);
            }
        }

        await d.Db.SaveChangesAsync(ct);
        if (!activ)
        {
            await d.Conturi.InchideSesiunileAsync(id, ct);
        }

        d.Permisiuni.Invalideaza(id);
        return Results.NoContent();
    }

    private static async Task<IResult> ReseteazaParola(Guid id, [AsParameters] Dependente d, CancellationToken ct)
    {
        var (u, eroare) = await UtilizatorCuDrepturiPeContAsync(id, d, ct);
        return u is null ? eroare! : Results.Ok(new ParolaTemporaraDto(await d.Conturi.ReseteazaParolaAsync(id, ct)));
    }

    private static async Task<IResult> ReseteazaMfa(Guid id, [AsParameters] Dependente d, CancellationToken ct)
    {
        var (u, eroare) = await UtilizatorCuDrepturiPeContAsync(id, d, ct);
        if (u is null)
        {
            return eroare!;
        }

        await d.Conturi.ReseteazaMfaAsync(id, ct);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- Atribuiri

    private static async Task<IResult> AdaugaAtribuire(Guid id, AdaugaAtribuireCerere cerere, [AsParameters] Dependente d, CancellationToken ct)
    {
        if (!await d.Db.Atribuiri.AnyAsync(a => a.UtilizatorId == id && a.FirmaId == d.FirmaId, ct))
        {
            return Results.NotFound();
        }

        if (!await d.Db.Roluri.AnyAsync(r => r.Id == cerere.RolId && r.FirmaId == d.FirmaId, ct))
        {
            return Eroare("Rolul ales nu există în această firmă.");
        }

        if (cerere.ValabilPana is not null && cerere.ValabilPana < cerere.ValabilDe)
        {
            return Eroare("Sfârșitul valabilității este înaintea începutului.");
        }

        // Proiectul e referit doar prin Id (D-03): existența lui o garantează lista din care alege aplicația.
        d.Db.Atribuiri.Add(new Atribuire(id, cerere.RolId, new Domeniu(d.FirmaId, cerere.ProiectId), cerere.ValabilDe, cerere.ValabilPana));
        await d.Db.SaveChangesAsync(ct);
        d.Permisiuni.Invalideaza(id);
        return Results.NoContent();
    }

    private static async Task<IResult> RevocaAtribuire(Guid id, [AsParameters] Dependente d, CancellationToken ct)
    {
        var atribuire = await d.Db.Atribuiri.FirstOrDefaultAsync(a => a.Id == id && a.FirmaId == d.FirmaId, ct);
        if (atribuire is null)
        {
            return Results.NotFound();
        }

        var rezultat = atribuire.Revoca(d.Ceas.Azi);
        if (rezultat == RezultatRevocare.DejaIncheiata)
        {
            return Results.NoContent();
        }

        if (rezultat == RezultatRevocare.DeSters)
        {
            d.Db.Atribuiri.Remove(atribuire);
        }

        if (!await RamaneAdministratorAsync(d, ct, atribuireStearsa: rezultat == RezultatRevocare.DeSters ? id : null))
        {
            return Eroare(MesajFaraAdministrator);
        }

        await d.Db.SaveChangesAsync(ct);
        d.Permisiuni.Invalideaza(atribuire.UtilizatorId);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- Roluri

    private static async Task<IResult> ListaRoluri([AsParameters] Dependente d, CancellationToken ct)
    {
        var azi = d.Ceas.Azi;
        var roluri = await d.Db.Roluri.AsNoTracking().Where(r => r.FirmaId == d.FirmaId).OrderBy(r => r.Denumire).ToListAsync(ct);
        var atribuiri = await d.Db.Atribuiri.AsNoTracking().Where(a => a.FirmaId == d.FirmaId).ToListAsync(ct);
        return Results.Ok(roluri.Select(r => new RolDto(
            r.Id, r.Cod, r.Denumire, r.Permisiuni.Order(StringComparer.Ordinal).ToList(),
            RoluriPredefinite.Gaseste(r.Cod) is not null,
            atribuiri.Where(a => a.RolId == r.Id && a.EsteValabila(azi)).Select(a => a.UtilizatorId).Distinct().Count())));
    }

    private static async Task<IResult> CreeazaRol(CreeazaRolCerere cerere, [AsParameters] Dependente d, CancellationToken ct)
    {
        var cod = (cerere.Cod ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
        if (cod.Length is 0 or > 60 || string.IsNullOrWhiteSpace(cerere.Denumire))
        {
            return Eroare("Codul (maxim 60 de caractere) și denumirea rolului sunt obligatorii.");
        }

        if (CatalogPermisiuni.Necunoscute(cerere.Permisiuni) is { Count: > 0 } necunoscute)
        {
            return Eroare("Permisiuni necunoscute: " + string.Join(", ", necunoscute));
        }

        if (await d.Db.Roluri.AnyAsync(r => r.FirmaId == d.FirmaId && r.Cod == cod, ct))
        {
            return Results.Conflict(new { mesaj = $"Există deja un rol cu codul „{cod}”." });
        }

        var rol = new Rol(d.FirmaId, cod, cerere.Denumire.Trim(), cerere.Permisiuni);
        d.Db.Roluri.Add(rol);
        await d.Db.SaveChangesAsync(ct);
        return Results.Created($"/api/administrare/roluri/{rol.Id}", new { rol.Id });
    }

    private static async Task<IResult> ModificaRol(Guid id, ModificaRolCerere cerere, [AsParameters] Dependente d, CancellationToken ct)
    {
        var rol = await d.Db.Roluri.FirstOrDefaultAsync(r => r.Id == id && r.FirmaId == d.FirmaId, ct);
        if (rol is null)
        {
            return Results.NotFound();
        }

        if (CatalogPermisiuni.Necunoscute(cerere.Permisiuni) is { Count: > 0 } necunoscute)
        {
            return Eroare("Permisiuni necunoscute: " + string.Join(", ", necunoscute));
        }

        if (string.IsNullOrWhiteSpace(cerere.Denumire))
        {
            return Eroare("Denumirea rolului este obligatorie.");
        }

        rol.Redenumeste(cerere.Denumire);
        rol.InlocuiestePermisiuni(cerere.Permisiuni);
        return await SalveazaRolAsync(rol, d, ct);
    }

    private static async Task<IResult> RestaureazaRol(Guid id, [AsParameters] Dependente d, CancellationToken ct)
    {
        var rol = await d.Db.Roluri.FirstOrDefaultAsync(r => r.Id == id && r.FirmaId == d.FirmaId, ct);
        if (rol is null)
        {
            return Results.NotFound();
        }

        if (RoluriPredefinite.Gaseste(rol.Cod) is not { } definitie)
        {
            return Eroare("Doar rolurile predefinite pot fi restaurate la permisiunile implicite.");
        }

        rol.Redenumeste(definitie.Denumire);
        rol.InlocuiestePermisiuni(definitie.ToatePermisiunile());
        return await SalveazaRolAsync(rol, d, ct);
    }

    private static async Task<IResult> SalveazaRolAsync(Rol rol, Dependente d, CancellationToken ct)
    {
        if (!await RamaneAdministratorAsync(d, ct))
        {
            return Eroare(MesajFaraAdministrator);
        }

        await d.Db.SaveChangesAsync(ct);

        // Permisiunile se citesc din cache la fiecare cerere: invalidarea le aplică imediat (REQ-DRP-09).
        var afectati = await d.Db.Atribuiri.AsNoTracking().Where(a => a.RolId == rol.Id).Select(a => a.UtilizatorId).Distinct().ToListAsync(ct);
        foreach (var u in afectati)
        {
            d.Permisiuni.Invalideaza(u);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> StergeRol(Guid id, [AsParameters] Dependente d, CancellationToken ct)
    {
        var rol = await d.Db.Roluri.FirstOrDefaultAsync(r => r.Id == id && r.FirmaId == d.FirmaId, ct);
        if (rol is null)
        {
            return Results.NotFound();
        }

        // Și atribuirile încheiate țin de istoric (cine ce drept a avut): rolul folosit vreodată rămâne.
        if (await d.Db.Atribuiri.AnyAsync(a => a.RolId == id, ct))
        {
            return Results.Conflict(new { mesaj = "Rolul a fost atribuit utilizatorilor și rămâne în istoric. Poți retrage atribuirile sau îi poți elimina permisiunile." });
        }

        d.Db.Roluri.Remove(rol);
        await d.Db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------- Firma activă și firmele

    private static async Task<IResult> CitesteFirma([AsParameters] Dependente d, CancellationToken ct)
    {
        var f = await d.Db.Firme.AsNoTracking().FirstAsync(x => x.Id == d.FirmaId, ct);
        return Results.Ok(new FirmaDetaliiDto(f.Id, f.Denumire, f.Cui, f.NrRegCom, f.Adresa, f.Telefon, f.Fax, f.Email, f.Website, f.ConturiBancare.ToList()));
    }

    /// <summary>Datele firmei active (antetul documentelor: comenzi, facturi, situații).</summary>
    private static async Task<IResult> ModificaFirma(DateFirma cerere, [AsParameters] Dependente d, CancellationToken ct)
    {
        var f = await d.Db.Firme.FirstAsync(x => x.Id == d.FirmaId, ct);
        var rezultat = f.ModificaDate(cerere with { ConturiBancare = cerere.ConturiBancare ?? [], Adresa = cerere.Adresa ?? Adresa.Goala });
        if (!rezultat.Succes)
        {
            return Eroare(rezultat.Eroare.Mesaj);
        }

        await d.Db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListaFirme([AsParameters] Dependente d, CancellationToken ct)
    {
        if ((await d.ActorAsync(ct)).Tip != TipUtilizator.AdministratorSistem)
        {
            return Results.Forbid();
        }

        var azi = d.Ceas.Azi;
        var atribuiri = await d.Db.Atribuiri.AsNoTracking().ToListAsync(ct);
        var firme = await d.Db.Firme.AsNoTracking().OrderBy(f => f.Denumire).ToListAsync(ct);
        return Results.Ok(firme.Select(f => new FirmaAdminDto(
            f.Id, f.Denumire, f.Cui, f.Activa,
            atribuiri.Where(a => a.FirmaId == f.Id && a.EsteValabila(azi)).Select(a => a.UtilizatorId).Distinct().Count())));
    }

    /// <summary>Firmă nouă: rolurile predefinite se copiază în ea, iar creatorul devine administratorul ei.</summary>
    private static async Task<IResult> CreeazaFirma(SalveazaFirmaCerere cerere, [AsParameters] Dependente d, CancellationToken ct)
    {
        if ((await d.ActorAsync(ct)).Tip != TipUtilizator.AdministratorSistem)
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(cerere.Denumire))
        {
            return Eroare("Denumirea firmei este obligatorie.");
        }

        if (!string.IsNullOrWhiteSpace(cerere.Cui) && !ValidareFiscala.EsteCuiValid(cerere.Cui))
        {
            return Eroare($"CUI-ul „{cerere.Cui}” nu este valid (cifra de control nu corespunde).");
        }

        var firma = new Firma(cerere.Denumire, string.IsNullOrWhiteSpace(cerere.Cui) ? null : cerere.Cui.Trim().ToUpperInvariant());
        var roluri = RoluriPredefinite.PentruFirma(firma.Id).ToList();
        var admin = roluri.Single(r => r.Cod == "admin_firma");

        d.Db.Firme.Add(firma);
        d.Db.Roluri.AddRange(roluri);
        d.Db.Atribuiri.Add(new Atribuire(d.UtilizatorId, admin.Id, new Domeniu(firma.Id), d.Ceas.Azi));
        await d.Db.SaveChangesAsync(ct);
        d.Permisiuni.Invalideaza(d.UtilizatorId);
        return Results.Created($"/api/administrare/firme/{firma.Id}", new FirmaDto(firma.Id, firma.Denumire, firma.Cui));
    }

    // ---------------------------------------------------------------- Utilitare

    private static IResult Eroare(string mesaj) => Results.BadRequest(new { mesaj });

    private static async Task<Dictionary<Guid, Rol>> RoluriFirmeiAsync(Dependente d, CancellationToken ct) =>
        await d.Db.Roluri.AsNoTracking().Where(r => r.FirmaId == d.FirmaId).ToDictionaryAsync(r => r.Id, ct);

    private static async Task<UtilizatorListaDto> CaDtoAsync(Dependente d, Utilizator u, IEnumerable<Atribuire> dinFirma, Dictionary<Guid, Rol> roluri)
    {
        var azi = d.Ceas.Azi;
        var active = dinFirma.Where(a => a.EsteValabila(azi)).ToList();
        var cont = await d.Conturi.CitesteAsync(u.Id);
        return new UtilizatorListaDto(
            u.Id, u.Nume, u.Email, u.Tip.ToString(), u.Activ, u.ExpiraLa,
            AreAcces: active.Count > 0 && AccesFirme.PoateIntra(u, azi),
            MfaActivat: cont?.TwoFactorEnabled ?? false,
            Blocat: cont?.LockoutEnd > d.Ceas.Acum,
            TrebuieSchimbataParola: cont?.TrebuieSchimbataParola ?? false,
            Roluri: active.Select(a => roluri.TryGetValue(a.RolId, out var r) ? r.Denumire : "?").Distinct().Order().ToList());
    }

    /// <summary>
    /// Utilizatorul (urmărit pentru modificare), dacă face parte din firmă și actorul are voie să-i modifice
    /// contul (nu are acces și la alte firme; nu e administrator de sistem, decât pentru alt administrator de sistem).
    /// </summary>
    private static async Task<(Utilizator? Utilizator, IResult? Eroare)> UtilizatorCuDrepturiPeContAsync(Guid id, Dependente d, CancellationToken ct)
    {
        var u = await d.Db.Utilizatori.FirstOrDefaultAsync(x => x.Id == id, ct);
        var firme = await d.Db.Atribuiri.AsNoTracking().Where(a => a.UtilizatorId == id).Select(a => a.FirmaId).Distinct().ToListAsync(ct);
        if (u is null || !firme.Contains(d.FirmaId))
        {
            return (null, Results.NotFound());
        }

        var actor = await d.ActorAsync(ct);
        if (!RegulaAdministrare.PoateGestionaTipul(actor.Tip, u.Tip))
        {
            return (null, Eroare("Doar un administrator de sistem poate modifica un administrator de sistem."));
        }

        if (!RegulaAdministrare.PoateModificaContul(actor.Tip, d.FirmaId, firme))
        {
            return (null, Eroare($"{u.Nume} are acces și la alte firme. Contul lui (parolă, MFA, activare) îl poate modifica doar administratorul de sistem."));
        }

        return (u, null);
    }

    /// <summary>Verifică regula „firma are un administrator” pe starea din memorie (după modificare, înainte de salvare).</summary>
    private static async Task<bool> RamaneAdministratorAsync(Dependente d, CancellationToken ct, Guid? utilizatorDezactivat = null, Guid? atribuireStearsa = null)
    {
        // Entitățile deja urmărite (modificate în această cerere) au prioritate față de valorile din baza de date.
        var atribuiri = await d.Db.Atribuiri.Where(a => a.FirmaId == d.FirmaId).ToListAsync(ct);
        var roluri = await d.Db.Roluri.Where(r => r.FirmaId == d.FirmaId).ToDictionaryAsync(r => r.Id, ct);
        var idUtilizatori = atribuiri.Select(a => a.UtilizatorId).Distinct().ToList();
        var activi = (await d.Db.Utilizatori.Where(u => idUtilizatori.Contains(u.Id)).ToListAsync(ct))
            .Where(u => u.Id != utilizatorDezactivat && AccesFirme.PoateIntra(u, d.Ceas.Azi))
            .Select(u => u.Id)
            .ToHashSet();

        return RegulaAdministrare.AreAdministrator(
            d.FirmaId, atribuiri.Where(a => a.Id != atribuireStearsa), roluri, activi, d.Ceas.Azi);
    }
}
