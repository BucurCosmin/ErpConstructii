using System.Globalization;
using System.Xml.Linq;

namespace ErpConstructii.SharedKernel;

/// <summary>Cursul unei monede: câți lei face o unitate, publicat de BNR pentru <paramref name="Data"/>.</summary>
public sealed record CursValutar(string Moneda, DateOnly Data, decimal LeiPeUnitate);

/// <summary>
/// Cursul BNR al unei monede la o dată: ultimul curs publicat în acea zi sau înainte (zilele fără publicare — weekend,
/// sărbători — iau cursul precedent). RON = 1. Null când cursul nu se poate afla (moneda necotată, BNR indisponibil).
/// </summary>
public interface ICursValutar
{
    Task<CursValutar?> CursAsync(string moneda, DateOnly data, CancellationToken ct = default);
}

/// <summary>
/// Cursurile de referință BNR dintr-un fișier XML (<c>nbrfxrates*.xml</c>: <c>Cube date</c> → <c>Rate currency multiplier</c>).
/// Logică pură: valoarea e împărțită la multiplicator (ex. HUF se publică la 100 de unități).
/// </summary>
public sealed class CursuriBnr
{
    private readonly SortedDictionary<DateOnly, Dictionary<string, decimal>> _zile = [];

    public IReadOnlyCollection<DateOnly> Zile => _zile.Keys;

    public static CursuriBnr Citeste(string xml)
    {
        var c = new CursuriBnr();
        var doc = XDocument.Parse(xml);
        foreach (var cube in doc.Descendants().Where(e => e.Name.LocalName == "Cube"))
        {
            if (!DateOnly.TryParseExact((string?)cube.Attribute("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                continue;
            var rate = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in cube.Elements().Where(e => e.Name.LocalName == "Rate"))
            {
                if ((string?)r.Attribute("currency") is not { Length: 3 } moneda
                    || !decimal.TryParse(r.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var valoare) || valoare <= 0)
                    continue;
                var multiplicator = decimal.TryParse((string?)r.Attribute("multiplier"), NumberStyles.Number, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : 1m;
                rate[moneda] = valoare / multiplicator;
            }

            c._zile[data] = rate;
        }

        return c;
    }

    /// <summary>Ultimul curs al monedei publicat la <paramref name="data"/> sau înainte; null dacă nu e în fișier.</summary>
    public CursValutar? Cauta(string moneda, DateOnly data)
    {
        if (string.Equals(moneda, "RON", StringComparison.OrdinalIgnoreCase))
            return new CursValutar("RON", data, 1m);
        foreach (var (zi, rate) in _zile.Reverse())
        {
            if (zi <= data && rate.TryGetValue(moneda, out var v))
                return new CursValutar(moneda.ToUpperInvariant(), zi, v);
        }

        return null;
    }

    public bool AreZiuaDupa(DateOnly data) => _zile.Count > 0 && _zile.Keys.Max() >= data;
}
