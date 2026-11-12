using System.Globalization;

namespace ErpConstructii.Devize.Analize;

public sealed class AnalizorAmprentaNumerelor : IAnalizorDeviz
{
    private static readonly decimal[] DensitatiImplicite = [1.6m, 1.8m, 2.4m, 7.85m];
    public string Cod => "A2";
    public string Versiune => "1.0";

    public RezultatAnalizor Analizeaza(ContextAnaliza context)
    {
        context.Valideaza();
        var deviz = context.Deviz;
        var config = context.Configuratie;
        var densitati = config.Densitati ?? DensitatiImplicite;
        var constatari = new List<ConstatareAnaliza>();
        var necunoscute = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var grupuri = new Dictionary<Guid, List<(ArticolDeviz Articol, UnitateAnaliza Um)>>();
        foreach (var capitol in deviz.Capitole.OrderBy(c => c.Id))
        {
            var stadiu = deviz.Cale(capitol).LastOrDefault(c => c.Tip == TipCapitol.StadiuFizic);
            var grup = stadiu?.Id ?? capitol.Id;
            foreach (var articol in capitol.Articole.OrderBy(a => a.Ordine).ThenBy(a => a.CheieStabila))
            {
                if (!UnitatiAnaliza.Incearca(articol.UnitateMasura, out var um))
                {
                    necunoscute.Add(articol.UnitateMasura);
                    continue;
                }
                if (articol.Cantitate < config.CantitateMinima || articol.Cantitate <= 0)
                    continue;
                if (!grupuri.TryGetValue(grup, out var articole))
                    grupuri.Add(grup, articole = []);
                articole.Add((articol, um));
            }
        }

        foreach (var articole in grupuri.Values)
        {
            var volume = articole.Where(x => x.Um.Dimensiune == DimensiuneAnaliza.Volum).ToArray();
            var mase = articole.Where(x => x.Um.Dimensiune == DimensiuneAnaliza.Masa).ToArray();
            foreach (var (volum, umVolum) in volume)
            {
                foreach (var (masa, umMasa) in mase)
                {
                    var volumBaza = volum.Cantitate * umVolum.FactorBaza;
                    var masaBaza = masa.Cantitate * umMasa.FactorBaza;
                    var factor = densitati.Where(d => Aproape(masaBaza, volumBaza * d, config.TolerantaFactor))
                        .OrderBy(d => Math.Abs(masaBaza - volumBaza * d))
                        .ThenBy(d => d).Select(d => (decimal?)d).FirstOrDefault();
                    if (factor is null)
                        continue;
                    var raport = masaBaza / volumBaza;
                    constatari.Add(new ConstatareAnaliza(
                        "A2.FACTOR_CONVERSIE", Cod, Versiune, SeveritateAnaliza.Info, 0.6m,
                        new[] { volum.CheieStabila, masa.CheieStabila }.Order().ToArray(),
                        $"{N(masa.Cantitate)} {masa.UnitateMasura} / {N(volum.Cantitate)} {volum.UnitateMasura} " +
                        $"înseamnă {N(raport)} t/mc, apropiat de factorul {N(factor.Value)} t/mc.",
                        [new("volum", volum.CheieStabila.ToString("D")), new("cantitate_volum", N(volum.Cantitate)),
                            new("um_volum", volum.UnitateMasura), new("masa", masa.CheieStabila.ToString("D")),
                            new("cantitate_masa", N(masa.Cantitate)), new("um_masa", masa.UnitateMasura),
                            new("raport_t_mc", N(raport)), new("factor_t_mc", N(factor.Value))]));
                }
            }
            foreach (var volumeEgale in volume.GroupBy(x => Rotunjit(x.Articol.Cantitate)))
            {
                var valoare = volumeEgale.Key;
                if (valoare is 1 or 10 or 100)
                    continue;
                var maseEgale = mase.Where(x => Rotunjit(x.Articol.Cantitate) == valoare).ToArray();
                if (maseEgale.Length == 0)
                    continue;
                var martoriPosibili = volume.SelectMany(v => maseEgale.Select(m => (Volum: v, Masa: m)))
                    .Where(x => densitati.Any(d => Aproape(
                        x.Masa.Articol.Cantitate * x.Masa.Um.FactorBaza,
                        x.Volum.Articol.Cantitate * x.Volum.Um.FactorBaza * d, config.TolerantaFactor)))
                    .ToArray();
                foreach (var (volum, umVolum) in volumeEgale)
                {
                    var martori = martoriPosibili.Where(x => x.Volum.Articol.CheieStabila != volum.CheieStabila).ToArray();
                    var corectii = martori.Select(x => x.Volum.Articol.Cantitate * x.Volum.Um.FactorBaza / umVolum.FactorBaza)
                        .Distinct().ToArray();
                    CorecturaCantitate? corectura = corectii.Length == 1
                        ? new(volum.CheieStabila, volum.Cantitate, corectii[0])
                        : null;
                    decimal? impactDirect = corectura is null ? null : Bani((corectura.CantitateNoua - volum.Cantitate) * volum.PretUnitar.Total);
                    decimal? impactOferta = corectura is null || !volum.ArePretOferta || deviz.Moneda != "RON" && deviz.Curs is null
                        ? null
                        : Bani((corectura.CantitateNoua - volum.Cantitate) * volum.PretOfertaUnitar.Total * (deviz.Curs ?? 1m));
                    var tinte = maseEgale.Select(x => x.Articol.CheieStabila)
                        .Concat(martori.Select(x => x.Volum.Articol.CheieStabila))
                        .Append(volum.CheieStabila).Distinct().Order().ToArray();
                    var dovezi = new List<DovadaAnaliza>
                {
                    new("articol", volum.CheieStabila.ToString("D")),
                    new("cantitate", N(volum.Cantitate)),
                    new("unitate", volum.UnitateMasura),
                    new("mase", string.Join(";", maseEgale.Select(x => $"{x.Articol.CheieStabila:D}:{N(x.Articol.Cantitate)}:{x.Articol.UnitateMasura}").Order(StringComparer.Ordinal))),
                    new("martori", string.Join(";", martori.Select(x =>
                        $"{x.Volum.Articol.CheieStabila:D}:{N(x.Volum.Articol.Cantitate)}:{x.Volum.Articol.UnitateMasura}:" +
                        $"{x.Masa.Articol.CheieStabila:D}:{N(x.Masa.Articol.Cantitate * x.Masa.Um.FactorBaza / (x.Volum.Articol.Cantitate * x.Volum.Um.FactorBaza))}")
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))),
                };
                    var explicatie = $"Articolul {volum.Simbol} are {N(volum.Cantitate)} {volum.UnitateMasura}, aceeași valoare numerică precum un articol de masă din același capitol sau stadiu.";
                    if (corectura is not null)
                    {
                        var martor = martori[0];
                        var raport = martor.Masa.Articol.Cantitate * martor.Masa.Um.FactorBaza
                            / (martor.Volum.Articol.Cantitate * martor.Volum.Um.FactorBaza);
                        explicatie += $" {N(martor.Masa.Articol.Cantitate)} {martor.Masa.Articol.UnitateMasura} / " +
                            $"{N(martor.Volum.Articol.Cantitate)} {martor.Volum.Articol.UnitateMasura} indică un raport de {N(raport)} t/mc; " +
                            $"verificați cantitatea {N(corectura.CantitateNoua)} {volum.UnitateMasura}.";
                    }
                    constatari.Add(new ConstatareAnaliza(
                        "A2.UNITATE_COPIATA", Cod, Versiune, SeveritateAnaliza.Info, corectura is null ? 0.5m : 0.8m,
                        tinte, explicatie, dovezi, corectura, impactDirect, impactOferta));
                }
            }
        }

        var limitari = new List<string> { "Verificarea zecimalelor și a altor perechi de dimensiuni nu este încă disponibilă." };
        if (necunoscute.Count > 0)
            limitari.Add($"Unități necunoscute, excluse din A2: {string.Join(", ", necunoscute)}.");
        return new(Cod, Versiune, constatari.OrderBy(c => c.Amprenta(), StringComparer.Ordinal).ToArray(), limitari);
    }

    private static bool Aproape(decimal valoare, decimal asteptat, decimal toleranta) =>
        asteptat > 0 && Math.Abs(valoare - asteptat) <= asteptat * toleranta;
    private static decimal Rotunjit(decimal valoare) => Math.Round(valoare, 2, MidpointRounding.AwayFromZero);
    private static decimal Bani(decimal valoare) => Math.Round(valoare, 2, MidpointRounding.AwayFromZero);
    private static string N(decimal valoare) => valoare.ToString("G29", CultureInfo.InvariantCulture);
}
