using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using ErpConstructii.Stocuri.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Stocuri.Infrastructure;

public sealed record CampBonDto<T>(T? Valoare, string Certitudine, string? TextSursa);

public sealed record LinieBonCititaDto(
    int Rand, CampBonDto<string> Denumire, string? Cod, CampBonDto<string> UnitateMasura, CampBonDto<decimal?> Cantitate,
    CampBonDto<decimal?> PretUnitar, CampBonDto<decimal?> Valoare);

public sealed record ProiectPropusBonDto(Guid Id, string Cod, string Denumire, double Scor, string Certitudine);

public sealed record ProblemaBonDto(string Severitate, string Locatie, string Mesaj);

/// <param name="Zona">Al câtelea bon din fișier (de la 1); <paramref name="NumarBonuri"/> = câte sunt în fișier.</param>
/// <param name="Numar">Numărul de pe hârtie; lipsă pe document = primul număr liber din an, propus (D-48). Se trimite ca
/// <c>NumarExtern</c> la confirmare.</param>
/// <param name="ObservatiiPropuse">Textul pus la observațiile bonului din aplicație (primitorul, lucrarea).</param>
public sealed record InterpretareBonConsumDto(
    string NumeFisier,
    bool DinOcr,
    int NumarBonuri,
    int Zona,
    bool Gol,
    CampBonDto<string> Numar,
    CampBonDto<DateOnly?> Data,
    CampBonDto<string> Lucrare,
    CampBonDto<string> Gestiune,
    string? Primitor,
    string? Gestionar,
    string? SefCompartiment,
    IReadOnlyList<LinieBonCititaDto> Linii,
    CampBonDto<decimal?> Total,
    ProiectPropusBonDto? ProiectPropus,
    IReadOnlyList<ProblemaBonDto> Probleme,
    string? BonExistent,
    string ObservatiiPropuse);

public sealed record LiniePotrivireCerere(string Denumire, string? UnitateMasura);

public sealed record PotrivireBonCerere(IReadOnlyList<LiniePotrivireCerere> Linii);

public sealed record MaterialSantierDto(Guid ArticolId, string Cod, string Denumire, string UnitateMasura, decimal Cantitate);

public sealed record PropunereMaterialDto(Guid ArticolId, string Sursa);

/// <param name="Propuneri">Pentru fiecare linie din cerere, în ordine: materialul propus sau null.</param>
public sealed record PotrivireBonDto(Guid? GestiuneId, IReadOnlyList<MaterialSantierDto> Materiale, IReadOnlyList<PropunereMaterialDto?> Propuneri, string? Mesaj);

/// <summary>
/// Importul bonurilor de consum de pe hârtie (formularul GC-087, câte două pe pagină): citirea (InterpretorBonConsum),
/// apoi, după alegerea proiectului, materialele din stocul șantierului propuse pentru fiecare linie. Confirmarea trece
/// prin bonul de consum obișnuit (POST /proiecte/{proiectId}/consumuri): consumul se evaluează la CMP-ul din stoc, iar
/// numărul de pe hârtie devine numărul extern al bonului.
/// </summary>
internal static class EndpointuriImportBonConsum
{
    private const long DimensiuneMaxima = 40_000_000;

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var creare = Permisiuni.Pentru(ModulErp.Aprovizionare, Actiune.Creare);
        g.MapPost("/bonuri-consum/import/interpretare", Interpreteaza)
            .WithName("InterpreteazaBonConsum")
            .WithSummary("Citește un bon de consum de pe hârtie (scanare / PDF); zona = al câtelea bon din fișier")
            .Produces<InterpretareBonConsumDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DimensiuneMaxima + 100_000))
            .CerePermisiune(creare);
        g.MapPost("/proiecte/{proiectId:guid}/bonuri-consum/import/potrivire", Potriveste)
            .WithName("PotrivesteBonConsum")
            .WithSummary("Materialele din stocul șantierului proiectului, propuse pentru liniile bonului citit")
            .Produces<PotrivireBonDto>()
            .CerePermisiune(creare);
    }

    private static async Task<IResult> Interpreteaza(
        IFormFile fisier, int? zona, IEnumerable<IExtractorText> extractori, IInterogareProiecte proiecte, StocuriDbContext db, ICeas ceas, CancellationToken ct)
    {
        if (fisier is null || fisier.Length == 0)
            return Results.BadRequest(new { mesaj = "Alege fișierul." });
        if (fisier.Length > DimensiuneMaxima)
            return Results.BadRequest(new { mesaj = $"Fișierul depășește limita de {DimensiuneMaxima / 1_000_000} MB." });

        await using var ms = new MemoryStream((int)fisier.Length);
        await fisier.CopyToAsync(ms, ct);
        var sursa = new SursaImport(Path.GetFileName(fisier.FileName), ms.ToArray());
        var extractor = extractori.FirstOrDefault(e => e.Accepta(sursa.NumeFisier));
        if (extractor is null)
            return Results.BadRequest(new { mesaj = "Format nesuportat. Încarcă un PDF sau o imagine (JPG, PNG, TIFF)." });

        var text = await extractor.ExtrageAsync(sursa, ct);
        if (text.Valoare is null)
            return Results.BadRequest(new { mesaj = text.Probleme.FirstOrDefault()?.Mesaj ?? "Documentul nu poate fi citit." });

        // Dintr-un dosar cu mai multe documente se citesc doar paginile bonurilor.
        var (document, _) = ClasificarePagini.PentruTip(text.Valoare, TipPagina.BonConsum);
        var bonuri = InterpretorBonConsum.Interpreteaza(document);
        var index = zona ?? 1;
        if (bonuri.Count == 0 || index < 1 || index > bonuri.Count)
            return Results.BadRequest(new { mesaj = bonuri.Count == 0 ? "Nu am găsit niciun bon de consum în fișier." : $"Fișierul are {bonuri.Count} bonuri de consum; bonul {index} nu există." });

        var b = bonuri[index - 1];
        var candidati = (await proiecte.ListaActiveAsync(ct)).Select(p => new ProiectCandidat(p.Id, p.Cod, p.Denumire)).ToList();
        var propunere = PotrivireProiect.Propune(b.Lucrare.Valoare, candidati) ?? PotrivireProiect.Propune(b.Gestiune.Valoare, candidati);

        var data = b.Data.Certitudine == Certitudine.Lipsa ? (DateOnly?)null : b.Data.Valoare;
        var existent = await EndpointuriNumereBonuri.BonDublatAsync(db, b.Numar.Valoare, data, ct);
        var observatii = "Bon de consum importat din document";
        observatii += string.Concat(
            b.Primitor is { } pr ? $". Primitor: {pr}" : string.Empty,
            b.Lucrare.Valoare is { } lu ? $". Lucrare: {lu}" : string.Empty);

        var probleme = text.Probleme.Concat(b.Probleme).ToList();
        if (b.Gol)
            probleme.Insert(0, new ProblemaImport(Severitate.Informare, "bon", "Bonul nu are materiale completate (formular gol): nu se importă."));
        if (existent is not null)
            probleme.Insert(0, new ProblemaImport(Severitate.Eroare, "bon", $"Bonul a fost deja importat: {existent}."));

        // D-48: seria de hârtie e pe proiect și an. Fără număr pe bon, se propune primul număr liber din seria proiectului propus;
        // un număr folosit deja în serie e semnalat. Fără proiect propus, nu se poate propune nimic.
        var numar = Camp(b.Numar);
        var an = data?.Year ?? ceas.Azi.Year;
        if (propunere is not null)
        {
            var serie = await EndpointuriNumereBonuri.DocumenteAsync(db, propunere.Proiect.Id, an, ct);
            if (NumerotareExterna.Valoare(b.Numar.Valoare) is not > 0)
            {
                var liber = NumerotareExterna.NumarLiber(serie.Select(d => d.NumarExtern)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                numar = new CampBonDto<string>(liber, Certitudine.Nesigur.ToString(), $"propus: primul număr liber pe {propunere.Proiect.Cod} în {an}");
                probleme.Insert(0, new ProblemaImport(Severitate.Informare, "numar",
                    $"Bonul nu are număr; propun nr. {liber}, primul liber pe proiectul {propunere.Proiect.Cod} în {an} (îl poți schimba)."));
            }
            else if (existent is null && NumerotareExterna.Folosit(b.Numar.Valoare, propunere.Proiect.Id, an, serie) is { Count: > 0 } folosit)
            {
                probleme.Insert(0, new ProblemaImport(Severitate.Avertisment, "numar",
                    $"Nr. {b.Numar.Valoare} e folosit deja pe proiectul {propunere.Proiect.Cod} în {an} de " +
                    $"{string.Join(", ", folosit.Select(d => $"{d.NumarSistem} din {d.Data:dd.MM.yyyy}"))}. Poți importa și renumerota apoi din „Verifică numerele”."));
            }
        }

        return Results.Ok(new InterpretareBonConsumDto(
            sursa.NumeFisier, document.DinOcr, bonuri.Count, index, b.Gol,
            numar, Data(b.Data), Camp(b.Lucrare), Camp(b.Gestiune), b.Primitor, b.Gestionar, b.SefCompartiment,
            b.Linii.Select(l => new LinieBonCititaDto(l.Rand, Camp(l.Denumire), l.Cod, Camp(l.UnitateMasura), Numar(l.Cantitate), Numar(l.PretUnitar), Numar(l.Valoare))).ToList(),
            Numar(b.Total),
            propunere is null ? null : new ProiectPropusBonDto(propunere.Proiect.Id, propunere.Proiect.Cod, propunere.Proiect.Denumire, propunere.Scor, propunere.Certitudine.ToString()),
            probleme.Select(p => new ProblemaBonDto(p.Severitate.ToString(), p.Locatie, p.Mesaj)).ToList(),
            existent,
            observatii));
    }

    private static async Task<IResult> Potriveste(Guid proiectId, PotrivireBonCerere cerere, StocuriDbContext db, CancellationToken ct)
    {
        var santier = await db.Gestiuni.AsNoTracking().FirstOrDefaultAsync(x => x.ProiectId == proiectId, ct);
        if (santier is null)
        {
            return Results.Ok(new PotrivireBonDto(null, [], cerere.Linii.Select(_ => (PropunereMaterialDto?)null).ToList(),
                "Proiectul nu are încă materiale în șantier: transferă-le întâi din depozit (Stocuri → Transfer)."));
        }

        var materiale = await (from s in db.Solduri.AsNoTracking()
                               join a in db.Articole.AsNoTracking() on s.ArticolId equals a.Id
                               where s.GestiuneId == santier.Id && s.Cantitate > 0
                               orderby a.Denumire
                               select new MaterialSantierDto(a.Id, a.Cod, a.Denumire, a.UnitateMasura, s.Cantitate)).ToListAsync(ct);
        var idUri = materiale.Select(m => m.ArticolId).ToHashSet();
        var aliasuri = (await db.Aliasuri.AsNoTracking().Where(x => idUri.Contains(x.ArticolId)).ToListAsync(ct))
            .Select(x => new AliasCandidat(x.FurnizorId, x.Cheie, x.ArticolId)).ToList();
        var candidati = materiale.Select(m => new ArticolCandidat(m.ArticolId, m.Cod, m.Denumire, m.UnitateMasura)).ToList();

        var propuneri = cerere.Linii
            .Select(l => PotrivireArticol.PropuneSauAsemanator(l.Denumire, l.UnitateMasura, aliasuri, candidati) is { } p
                ? new PropunereMaterialDto(p.Articol.Id, p.Sursa.ToString())
                : null)
            .ToList();
        return Results.Ok(new PotrivireBonDto(santier.Id, materiale, propuneri,
            materiale.Count == 0 ? "Stocul șantierului proiectului e gol: transferă materialele din depozit înainte de consum." : null));
    }

    private static CampBonDto<string> Camp(CampCitit<string> c) => new(c.Valoare, c.Certitudine.ToString(), c.TextSursa);

    private static CampBonDto<decimal?> Numar(CampCitit<decimal> c) =>
        new(c.Certitudine == Certitudine.Lipsa ? null : c.Valoare, c.Certitudine.ToString(), c.TextSursa);

    private static CampBonDto<DateOnly?> Data(CampCitit<DateOnly> c) =>
        new(c.Certitudine == Certitudine.Lipsa ? null : c.Valoare, c.Certitudine.ToString(), c.TextSursa);
}
