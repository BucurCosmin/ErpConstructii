using System.Collections.Concurrent;
using ErpConstructii.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ErpConstructii.Infrastructure;

/// <summary>
/// Cursurile BNR din fișierele anuale publicate (<c>https://curs.bnr.ro/files/xml/years/nbrfxratesAAAA.xml</c>, configurabil cu
/// <c>CursBnr:Adresa</c>, cu <c>{an}</c> în locul anului). Un fișier pe an, ținut în memorie: anii trecuți o dată, anul curent
/// se reîncarcă după 3 ore. Pentru o dată de la începutul anului (înainte de primul curs al anului) se caută în anul anterior.
/// BNR indisponibil: null (cursul se introduce de mână acolo unde e nevoie).
/// </summary>
public sealed class CursBnr(HttpClient http, IConfiguration configurare, ICeas ceas, ILogger<CursBnr> jurnal) : ICursValutar
{
    private const string AdresaImplicita = "https://curs.bnr.ro/files/xml/years/nbrfxrates{an}.xml";
    private static readonly TimeSpan ReincarcareAnCurent = TimeSpan.FromHours(3);
    private static readonly ConcurrentDictionary<int, (CursuriBnr Cursuri, DateTimeOffset Citit)> Ani = new();
    private static readonly SemaphoreSlim Blocare = new(1, 1);

    public async Task<CursValutar?> CursAsync(string moneda, DateOnly data, CancellationToken ct = default)
    {
        if (string.Equals(moneda, "RON", StringComparison.OrdinalIgnoreCase))
            return new CursValutar("RON", data, 1m);
        var azi = DateOnly.FromDateTime(ceas.Acum.UtcDateTime);
        if (data > azi)
            data = azi;
        return (await AnAsync(data.Year, ct))?.Cauta(moneda, data)
            ?? (await AnAsync(data.Year - 1, ct))?.Cauta(moneda, data);
    }

    private async Task<CursuriBnr?> AnAsync(int an, CancellationToken ct)
    {
        var anCurent = ceas.Acum.Year;
        if (an > anCurent || an < 2005)
            return null;
        if (Ani.TryGetValue(an, out var x) && (an < anCurent || ceas.Acum - x.Citit < ReincarcareAnCurent))
            return x.Cursuri;

        await Blocare.WaitAsync(ct);
        try
        {
            if (Ani.TryGetValue(an, out x) && (an < anCurent || ceas.Acum - x.Citit < ReincarcareAnCurent))
                return x.Cursuri;
            var adresa = (configurare["CursBnr:Adresa"] ?? AdresaImplicita).Replace("{an}", an.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var xml = await http.GetStringAsync(adresa, cts.Token);
            var cursuri = CursuriBnr.Citeste(xml);
            Ani[an] = (cursuri, ceas.Acum);
            return cursuri;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            jurnal.LogWarning(e, "Cursurile BNR pe {An} nu au putut fi citite.", an);
            // Rămâne ce s-a citit anterior (dacă există), chiar dacă e mai vechi.
            return Ani.TryGetValue(an, out x) ? x.Cursuri : null;
        }
        finally
        {
            Blocare.Release();
        }
    }
}
