using System.Security.Claims;
using ErpConstructii.Api.Autentificare;
using ErpConstructii.Devize;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Devize.Infrastructure;
using ErpConstructii.Identitate;
using ErpConstructii.Identitate.Infrastructure;
using ErpConstructii.Proiecte;
using ErpConstructii.Proiecte.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Api.Dezvoltare;

/// <summary>
/// Date demo DOAR pentru Development: un utilizator cu toate drepturile, o firmă, câteva proiecte
/// și un deviz-ofertă. Rulează la pornire, o singură dată (se oprește dacă utilizatorul demo există).
///
/// Firma nu are încă entitate proprie (vine cu administrarea firmelor, backlog faza 0, pasul 4),
/// deci folosim un Id fix.
/// </summary>
public static class DateDemo
{
    public static readonly Guid FirmaDemoId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    public const string FirmaDemoNume = "Construct Demo SRL";
    public const string EmailDemo = "demo@erpconstructii.ro";

    public sealed record SesiuneDev(Guid UtilizatorId, Guid FirmaId, string FirmaNume);

    /// <summary>Endpoint anonim folosit de aplicația web în Development ca să afle identitatea demo.</summary>
    public static void MapeazaSesiuneDev(this WebApplication app) =>
        app.MapGet("/dev/sesiune", async (IdentitateDbContext db, CancellationToken ct) =>
        {
            var u = await db.Utilizatori.AsNoTracking().FirstOrDefaultAsync(x => x.Email == EmailDemo, ct);
            return u is null ? Results.NotFound() : Results.Ok(new SesiuneDev(u.Id, FirmaDemoId, FirmaDemoNume));
        }).AllowAnonymous().ExcludeFromDescription();

    public static async Task SeteazaAsync(IServiceProvider radacina, CancellationToken ct = default)
    {
        await using var scope = radacina.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var identitate = sp.GetRequiredService<IdentitateDbContext>();

        if (await identitate.Utilizatori.AnyAsync(u => u.Email == EmailDemo, ct))
        {
            return;
        }

        // 1. Identitate (schema fără RLS).
        var utilizator = new Utilizator(EmailDemo, "Andrei Popescu", TipUtilizator.AdministratorFirma);
        var toate = RoluriPredefinite.Toate.SelectMany(r => r.ToatePermisiunile()).Distinct();
        var rol = new Rol(FirmaDemoId, "demo_complet", "Acces complet (demo)", toate);
        identitate.Utilizatori.Add(utilizator);
        identitate.Roluri.Add(rol);
        identitate.Atribuiri.Add(new Atribuire(utilizator.Id, rol.Id, new Domeniu(FirmaDemoId), new DateOnly(2026, 1, 1)));
        await identitate.SaveChangesAsync(ct);

        // 2. Date de business: RLS cere firma curentă. Interceptorii o citesc din HttpContext,
        // deci simulăm o cerere a utilizatorului demo.
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimuriErp.Utilizator, utilizator.Id.ToString()),
                new Claim(ClaimuriErp.Firma, FirmaDemoId.ToString()),
            ], "Demo")),
        };

        var proiecte = sp.GetRequiredService<ProiecteDbContext>();
        var lista = new List<Proiect>
        {
            Nou("P-2026-001", "Bloc de locuințe P+8, str. Fabricii 12", "Rezidențial Nord SRL", "RO31234567", "Cluj-Napoca, str. Fabricii 12", new(2026, 3, 1), new(2027, 9, 30), executie: true),
            Nou("P-2026-002", "Hală logistică 12.000 mp", "LogiPark Invest SA", "RO28765432", "Ploiești, DN1 km 58", new(2026, 5, 15), new(2027, 3, 31), executie: true),
            Nou("P-2026-003", "Modernizare DJ 107, km 12+000 – 24+500", "Consiliul Județean Alba", "4562201", "Alba, DJ 107", new(2026, 7, 1), new(2027, 11, 30)),
            Nou("P-2026-004", "Școala gimnazială nr. 5 – reabilitare energetică", "Primăria Municipiului Brașov", "4384206", "Brașov, str. Lungă 150"),
            Nou("P-2026-005", "Clădire birouri S+P+6", "Urban Office Development SRL", "RO40112233", "București, Bd. Iuliu Maniu 7"),
            Nou("P-2025-014", "Stație de epurare 5.000 l.e.", "Compania de Apă Someș SA", "RO2761040", "Dej, str. Bistriței 2", new(2025, 4, 1), new(2026, 10, 31), executie: true),
        };
        proiecte.Proiecte.AddRange(lista);
        await proiecte.SaveChangesAsync(ct);

        var devize = sp.GetRequiredService<DevizeDbContext>();
        devize.Versiuni.Add(DevizOferta(lista[0].Id));
        await devize.SaveChangesAsync(ct);
    }

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

        var terasamente = obiect.AdaugaCategorie("01.1", "Terasamente");
        terasamente.AdaugaArticol("TsC03B1", "Săpătură mecanică cu excavator 0,40–0,70 mc, teren categoria II", "100 mc", 18.5m, new(0m, 85.40m, 612.30m, 0m));
        terasamente.AdaugaArticol("TsD02A1", "Încărcare pământ în autovehicule", "100 mc", 18.5m, new(0m, 42.10m, 310.00m, 0m));
        terasamente.AdaugaArticol("TRA01A10", "Transport pământ cu autobasculanta la 10 km", "t", 3330m, new(0m, 0m, 0m, 18.20m));

        var fundatii = obiect.AdaugaCategorie("01.2", "Fundații");
        fundatii.AdaugaArticol("CA02C1", "Beton C8/10 în fundații, turnat cu pompa", "mc", 95m, new(410.00m, 28.50m, 22.00m, 0m));
        fundatii.AdaugaArticol("CA04A1", "Cofraje din panouri refolosibile pentru fundații", "mp", 640m, new(38.20m, 31.40m, 1.10m, 0m));
        fundatii.AdaugaArticol("CC02K1", "Armături din oțel beton PC52 în fundații", "kg", 48500m, new(5.85m, 0.92m, 0.06m, 0m));
        fundatii.AdaugaArticol("CA02F1", "Beton C25/30 în radier, turnat cu pompa", "mc", 860m, new(520.00m, 31.20m, 24.50m, 0m));

        var suprastructura = obiect.AdaugaCategorie("01.3", "Structură de rezistență");
        suprastructura.AdaugaArticol("CB02B1", "Beton C30/37 în stâlpi și diafragme", "mc", 1240m, new(560.00m, 48.30m, 26.00m, 0m));
        suprastructura.AdaugaArticol("CB03C1", "Beton C25/30 în plăci și grinzi", "mc", 2180m, new(525.00m, 39.80m, 24.10m, 0m));
        suprastructura.AdaugaArticol("CB10A1", "Cofraje pentru stâlpi, diafragme, plăci", "mp", 15800m, new(42.50m, 36.70m, 1.40m, 0m));
        suprastructura.AdaugaArticol("CC02M1", "Armături PC52 în elemente de suprastructură", "kg", 412000m, new(5.85m, 1.04m, 0.07m, 0m));

        var zidarii = obiect.AdaugaCategorie("01.4", "Zidării", subcontractata: true);
        zidarii.AdaugaArticol("CG04A1", "Zidărie din blocuri ceramice cu goluri 25 cm", "mc", 1650m, new(395.00m, 92.00m, 3.20m, 0m));
        zidarii.AdaugaArticol("CG07B1", "Pereți despărțitori BCA 10 cm", "mp", 5200m, new(48.00m, 24.50m, 0.40m, 0m));

        return v;
    }
}
