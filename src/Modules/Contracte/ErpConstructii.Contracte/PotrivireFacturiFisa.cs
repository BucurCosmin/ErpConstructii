using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte;

/// <summary>
/// Un rând din fișă (introdus / importat din Excel, nelegat) și factura din modulul Facturi care e aceeași.
/// <paramref name="Sigura"/>: numărul și suma se potrivesc (sau rândul nu are sume) — se poate lega automat.
/// </summary>
public sealed record PotrivireFactura(Guid RandId, Guid FacturaId, string FacturaNumar, bool Sigura, string? Diferente);

/// <summary>
/// Potrivirea facturilor din fișa contractului cu facturile din modulul Facturi (D-56): ca aceeași factură să nu apară de
/// două ori (rândul preluat din fișa Excel și factura importată ulterior în Facturi). Se potrivesc doar rândurile nelegate
/// ale membrului care e firma activă (facturile celuilalt membru nu sunt în modulul Facturi), după numărul facturii
/// (fără spații / semne; și doar cifrele, la numerele lungi), cu verificarea datei și a sumei fără TVA. Logică pură.
/// </summary>
public static class PotrivireFacturiFisa
{
    /// <summary>Diferența de sumă tolerată (rotunjiri de pe factură / din fișă).</summary>
    public const decimal Toleranta = 1m;

    private static readonly System.Globalization.CultureInfo Ro = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

    public static IReadOnlyList<PotrivireFactura> Gaseste(IReadOnlyList<FacturaContract> randuri, IReadOnlyList<FacturaPeContract> facturi, string? membruFirma)
    {
        var legate = randuri.Where(r => r.FacturaId is not null).Select(r => r.FacturaId!.Value).ToHashSet();
        var candidati = randuri.Where(r => r.FacturaId is null && (membruFirma is null || Membru(r.Membru) == Membru(membruFirma))).ToList();
        var libere = facturi.Where(f => !legate.Contains(f.Id)).ToList();
        var rezultat = new List<PotrivireFactura>();
        foreach (var f in libere)
        {
            var potrivite = candidati.Where(r => AcelasiNumar(r.Numar, f.Numar)).ToList();
            // Mai multe rânduri cu același număr: nu se ghicește.
            if (potrivite.Count != 1 || rezultat.Any(x => x.RandId == potrivite[0].Id))
                continue;
            var r = potrivite[0];
            var diferente = new List<string>();
            if (r.Data != f.Data)
                diferente.Add($"data: fișă {r.Data.ToString("dd.MM.yyyy", Ro)}, factură {f.Data.ToString("dd.MM.yyyy", Ro)}");
            var inLei = string.Equals(f.Moneda, "RON", StringComparison.OrdinalIgnoreCase);
            var sumaDiferita = inLei && r.NetRon != 0 && Math.Abs(r.NetRon - f.TotalFaraTva) > Toleranta;
            if (sumaDiferita)
                diferente.Add($"fără TVA: fișă {r.NetRon.ToString("N2", Ro)}, factură {f.TotalFaraTva.ToString("N2", Ro)} lei");
            rezultat.Add(new PotrivireFactura(r.Id, f.Id, f.Numar, !sumaDiferita, diferente.Count == 0 ? null : string.Join("; ", diferente)));
        }

        return rezultat;
    }

    /// <summary>Același număr de factură: fără spații și semne; la numerele lungi (≥ 4 cifre), aceleași cifre (ex. „HTC 20260145” = „20260145”).</summary>
    public static bool AcelasiNumar(string a, string b)
    {
        static string Alfanumeric(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        static string Cifre(string s) => new(s.Where(char.IsAsciiDigit).ToArray());
        if (Alfanumeric(a) is { Length: > 0 } x && x == Alfanumeric(b))
            return true;
        var ca = Cifre(a);
        return ca.Length >= 4 && ca == Cifre(b);
    }

    private static string Membru(string s) => ValoriFoaie.Cheie(s);
}

/// <summary>
/// Implementat de Contracte, apelat de Facturi după ce o factură a fost legată de un contract (import / corectură): rândul
/// din fișă cu același număr (al firmei, încă nelegat) primește legătura, dacă potrivirea e sigură. Fără modulul
/// Contracte: nimic de făcut.
/// </summary>
public interface ILegareFacturiContract
{
    /// <returns>True dacă un rând din fișă a fost legat de factură.</returns>
    Task<bool> LeagaAsync(Guid contractId, Guid facturaId, CancellationToken ct = default);
}
