using System.Text.Json;
using ErpConstructii.Devize.Calcul;
using ErpConstructii.Import.Xlsx;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;
using Microsoft.Extensions.DependencyInjection;

namespace ErpConstructii.Devize.Import.Boq;

/// <summary>
/// Conversia BOQ-ului citit în modelul canonic (D-58): un obiect pe primul nivel (ex. „B5 — Building 5”), secțiunile ca
/// capitole, grupurile ca subcapitole, articolele cu prețul de ofertă pe componente (în moneda BOQ-ului) și costul direct gol.
/// Codul articolului rămâne simbolul (ex. „B5 C 20000 1 A”), iar cheia lui fără spații („B5C200001A”) e codul WBS — aceeași
/// cu codurile resurselor / costurilor din graficul Primavera, pentru legătura cu activitățile. Logică pură.
/// </summary>
public static class ConversieBoq
{
    public static DevizCanonic Converteste(BoqCitit b, ProfilBoq profil, string moneda, IReadOnlyDictionary<string, TipResursa>? alegere)
    {
        // Componentele: cele clare după profil; celelalte după alegerea utilizatorului (fără alegere: nealese, importul nu se confirmă).
        var componente = b.Componente.Select(c =>
        {
            var p = profil.Componente.FirstOrDefault(x => x.Cheie == c.Key);
            TipResursa? tip = alegere is not null && alegere.TryGetValue(c.Key, out var t) ? t : p?.Tip;
            return new ComponentaPret(c.Key, c.Value, tip, p?.Tip is not null);
        }).ToList();
        var tipuri = componente.ToDictionary(c => c.Cheie, c => c.Tip ?? TipResursa.Utilaj);

        var coduriFolosite = new Dictionary<string, int>(StringComparer.Ordinal);
        ArticolCanonic Articol(ArticolBoq a, SectiuneBoq s)
        {
            var cheie = a.Cod is not null ? CititorBoq.CheieCod(a.Cod) : $"{b.Obiect}.{s.Cod}.{a.Numar ?? a.Rand.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var n = coduriFolosite[cheie] = coduriFolosite.GetValueOrDefault(cheie) + 1;
            if (n > 1)
                cheie += $"~{n}";
            var simbol = a.Cod ?? $"{s.Cod}-{a.Numar ?? a.Rand.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (n > 1)
                simbol += $"~{n}";
            return new ArticolCanonic(
                Taie(simbol, 40)!, Taie(a.Denumire, 1000)!, Taie(a.UnitateMasura, 20)!, a.Cantitate, ValoriResurse.Zero, [],
                Rand: $"{s.Foaie}!{a.Rand}", CodWbs: Taie(cheie, 100),
                PretOfertaUnitar: Suma(a.Componente, tipuri, v => v.PretUnitar),
                ValoareOferta: Suma(a.Componente, tipuri, v => v.Valoare),
                Specificatie: a.Specificatie, Observatii: a.Observatii);
        }

        var sectiuni = b.Sectiuni.Select(s => new CapitolCanonic(
            s.Cod, Taie(s.Denumire, 500)!, TipCapitol.Capitol,
            s.Grupuri.Select(g => new CapitolCanonic($"{s.Cod}.{g.Cod}", Taie(g.Denumire, 500)!, TipCapitol.Capitol, [],
                g.Articole.Select(a => Articol(a, s)).ToList(), $"{b.Obiect}.{s.Cod}.{g.Cod}")).ToList(),
            s.Articole.Select(a => Articol(a, s)).ToList(), $"{b.Obiect}.{s.Cod}")).ToList();
        var obiect = new CapitolCanonic(b.Obiect, Taie(b.DenumireObiect, 500)!, TipCapitol.Obiect, sectiuni, [], b.Obiect);

        return new DevizCanonic($"Ofertă {b.Obiect} — {b.DenumireObiect}", [obiect], new ParametriRecapitulatie(0, 0, 0, 0), null)
        {
            Antet = new AntetCanonic(null, $"{b.Obiect} — {b.DenumireObiect}", null),
            Moneda = moneda,
            PeObiecte = true,
            PoliticaRotunjire = PoliticaRotunjire.Fara,
            Componente = componente,
            Sectiuni = b.Sectiuni.Select(s => new SectiuneImport(s.Cod, s.Denumire, s.Foaie, s.ArticoleToate().Count(), s.Valoare, s.TotalDeclarat, s.TotalRezumat)).ToList(),
            Ignorate = b.Ignorate,
            TotaluriDeclarateJson = JsonSerializer.Serialize(new { b.Obiect, b.TotalGeneral, Sectiuni = b.Rezumat }),
        };
    }

    private static ValoriResurse Suma(IReadOnlyDictionary<string, ValoareComponenta> valori, IReadOnlyDictionary<string, TipResursa> tipuri, Func<ValoareComponenta, decimal> f)
    {
        decimal m = 0, mn = 0, u = 0, t = 0, e = 0;
        foreach (var (cheie, v) in valori)
        {
            switch (tipuri.GetValueOrDefault(cheie, TipResursa.Utilaj))
            {
                case TipResursa.Material: m += f(v); break;
                case TipResursa.Manopera: mn += f(v); break;
                case TipResursa.Transport: t += f(v); break;
                case TipResursa.Echipament: e += f(v); break;
                default: u += f(v); break;
            }
        }

        return new ValoriResurse(m, mn, u, t, e);
    }

    private static string? Taie(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}

/// <summary>
/// Importatorul devizelor de ofertă BOQ din Excel (D-58): recunoaște fișierul după antetul de BOQ al unui profil cunoscut
/// (<see cref="ProfilBoq.Toate"/>), îl citește (<see cref="CititorBoq"/>) și îl convertește (<see cref="ConversieBoq"/>).
/// Moneda: cea aleasă la import, altfel cea implicită a profilului; componentele neclare se aleg la import.
/// </summary>
public sealed class ImportatorBoq : IImportatorDevizCuAnexe
{
    public string FormatId => "boq";

    public string Denumire => "BOQ ofertă pe obiecte (Excel)";

    public RezultatDetectare PoateCiti(SursaImport sursa)
    {
        if (sursa.Extensie != ".xlsx")
            return RezultatDetectare.Nu("BOQ-ul se citește din .xlsx.");
        var foi = Foi(sursa);
        if (foi is null)
            return RezultatDetectare.Nu("Fișierul nu e un .xlsx valid.");
        var profil = Profil(foi);
        if (profil is null)
            return RezultatDetectare.Nu("Nicio foaie cu antet de BOQ (Work Items / Quantity / Material…).");
        var areRezumat = foi.Any(f => profil.FoiRezumat.Contains(ValoriFoaie.Cheie(f.Nume)));
        return new RezultatDetectare(areRezumat ? 0.92 : 0.8, $"{profil.Denumire}{(areRezumat ? ", cu foaia de rezumat" : string.Empty)}.");
    }

    public Task<RezultatImport<DevizCanonic>> CitesteAsync(SursaImport sursa, CancellationToken ct = default) =>
        CitesteAsync(sursa, new OptiuniImportDeviz([]), ct);

    public Task<RezultatImport<DevizCanonic>> CitesteAsync(SursaImport principal, OptiuniImportDeviz optiuni, CancellationToken ct = default)
    {
        if (optiuni.Anexe.Count > 0)
            return Task.FromResult(RezultatImport<DevizCanonic>.Esuat("BOQ-ul se importă câte un fișier (un obiect) odată."));
        var foi = Foi(principal);
        if (foi is null)
            return Task.FromResult(RezultatImport<DevizCanonic>.Esuat("Fișierul nu e un .xlsx valid."));
        var profil = Profil(foi);
        if (profil is null)
            return Task.FromResult(RezultatImport<DevizCanonic>.Esuat("Nu am găsit nicio foaie cu antet de BOQ."));

        var citit = CititorBoq.Citeste(foi, profil, principal.NumeFisier);
        var moneda = string.IsNullOrWhiteSpace(optiuni.Moneda) ? profil.MonedaImplicita : optiuni.Moneda.Trim().ToUpperInvariant();
        var deviz = ConversieBoq.Converteste(citit, profil, moneda, optiuni.Componente);
        var probleme = citit.Probleme.ToList();
        probleme.Insert(0, new ProblemaImport(Severitate.Informare, "fișier",
            $"{profil.Denumire}: obiectul {citit.Obiect} ({citit.DenumireObiect}), {citit.Sectiuni.Count} secțiuni, " +
            $"{citit.Sectiuni.Sum(s => s.ArticoleToate().Count())} articole, valoare {citit.Sectiuni.Sum(s => s.Valoare).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("ro-RO"))} {moneda}" +
            $"{(citit.Ignorate.Count > 0 ? $"; {citit.Ignorate.Count} foi lăsate deoparte" : string.Empty)}."));
        return Task.FromResult(new RezultatImport<DevizCanonic>(deviz, probleme, new Dictionary<string, string> { ["profil"] = profil.Id, ["obiect"] = citit.Obiect }));
    }

    private static IReadOnlyList<FoaieTabel>? Foi(SursaImport sursa)
    {
        using var ms = new MemoryStream(sursa.Continut, false);
        return CititorFoiXlsx.Citeste(ms);
    }

    private static ProfilBoq? Profil(IReadOnlyList<FoaieTabel> foi) =>
        ProfilBoq.Toate.FirstOrDefault(p => foi.Any(f => CititorBoq.AreAntet(f, p)));
}

public static class ExtensiiImportatorBoq
{
    /// <summary>Înregistrează importatorul BOQ (D-58), ca la ceilalți importatori de devize.</summary>
    public static IServiceCollection AdaugaImportatorBoq(this IServiceCollection servicii) =>
        servicii.AddSingleton<IImportatorDeviz, ImportatorBoq>();
}
