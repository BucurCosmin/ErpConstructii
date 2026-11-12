using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ErpConstructii.Devize.Analize;

public enum SeveritateAnaliza
{
    Eroare,
    Avertisment,
    Info,
    Risc,
}

public sealed record DovadaAnaliza(string Nume, string Valoare);

public sealed record CorecturaCantitate(Guid Articol, decimal CantitateVeche, decimal CantitateNoua);

public sealed record ConstatareAnaliza(
    string Cod,
    string Analizor,
    string VersiuneAnalizor,
    SeveritateAnaliza Severitate,
    decimal Incredere,
    IReadOnlyList<Guid> Articole,
    string Explicatie,
    IReadOnlyList<DovadaAnaliza> Dovezi,
    CorecturaCantitate? Corectura = null,
    decimal? ImpactCostDirect = null,
    decimal? ImpactOferta = null)
{
    /// <summary>Amprenta depinde de regulă, ținte și dovezi, nu de versiunea analizorului sau de textul explicației.</summary>
    public string Amprenta()
    {
        var text = new StringBuilder();
        void Adauga(string valoare) => text.Append(valoare.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(valoare);
        Adauga(Cod);
        foreach (var articol in Articole.Order())
            Adauga(articol.ToString("D"));
        foreach (var dovada in Dovezi.OrderBy(d => d.Nume, StringComparer.Ordinal).ThenBy(d => d.Valoare, StringComparer.Ordinal))
        {
            Adauga(dovada.Nume);
            Adauga(dovada.Valoare);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

public sealed record ContextAnaliza(VersiuneDeviz Deviz, ConfiguratieAnaliza Configuratie)
{
    public void Valideaza()
    {
        ArgumentNullException.ThrowIfNull(Deviz);
        ArgumentNullException.ThrowIfNull(Configuratie);
        if (Configuratie.CantitateMinima < 0 || Configuratie.TolerantaFactor is < 0 or >= 1
            || Configuratie.Densitati is { } densitati && densitati.Any(d => d <= 0))
            throw new ArgumentOutOfRangeException(nameof(Configuratie), "Pragurile analizei sunt invalide.");
    }
}

public sealed record ConfiguratieAnaliza(
    decimal CantitateMinima = 1m,
    decimal TolerantaFactor = 0.001m,
    IReadOnlyList<decimal>? Densitati = null);

public sealed record RezultatAnalizor(
    string Cod,
    string Versiune,
    IReadOnlyList<ConstatareAnaliza> Constatari,
    IReadOnlyList<string> Limitari);

public interface IAnalizorDeviz
{
    string Cod { get; }
    string Versiune { get; }
    RezultatAnalizor Analizeaza(ContextAnaliza context);
}

public sealed class RegistruAnalizori(IEnumerable<IAnalizorDeviz> analizori)
{
    private readonly IReadOnlyList<IAnalizorDeviz> _analizori = Verifica(analizori);

    public IReadOnlyList<RezultatAnalizor> Analizeaza(ContextAnaliza context, IReadOnlySet<string> activi)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(activi);
        context.Valideaza();

        var necunoscuti = activi.Except(_analizori.Select(a => a.Cod), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (necunoscuti.Length > 0)
            throw new ArgumentException($"Analizori necunoscuți: {string.Join(", ", necunoscuti)}.", nameof(activi));
        return _analizori.Where(a => activi.Contains(a.Cod)).Select(a => a.Analizeaza(context)).ToArray();
    }

    private static IReadOnlyList<IAnalizorDeviz> Verifica(IEnumerable<IAnalizorDeviz> analizori)
    {
        var lista = analizori.OrderBy(a => a.Cod, StringComparer.Ordinal).ToArray();
        if (lista.GroupBy(a => a.Cod, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("Codurile analizorilor trebuie să fie unice.", nameof(analizori));
        return lista;
    }
}
