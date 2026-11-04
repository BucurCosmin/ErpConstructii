using System.Text.Json;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

public sealed record LinieStornoNir(Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal Cantitate, decimal Valoare);

/// <summary>
/// Stornarea unui NIR (regula 6: NIR-ul rămâne neschimbat, stornarea e un document separat): materialele recepționate ies
/// din depozit la valoarea cu care au intrat. Se face când factura a fost anulată sau recepția a fost greșită; după
/// stornare, o factură neanulată poate fi recepționată din nou.
/// </summary>
public sealed class StornoNir : EntitateFirma, IDocumentCuStare
{
    private IReadOnlyList<LinieStornoNir>? _linii;

    private StornoNir() { }

    public Guid NirId { get; private set; }
    public string NirNumar { get; private set; } = string.Empty;
    public Guid FacturaId { get; private set; }
    public DateOnly Data { get; private set; }
    public string Motiv { get; private set; } = string.Empty;
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieStornoNir> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieStornoNir>>(LiniiJson) ?? [];
    public decimal Valoare { get; private set; }
    public Guid StornatDeId { get; private set; }
    public string StornatDeNume { get; private set; } = string.Empty;
    public bool EsteBlocat => true;

    public static Rezultat<StornoNir> Emite(Nir nir, DateOnly data, string motiv, Guid utilizatorId, string utilizatorNume)
    {
        if (string.IsNullOrWhiteSpace(motiv) || motiv.Trim().Length > 1000)
            return Rezultat.Esuat<StornoNir>(Eroare.Validare("Motivul stornării este obligatoriu (cel mult 1000 de caractere)."));
        if (data < nir.Data)
            return Rezultat.Esuat<StornoNir>(Eroare.Validare("Stornarea nu poate avea data înaintea NIR-ului."));
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            return Rezultat.Esuat<StornoNir>(Eroare.Validare("Utilizatorul este obligatoriu."));

        var linii = nir.Linii.Where(l => l.CantitateReceptionata > 0)
            .Select(l => new LinieStornoNir(l.ArticolId, l.ArticolCod, l.Denumire, l.UnitateMasura, l.CantitateReceptionata, l.Valoare)).ToList();
        return Rezultat.Ok(new StornoNir
        {
            NirId = nir.Id,
            NirNumar = nir.NumarAfisat,
            FacturaId = nir.FacturaId,
            Data = data,
            Motiv = motiv.Trim(),
            LiniiJson = JsonSerializer.Serialize(linii),
            Valoare = linii.Sum(l => l.Valoare),
            StornatDeId = utilizatorId,
            StornatDeNume = utilizatorNume.Trim(),
        });
    }
}

public enum StareInventar
{
    /// <summary>Numărătoarea e în curs; gestiunea e blocată pentru alte mișcări.</summary>
    Deschis,

    /// <summary>Diferențele au fost înregistrate în stoc; documentul e imuabil.</summary>
    Finalizat,
}

/// <param name="CantitateScriptica">Stocul din evidență la deschiderea inventarului.</param>
/// <param name="CantitateFaptica">Ce a numărat comisia; null = nenumărat încă.</param>
/// <param name="PretUnitarPlus">Prețul pentru plusuri când materialul nu mai are CMP (stoc scriptic zero).</param>
/// <param name="ValoareScriptica">Valoarea din evidență la deschidere (pentru CMP-ul plusurilor).</param>
/// <param name="ValoareDiferenta">Valoarea plusului (+) sau a minusului (−), calculată la finalizare.</param>
public sealed record LinieInventar(
    Guid ArticolId, string ArticolCod, string Denumire, string UnitateMasura, decimal CantitateScriptica, decimal ValoareScriptica,
    decimal? CantitateFaptica, decimal? PretUnitarPlus, decimal? ValoareDiferenta)
{
    public decimal? Diferenta => CantitateFaptica - CantitateScriptica;
}

public sealed record NumarareLinie(Guid ArticolId, decimal? CantitateFaptica, decimal? PretUnitarPlus);

/// <summary>
/// Inventarul unei gestiuni (depozit sau șantier): stocul scriptic se preia la deschidere, comisia introduce cantitățile
/// numărate, iar la finalizare plusurile intră la CMP (sau la prețul dat, dacă materialul nu are CMP) și minusurile ies
/// la CMP. Finalizat, devine imuabil (regula 6). Materialele găsite fără să fie în evidență se adaugă ca linii noi.
/// </summary>
public sealed class Inventar : EntitateFirma, IDocumentCuStare
{
    private List<LinieInventar>? _linii;

    private Inventar() { }

    public int An { get; private set; }
    public int Numar { get; private set; }
    public string NumarAfisat => $"INV/{An}/{Numar:D4}";
    public DateOnly Data { get; private set; }
    public Guid GestiuneId { get; private set; }
    public string GestiuneDenumire { get; private set; } = string.Empty;
    public StareInventar Stare { get; private set; }
    public string LiniiJson { get; private set; } = "[]";
    public IReadOnlyList<LinieInventar> Linii => _linii ??= JsonSerializer.Deserialize<List<LinieInventar>>(LiniiJson) ?? [];
    public string? Comisie { get; private set; }
    public Guid DeschisDeId { get; private set; }
    public string DeschisDeNume { get; private set; } = string.Empty;
    public Guid? FinalizatDeId { get; private set; }
    public string? FinalizatDeNume { get; private set; }
    public bool EsteBlocat => Stare == StareInventar.Finalizat;

    public static Rezultat<Inventar> Deschide(
        int an, int numar, DateOnly data, Gestiune gestiune, IReadOnlyList<(ArticolCandidat Articol, Sold Sold)> stoc, string? comisie,
        Guid utilizatorId, string utilizatorNume)
    {
        if (an < 2000 || numar < 1)
            return Rezultat.Esuat<Inventar>(Eroare.Validare("Numărul inventarului nu este valid."));
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            return Rezultat.Esuat<Inventar>(Eroare.Validare("Utilizatorul este obligatoriu."));
        var linii = stoc.OrderBy(x => x.Articol.Denumire)
            .Select(x => new LinieInventar(x.Articol.Id, x.Articol.Cod, x.Articol.Denumire, x.Articol.UnitateMasura, x.Sold.Cantitate, x.Sold.Valoare, null, null, null))
            .ToList();
        return Rezultat.Ok(new Inventar
        {
            An = an,
            Numar = numar,
            Data = data,
            GestiuneId = gestiune.Id,
            GestiuneDenumire = gestiune.Denumire,
            Stare = StareInventar.Deschis,
            LiniiJson = JsonSerializer.Serialize(linii),
            Comisie = string.IsNullOrWhiteSpace(comisie) ? null : comisie.Trim(),
            DeschisDeId = utilizatorId,
            DeschisDeNume = utilizatorNume.Trim(),
        });
    }

    /// <summary>Cantitățile numărate (se pot introduce în mai multe tranșe). Materialele noi intră cu stoc scriptic zero.</summary>
    public Rezultat Numara(IReadOnlyList<NumarareLinie> numarari, IReadOnlyDictionary<Guid, ArticolCandidat> articoleNoi)
    {
        if (Stare != StareInventar.Deschis)
            return Rezultat.Esuat(Eroare.Validare("Inventarul este finalizat; nu se mai poate modifica."));
        if (numarari.Any(n => n.CantitateFaptica < 0 || n.PretUnitarPlus < 0))
            return Rezultat.Esuat(Eroare.Validare("Cantitățile și prețurile nu pot fi negative."));

        var linii = Linii.ToList();
        foreach (var n in numarari)
        {
            var i = linii.FindIndex(l => l.ArticolId == n.ArticolId);
            if (i >= 0)
            {
                linii[i] = linii[i] with { CantitateFaptica = n.CantitateFaptica, PretUnitarPlus = n.PretUnitarPlus };
            }
            else if (articoleNoi.TryGetValue(n.ArticolId, out var a))
            {
                linii.Add(new LinieInventar(a.Id, a.Cod, a.Denumire, a.UnitateMasura, 0, 0, n.CantitateFaptica, n.PretUnitarPlus, null));
            }
            else
            {
                return Rezultat.Esuat(Eroare.Validare("Un material numărat nu există în nomenclator."));
            }
        }

        Seteaza(linii);
        return Rezultat.Ok();
    }

    /// <summary>
    /// Calculează diferențele față de stocul actual al gestiunii (blocată cât timp inventarul e deschis, deci egal cu cel
    /// scriptic) și închide inventarul. Întoarce soldurile noi pe articol, pentru înregistrarea mișcărilor.
    /// </summary>
    public Rezultat<IReadOnlyList<(LinieInventar Linie, Sold SoldNou)>> Finalizeaza(
        IReadOnlyDictionary<Guid, Sold> solduriActuale, Guid utilizatorId, string utilizatorNume)
    {
        if (Stare != StareInventar.Deschis)
            return Rezultat.Esuat<IReadOnlyList<(LinieInventar, Sold)>>(Eroare.Validare("Inventarul este deja finalizat."));
        var nenumarate = Linii.Where(l => l.CantitateFaptica is null).Select(l => l.Denumire).ToList();
        if (nenumarate.Count > 0)
            return Rezultat.Esuat<IReadOnlyList<(LinieInventar, Sold)>>(Eroare.Validare(
                $"Mai sunt {nenumarate.Count} materiale nenumărate (ex. „{nenumarate[0]}”); introdu 0 pentru ce lipsește."));
        if (utilizatorId == Guid.Empty || string.IsNullOrWhiteSpace(utilizatorNume))
            return Rezultat.Esuat<IReadOnlyList<(LinieInventar, Sold)>>(Eroare.Validare("Utilizatorul este obligatoriu."));

        var rezultat = new List<(LinieInventar, Sold)>();
        var linii = new List<LinieInventar>();
        foreach (var l in Linii)
        {
            var sold = solduriActuale.GetValueOrDefault(l.ArticolId, Sold.Zero);
            var diferenta = l.CantitateFaptica!.Value - sold.Cantitate;
            Sold nou;
            decimal valoare;
            if (diferenta > 0)
            {
                var pret = sold.Cantitate > 0 ? sold.Valoare / sold.Cantitate : l.PretUnitarPlus;
                if (pret is null)
                    return Rezultat.Esuat<IReadOnlyList<(LinieInventar, Sold)>>(Eroare.Validare(
                        $"„{l.Denumire}” are plus, dar nu are preț mediu în evidență: completează prețul unitar al plusului."));
                valoare = Rotunjire.Lei.Aplica(diferenta * pret.Value);
                nou = CalculStoc.Intrare(sold, diferenta, valoare).Valoare;
            }
            else if (diferenta < 0)
            {
                var iesire = CalculStoc.Iesire(sold, -diferenta, l.Denumire);
                if (iesire.Esec)
                    return Rezultat.Esuat<IReadOnlyList<(LinieInventar, Sold)>>(iesire.Eroare);
                (nou, valoare) = (iesire.Valoare.Ramas, -iesire.Valoare.ValoareIesire);
            }
            else
            {
                (nou, valoare) = (sold, 0);
            }

            var finala = l with { ValoareDiferenta = valoare };
            linii.Add(finala);
            if (diferenta != 0)
                rezultat.Add((finala, nou));
        }

        Seteaza(linii);
        Stare = StareInventar.Finalizat;
        FinalizatDeId = utilizatorId;
        FinalizatDeNume = utilizatorNume.Trim();
        return Rezultat.Ok<IReadOnlyList<(LinieInventar, Sold)>>(rezultat);
    }

    private void Seteaza(List<LinieInventar> linii)
    {
        _linii = linii;
        LiniiJson = JsonSerializer.Serialize(linii);
    }
}
