using System.Text.Json;
using ErpConstructii.Contracte.Import;
using ErpConstructii.Identitate;
using ErpConstructii.Infrastructure.Autorizare;
using ErpConstructii.Infrastructure.Import;
using ErpConstructii.Parteneri;
using ErpConstructii.Proiecte;
using ErpConstructii.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Contracte.Infrastructure;

/// <summary>Ce a verificat / corectat utilizatorul în ecranul de import (datele de bază; restul fișei se preia cum a fost citit).</summary>
/// <param name="PartenerId">Subantreprenorul din nomenclator; fără el, se caută / adaugă după denumire și CUI.</param>
/// <param name="Membri">Asocierea corectată (cote, firma activă); null = cea citită.</param>
public sealed record ImportFisaCerere(
    string? NumarExtern, DateOnly DataSemnarii, Guid? PartenerId, string? PartenerDenumire, string? PartenerCui, string? Tip, string? Obiect, string? Moneda,
    decimal Valoare, IReadOnlyList<MembruAsociere>? Membri);

/// <param name="Avertismente">Rândurile fișei care nu s-au putut prelua (cu motivul): restul contractului e creat.</param>
public sealed record RezultatImportFisa(Guid Id, string Numar, IReadOnlyList<string> Avertismente);

/// <summary>
/// Importul fișei de urmărire a contractului din Excel (D-56, etapa 3): analiza (previzualizarea, fără salvare) și confirmarea,
/// care creează contractul ca ciornă (editabil), cu condițiile, actele adiționale, registrele, facturile și plățile din fișă și
/// fișierul original atașat. Interpretarea e pură (<see cref="InterpretorFisaContract"/>); fișierul se recitește la confirmare.
/// </summary>
internal static class EndpointuriImportFisa
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Mapeaza(RouteGroupBuilder g)
    {
        var creare = Permisiuni.Pentru(ModulErp.Contracte, Actiune.Creare);
        g.MapPost("/import/analiza", Analiza).WithName("AnalizaFisaContract")
            .WithSummary("Citește fișa de urmărire (.xlsx) și întoarce ce s-a găsit, fără să salveze")
            .Produces<FisaCitita>().Produces(StatusCodes.Status400BadRequest).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentContract.DimensiuneMaxima + 100_000)).CerePermisiune(creare);
        g.MapPost("/proiecte/{proiectId:guid}/import", Confirma).WithName("ImportFisaContract")
            .WithSummary("Creează contractul (ciornă) din fișa Excel: fișierul + câmpul „date” (JSON, datele verificate)")
            .Produces<RezultatImportFisa>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentContract.DimensiuneMaxima + 100_000)).CerePermisiune(creare);
    }

    private static async Task<(FisaCitita? Fisa, byte[] Continut, string? Eroare)> CitesteAsync(IFormFile? fisier, IInterogareFirma firme, CancellationToken ct)
    {
        if (fisier is null || fisier.Length == 0)
            return (null, [], "Alege fișa contractului (.xlsx).");
        if (fisier.Length > DocumentContract.DimensiuneMaxima)
            return (null, [], "Fișierul depășește 25 MB.");
        using var ms = new MemoryStream();
        await fisier.CopyToAsync(ms, ct);
        var continut = ms.ToArray();
        var foi = CititorFoiXlsx.Citeste(new MemoryStream(continut));
        if (foi is null)
            return (null, continut, "Fișierul nu e un .xlsx valid (fișele vechi .xls se salvează întâi ca .xlsx din Excel).");
        var firma = await firme.FirmaActivaAsync(ct);
        return (InterpretorFisaContract.Citeste(foi, firma?.Denumire, firma?.Cui), continut, null);
    }

    private static async Task<IResult> Analiza(IFormFile fisier, IInterogareFirma firme, CancellationToken ct)
    {
        var (fisa, _, eroare) = await CitesteAsync(fisier, firme, ct);
        return fisa is null ? Results.BadRequest(new { mesaj = eroare }) : Results.Ok(fisa);
    }

    private static async Task<IResult> Confirma(
        Guid proiectId, IFormFile fisier, [FromForm] string date, ContracteDbContext db, IInterogareProiecte proiecte, IServiciuParteneri parteneri,
        IInterogareFirma firme, IFirmaCurenta firma, IUtilizatorCurent utilizator, CancellationToken ct)
    {
        if (firma.FirmaId is not { } fid || utilizator.UtilizatorId is not { } uid || string.IsNullOrWhiteSpace(utilizator.Nume))
            return Results.Unauthorized();
        ImportFisaCerere? cerere;
        try
        {
            cerere = JsonSerializer.Deserialize<ImportFisaCerere>(date, Json);
        }
        catch (JsonException)
        {
            cerere = null;
        }

        if (cerere is null)
            return Results.BadRequest(new { mesaj = "Datele verificate lipsesc." });
        var proiect = await proiecte.CautaPentruDocumentAsync(proiectId, ct);
        if (proiect is null)
            return Results.NotFound(new { mesaj = "Proiectul nu există." });
        if (proiect.Arhivat)
            return Results.Conflict(new { mesaj = proiect.MesajArhivat });
        var (fisa, continut, eroareFisier) = await CitesteAsync(fisier, firme, ct);
        if (fisa is null)
            return Results.BadRequest(new { mesaj = eroareFisier });

        // Datele de bază: cele verificate în ecran; restul din fișă.
        var observatii = fisa.Observatii is { Length: > 2000 } o ? o[..1997] + "…" : fisa.Observatii;
        var (dateContract, eroare) = await ModulContracte.DateAsync(new ContractCerere(
            cerere.NumarExtern, cerere.DataSemnarii, cerere.PartenerId, cerere.PartenerDenumire, cerere.PartenerCui, cerere.Tip ?? nameof(TipContract.Subantreprenoriat),
            cerere.Obiect ?? string.Empty, cerere.Moneda, cerere.Valoare, [], fisa.DataInceput, fisa.DataSfarsit, fisa.TermenPlataZile, fisa.ProcentGarantie, 0, true,
            observatii), proiect.Id, proiect.Cod, parteneri, ct);
        if (eroare is not null)
            return Results.BadRequest(new { mesaj = eroare });

        var avertismente = new List<string>(fisa.Note);
        await using var tranzactie = await db.Database.BeginTransactionAsync(ct);
        await ModulContracte.BlocheazaAsync(db, tranzactie, fid, ct);
        var an = dateContract!.DataSemnarii.Year;
        var numar = (await db.Contracte.Where(c => c.An == an).MaxAsync(c => (int?)c.Numar, ct) ?? 0) + 1;
        var r = ContractPartener.Creeaza(an, numar, dateContract, uid, utilizator.Nume);
        if (r.Esec)
            return Results.BadRequest(new { mesaj = r.Eroare.Mesaj });
        var contract = r.Valoare;
        var conditii = fisa.Conditii with { Membri = cerere.Membri ?? fisa.Conditii.Membri };
        if (contract.SeteazaConditii(conditii) is { Esec: true } rc)
            return Results.BadRequest(new { mesaj = rc.Eroare.Mesaj });
        db.Contracte.Add(contract);

        // Actele adiționale (contractul e ciornă, dar actele există deja în fișă).
        var valoareActuala = contract.Valoare;
        foreach (var (a, i) in fisa.Acte.Select((a, i) => (a, i)))
        {
            var dataAct = a.Data is { } d && d >= contract.DataSemnarii ? d : contract.DataSemnarii;
            var obiect = a.Obiect.Length > 1000 ? a.Obiect[..1000] : a.Obiect;
            var act = ActAditional.Creeaza(contract, null, i + 1, a.NumarExtern, dataAct, obiect, a.Diferenta, null, valoareActuala, utilizator.Nume, a.Semnat, laImport: true);
            if (act.Esec) { avertismente.Add($"Actul adițional „{a.Obiect}”: {act.Eroare.Mesaj}"); continue; }
            db.ActeAditionale.Add(act.Valoare);
            if (a.Semnat) valoareActuala += a.Diferenta;
        }

        // Registrele, în ordinea din fișă; un rând care nu trece validarea e sărit, cu avertisment.
        void Adauga<T>(IEnumerable<T> randuri, Func<int, T, Rezultat<RandFisaContract>> creeaza, string ce)
        {
            var ordine = 0;
            foreach (var x in randuri)
            {
                var rr = creeaza(++ordine, x);
                if (rr.Succes) db.Add(rr.Valoare);
                else avertismente.Add($"{ce} {ordine}: {rr.Eroare.Mesaj}");
            }
        }

        static Rezultat<RandFisaContract> Ca<T>(Rezultat<T> x) where T : RandFisaContract =>
            x.Succes ? Rezultat.Ok<RandFisaContract>(x.Valoare) : Rezultat.Esuat<RandFisaContract>(x.Eroare);

        Adauga(fisa.Obligatii, (o, x) => Ca(ObligatieContract.Creeaza(contract.Id, o, x)), "Obligația");
        Adauga(fisa.Actiuni, (o, x) => Ca(ActiuneContract.Creeaza(contract.Id, o, x)), "Acțiunea");
        Adauga(fisa.Faze, (o, x) => Ca(FazaExecutie.Creeaza(contract.Id, o, x)), "Faza");
        Adauga(fisa.Riscuri, (o, x) => Ca(RiscContract.Creeaza(contract.Id, o, x)), "Aspectul");
        Adauga(fisa.Instrumente, (o, x) => Ca(InstrumentGarantie.Creeaza(contract.Id, o, x)), "Instrumentul");
        Adauga(fisa.Surse, (o, x) => Ca(SursaContract.Creeaza(contract.Id, o, x)), "Sursa");

        // Facturile (introduse în fișă; legătura cu facturile din modulul Facturi se face din fișă), apoi plățile lor.
        var dupaNumar = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var ordineFactura = 0;
        foreach (var f in fisa.Facturi)
        {
            var rf = FacturaContract.Creeaza(contract.Id, ++ordineFactura, f);
            if (rf.Esec) { avertismente.Add($"Factura {f.Numar}: {rf.Eroare.Mesaj}"); continue; }
            db.FacturiFisa.Add(rf.Valoare);
            dupaNumar.TryAdd(f.Numar, rf.Valoare.Id);
        }

        var ordinePlata = 0;
        foreach (var p in fisa.Plati)
        {
            var rp = PlataContract.Creeaza(contract.Id, ++ordinePlata, p.Plata with { FacturaContractId = dupaNumar.GetValueOrDefault(p.NumarFactura) is var id && id != Guid.Empty ? id : null });
            if (rp.Esec) { avertismente.Add($"Plata facturii {p.NumarFactura}: {rp.Eroare.Mesaj}"); continue; }
            db.Plati.Add(rp.Valoare);
        }

        var nume = Path.GetFileName(fisier.FileName);
        db.Documente.Add(new DocumentContract(contract.Id, null, nume.Length > 260 ? nume[^260..] : nume,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", continut, utilizator.Nume));
        await db.SaveChangesAsync(ct);
        await tranzactie.CommitAsync(ct);
        return Results.Created($"/api/contracte/proiecte/{proiectId}/{contract.Id}", new RezultatImportFisa(contract.Id, contract.NumarAfisat, avertismente));
    }
}
