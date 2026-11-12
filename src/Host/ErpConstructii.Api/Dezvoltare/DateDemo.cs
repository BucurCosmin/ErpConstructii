using System.Security.Claims;
using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Infrastructure;
using ErpConstructii.Identitate;
using ErpConstructii.Identitate.Infrastructure;
using ErpConstructii.Identitate.Infrastructure.Autentificare;
using ErpConstructii.Proiecte;
using ErpConstructii.Proiecte.Infrastructure;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Api.Dezvoltare;

/// <summary>
/// Date demo DOAR pentru Development: două firme, un utilizator cu acces complet la ambele
/// (cont cu parolă; MFA se configurează la primul login), proiecte și un deviz-ofertă.
/// Fiecare pas verifică dacă datele există deja, deci se poate rula la fiecare pornire.
/// </summary>
public static class DateDemo
{
    public static readonly Guid FirmaDemoId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    public static readonly Guid FirmaDemoSudId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    public const string EmailDemo = "demo@erpconstructii.ro";

    /// <summary>Parola contului demo (doar dezvoltare locală; documentată în README).</summary>
    public const string ParolaDemo = "Demo-Constructii-2026";

    public static async Task SeteazaAsync(IServiceProvider radacina, CancellationToken ct = default)
    {
        await using var scope = radacina.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var identitate = sp.GetRequiredService<IdentitateDbContext>();

        // 1. Firmele.
        await AsiguraFirmaAsync(identitate, FirmaDemoId, "Construct Demo SRL", "RO23456783", ct);
        await AsiguraFirmaAsync(identitate, FirmaDemoSudId, "Construct Demo Sud SRL", "RO34567892", ct);

        // 2. Utilizatorul, cu rol complet pe fiecare firmă.
        var utilizator = await identitate.Utilizatori.FirstOrDefaultAsync(u => u.Email == EmailDemo, ct);
        if (utilizator is null)
        {
            utilizator = new Utilizator(EmailDemo, "Andrei Popescu", TipUtilizator.AdministratorSistem);
            identitate.Utilizatori.Add(utilizator);
        }
        else if (utilizator.Tip != TipUtilizator.AdministratorSistem)
        {
            // Contul demo administrează și firmele (creare firmă nouă), nu doar utilizatorii.
            utilizator.Modifica(utilizator.Nume, TipUtilizator.AdministratorSistem);
        }

        // Rolurile predefinite în fiecare firmă demo (ca la crearea unei firme reale).
        foreach (var firmaId in new[] { FirmaDemoId, FirmaDemoSudId })
        {
            var existente = await identitate.Roluri.Where(r => r.FirmaId == firmaId).Select(r => r.Cod).ToListAsync(ct);
            identitate.Roluri.AddRange(RoluriPredefinite.PentruFirma(firmaId).Where(r => !existente.Contains(r.Cod)));
        }

        await identitate.SaveChangesAsync(ct);

        var toate = RoluriPredefinite.Toate.SelectMany(r => r.ToatePermisiunile()).Distinct().ToList();
        foreach (var firmaId in new[] { FirmaDemoId, FirmaDemoSudId })
        {
            var rol = await identitate.Roluri.FirstOrDefaultAsync(r => r.FirmaId == firmaId && r.Cod == "demo_complet", ct);
            if (rol is null)
            {
                rol = new Rol(firmaId, "demo_complet", "Acces complet (demo)", toate);
                identitate.Roluri.Add(rol);
            }

            var idUtilizator = utilizator.Id;
            if (!await identitate.Atribuiri.AnyAsync(a => a.UtilizatorId == idUtilizator && a.FirmaId == firmaId, ct))
            {
                identitate.Atribuiri.Add(new Atribuire(utilizator.Id, rol.Id, new Domeniu(firmaId), new DateOnly(2026, 1, 1)));
            }
        }

        await identitate.SaveChangesAsync(ct);

        // 3. Contul de autentificare (același Id ca utilizatorul).
        var conturi = sp.GetRequiredService<UserManager<ContUtilizator>>();
        if (await conturi.FindByIdAsync(utilizator.Id.ToString()) is null)
        {
            var cont = new ContUtilizator { Id = utilizator.Id, UserName = EmailDemo, Email = EmailDemo, EmailConfirmed = true };
            var rezultat = await conturi.CreateAsync(cont, ParolaDemo);
            if (!rezultat.Succeeded)
            {
                throw new InvalidOperationException("Contul demo nu a putut fi creat: " + string.Join("; ", rezultat.Errors.Select(e => e.Description)));
            }
        }

        // 4. Date de business, separat pe fiecare firmă (RLS cere firma curentă în context).
        var proiecte = await DateFirmaAsync(sp, utilizator.Id, FirmaDemoId, ProiecteFirmaDemo, cuDeviz: true, ct);
        await DateFirmaAsync(sp, utilizator.Id, FirmaDemoSudId, ProiecteFirmaSud, cuDeviz: false, ct);

        // 5. Colegi demo, cu roluri diferite (pentru ecranul de administrare).
        await ColegiAsync(sp, identitate, proiecte, ct);
    }

    private static async Task ColegiAsync(IServiceProvider sp, IdentitateDbContext db, IReadOnlyDictionary<string, Guid> proiecte, CancellationToken ct)
    {
        var colegi = new (string Email, string Nume, TipUtilizator Tip, Guid Firma, string Rol, string? Proiect)[]
        {
            ("maria.ionescu@erpconstructii.ro", "Maria Ionescu", TipUtilizator.Intern, FirmaDemoId, "ofertant", null),
            ("radu.marin@erpconstructii.ro", "Radu Marin", TipUtilizator.Intern, FirmaDemoId, "manager_proiect", "P-2026-001"),
            ("ion.vasile@erpconstructii.ro", "Ion Vasile", TipUtilizator.Santier, FirmaDemoId, "sef_santier", "P-2026-001"),
            ("elena.dumitru@erpconstructii.ro", "Elena Dumitru", TipUtilizator.Intern, FirmaDemoId, "contabil", null),
            ("elena.dumitru@erpconstructii.ro", "Elena Dumitru", TipUtilizator.Intern, FirmaDemoSudId, "contabil", null),
            ("george.stan@erpconstructii.ro", "George Stan", TipUtilizator.Intern, FirmaDemoSudId, "director", null),
        };

        var conturi = sp.GetRequiredService<ServiciuConturi>();
        foreach (var c in colegi)
        {
            var u = await db.Utilizatori.FirstOrDefaultAsync(x => x.Email == c.Email, ct);
            var nou = u is null;
            if (u is null)
            {
                u = new Utilizator(c.Email, c.Nume, c.Tip);
                db.Utilizatori.Add(u);
            }

            var idUtilizator = u.Id;
            if (!await db.Atribuiri.AnyAsync(a => a.UtilizatorId == idUtilizator && a.FirmaId == c.Firma, ct))
            {
                var rol = await db.Roluri.FirstAsync(r => r.FirmaId == c.Firma && r.Cod == c.Rol, ct);
                Guid? proiectId = c.Proiect is not null && proiecte.TryGetValue(c.Proiect, out var p) ? p : null;
                db.Atribuiri.Add(new Atribuire(u.Id, rol.Id, new Domeniu(c.Firma, proiectId), new DateOnly(2026, 2, 1)));
            }

            await db.SaveChangesAsync(ct);
            if (nou)
            {
                await conturi.CreeazaAsync(u); // parolă temporară; administratorul o poate reseta din aplicație
            }
        }
    }

    private static async Task AsiguraFirmaAsync(IdentitateDbContext db, Guid id, string denumire, string cui, CancellationToken ct)
    {
        var firma = await db.Firme.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (firma is null)
        {
            db.Firme.Add(new Firma(id, denumire, cui));
        }
        else
        {
            // Doar ce lipsește sau e greșit: denumirea generată de migrarea „Firme” („Firmă 0190…”) și un CUI invalid.
            // Ce a modificat utilizatorul în Administrare (denumire, CUI valid) rămâne neatins.
            var denumireNoua = firma.Denumire.StartsWith("Firmă ", StringComparison.Ordinal) ? denumire : firma.Denumire;
            var cuiNou = firma.Cui is { } c && ValidareFiscala.EsteCuiValid(c) ? firma.Cui : cui;
            if (denumireNoua != firma.Denumire || cuiNou != firma.Cui)
            {
                firma.Modifica(denumireNoua, cuiNou);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creează proiectele firmei (dacă nu are) și întoarce codurile lor cu Id-urile.</summary>
    private static async Task<IReadOnlyDictionary<string, Guid>> DateFirmaAsync(
        IServiceProvider sp, Guid utilizatorId, Guid firmaId, Func<List<Proiect>> proiecteNoi, bool cuDeviz, CancellationToken ct)
    {
        // Interceptorii RLS și de audit citesc firma și utilizatorul din HttpContext: simulăm o cerere.
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimuriErp.Utilizator, utilizatorId.ToString()),
                new Claim(ClaimuriErp.Firma, firmaId.ToString()),
            ], "Demo")),
        };

        // DbContext-uri noi: conexiunea se deschide (și primește app.firma_id) după schimbarea contextului.
        await using var proiecte = ActivatorUtilities.CreateInstance<ProiecteDbContext>(sp, sp.GetRequiredService<DbContextOptions<ProiecteDbContext>>());
        if (!await proiecte.Proiecte.AnyAsync(ct))
        {
            var lista = proiecteNoi();
            proiecte.Proiecte.AddRange(lista);
            proiecte.Santiere.AddRange(lista.Select(p => Santier.Creeaza(p.Id, "Șantier principal", p.AdresaSantier).Valoare));
            await proiecte.SaveChangesAsync(ct);

            if (cuDeviz)
            {
                await using var devize = ActivatorUtilities.CreateInstance<DevizeDbContext>(sp, sp.GetRequiredService<DbContextOptions<DevizeDbContext>>());
                devize.Versiuni.Add(DevizOferta(lista[0].Id));
                await devize.SaveChangesAsync(ct);
            }
        }

        return await proiecte.Proiecte.AsNoTracking().ToDictionaryAsync(p => p.Cod, p => p.Id, ct);
    }

    private static List<Proiect> ProiecteFirmaDemo() =>
    [
        Nou("P-2026-001", "Bloc de locuințe P+8, str. Fabricii 12", "Rezidențial Nord SRL", "RO31234567", "Cluj-Napoca, str. Fabricii 12", new(2026, 3, 1), new(2027, 9, 30), executie: true),
        Nou("P-2026-002", "Hală logistică 12.000 mp", "LogiPark Invest SA", "RO28765432", "Ploiești, DN1 km 58", new(2026, 5, 15), new(2027, 3, 31), executie: true),
        Nou("P-2026-003", "Modernizare DJ 107, km 12+000 – 24+500", "Consiliul Județean Alba", "4562201", "Alba, DJ 107", new(2026, 7, 1), new(2027, 11, 30)),
        Nou("P-2026-004", "Școala gimnazială nr. 5 – reabilitare energetică", "Primăria Municipiului Brașov", "4384206", "Brașov, str. Lungă 150"),
        Nou("P-2026-005", "Clădire birouri S+P+6", "Urban Office Development SRL", "RO40112233", "București, Bd. Iuliu Maniu 7"),
        Nou("P-2025-014", "Stație de epurare 5.000 l.e.", "Compania de Apă Someș SA", "RO2761040", "Dej, str. Bistriței 2", new(2025, 4, 1), new(2026, 10, 31), executie: true),
    ];

    private static List<Proiect> ProiecteFirmaSud() =>
    [
        Nou("S-2026-001", "Ansamblu rezidențial 4 blocuri P+4", "Sud Residence SRL", "RO39887766", "Craiova, str. Brestei 210", new(2026, 4, 1), new(2028, 3, 31), executie: true),
        Nou("S-2026-002", "Pod peste râul Jiu, DC 45", "Primăria Comunei Podari", "4554133", "Podari, jud. Dolj"),
    ];

    private static Proiect Nou(
        string cod, string denumire, string beneficiar, string cui, string adresa,
        DateOnly? inceput = null, DateOnly? termen = null, bool executie = false)
    {
        var p = new Proiect(cod, denumire, beneficiar, cui, adresa);
        if (inceput is { } i && termen is { } t)
        {
            p.Contracteaza(i, t);
            if (executie)
            {
                p.IncepeExecutia();
            }
        }

        return p;
    }

    /// <summary>Deviz-ofertă de test (prețuri ilustrative, nu de piață).</summary>
    private static VersiuneDeviz DevizOferta(Guid proiectId)
    {
        var v = new VersiuneDeviz(proiectId, TipVersiuneDeviz.Oferta, 1, "Ofertă inițială – structură de rezistență");
        v.SeteazaSursa("demo", null);
        // Cote ilustrative pentru demo; în producție vin din setările firmei sau din fișierul importat.
        v.SeteazaParametri(new ParametriRecapitulatie(0.0225m, 0.10m, 0.05m, 0.21m));

        var obiect = v.AdaugaObiect("01", "Bloc locuințe P+8");

        var terasamente = v.AdaugaCapitol("01.1", "Terasamente", TipCapitol.Capitol, obiect);
        terasamente.AdaugaArticol("TsC03B1", "Săpătură mecanică cu excavator 0,40–0,70 mc, teren categoria II", "100 mc", 18.5m, new(0m, 85.40m, 612.30m, 0m));
        terasamente.AdaugaArticol("TsD02A1", "Încărcare pământ în autovehicule", "100 mc", 18.5m, new(0m, 42.10m, 310.00m, 0m));
        terasamente.AdaugaArticol("TRA01A10", "Transport pământ cu autobasculanta la 10 km", "t", 3330m, new(0m, 0m, 0m, 18.20m));

        var fundatii = v.AdaugaCapitol("01.2", "Fundații", TipCapitol.Capitol, obiect);
        fundatii.AdaugaArticol("CA02C1", "Beton C8/10 în fundații, turnat cu pompa", "mc", 95m, new(410.00m, 28.50m, 22.00m, 0m));
        fundatii.AdaugaArticol("CA04A1", "Cofraje din panouri refolosibile pentru fundații", "mp", 640m, new(38.20m, 31.40m, 1.10m, 0m));
        fundatii.AdaugaArticol("CC02K1", "Armături din oțel beton PC52 în fundații", "kg", 48500m, new(5.85m, 0.92m, 0.06m, 0m));
        fundatii.AdaugaArticol("CA02F1", "Beton C25/30 în radier, turnat cu pompa", "mc", 860m, new(520.00m, 31.20m, 24.50m, 0m));

        var suprastructura = v.AdaugaCapitol("01.3", "Structură de rezistență", TipCapitol.Capitol, obiect);
        suprastructura.AdaugaArticol("CB02B1", "Beton C30/37 în stâlpi și diafragme", "mc", 1240m, new(560.00m, 48.30m, 26.00m, 0m));
        suprastructura.AdaugaArticol("CB03C1", "Beton C25/30 în plăci și grinzi", "mc", 2180m, new(525.00m, 39.80m, 24.10m, 0m));
        suprastructura.AdaugaArticol("CB10A1", "Cofraje pentru stâlpi, diafragme, plăci", "mp", 15800m, new(42.50m, 36.70m, 1.40m, 0m));
        suprastructura.AdaugaArticol("CC02M1", "Armături PC52 în elemente de suprastructură", "kg", 412000m, new(5.85m, 1.04m, 0.07m, 0m));

        var zidarii = v.AdaugaCapitol("01.4", "Zidării", TipCapitol.Capitol, obiect, subcontractat: true);
        zidarii.AdaugaArticol("CG04A1", "Zidărie din blocuri ceramice cu goluri 25 cm", "mc", 1650m, new(395.00m, 92.00m, 3.20m, 0m));
        zidarii.AdaugaArticol("CG07B1", "Pereți despărțitori BCA 10 cm", "mp", 5200m, new(48.00m, 24.50m, 0.40m, 0m));

        return v;
    }
}
