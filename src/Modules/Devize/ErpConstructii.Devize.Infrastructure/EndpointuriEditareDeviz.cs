using System.Globalization;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Devize.Infrastructure;

public sealed record RedenumireVersiuneCerere(string? Denumire);

/// <param name="PretUnitar">Costul direct unitar în lei, pe resurse.</param>
public sealed record ArticolCerere(string? Simbol, string? Denumire, string? UnitateMasura, decimal Cantitate, ValoriResurse PretUnitar);

public sealed record VersiuneNouaCerere(string? Denumire);

/// <summary>Versiunea în care s-a făcut modificarea: ciorna de lucru, creată la prima modificare dacă <paramref name="VersiuneNoua"/>.</summary>
public sealed record RezultatEditareDeviz(Guid VersiuneId, int Numar, bool VersiuneNoua, Guid? ArticolId = null);

public sealed record ArticolComparatDto(
    Guid ArticolId, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal Valoare, string Capitol);

public sealed record DiferentaVersiuneDto(
    Guid CheieStabila, string Tip, ArticolComparatDto? Vechi, ArticolComparatDto? Nou, IReadOnlyList<string> Campuri, decimal DiferentaValoare);

/// <summary>Ce s-a schimbat față de versiunea din care a pornit (sau față de <c>?fata=</c>).</summary>
public sealed record ComparatieVersiuniDto(
    Guid VersiuneVecheId, int NumarVechi, string StareVeche, IReadOnlyList<DiferentaVersiuneDto> Diferente,
    decimal TotalFaraTvaVechi, decimal TotalFaraTvaNou, decimal TotalCuTvaVechi, decimal TotalCuTvaNou);

/// <summary>
/// Lucrul pe o versiune de deviz: redenumire și ștergere (ciornă), editarea articolelor, „Modifică” = ciornă nouă pornită
/// dintr-o versiune (aceleași chei stabile), aprobarea (versiunea din care a pornit devine „înlocuită”), comparația cu
/// versiunea anterioară, tipărirea (PDF) și exportul Excel. Nici versiunea aprobată (regula 6, REQ-DEV-06), nici devizul
/// importat nu se modifică pe loc: prima modificare a lor creează ciorna de lucru (D-53), unde continuă editarea.
/// </summary>
internal static class EndpointuriEditareDeviz
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare);
        var editare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Editare);
        var aprobare = Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Aprobare);

        g.MapPut("/{versiuneId:guid}", Redenumeste).WithName("RedenumesteDeviz").WithSummary("Redenumește versiunea (ciornă)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).CerePermisiune(editare);
        g.MapDelete("/{versiuneId:guid}", Sterge).WithName("StergeDeviz").WithSummary("Șterge versiunea (doar ciornă), cu fișierele ei originale")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status409Conflict).CerePermisiune(editare);
        g.MapPost("/{versiuneId:guid}/versiune-noua", VersiuneNoua).WithName("VersiuneNouaDeviz")
            .WithSummary("Ciornă nouă pornită din versiune (aceleași articole și chei stabile); la aprobare o înlocuiește")
            .Produces(StatusCodes.Status201Created).CerePermisiune(editare);
        g.MapPost("/{versiuneId:guid}/aprobare", Aproba).WithName("AprobaDeviz")
            .WithSummary("Aprobă ciorna: devine imuabilă; versiunea din care a pornit devine „înlocuită”")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest).CerePermisiune(aprobare);
        g.MapPut("/{versiuneId:guid}/articole/{articolId:guid}", ModificaArticol).WithName("ModificaArticolDeviz")
            .WithSummary("Modifică un articol al ciornei (simbol, denumire, UM, cantitate, prețuri pe resurse)")
            .Produces<RezultatEditareDeviz>().Produces(StatusCodes.Status400BadRequest).CerePermisiune(editare);
        g.MapPost("/{versiuneId:guid}/capitole/{capitolId:guid}/articole", AdaugaArticol).WithName("AdaugaArticolDeviz")
            .WithSummary("Adaugă un articol introdus manual în capitol (ciornă)")
            .Produces<RezultatEditareDeviz>().Produces(StatusCodes.Status400BadRequest).CerePermisiune(editare);
        g.MapDelete("/{versiuneId:guid}/articole/{articolId:guid}", StergeArticol).WithName("StergeArticolDeviz")
            .WithSummary("Scoate un articol din ciornă").Produces<RezultatEditareDeviz>().CerePermisiune(editare);
        g.MapGet("/{versiuneId:guid}/diferente", Diferente).WithName("DiferenteDeviz")
            .WithSummary("Ce s-a schimbat față de versiunea din care a pornit (sau față de ?fata=): articole adăugate, modificate, scoase")
            .Produces<ComparatieVersiuniDto>().Produces(StatusCodes.Status204NoContent).CerePermisiune(vizualizare);
        g.MapGet("/{versiuneId:guid}/pdf", Pdf).WithName("PdfDeviz").WithSummary("Devizul tipărit (PDF): capitole, articole, recapitulație")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").CerePermisiune(vizualizare);
        g.MapGet("/{versiuneId:guid}/excel", Excel).WithName("ExcelDeviz").WithSummary("Devizul în Excel (prețuri pe resurse, valori, ofertă, recapitulație)")
            .Produces(StatusCodes.Status200OK).CerePermisiune(vizualizare);
    }

    /// <summary>Versiunea urmărită de EF, cu tot arborele (pentru modificări).</summary>
    private static Task<VersiuneDeviz?> PentruModificareAsync(DevizeDbContext db, Guid proiectId, Guid versiuneId, CancellationToken ct) =>
        db.Versiuni.Include(v => v.Capitole).ThenInclude(c => c.Articole).ThenInclude(a => a.Consumuri).AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == versiuneId && v.ProiectId == proiectId, ct);

    private static async Task<IResult> Redenumeste(Guid proiectId, Guid versiuneId, RedenumireVersiuneCerere cerere, DevizeDbContext db, CancellationToken ct)
    {
        var v = await db.Versiuni.FirstOrDefaultAsync(x => x.Id == versiuneId && x.ProiectId == proiectId, ct);
        if (v is null)
            return Results.NotFound();
        var r = v.Redenumeste(cerere.Denumire ?? string.Empty);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Sterge(Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct)
    {
        var v = await PentruModificareAsync(db, proiectId, versiuneId, ct);
        if (v is null)
            return Results.NotFound();
        if (v.EsteBlocat)
            return Results.Conflict(new { mesaj = "Versiunea aprobată nu se șterge (poate fi folosită de alte module); creează o versiune nouă." });
        db.Versiuni.Remove(v);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> VersiuneNoua(Guid proiectId, Guid versiuneId, VersiuneNouaCerere? cerere, DevizeDbContext db, CancellationToken ct)
    {
        var v = await ModulDevize.IncarcaAsync(db, versiuneId, ct);
        if (v is null || v.ProiectId != proiectId)
            return Results.NotFound();
        // O singură ciornă de lucru pe versiune: dacă există deja, modificările continuă acolo.
        if (await db.Versiuni.AsNoTracking().FirstOrDefaultAsync(
                x => x.ProiectId == proiectId && x.VersiuneAnterioaraId == v.Id && x.Stare == StareVersiuneDeviz.Ciorna, ct) is { } existenta)
            return Results.Ok(new { existenta.Id, existenta.Numar });
        var copie = await CopiazaAsync(db, v, cerere?.Denumire, ct);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/proiecte/{proiectId}/devize/{copie.Id}", new { copie.Id, copie.Numar });
    }

    private static async Task<IResult> Aproba(Guid proiectId, Guid versiuneId, DevizeDbContext db, ICeas ceas, CancellationToken ct)
    {
        var v = await PentruModificareAsync(db, proiectId, versiuneId, ct);
        if (v is null)
            return Results.NotFound();
        var r = v.Aproba();
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        if (v.VersiuneAnterioaraId is { } anterioara
            && await db.Versiuni.FirstOrDefaultAsync(x => x.Id == anterioara && x.ProiectId == proiectId, ct) is { } veche
            && veche.Stare != StareVersiuneDeviz.Inlocuita)
        {
            // Versiunea din care a pornit ciorna (aprobată, sau devizul importat neaprobat) devine „înlocuită”. La cea aprobată,
            // schimbarea stării e însoțită de corectura ei (istoricul înlocuirii), regula 6; la ciornă rămâne ca istoric.
            veche.Inlocuieste();
            db.Inlocuiri.Add(new InlocuireVersiuneDeviz(veche.Id, v.Id, ceas.Acum));
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Versiunea în care se face o modificare de articol: ciorna de lucru însăși; pentru devizul importat sau aprobat, ciorna
    /// de lucru existentă pornită din el ori una nouă (copie cu aceleași chei), ca originalul să rămână neschimbat (D-53).
    /// Articolele și capitolele cerute se regăsesc în copie după cheia stabilă, respectiv calea capitolului.
    /// </summary>
    internal static async Task<Rezultat<CiornaDeLucru>> CiornaDeLucruAsync(DevizeDbContext db, Guid proiectId, Guid versiuneId, CancellationToken ct)
    {
        var v = await PentruModificareAsync(db, proiectId, versiuneId, ct);
        if (v is null)
            return Rezultat.Esuat<CiornaDeLucru>(Eroare.NuExista("Versiunea de deviz"));
        if (v.EsteCiornaDeLucru)
            return Rezultat.Ok(new CiornaDeLucru(v, v, false));
        if (v.Stare == StareVersiuneDeviz.Inlocuita)
            return Rezultat.Esuat<CiornaDeLucru>(Eroare.Validare("Versiunea a fost înlocuită de una mai nouă; modificările se fac pe versiunea în vigoare."));

        var idExistenta = await db.Versiuni
            .Where(x => x.ProiectId == proiectId && x.VersiuneAnterioaraId == v.Id && x.Stare == StareVersiuneDeviz.Ciorna)
            .OrderByDescending(x => x.Numar).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (idExistenta is { } id && await PentruModificareAsync(db, proiectId, id, ct) is { } existenta)
            return Rezultat.Ok(new CiornaDeLucru(v, existenta, false));

        // Copia se salvează întâi (în tranzacția apelantului), apoi se editează ca orice ciornă din bază.
        var copie = await CopiazaAsync(db, v, null, ct);
        await db.SaveChangesAsync(ct);
        return Rezultat.Ok(new CiornaDeLucru(v, copie, true));
    }

    internal sealed record CiornaDeLucru(VersiuneDeviz Ceruta, VersiuneDeviz Ciorna, bool Noua)
    {
        public ArticolDeviz? Articol(Guid articolIdCerut) =>
            Ceruta.Articole().FirstOrDefault(x => x.Id == articolIdCerut) is { } a
                ? Ciorna.Articole().FirstOrDefault(x => x.CheieStabila == a.CheieStabila)
                : null;

        public CapitolDeviz? Capitol(Guid capitolIdCerut)
        {
            var c = Ceruta.Capitole.FirstOrDefault(x => x.Id == capitolIdCerut);
            if (c is null)
                return null;
            var cheie = Ceruta.CheieCapitol(c);
            return Ciorna.Capitole.FirstOrDefault(x => Ciorna.CheieCapitol(x) == cheie);
        }

        public RezultatEditareDeviz Raspuns(Guid? articolId = null) => new(Ciorna.Id, Ciorna.Numar, Noua, articolId);
    }

    private static async Task<VersiuneDeviz> CopiazaAsync(DevizeDbContext db, VersiuneDeviz v, string? denumire, CancellationToken ct)
    {
        var numar = await db.Versiuni.Where(x => x.ProiectId == v.ProiectId && x.Tip == v.Tip).MaxAsync(x => (int?)x.Numar, ct) + 1 ?? 1;
        var copie = v.CopieCiorna(numar, denumire);
        db.Versiuni.Add(copie);
        return copie;
    }

    internal static IResult Refuz(Eroare e) =>
        e.Cod == "nu_exista" ? Results.NotFound(new { mesaj = e.Mesaj }) : Results.BadRequest(new { mesaj = e.Mesaj });

    private static async Task<IResult> ModificaArticol(
        Guid proiectId, Guid versiuneId, Guid articolId, ArticolCerere cerere, DevizeDbContext db, CancellationToken ct)
    {
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        var lucru = await CiornaDeLucruAsync(db, proiectId, versiuneId, ct);
        if (lucru.Esec)
            return Refuz(lucru.Eroare);
        if (lucru.Valoare.Articol(articolId) is not { } articol)
            return Results.NotFound(new { mesaj = "Articolul nu există." });
        var r = lucru.Valoare.Ciorna.ModificaArticol(articol.Id, Date(cerere));
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(lucru.Valoare.Raspuns(articol.Id));
    }

    private static async Task<IResult> AdaugaArticol(
        Guid proiectId, Guid versiuneId, Guid capitolId, ArticolCerere cerere, DevizeDbContext db, CancellationToken ct)
    {
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        var lucru = await CiornaDeLucruAsync(db, proiectId, versiuneId, ct);
        if (lucru.Esec)
            return Refuz(lucru.Eroare);
        if (lucru.Valoare.Capitol(capitolId) is not { } capitol)
            return Results.NotFound(new { mesaj = "Capitolul nu există." });
        var r = lucru.Valoare.Ciorna.AdaugaArticolManual(capitol.Id, Date(cerere));
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        // Id-ul e generat în aplicație: EF trebuie anunțat că articolul e nou (altfel l-ar căuta în bază).
        db.Articole.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(lucru.Valoare.Raspuns(r.Valoare.Id));
    }

    private static async Task<IResult> StergeArticol(Guid proiectId, Guid versiuneId, Guid articolId, DevizeDbContext db, CancellationToken ct)
    {
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        var lucru = await CiornaDeLucruAsync(db, proiectId, versiuneId, ct);
        if (lucru.Esec)
            return Refuz(lucru.Eroare);
        if (lucru.Valoare.Articol(articolId) is not { } articol)
            return Results.NotFound(new { mesaj = "Articolul nu există." });
        var r = lucru.Valoare.Ciorna.StergeArticol(articol.Id);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        db.Articole.Remove(articol);
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Ok(lucru.Valoare.Raspuns());
    }

    private static async Task<IResult> Diferente(Guid proiectId, Guid versiuneId, Guid? fata, DevizeDbContext db, CancellationToken ct)
    {
        var noua = await ModulDevize.IncarcaAsync(db, versiuneId, ct);
        if (noua is null || noua.ProiectId != proiectId)
            return Results.NotFound();
        var vechi = (fata ?? noua.VersiuneAnterioaraId) is { } id ? await ModulDevize.IncarcaAsync(db, id, ct) : null;
        if (vechi is null || vechi.ProiectId != proiectId)
            return Results.NoContent(); // versiune fără precedent (ex. devizul importat)

        var c = ComparareVersiuni.Compara(vechi, noua);
        static ArticolComparatDto? Dto(ArticolComparat? a) => a is null
            ? null
            : new(a.ArticolId, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretUnitar.Total, a.Valoare, a.Capitol);
        return Results.Ok(new ComparatieVersiuniDto(vechi.Id, vechi.Numar, vechi.Stare.ToString(),
            c.Diferente.Select(d => new DiferentaVersiuneDto(d.CheieStabila, d.Tip.ToString(), Dto(d.Vechi), Dto(d.Nou), d.Campuri, d.DiferentaValoare)).ToList(),
            c.RecapitulatieVeche.TotalFaraTva, c.RecapitulatieNoua.TotalFaraTva, c.RecapitulatieVeche.TotalCuTva, c.RecapitulatieNoua.TotalCuTva));
    }

    private static async Task<IResult> Excel(Guid proiectId, Guid versiuneId, DevizeDbContext db, CancellationToken ct)
    {
        var v = await ModulDevize.IncarcaAsync(db, versiuneId, ct);
        if (v is null || v.ProiectId != proiectId)
            return Results.NotFound();
        return Results.File(ExportDeviz.Excel(v, v.Obiectiv ?? string.Empty), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{NumeFisier(v)}.xlsx");
    }

    private static async Task<IResult> Pdf(
        Guid proiectId, Guid versiuneId, DevizeDbContext db, IServiciuDocumente documente, IInterogareFirma firme, ICeas ceas, CancellationToken ct)
    {
        var v = await ModulDevize.IncarcaAsync(db, versiuneId, ct);
        if (v is null || v.ProiectId != proiectId)
            return Results.NotFound();
        var r = v.Recapitulatie();
        var p = v.Parametri();
        var f = await firme.FirmaActivaAsync(ct);
        var antet = new AntetFirma(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa.Formatata());
        var areOferta = v.Articole().Any(a => a.ArePretOferta);
        var linii = ExportDeviz.Randuri(v).Select(x => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
        {
            ["cod"] = x.Cod,
            ["simbol"] = x.Simbol,
            ["denumire"] = x.EsteCapitol ? $"{new string(' ', x.Adancime * 2)}{x.Denumire.ToUpper(Ro)}" : $"{new string(' ', x.Adancime * 2)}{x.Denumire}",
            ["um"] = x.UnitateMasura,
            ["cantitate"] = x.Cantitate is { } q ? q.ToString("#,##0.###", Ro) : string.Empty,
            ["pu"] = x.PretUnitar is { } pu ? Lei(pu.Total) : string.Empty,
            ["valoare"] = Lei(x.Valoare.Total),
            ["oferta"] = x.ValoareOferta is { } o ? Lei(o) : string.Empty,
        }).ToList();
        var campuri = new Dictionary<string, string>
        {
            ["obiectiv"] = v.Obiectiv ?? string.Empty,
            ["versiune"] = $"{ExportDeviz.TipText(v.Tip)} nr. {v.Numar} · {(v.Stare == StareVersiuneDeviz.Ciorna ? "ciornă" : v.Stare == StareVersiuneDeviz.Aprobata ? "aprobată" : "înlocuită")}",
            ["moneda"] = areOferta ? $"costuri directe în lei; ofertă în {v.Moneda}{(v.Curs is { } c ? $" (curs {c.ToString("0.####", Ro)})" : string.Empty)}" : "lei",
            ["directe"] = Lei(r.CheltuieliDirecteResurse.Total),
            ["cam"] = Lei(r.ContributiiManopera),
            ["indirecte"] = Lei(r.CheltuieliIndirecte),
            ["profit"] = Lei(r.Profit),
            ["fara_tva"] = Lei(r.TotalFaraTva),
            ["tva"] = Lei(r.Tva),
            ["cu_tva"] = Lei(r.TotalCuTva),
            ["observatii"] = string.Empty,
        };
        List<ColoanaPdf> coloane =
        [
            new("cod", "WBS / cod", 1.6), new("simbol", "Simbol", 1.3), new("denumire", "Denumire", 5.0), new("um", "UM", 0.6, AliniereColoana.Centru),
            new("cantitate", "Cantitate", 1.1, AliniereColoana.Dreapta), new("pu", "PU (lei)", 1.1, AliniereColoana.Dreapta),
            new("valoare", "Valoare (lei)", 1.4, AliniereColoana.Dreapta),
        ];
        if (areOferta)
            coloane.Add(new("oferta", $"Ofertă ({v.Moneda})", 1.4, AliniereColoana.Dreapta));
        var aspect = new AspectDocument(v.Denumire, null, $"Întocmit la {ceas.Azi:dd.MM.yyyy}",
            [new("Obiectiv", "obiectiv"), new("Versiune", "versiune"), new("Valori", "moneda")],
            "linii", coloane,
            [
                new("Cheltuieli directe pe resurse:", "directe"), new($"Contribuții manoperă (CAM) {p.CotaContributiiManopera:P2}:", "cam"),
                new($"Cheltuieli indirecte {p.CotaCheltuieliIndirecte:P2}:", "indirecte"), new($"Profit {p.CotaProfit:P2}:", "profit"),
                new("Total fără TVA:", "fara_tva"), new($"TVA {p.CotaTva:P2}:", "tva"), new("TOTAL CU TVA:", "cu_tva"),
            ],
            ["Întocmit", "Verificat"], "observatii", Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync("deviz-oferta", antet, aspect, date, $"{NumeFisier(v)}.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private static DateArticol Date(ArticolCerere c) =>
        new(c.Simbol ?? string.Empty, c.Denumire ?? string.Empty, c.UnitateMasura ?? string.Empty, c.Cantitate, c.PretUnitar);

    private static string Lei(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("N2", Ro);

    private static string NumeFisier(VersiuneDeviz v)
    {
        var nume = string.Concat($"Deviz_{ExportDeviz.TipText(v.Tip)}_{v.Numar}_{v.Denumire}".Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        return nume.Length > 80 ? nume[..80] : nume;
    }
}
