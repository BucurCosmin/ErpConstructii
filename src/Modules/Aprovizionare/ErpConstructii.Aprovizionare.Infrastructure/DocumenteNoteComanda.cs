using System.Globalization;
using System.Text.RegularExpressions;
using ErpConstructii.Formulare;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Aprovizionare.Infrastructure;

/// <param name="Tip">registru (o linie pe notă) | detaliat (o linie pe material din fiecare notă) | materiale (cantitățile adunate pe lucrare).</param>
/// <param name="Filtre">Descrierea filtrelor din ecran, tipărită pe raport.</param>
public sealed record RaportNoteComandaCerere(IReadOnlyList<Guid>? Id, string? Tip, string? Filtre);

public sealed record NoteComandaAlese(IReadOnlyList<Guid>? Id);

/// <summary>
/// Documentele notelor de comandă: Word din șablonul firmei (tipul „nota-comanda”, formularul GC 007), PDF (din șablon prin
/// conversie sau în formatul standard) și rapoartele pe lucrare (registru, detaliat, materiale comandate).
/// </summary>
internal static class DocumenteNoteComanda
{
    private const string TipDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const int MaximTiparire = 200;
    private const int MaximRaport = 5000;
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    private const string ActeInsotitoare = "certificate de calitate, declarații de conformitate, buletine de analiză etc.";

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var vizualizare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare);
        g.MapGet("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/document.docx", Word).WithName("WordNotaComanda")
            .WithSummary("Nota de comandă în Word, din șablonul firmei (GC 007)")
            .Produces(StatusCodes.Status200OK, contentType: TipDocx).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .CerePermisiune(vizualizare);
        g.MapGet("/proiecte/{proiectId:guid}/note-comanda/{id:guid}/pdf", Pdf).WithName("PdfNotaComanda")
            .WithSummary("Nota de comandă în PDF").Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound).CerePermisiune(vizualizare);
        g.MapPost("/note-comanda/pdf", PdfMultiple).WithName("PdfNoteComanda")
            .WithSummary("Mai multe note de comandă într-un singur PDF (tipărire)").Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status400BadRequest).CerePermisiune(vizualizare);
        g.MapPost("/note-comanda/raport/pdf", Raport).WithName("RaportNoteComanda")
            .WithSummary("Raportul notelor de comandă alese, pe lucrare: registru, detaliat pe materiale sau materialele comandate (adunate)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").Produces(StatusCodes.Status400BadRequest).CerePermisiune(vizualizare);
    }

    private static async Task<IResult> Word(
        Guid proiectId, Guid id, AprovizionareDbContext db, IInterogareSabloaneFormulare sabloane, IGeneratorFormular generator, IInterogareFirma firme,
        CancellationToken ct)
    {
        var n = await db.NoteComanda.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (n is null)
            return Results.NotFound();
        var sablon = n.SablonId is { } sid && n.VersiuneSablon is { } versiune
            ? await sabloane.SablonAsync(sid, versiune, ct)
            : null;
        sablon ??= await sabloane.SablonActivAsync(CatalogFormulare.NotaComanda.Cod, ct);
        if (sablon is null)
            return Results.Conflict(new { mesaj = "Nu există șablonul Word „Notă de comandă către furnizor (GC 007)”. Încarcă-l în Formulare și documente." });

        var date = Date(n, await firme.FirmaActivaAsync(ct));
        var docx = generator.Genereaza(sablon.Continut, CatalogFormulare.NotaComanda, date,
            new MetadateFormular(CatalogFormulare.NotaComanda.Cod, sablon.Versiune, n.Id, n.NumarAfisat));
        return Results.File(docx, TipDocx, $"{NumeFisier(n)}.docx");
    }

    private static async Task<IResult> Pdf(
        Guid proiectId, Guid id, AprovizionareDbContext db, IServiciuDocumente documente, IInterogareFirma firme, CancellationToken ct)
    {
        var n = await db.NoteComanda.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ProiectId == proiectId, ct);
        if (n is null)
            return Results.NotFound();
        var doc = await PdfAsync(n, await firme.FirmaActivaAsync(ct), documente, ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private static async Task<IResult> PdfMultiple(
        NoteComandaAlese cerere, AprovizionareDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (cerere.Id is not { Count: > 0 and <= MaximTiparire } ids)
            return Results.BadRequest(new { mesaj = $"Alege între 1 și {MaximTiparire} note de comandă." });
        var note = await AleseAsync(ids, db, permisiuni, utilizator, firma, ct);
        if (note is null)
            return Results.Unauthorized();
        if (note.Count == 0)
            return Results.BadRequest(new { mesaj = "Notele de comandă alese nu există." });
        var f = await firme.FirmaActivaAsync(ct);
        var pdf = new List<DocumentGenerat>();
        foreach (var n in note.OrderBy(x => x.An).ThenBy(x => x.Numar))
            pdf.Add(await PdfAsync(n, f, documente, ct));
        var doc = documente.Combina(pdf, pdf.Count == 1 ? pdf[0].NumeFisier : $"Note_comanda_{pdf.Count}.pdf");
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    private static async Task<IResult> Raport(
        RaportNoteComandaCerere cerere, AprovizionareDbContext db, IServiciuDocumente documente, IInterogareFirma firme, IServiciuPermisiuni permisiuni,
        IUtilizatorCurent utilizator, IFirmaCurenta firma, ICeas ceas, CancellationToken ct)
    {
        if (cerere.Id is not { Count: > 0 and <= MaximRaport } ids)
            return Results.BadRequest(new { mesaj = $"Alege între 1 și {MaximRaport} note de comandă." });
        var tip = (cerere.Tip ?? "registru").Trim().ToLowerInvariant();
        if (tip is not ("registru" or "detaliat" or "materiale"))
            return Results.BadRequest(new { mesaj = "Tipul raportului poate fi: registru, detaliat sau materiale." });
        var note = await AleseAsync(ids, db, permisiuni, utilizator, firma, ct);
        if (note is null)
            return Results.Unauthorized();
        if (note.Count == 0)
            return Results.BadRequest(new { mesaj = "Notele de comandă alese nu există." });

        note = note.OrderBy(n => n.ProiectCod, StringComparer.Ordinal).ThenBy(n => n.Data).ThenBy(n => n.An).ThenBy(n => n.Numar).ToList();
        var f = await firme.FirmaActivaAsync(ct);
        var antet = Antet(f);
        var proiecte = note.Select(n => n.ProiectCod).Distinct().ToList();
        var campuri = new Dictionary<string, string>
        {
            ["perioada"] = $"{Data(note.Min(n => n.Data))} – {Data(note.Max(n => n.Data))}",
            ["proiecte"] = proiecte.Count <= 6 ? string.Join(", ", proiecte) : $"{proiecte.Count} lucrări",
            ["note"] = $"{note.Count} ({note.Count(n => n.Sursa == SursaDocument.Sistem)} emise în aplicație, {note.Count(n => n.Sursa == SursaDocument.Import)} de pe hârtie)",
            ["observatii"] = string.IsNullOrWhiteSpace(cerere.Filtre) ? string.Empty : $"Filtre: {cerere.Filtre.Trim()}",
        };
        ElementAntet[] elemente = [new("Perioada", "perioada"), new("Lucrări", "proiecte"), new("Note de comandă", "note")];

        List<IReadOnlyDictionary<string, string>> linii;
        List<ColoanaPdf> coloane;
        string titlu;
        if (tip == "registru")
        {
            titlu = "Registrul notelor de comandă pe lucrări";
            linii = note.Select((n, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("proiect", n.ProiectCod), ("numar", n.NumarAfisat), ("numar_extern", n.NumarExtern ?? string.Empty),
                ("data", Data(n.Data)), ("lucrare", n.Lucrare ?? string.Empty), ("furnizor", n.FurnizorDenumire),
                ("materiale", n.Linii.Count.ToString(Ro)), ("intocmit", n.InregistrataDeNume))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.3), new("numar", "Notă", 1.4),
                new("numar_extern", "Nr. hârtie", 0.9), new("data", "Data", 1.0, AliniereColoana.Centru), new("lucrare", "Obiect / lucrare", 2.6),
                new("furnizor", "Furnizor", 2.8), new("materiale", "Poz.", 0.6, AliniereColoana.Dreapta), new("intocmit", "Întocmit", 1.6),
            ];
        }
        else if (tip == "detaliat")
        {
            titlu = "Notele de comandă pe lucrări — materiale comandate";
            var i = 0;
            linii = note.SelectMany(n => n.Linii.Select(l => Rand(
                ("numar_crt", (++i).ToString(Ro)), ("proiect", n.ProiectCod), ("numar", n.NumarAfisat), ("data", Data(n.Data)),
                ("furnizor", n.FurnizorDenumire), ("material", l.Denumire), ("um", l.UnitateMasura), ("cantitate", Cant(l.Cantitate)),
                ("termen", l.Termen ?? string.Empty)))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.2), new("numar", "Notă", 1.3),
                new("data", "Data", 0.9, AliniereColoana.Centru), new("furnizor", "Furnizor", 2.2), new("material", "Material / produs", 3.6),
                new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.0, AliniereColoana.Dreapta), new("termen", "Termen", 1.1),
            ];
        }
        else
        {
            titlu = "Materiale comandate pe lucrări (cantități adunate)";
            linii = RaportNoteComanda.MaterialeComandate(note).Select((m, i) => Rand(
                ("numar_crt", (i + 1).ToString(Ro)), ("proiect", m.ProiectCod), ("material", m.Denumire), ("um", m.UnitateMasura),
                ("cantitate", Cant(m.Cantitate)), ("note", m.Note.ToString(Ro)), ("furnizori", string.Join(", ", m.Furnizori)),
                ("perioada", m.PrimaData == m.UltimaData ? Data(m.PrimaData) : $"{Data(m.PrimaData)} – {Data(m.UltimaData)}"))).ToList();
            coloane =
            [
                new("numar_crt", "Nr. crt.", 0.6, AliniereColoana.Centru), new("proiect", "Proiect", 1.2), new("material", "Material / produs", 3.8),
                new("um", "UM", 0.6, AliniereColoana.Centru), new("cantitate", "Cantitate totală", 1.2, AliniereColoana.Dreapta),
                new("note", "Note", 0.6, AliniereColoana.Dreapta), new("furnizori", "Furnizori", 2.6), new("perioada", "Comandat", 1.8),
            ];
        }

        var aspect = new AspectDocument(titlu, null, $"Întocmit la {Data(ceas.Azi)}", elemente, "linii", coloane, [], ["Întocmit"], "observatii", Peisaj: true);
        var date = new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
        var doc = await documente.PdfAsync($"raport-note-comanda-{tip}", antet, aspect, date, $"Note_comanda_{tip}.pdf", ct);
        return Results.File(doc.Continut, doc.TipContinut, doc.NumeFisier);
    }

    /// <summary>Notele alese din proiectele accesibile utilizatorului (null = neautentificat).</summary>
    private static async Task<List<NotaComanda>?> AleseAsync(
        IReadOnlyList<Guid> ids, AprovizionareDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
            return null;
        var accesibile = (await permisiuni.PentruUtilizatorAsync(uid, ct)).ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare), fid);
        var alese = ids.Distinct().ToList();
        var q = db.NoteComanda.AsNoTracking().Where(n => alese.Contains(n.Id));
        if (accesibile is not null)
        {
            var proiecte = accesibile.ToList();
            q = q.Where(n => proiecte.Contains(n.ProiectId));
        }

        return await q.ToListAsync(ct);
    }

    private static Task<DocumentGenerat> PdfAsync(NotaComanda n, DateFirmaDocument? firma, IServiciuDocumente documente, CancellationToken ct)
    {
        var date = Date(n, firma);
        var campuri = new Dictionary<string, string>(date.Campuri)
        {
            ["conditii"] = TextConditii(n.Conditii),
            ["acte"] = ActeInsotitoare,
            ["text_observatii"] = n.Observatii ?? string.Empty,
        };
        var aspect = new AspectDocument(
            "COMANDĂ",
            null,
            $"Nr. {date.Campuri["numar"]} din {date.Campuri["data"]}",
            [
                new("Furnizor", "furnizor_denumire"), new("CUI furnizor", "furnizor_cui"), new("Adresa", "furnizor_adresa"),
                new("Telefon / e-mail", "furnizor_contact"), new("Obiect / lucrare", "lucrare"), new("Proiect", "proiect_cod"),
                new("Condiții", "conditii"), new("Acte însoțitoare", "acte"),
            ],
            "linii",
            [
                new("numar_crt", "Nr. crt.", 0.7, AliniereColoana.Centru), new("denumire", "Material / produs", 6.0),
                new("um", "U.M.", 1.0, AliniereColoana.Centru), new("cantitate", "Cantitate", 1.4, AliniereColoana.Dreapta), new("termen", "Termen", 1.6),
            ],
            [],
            ["Aprobat,\nDirector", "Avizat,", $"Întocmit,\n{n.InregistrataDeNume}"],
            "text_observatii");
        campuri["furnizor_contact"] = string.Join(" / ", new[] { n.FurnizorTelefon, n.FurnizorEmail }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return documente.PdfAsync(CatalogFormulare.NotaComanda.Cod, Antet(firma), aspect, new DateFormular(campuri, date.Tabele), $"{NumeFisier(n)}.pdf", ct);
    }

    /// <summary>Datele notei în schema formularului „nota-comanda” (aceleași pentru Word și PDF).</summary>
    internal static DateFormular Date(NotaComanda n, DateFirmaDocument? f)
    {
        static string Bifa(bool bifat) => bifat ? "☒" : "☐";
        var c = n.Conditii;
        var campuri = new Dictionary<string, string>
        {
            ["numar"] = n.Sursa == SursaDocument.Import && n.NumarExtern is { } ext ? $"{ext} ({n.NumarAfisat})" : n.NumarAfisat,
            ["data"] = Data(n.Data),
            ["proiect_cod"] = n.ProiectCod,
            ["proiect_denumire"] = n.Lucrare ?? string.Empty,
            ["lucrare"] = n.Lucrare ?? string.Empty,
            ["intocmit_de"] = n.InregistrataDeNume,
            ["firma_denumire"] = f?.Denumire ?? string.Empty,
            ["firma_adresa"] = f?.Adresa.Formatata() ?? string.Empty,
            ["firma_cui"] = f?.Cui ?? string.Empty,
            ["firma_nr_reg_com"] = f?.NrRegCom ?? string.Empty,
            ["firma_cont"] = f?.Cont is { } cont ? $"{cont.Banca}: {cont.Iban}" : string.Empty,
            ["firma_telefon"] = f?.Telefon ?? string.Empty,
            ["firma_fax"] = f?.Fax ?? string.Empty,
            ["firma_email"] = f?.Email ?? string.Empty,
            ["furnizor_denumire"] = n.FurnizorDenumire,
            ["furnizor_cui"] = n.FurnizorCui ?? string.Empty,
            ["furnizor_adresa"] = n.FurnizorAdresa ?? string.Empty,
            ["furnizor_telefon"] = n.FurnizorTelefon ?? string.Empty,
            ["furnizor_email"] = n.FurnizorEmail ?? string.Empty,
            ["bifa_receptie_furnizor"] = Bifa(c.Receptie == ReceptieComanda.LaFurnizor),
            ["bifa_receptie_primire"] = Bifa(c.Receptie == ReceptieComanda.LaPrimire),
            ["bifa_clasa_4"] = Bifa(c.ClasaCalitate == ClasaCalitateComanda.Clasa4),
            ["bifa_clasa_best"] = Bifa(c.ClasaCalitate == ClasaCalitateComanda.BestComercial),
            ["bifa_transport_auto"] = Bifa(c.Transport == TransportComanda.Auto),
            ["bifa_transport_cfr"] = Bifa(c.Transport == TransportComanda.Cfr),
            ["bifa_plata_op"] = Bifa(c.Plata == PlataComanda.Op),
            ["bifa_plata_cec"] = Bifa(c.Plata == PlataComanda.FilaCec),
            ["bifa_plata_alte"] = Bifa(c.Plata == PlataComanda.Alte),
            ["observatii"] = string.IsNullOrWhiteSpace(n.Observatii) ? string.Empty : $"Observații: {n.Observatii}",
        };
        var linii = n.Linii.Select((l, i) => Rand(
            ("numar_crt", (i + 1).ToString(Ro)), ("denumire", l.Denumire), ("um", l.UnitateMasura), ("cantitate", Cant(l.Cantitate)), ("termen", l.Termen ?? string.Empty))).ToList();
        return new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii });
    }

    /// <summary>Condițiile bifate, ca text pentru formatul standard (ex. „Recepție la primire; best comercial; transport auto; plata cu OP”).</summary>
    internal static string TextConditii(ConditiiComanda c)
    {
        var parti = new List<string>();
        if (c.Receptie is { } r)
            parti.Add(r == ReceptieComanda.LaFurnizor ? "recepție la furnizor" : "recepție la primire");
        if (c.ClasaCalitate is { } q)
            parti.Add(q == ClasaCalitateComanda.Clasa4 ? "clasa de calitate 4" : "best comercial");
        if (c.Transport is { } t)
            parti.Add(t == TransportComanda.Auto ? "transport auto" : "transport CFR");
        if (c.Plata is { } p)
            parti.Add(p switch { PlataComanda.Op => "plata cu OP", PlataComanda.FilaCec => "plata cu fila CEC", _ => "plata: alte modalități" });
        var text = string.Join("; ", parti);
        return text.Length == 0 ? "conf. STAS / normative în vigoare" : char.ToUpper(text[0], Ro) + text[1..];
    }

    private static AntetFirma Antet(DateFirmaDocument? f) => new(f?.Denumire ?? string.Empty, f?.Cui, f?.NrRegCom, f?.Adresa.Formatata());

    private static string NumeFisier(NotaComanda n) =>
        Regex.Replace($"{n.NumarAfisat}_{n.FurnizorDenumire}", @"[^a-zA-Z0-9_-]+", "_", RegexOptions.CultureInvariant).Trim('_');

    private static IReadOnlyDictionary<string, string> Rand(params (string Camp, string Valoare)[] valori) => valori.ToDictionary(v => v.Camp, v => v.Valoare);

    private static string Data(DateOnly d) => d.ToString("dd.MM.yyyy", Ro);

    private static string Cant(decimal v) => v.ToString("#,##0.####", Ro);
}
