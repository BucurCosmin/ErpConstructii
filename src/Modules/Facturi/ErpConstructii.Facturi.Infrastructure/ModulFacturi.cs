using System.IO.Compression;
using System.Text.Json;
using ErpConstructii.Facturi.Import;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Parteneri;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Facturi.Infrastructure;

// ---------------------------------------------------------------- DTO-uri

public sealed record CampTextDto(string? Valoare, string Certitudine, string? TextSursa);

public sealed record CampDataDto(DateOnly? Valoare, string Certitudine, string? TextSursa);

public sealed record CampNumarDto(decimal? Valoare, string Certitudine, string? TextSursa);

public sealed record LinieFacturaCititaDto(int Rand, CampTextDto Denumire, CampTextDto UnitateMasura, CampNumarDto Cantitate,
    CampNumarDto PretUnitar, CampNumarDto ValoareFaraTva, CampNumarDto CotaTva);

public sealed record FurnizorGasitDto(Guid Id, string Denumire, string? Cui);

public sealed record ProiectPropusDto(Guid Id, string Cod, string Denumire, double Scor, string Certitudine);

public sealed record ProblemaDto(string Severitate, string Locatie, string Mesaj);

/// <summary>Ce a înțeles sistemul din factură; nimic nu se salvează până la confirmare.</summary>
/// <param name="FurnizorInNomenclator">Furnizorul găsit după CUI în nomenclatorul firmei, dacă există.</param>
/// <param name="FacturaExistenta">Descrierea facturii deja înregistrate cu același fișier sau același număr de la același furnizor.</param>
public sealed record InterpretareFacturaDto(
    string NumeFisier,
    string Sursa,
    bool DinOcr,
    CampTextDto Numar,
    CampDataDto DataEmitere,
    CampDataDto DataScadenta,
    CampTextDto Moneda,
    CampTextDto FurnizorDenumire,
    CampTextDto FurnizorCui,
    CampTextDto CumparatorCui,
    CampNumarDto TotalFaraTva,
    CampNumarDto TotalTva,
    CampNumarDto TotalDePlata,
    IReadOnlyList<LinieFacturaCititaDto> Linii,
    string? Referinta,
    FurnizorGasitDto? FurnizorInNomenclator,
    ProiectPropusDto? ProiectPropus,
    IReadOnlyList<ProblemaDto> Probleme,
    string? FacturaExistenta,
    IReadOnlyList<PaginaDocumentDto> Pagini,
    IReadOnlyList<int> PaginiFactura);

/// <summary>Ce conține o pagină a PDF-ului (dosarele scanate au la un loc factura, nota de comandă, avizul, certificatele).</summary>
public sealed record PaginaDocumentDto(int Pagina, string Tip, string Descriere, string? Numar, DateOnly? Data);

public sealed record LinieFacturaCerere(string Denumire, string UnitateMasura, decimal Cantitate, decimal PretUnitar, decimal ValoareFaraTva, decimal CotaTva, bool IntraInStoc = true);

/// <summary>Datele verificate de utilizator. Pentru XML e-Factura serverul recitește XML-ul (documentul oficial) și ignoră liniile trimise.</summary>
public sealed record ConfirmaFacturaCerere(
    string Numar,
    DateOnly DataEmitere,
    DateOnly? DataScadenta,
    string Moneda,
    string FurnizorDenumire,
    string? FurnizorCui,
    bool AdaugaFurnizorInNomenclator,
    IReadOnlyList<LinieFacturaCerere> Linii,
    decimal TotalFaraTva,
    decimal TotalTva,
    decimal TotalDePlata,
    string? Referinta,
    string? Observatii);

public sealed record FacturaPrimitaDto(
    Guid Id,
    string Numar,
    DateOnly DataEmitere,
    DateOnly? DataScadenta,
    string Moneda,
    Guid? FurnizorId,
    string FurnizorDenumire,
    string? FurnizorCui,
    Guid ProiectId,
    string ProiectCod,
    decimal TotalFaraTva,
    decimal TotalTva,
    decimal TotalDePlata,
    string? Referinta,
    string? Observatii,
    string Sursa,
    string Stare,
    bool EsteStorno,
    bool AreXml,
    bool ArePdf,
    string InregistrataDeNume,
    DateTimeOffset InregistrataLa,
    string? MotivAnulare,
    IReadOnlyList<LinieFactura> Linii,
    IReadOnlyList<string>? MaterialeGasite = null);

public sealed record AnuleazaFacturaCerere(string Motiv);

/// <summary>
/// Facturile primite (REQ-FCT-07/08/11): import din XML e-Factura (sau arhiva ZIP din SPV) și din PDF (text sau OCR),
/// verificare de utilizator, înregistrare pe proiect cu originalul arhivat; lista și anularea.
/// Drepturi: modulul Financiar (contabilul înregistrează; achizitorul și managerul de proiect văd).
/// </summary>
public sealed class ModulFacturi : IModul
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] ExtensiiPdf = [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"];

    public string Nume => FacturiDbContext.NumeSchema;

    public void AdaugaServicii(IServiceCollection servicii, IConfiguration configurare)
    {
        servicii.AdaugaDbContextModul<FacturiDbContext>(configurare, Nume);
        servicii.AddScoped<IInterogareFacturiPrimite, InterogareFacturiPrimite>();
    }

    public void MapeazaEndpointuri(IEndpointRouteBuilder rute)
    {
        var g = rute.MapGroup("/facturi").WithTags("Facturi");
        var vizualizare = Permisiuni.Pentru(ModulErp.Financiar, Actiune.Vizualizare);
        var creare = Permisiuni.Pentru(ModulErp.Financiar, Actiune.Creare);
        var anulare = Permisiuni.Pentru(ModulErp.Financiar, Actiune.Anulare);

        g.MapGet("/primite", Lista).CerePermisiune(vizualizare);
        g.MapGet("/primite/{id:guid}", Detalii).CerePermisiune(vizualizare);
        // Și gestionarul (aprovizionare) vede originalul: recepția (NIR) se face cu factura în față.
        g.MapGet("/primite/{id:guid}/original/{tip}", Original)
            .CereOricarePermisiune(vizualizare, Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Vizualizare));
        g.MapPost("/primite/{id:guid}/anulare", Anuleaza).CerePermisiune(anulare);

        g.MapPost("/primite/import/interpretare", Interpreteaza)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentFactura.DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);
        g.MapPost("/proiecte/{proiectId:guid}/primite/import", Confirma)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentFactura.DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);
    }

    // ---------------------------------------------------------------- listă / detalii

    /// <param name="cauta">Text liber: furnizor, CUI sau numărul facturii.</param>
    /// <param name="material">Un material din liniile facturii (fără diacritice / majuscule).</param>
    private static async Task<IResult> Lista(
        Guid? proiectId, string? furnizor, string? cauta, string? material, DateOnly? dataDe, DateOnly? dataPanaLa, string? stare,
        FacturiDbContext db, IServiciuPermisiuni permisiuni, IUtilizatorCurent utilizator, IFirmaCurenta firma, CancellationToken ct)
    {
        if (utilizator.UtilizatorId is not { } uid || firma.FirmaId is not { } fid)
        {
            return Results.Unauthorized();
        }

        var q = db.FacturiPrimite.AsNoTracking();
        var accesibile = (await permisiuni.PentruUtilizatorAsync(uid, ct)).ProiecteAccesibile(Permisiuni.Pentru(ModulErp.Financiar, Actiune.Vizualizare), fid);
        if (accesibile is not null)
        {
            var lista = accesibile.ToList();
            q = q.Where(f => lista.Contains(f.ProiectId));
        }

        if (proiectId is { } p) q = q.Where(f => f.ProiectId == p);
        if (!string.IsNullOrWhiteSpace(furnizor)) q = q.Where(f => EF.Functions.ILike(f.FurnizorDenumire, $"%{furnizor.Trim()}%") || f.FurnizorCui == furnizor.Trim());
        if (!string.IsNullOrWhiteSpace(cauta))
        {
            var tipar = $"%{cauta.Trim()}%";
            var cui = cauta.Trim().ToUpperInvariant().Replace("RO", string.Empty, StringComparison.Ordinal);
            q = q.Where(f => EF.Functions.ILike(f.FurnizorDenumire, tipar) || f.FurnizorCui == cui || EF.Functions.ILike(f.Numar, tipar));
        }

        string? termen = null;
        if (!string.IsNullOrWhiteSpace(material))
        {
            termen = FaraDiacritice(material.Trim());
            var tipar = $"%{termen}%";
            // Materialele stau în liniile facturii (jsonb); textul se compară fără diacritice și fără majuscule.
            var cuMaterial = db.Database.SqlQuery<Guid>(
                $"""SELECT f.id AS "Value" FROM facturi.facturi_primite f WHERE EXISTS (SELECT 1 FROM jsonb_array_elements(f.linii_json) e WHERE translate(lower(e->>'Denumire'), 'ăâîșşțţ', 'aaisstt') LIKE {tipar})""");
            q = q.Where(f => cuMaterial.Contains(f.Id));
        }

        if (dataDe is { } d1) q = q.Where(f => f.DataEmitere >= d1);
        if (dataPanaLa is { } d2) q = q.Where(f => f.DataEmitere <= d2);
        if (string.Equals(stare, "Anulata", StringComparison.OrdinalIgnoreCase)) q = q.Where(f => db.Anulari.Any(a => a.FacturaId == f.Id));
        if (string.Equals(stare, "Inregistrata", StringComparison.OrdinalIgnoreCase)) q = q.Where(f => !db.Anulari.Any(a => a.FacturaId == f.Id));

        var facturi = await q.OrderByDescending(f => f.DataEmitere).ThenByDescending(f => f.CreatLa).Take(500).ToListAsync(ct);
        var id = facturi.Select(f => f.Id).ToList();
        var anulari = await db.Anulari.AsNoTracking().Where(a => id.Contains(a.FacturaId)).ToDictionaryAsync(a => a.FacturaId, ct);
        return Results.Ok(facturi.Select(f => CaDto(f, anulari.GetValueOrDefault(f.Id)) with
        {
            // Ce materiale din factură au potrivit căutarea (afișate în listă).
            MaterialeGasite = termen is null ? null : f.Linii.Select(l => l.Denumire).Where(d => FaraDiacritice(d).Contains(termen, StringComparison.Ordinal)).Distinct().ToList(),
        }));
    }

    private static string FaraDiacritice(string text)
    {
        var fd = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(fd.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static async Task<IResult> Detalii(Guid id, FacturiDbContext db, CancellationToken ct) =>
        await db.FacturiPrimite.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct) is { } f
            ? Results.Ok(CaDto(f, await db.Anulari.AsNoTracking().FirstOrDefaultAsync(a => a.FacturaId == id, ct)))
            : Results.NotFound();

    private static async Task<IResult> Original(Guid id, string tip, FacturiDbContext db, CancellationToken ct)
    {
        var f = await db.FacturiPrimite.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var documentId = tip.ToLowerInvariant() switch
        {
            "xml" => f?.DocumentXmlId,
            "pdf" => f?.DocumentPdfId,
            _ => null,
        };
        var document = documentId is null ? null : await db.Documente.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        return document is null ? Results.NotFound() : Results.File(document.Continut, document.TipContinut, document.NumeFisier, enableRangeProcessing: true);
    }

    private static async Task<IResult> Anuleaza(
        Guid id, AnuleazaFacturaCerere cerere, FacturiDbContext db, IUtilizatorCurent utilizator, ICeas ceas, CancellationToken ct)
    {
        var f = await db.FacturiPrimite.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
        {
            return Results.NotFound();
        }

        if (await db.Anulari.AnyAsync(a => a.FacturaId == id, ct))
        {
            return Results.Conflict(new { mesaj = "Factura este deja anulată." });
        }

        var r = AnulareFacturaPrimita.Creeaza(id, cerere.Motiv ?? string.Empty, utilizator.UtilizatorId ?? Guid.Empty, utilizator.Nume ?? string.Empty, ceas.Acum);
        if (!r.Succes)
        {
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        }

        db.Anulari.Add(r.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Ok(CaDto(f, r.Valoare));
    }

    // ---------------------------------------------------------------- import

    private static async Task<IResult> Interpreteaza(
        IFormFile fisier,
        IEnumerable<IExtractorText> extractori,
        IInterogareFirma firme,
        IServiciuParteneri parteneri,
        IInterogareProiecte proiecte,
        FacturiDbContext db,
        CancellationToken ct)
    {
        var (sursa, eroare) = await CitesteAsync(fisier, ct);
        if (eroare is not null)
        {
            return eroare;
        }

        var firma = await firme.FirmaActivaAsync(ct);
        var (citita, dinOcr, problemaCitire, pagini) = await InterpreteazaAsync(sursa!, extractori, firma?.Cui, ct);
        if (citita is null)
        {
            return Results.BadRequest(new { mesaj = problemaCitire });
        }

        var probleme = citita.Probleme.ToList();
        if (citita.Sursa == SursaFactura.EFactura && firma?.Cui is { } cuiFirma && citita.CumparatorCui.Valoare is { } cumparator &&
            ValidareFiscala.NormalizeazaCui(cuiFirma) != cumparator)
        {
            probleme.Add(new ProblemaImport(Severitate.Eroare, "cumpărător",
                $"Factura e emisă către CUI {cumparator}, nu către firma activă ({firma.Denumire}, CUI {ValidareFiscala.NormalizeazaCui(cuiFirma)}). Schimbă firma activă."));
        }

        if (string.IsNullOrWhiteSpace(firma?.Cui))
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "firmă",
                "Firma activă nu are CUI completat (Administrare → Datele firmei); nu pot verifica cumpărătorul."));
        }

        // După CUI sau, dacă nu există, după denumire (furnizor adăugat din nota de comandă, încă fără CUI).
        var furnizor = await parteneri.CautaFurnizorAsync(citita.FurnizorCui.Valoare, citita.FurnizorDenumire.Valoare, ct);
        var candidati = (await proiecte.ListaActiveAsync(ct)).Select(p => new ProiectCandidat(p.Id, p.Cod, p.Denumire)).ToList();
        var propunere = PotrivireProiect.Propune(citita.Referinta, candidati);
        var existenta = await FacturaExistentaAsync(db, DocumentFactura.Amprenta(sursa!.Continut), citita.FurnizorCui.Valoare, citita.Numar.Valoare, citita.DataEmitere.Valoare, ct);

        return Results.Ok(new InterpretareFacturaDto(
            sursa.NumeFisier,
            citita.Sursa.ToString(),
            dinOcr,
            Text(citita.Numar),
            Data(citita.DataEmitere),
            Data(citita.DataScadenta),
            Text(citita.Moneda),
            Text(citita.FurnizorDenumire),
            Text(citita.FurnizorCui),
            Text(citita.CumparatorCui),
            Numar(citita.TotalFaraTva),
            Numar(citita.TotalTva),
            Numar(citita.TotalDePlata),
            citita.Linii.Select(l => new LinieFacturaCititaDto(l.Rand, Text(l.Denumire), Text(l.UnitateMasura), Numar(l.Cantitate),
                Numar(l.PretUnitar), Numar(l.ValoareFaraTva), Numar(l.CotaTva))).ToList(),
            citita.Referinta,
            furnizor is null ? null : new FurnizorGasitDto(furnizor.Id, furnizor.Denumire, furnizor.Cui),
            propunere is null ? null : new ProiectPropusDto(propunere.Proiect.Id, propunere.Proiect.Cod, propunere.Proiect.Denumire, propunere.Scor, propunere.Certitudine.ToString()),
            probleme.Select(p => new ProblemaDto(p.Severitate.ToString(), p.Locatie, p.Mesaj)).ToList(),
            existenta,
            pagini.Select(p => new PaginaDocumentDto(p.Pagina, p.Tip.ToString(), p.Descriere, p.Numar, p.Data)).ToList(),
            pagini.Where(p => p.Tip == TipPaginaFactura(pagini)).Select(p => p.Pagina).ToList()));
    }

    private static async Task<IResult> Confirma(
        Guid proiectId,
        IFormFile fisier,
        [FromForm] string date,
        IEnumerable<IExtractorText> extractori,
        IInterogareFirma firme,
        IServiciuParteneri parteneri,
        IInterogareProiecte proiecte,
        FacturiDbContext db,
        IUtilizatorCurent utilizator,
        CancellationToken ct)
    {
        ConfirmaFacturaCerere? cerere;
        try
        {
            cerere = JsonSerializer.Deserialize<ConfirmaFacturaCerere>(date, Json);
        }
        catch (JsonException)
        {
            cerere = null;
        }

        if (cerere is null)
        {
            return Results.BadRequest(new { mesaj = "Datele facturii nu sunt valide." });
        }

        if (utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
        {
            return Results.BadRequest(new { mesaj = "Utilizatorul nu este disponibil în sesiune." });
        }

        var (sursa, eroare) = await CitesteAsync(fisier, ct);
        if (eroare is not null)
        {
            return eroare;
        }

        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
        {
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        }

        var firma = await firme.FirmaActivaAsync(ct);
        var esteXml = EsteXmlSauZip(sursa!);
        DateFacturaPrimita dateFactura;

        if (esteXml)
        {
            // e-Factura: documentul oficial e XML-ul; datele lui au prioritate față de cele trimise din formular.
            var (citita, _, problema, _) = await InterpreteazaAsync(sursa!, extractori, firma?.Cui, ct);
            if (citita is null)
            {
                return Results.BadRequest(new { mesaj = problema });
            }

            if (firma?.Cui is { } cuiFirma && citita.CumparatorCui.Valoare is { } cumparator && ValidareFiscala.NormalizeazaCui(cuiFirma) != cumparator)
            {
                return Results.Conflict(new { mesaj = $"Factura e emisă către CUI {cumparator}, nu către firma activă. Schimbă firma activă și reîncearcă." });
            }

            dateFactura = DinFacturaCitita(citita, proiect, cerere);
        }
        else
        {
            dateFactura = new DateFacturaPrimita(
                cerere.Numar ?? string.Empty, cerere.DataEmitere, cerere.DataScadenta, cerere.Moneda ?? "RON", null,
                cerere.FurnizorDenumire ?? string.Empty, cerere.FurnizorCui, proiect.Id, proiect.Cod,
                (cerere.Linii ?? []).Select(l => new LinieFactura(l.Denumire ?? string.Empty, l.UnitateMasura ?? string.Empty, l.Cantitate, l.PretUnitar,
                    l.ValoareFaraTva, l.CotaTva, VerificareTotaluriFactura.TvaLinie(l.ValoareFaraTva, l.CotaTva), l.IntraInStoc)).ToList(),
                cerere.TotalFaraTva, cerere.TotalTva, cerere.TotalDePlata, cerere.Referinta, cerere.Observatii);
        }

        var hash = DocumentFactura.Amprenta(sursa!.Continut);
        if (await FacturaExistentaAsync(db, hash, dateFactura.FurnizorCui, dateFactura.Numar, dateFactura.DataEmitere, ct) is { } existenta)
        {
            return Results.Conflict(new { mesaj = $"Factura a fost deja înregistrată: {existenta}." });
        }

        // Furnizorul în nomenclator: găsit după CUI sau denumire (cel găsit după denumire, fără CUI, primește CUI-ul facturii)
        // ori adăugat (la cerere) cu datele minime din factură.
        Guid? furnizorId = null;
        var gasit = await parteneri.CautaFurnizorAsync(dateFactura.FurnizorCui, dateFactura.FurnizorDenumire, ct);
        if (gasit is not null && (gasit.Cui is not null || string.IsNullOrWhiteSpace(dateFactura.FurnizorCui)))
        {
            furnizorId = gasit.Id;
        }
        else if (gasit is not null || cerere.AdaugaFurnizorInNomenclator)
        {
            var r = await parteneri.AsiguraFurnizorAsync(dateFactura.FurnizorDenumire, dateFactura.FurnizorCui, platitorTva: dateFactura.TotalTva != 0, ct);
            if (!r.Succes)
            {
                return Results.BadRequest(new { mesaj = "Furnizorul nu a putut fi adăugat în nomenclator: " + r.Eroare.Mesaj });
            }

            furnizorId = r.Valoare.Id;
        }

        // Originalul unei facturi anulate (reînregistrată, ex. pe proiectul corect) se refolosește: arhiva păstrează un singur exemplar.
        var documentExistent = await db.Documente.AsNoTracking().Where(d => d.HashSha256 == hash).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        var document = documentExistent is null ? new DocumentFactura(sursa.NumeFisier, TipContinut(sursa.Extensie), sursa.Continut) : null;
        var documentId = documentExistent ?? document!.Id;
        var rezultat = FacturaPrimita.Inregistreaza(
            dateFactura with { FurnizorId = furnizorId },
            esteXml ? SursaFactura.EFactura : SursaFactura.Pdf,
            esteXml ? documentId : null,
            esteXml ? null : documentId,
            uid,
            utilizator.Nume);
        if (!rezultat.Succes)
        {
            return Results.BadRequest(new { mesaj = rezultat.Eroare.Mesaj });
        }

        if (document is not null)
        {
            db.Documente.Add(document);
        }

        db.FacturiPrimite.Add(rezultat.Valoare);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/facturi/primite/{rezultat.Valoare.Id}", CaDto(rezultat.Valoare, null));
    }

    // ---------------------------------------------------------------- utilitare

    private static DateFacturaPrimita DinFacturaCitita(FacturaCitita c, DateProiectPentruDocument proiect, ConfirmaFacturaCerere cerere) => new(
        c.Numar.Valoare ?? string.Empty,
        c.DataEmitere.Valoare,
        c.DataScadenta.Certitudine == Certitudine.Lipsa ? null : c.DataScadenta.Valoare,
        c.Moneda.Valoare ?? "RON",
        null,
        c.FurnizorDenumire.Valoare ?? string.Empty,
        c.FurnizorCui.Valoare,
        proiect.Id,
        proiect.Cod,
        // XML: liniile oficiale din XML; din formular se ia doar bifa „intră în stoc”, pe poziție.
        c.Linii.Select((l, i) => new LinieFactura(
            l.Denumire.Valoare ?? string.Empty, l.UnitateMasura.Valoare ?? string.Empty, l.Cantitate.Valoare, l.PretUnitar.Valoare,
            l.ValoareFaraTva.Valoare, l.CotaTva.Valoare, VerificareTotaluriFactura.TvaLinie(l.ValoareFaraTva.Valoare, l.CotaTva.Valoare),
            cerere.Linii is { } lc && i < lc.Count ? lc[i].IntraInStoc : true)).ToList(),
        c.TotalFaraTva.Valoare,
        c.TotalTva.Valoare,
        c.TotalDePlata.Valoare,
        c.Referinta,
        cerere.Observatii);

    /// <summary>
    /// Paginile din care se citește factura: vizualizarea e-Factura din SPV (conținutul legal, același format la toți
    /// furnizorii); în lipsa ei, factura tipărită de furnizor.
    /// </summary>
    private static TipPagina TipPaginaFactura(IReadOnlyList<PaginaClasificata> pagini) => CitireFacturaText.TipPaginaFactura(pagini);

    private static async Task<(FacturaCitita? Factura, bool DinOcr, string? Problema, IReadOnlyList<PaginaClasificata> Pagini)> InterpreteazaAsync(
        SursaImport sursa, IEnumerable<IExtractorText> extractori, string? cuiFirma, CancellationToken ct)
    {
        if (EsteXmlSauZip(sursa))
        {
            var xml = sursa.Extensie == ".zip" ? XmlDinArhiva(sursa.Continut) : sursa.Continut;
            if (xml is null)
            {
                return (null, false, "Arhiva nu conține un XML e-Factura.", []);
            }

            var r = InterpretorEFactura.Interpreteaza(xml);
            return r.Valoare is null ? (null, false, r.Probleme.FirstOrDefault()?.Mesaj, []) : (r.Valoare, false, null, []);
        }

        var extractor = extractori.FirstOrDefault(e => e.Accepta(sursa.NumeFisier));
        if (extractor is null)
        {
            return (null, false, "Format nesuportat pentru citire automată.", []);
        }

        var text = await extractor.ExtrageAsync(sursa, ct);
        if (text.Valoare is null)
        {
            return (null, false, text.Probleme.FirstOrDefault()?.Mesaj ?? "Documentul nu poate fi citit.", []);
        }

        var (factura, pagini) = CitireFacturaText.Citeste(text.Valoare, text.Probleme, cuiFirma);
        return (factura, text.Valoare.DinOcr, null, pagini);
    }

    /// <summary>Arhiva descărcată din SPV conține XML-ul facturii și „semnatura_*.xml” (semnătura ANAF).</summary>
    private static byte[]? XmlDinArhiva(byte[] zip)
    {
        try
        {
            using var arhiva = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            foreach (var intrare in arhiva.Entries.Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                                                              !e.Name.StartsWith("semnatura", StringComparison.OrdinalIgnoreCase)))
            {
                if (intrare.Length > DocumentFactura.DimensiuneMaxima)
                {
                    continue; // protecție la arhive „bombă”
                }

                using var ms = new MemoryStream();
                using (var s = intrare.Open())
                {
                    s.CopyTo(ms);
                }

                if (InterpretorEFactura.EsteEFactura(ms.ToArray()))
                {
                    return ms.ToArray();
                }
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return null;
    }

    private static async Task<(SursaImport? Sursa, IResult? Eroare)> CitesteAsync(IFormFile? fisier, CancellationToken ct)
    {
        if (fisier is null || fisier.Length == 0)
        {
            return (null, Results.BadRequest(new { mesaj = "Alege factura (XML / ZIP din SPV sau PDF)." }));
        }

        if (fisier.Length > DocumentFactura.DimensiuneMaxima)
        {
            return (null, Results.BadRequest(new { mesaj = "Fișierul depășește limita de 15 MB." }));
        }

        var ext = Path.GetExtension(fisier.FileName).ToLowerInvariant();
        if (ext is not (".xml" or ".zip") && !ExtensiiPdf.Contains(ext))
        {
            return (null, Results.BadRequest(new { mesaj = "Format nesuportat. Încarcă XML-ul e-Factura (sau arhiva ZIP din SPV), un PDF sau o imagine." }));
        }

        using var ms = new MemoryStream((int)fisier.Length);
        await fisier.CopyToAsync(ms, ct);
        return (new SursaImport(fisier.FileName, ms.ToArray()), null);
    }

    private static bool EsteXmlSauZip(SursaImport s) => s.Extensie is ".xml" or ".zip";

    /// <summary>Aceeași factură: același fișier, sau același număr de la același furnizor în același an.</summary>
    private static async Task<string?> FacturaExistentaAsync(FacturiDbContext db, string hash, string? cui, string? numar, DateOnly data, CancellationToken ct)
    {
        var dupaFisier = await db.Documente.AsNoTracking().Where(d => d.HashSha256 == hash).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        var q = db.FacturiPrimite.AsNoTracking().Where(f => !db.Anulari.Any(a => a.FacturaId == f.Id));
        var existenta = dupaFisier is { } idDoc
            ? await q.FirstOrDefaultAsync(f => f.DocumentXmlId == idDoc || f.DocumentPdfId == idDoc, ct)
            : null;

        if (existenta is null && !string.IsNullOrWhiteSpace(cui) && !string.IsNullOrWhiteSpace(numar))
        {
            var cuiNormalizat = ValidareFiscala.NormalizeazaCui(cui);
            var numarCurat = numar.Trim();
            existenta = await q.FirstOrDefaultAsync(f => f.FurnizorCui == cuiNormalizat && f.Numar == numarCurat && f.DataEmitere.Year == data.Year, ct);
        }

        return existenta is null ? null : $"factura {existenta.Numar} din {existenta.DataEmitere:dd.MM.yyyy} de la {existenta.FurnizorDenumire}, proiect {existenta.ProiectCod}";
    }

    private static string TipContinut(string extensie) => extensie switch
    {
        ".xml" => "application/xml",
        ".zip" => "application/zip",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tif" or ".tiff" => "image/tiff",
        _ => "application/octet-stream",
    };

    private static CampTextDto Text(CampCitit<string> c) => new(c.Valoare, c.Certitudine.ToString(), c.TextSursa);

    private static CampDataDto Data(CampCitit<DateOnly> c) => new(c.Certitudine == Certitudine.Lipsa ? null : c.Valoare, c.Certitudine.ToString(), c.TextSursa);

    private static CampNumarDto Numar(CampCitit<decimal> c) => new(c.Certitudine == Certitudine.Lipsa ? null : c.Valoare, c.Certitudine.ToString(), c.TextSursa);

    private static FacturaPrimitaDto CaDto(FacturaPrimita f, AnulareFacturaPrimita? anulare) => new(
        f.Id, f.Numar, f.DataEmitere, f.DataScadenta, f.Moneda, f.FurnizorId, f.FurnizorDenumire, f.FurnizorCui, f.ProiectId, f.ProiectCod,
        f.TotalFaraTva, f.TotalTva, f.TotalDePlata, f.Referinta, f.Observatii, f.Sursa.ToString(),
        anulare is null ? "Inregistrata" : "Anulata", f.EsteStorno,
        f.DocumentXmlId is not null, f.DocumentPdfId is not null, f.InregistrataDeNume, f.CreatLa,
        anulare is null ? null : $"{anulare.Motiv} ({anulare.AnulataDeNume}, {anulare.AnulataLa:dd.MM.yyyy})", f.Linii);
}
