using System.Globalization;
using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Devize.Analize;

public sealed class AnalizorPreturiSpeciale : IAnalizorDeviz
{
    public string Cod => "A5";
    public string Versiune => "1.0";

    public RezultatAnalizor Analizeaza(ContextAnaliza context)
    {
        context.Valideaza();
        var consumuri = context.Deviz.Articole().OrderBy(a => a.CheieStabila)
            .SelectMany(a => a.Consumuri.OrderBy(c => c.Ordine).ThenBy(c => c.Id).Select(c => (Articol: a, Consum: c)))
            .Where(x => x.Consum.CantitateTotala > 0)
            .ToArray();
        var constatari = new List<ConstatareAnaliza>();

        foreach (var (articol, consum) in consumuri.Where(x => x.Consum.PretUnitar == 0))
        {
            constatari.Add(new ConstatareAnaliza(
                "A5.PRET_ZERO", Cod, Versiune, SeveritateAnaliza.Avertisment, 1m, [articol.CheieStabila],
                $"Resursa {consum.CodResursa} ({consum.Denumire}) are consum {N(consum.CantitateTotala)} {consum.UnitateMasura} și preț zero.",
                [new("resursa", consum.CodResursa), new("um", consum.UnitateMasura),
                    new("cantitate", N(consum.CantitateTotala)), new("pret", "0")]));
        }

        var manopera = consumuri.Where(x => x.Consum.Tip == TipResursa.Manopera
            && UnitatiAnaliza.Incearca(x.Consum.UnitateMasura, out var um) && um.Dimensiune == DimensiuneAnaliza.Timp).ToArray();
        var categorii = manopera.Select(x => x.Consum.CodResursa).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var tarife = manopera.Select(x => x.Consum.PretUnitar).Distinct().ToArray();
        if (categorii >= 2 && tarife.Length == 1 && tarife[0] > 0)
        {
            constatari.Add(new ConstatareAnaliza(
                "A5.TARIF_UNIFORM", Cod, Versiune, SeveritateAnaliza.Info, 1m,
                manopera.Select(x => x.Articol.CheieStabila).Distinct().Order().ToArray(),
                $"Toate cele {categorii} categorii de manoperă au același tarif: {N(tarife[0])}.",
                [new("categorii", string.Join(",", manopera.Select(x => x.Consum.CodResursa)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))),
                    new("tarif", N(tarife[0]))]));
        }

        return new(Cod, Versiune, constatari,
            ["Comparațiile statistice de preț și consum nu sunt disponibile fără istoric și indici de preț."]);
    }

    private static string N(decimal valoare) => valoare.ToString("G29", CultureInfo.InvariantCulture);
}
