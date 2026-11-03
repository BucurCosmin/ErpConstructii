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

    public async Task<Rezultat<PartenerRezumat>> AsiguraFurnizorAsync(string denumire, string? cui, bool platitorTva, CancellationToken ct = default)
    {
        var normalizat = string.IsNullOrWhiteSpace(cui) ? null : ValidareFiscala.NormalizeazaCui(cui);
        var existent = normalizat is null ? null : await db.Parteneri.FirstOrDefaultAsync(x => x.Cui == normalizat, ct);
        if (existent is not null)
        {
            if (!existent.EsteFurnizor)
            {
                var r = existent.Modifica(new DatePartener(
                    existent.Denumire, existent.Cui, existent.NrRegCom, existent.PlatitorTva, existent.Adresa, existent.Telefon,
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
            denumire, normalizat, null, platitorTva, Adresa.Goala, null, null, null, null,
            "Adăugat automat la importul unei facturi primite.", [], EsteFurnizor: true, EsteClient: false, EsteSubantreprenor: false));
        if (!nou.Succes)
        {
            return Rezultat.Esuat<PartenerRezumat>(nou.Eroare);
        }

        db.Parteneri.Add(nou.Valoare);
        await db.SaveChangesAsync(ct);
        return Rezultat.Ok(Rezumat(nou.Valoare));
    }

    private static PartenerRezumat Rezumat(Partener p) => new(p.Id, p.Denumire, p.Cui, p.PlatitorTva, p.EsteFurnizor, p.Activ);
}
