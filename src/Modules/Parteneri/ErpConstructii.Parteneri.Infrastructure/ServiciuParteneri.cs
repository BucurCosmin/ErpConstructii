using ErpConstructii.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ErpConstructii.Parteneri.Infrastructure;

internal sealed class ServiciuParteneri(ParteneriDbContext db) : IServiciuParteneri
{
    public async Task<PartenerRezumat?> CautaDupaCuiAsync(string cui, CancellationToken ct = default)
    {
        var normalizat = ValidareFiscala.NormalizeazaCui(cui);
        var p = await db.Parteneri.AsNoTracking().FirstOrDefaultAsync(x => x.Cui == normalizat, ct);
        return p is null ? null : Rezumat(p);
    }

    public async Task<PartenerRezumat?> CautaFurnizorAsync(string? cui, string? denumire, CancellationToken ct = default)
    {
        var p = await GasesteAsync(cui, denumire, urmarit: false, ct);
        return p is null ? null : Rezumat(p);
    }

    public Task<Rezultat<PartenerRezumat>> AsiguraFurnizorAsync(string denumire, string? cui, bool platitorTva, CancellationToken ct = default) =>
        AsiguraFurnizorAsync(denumire, cui, platitorTva, null, null, ct);

    public async Task<Rezultat<PartenerRezumat>> AsiguraFurnizorAsync(
        string denumire, string? cui, bool platitorTva, string? adresa, string? telefon, CancellationToken ct = default)
    {
        var normalizat = string.IsNullOrWhiteSpace(cui) ? null : ValidareFiscala.NormalizeazaCui(cui);
        var existent = await GasesteAsync(normalizat, denumire, urmarit: true, ct);
        if (existent is not null)
        {
            // Găsit după denumire (ex. adăugat din nota de comandă, care nu are CUI): primește CUI-ul de pe factură.
            var completeazaCui = existent.Cui is null && normalizat is not null;
            if (!existent.EsteFurnizor || completeazaCui)
            {
                var r = existent.Modifica(new DatePartener(
                    existent.Denumire, existent.Cui ?? normalizat, existent.NrRegCom, completeazaCui ? platitorTva : existent.PlatitorTva, existent.Adresa, existent.Telefon,
                    existent.Email, existent.PersoanaContact, existent.TelefonContact, existent.Observatii, existent.ConturiBancare.ToList(),
                    EsteFurnizor: true, existent.EsteClient, existent.EsteSubantreprenor));
                if (!r.Succes)
                {
                    return Rezultat.Esuat<PartenerRezumat>(r.Eroare);
                }

                await db.SaveChangesAsync(ct);
            }

            return Rezultat.Ok(Rezumat(existent));
        }

        var nou = Partener.Creeaza(new DatePartener(
            denumire, normalizat, null, platitorTva,
            string.IsNullOrWhiteSpace(adresa) ? Adresa.Goala : new Adresa(adresa.Trim(), string.Empty, null, null),
            string.IsNullOrWhiteSpace(telefon) ? null : telefon.Trim(), null, null, null,
            "Adăugat automat la importul unui document (factură / comandă).", [], EsteFurnizor: true, EsteClient: false, EsteSubantreprenor: false));
        if (!nou.Succes)
        {
            return Rezultat.Esuat<PartenerRezumat>(nou.Eroare);
        }

        db.Parteneri.Add(nou.Valoare);
        await db.SaveChangesAsync(ct);
        return Rezultat.Ok(Rezumat(nou.Valoare));
    }

    /// <summary>După CUI; fără CUI, după cheia denumirii (nomenclatorul unei firme are sute de parteneri: comparăm în memorie).</summary>
    private async Task<Partener?> GasesteAsync(string? cui, string? denumire, bool urmarit, CancellationToken ct)
    {
        var q = urmarit ? db.Parteneri : db.Parteneri.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(cui))
        {
            var normalizat = ValidareFiscala.NormalizeazaCui(cui);
            if (await q.FirstOrDefaultAsync(x => x.Cui == normalizat, ct) is { } dupaCui)
            {
                return dupaCui;
            }
        }

        if (string.IsNullOrWhiteSpace(denumire) || DenumireFirma.Cheie(denumire) is not { Length: >= 2 } cheie)
        {
            return null;
        }

        // După denumire, doar printre partenerii fără CUI sau cu același CUI: două firme cu același nume și CUI-uri diferite rămân separate.
        var normalizatCui = string.IsNullOrWhiteSpace(cui) ? null : ValidareFiscala.NormalizeazaCui(cui);
        var toti = await q.Select(x => new { x.Id, x.Denumire, x.Cui }).ToListAsync(ct);
        var gasit = toti.FirstOrDefault(x => DenumireFirma.Cheie(x.Denumire) == cheie && (normalizatCui is null || x.Cui is null || x.Cui == normalizatCui));
        return gasit is null ? null : await q.FirstAsync(x => x.Id == gasit.Id, ct);
    }

    private static PartenerRezumat Rezumat(Partener p) => new(p.Id, p.Denumire, p.Cui, p.PlatitorTva, p.EsteFurnizor, p.Activ);
}
