namespace ErpConstructii.SharedKernel.Import;

/// <summary>Ce se poate importa dintr-un fișier cu mai multe documente.</summary>
public enum TipDocumentImport
{
    /// <summary>Factura furnizorului, cu e-Factura (vizualizarea SPV), avizul, certificatele și anexele ei.</summary>
    Factura,

    NotaComanda,

    NecesarMateriale,

    BonConsum,

    /// <summary>Pagini care nu țin de niciun document importabil (ex. doar certificate).</summary>
    Anexe,
}

/// <param name="Pagini">Paginile documentului (numerotarea din fișier, crescător).</param>
/// <param name="Numar">Numărul documentului, dacă s-a citit (pentru factură: din factura tipărită sau din e-Factura).</param>
public sealed record DocumentDetectat(TipDocumentImport Tip, IReadOnlyList<int> Pagini, string? Numar, DateOnly? Data);

/// <summary>
/// Împarte un fișier scanat în documente: pe hârtie, oamenii țin la un loc mai multe achiziții (fiecare cu factura,
/// e-Factura, nota de comandă, avizul, certificatele), necesare de materiale și bonuri de consum, iar scanerul le pune
/// pe toate într-un singur PDF. Reguli, în ordinea paginilor:
/// <list type="bullet">
/// <item>paginile consecutive de același tip formează un document, cât timp numărul (și data) nu se schimbă;</item>
/// <item>factura și e-Factura ei (același număr, sau număr necitit) sunt un singur document;</item>
/// <item>avizul, certificatele și anexele merg la ultima factură (dacă nu există încă, la prima factură care urmează);</item>
/// <item>paginile fără titlu continuă documentul curent.</item>
/// </list>
/// Rezultatul e o propunere: utilizatorul poate uni sau despărți documentele. Funcție pură.
/// </summary>
public static class ImpartireDocumente
{
    private sealed class Grup(TipDocumentImport tip)
    {
        public TipDocumentImport Tip { get; } = tip;
        public List<PaginaClasificata> Pagini { get; } = [];
        public bool AreEFactura => Pagini.Any(p => p.Tip == TipPagina.EFactura);

        /// <summary>Paginile proprii ale documentului (fără aviz, certificate, anexe).</summary>
        private IEnumerable<PaginaClasificata> Principale => Pagini.Where(p => TipDocument(p.Tip) == Tip);

        /// <summary>La factură, numărul din factura tipărită are prioritate (seria e citită complet).</summary>
        public string? Numar => Principale.OrderBy(p => p.Tip == TipPagina.Factura ? 0 : 1).FirstOrDefault(p => p.Numar is not null)?.Numar;

        public DateOnly? Data => Principale.FirstOrDefault(p => p.Data is not null)?.Data;
    }

    public static IReadOnlyList<DocumentDetectat> Imparte(IReadOnlyList<PaginaClasificata> pagini)
    {
        var grupuri = new List<Grup>();
        var inainte = new List<PaginaClasificata>(); // anexe dinaintea primului document
        Grup? curent = null;
        Grup? anterior = null; // documentul paginii anterioare

        foreach (var p in pagini.OrderBy(p => p.Pagina))
        {
            var tip = TipDocument(p.Tip);
            if (tip is null)
            {
                // Aviz, certificate, anexe: la ultima factură; paginile fără titlu: la documentul curent.
                var destinatie = p.Tip == TipPagina.Altele ? anterior : grupuri.LastOrDefault(g => g.Tip == TipDocumentImport.Factura) ?? curent;
                if (destinatie is null)
                {
                    inainte.Add(p);
                }
                else
                {
                    destinatie.Pagini.Add(p);
                }

                anterior = destinatie;
                continue;
            }

            if (curent is null || !AcelasiDocument(curent, p, tip.Value))
            {
                curent = new Grup(tip.Value);
                grupuri.Add(curent);
            }

            curent.Pagini.Add(p);
            anterior = curent;
        }

        if (inainte.Count > 0)
        {
            var tinta = grupuri.FirstOrDefault(g => g.Tip == TipDocumentImport.Factura) ?? grupuri.FirstOrDefault();
            if (tinta is null)
            {
                tinta = new Grup(TipDocumentImport.Anexe);
                grupuri.Add(tinta);
            }

            tinta.Pagini.AddRange(inainte);
        }

        return grupuri
            .Select(g => new DocumentDetectat(g.Tip, g.Pagini.Select(p => p.Pagina).Order().ToList(), g.Numar, g.Data))
            .OrderBy(d => d.Pagini[0])
            .ToList();
    }

    private static TipDocumentImport? TipDocument(TipPagina tip) => tip switch
    {
        TipPagina.Factura or TipPagina.EFactura => TipDocumentImport.Factura,
        TipPagina.NotaComanda => TipDocumentImport.NotaComanda,
        TipPagina.NecesarMateriale => TipDocumentImport.NecesarMateriale,
        TipPagina.BonConsum => TipDocumentImport.BonConsum,
        _ => null,
    };

    private static bool AcelasiDocument(Grup curent, PaginaClasificata p, TipDocumentImport tip)
    {
        if (curent.Tip != tip)
        {
            return false;
        }

        // Numere diferite (comparate pe cifre: seria se citește diferit în factură și în e-Factura) → alt document.
        var numar = ClasificarePagini.CheieNumar(p.Numar);
        var numereGrup = curent.Pagini.Where(x => TipDocument(x.Tip) == tip).Select(x => ClasificarePagini.CheieNumar(x.Numar)).Where(n => n is not null).ToHashSet();
        if (numar is not null && numereGrup.Count > 0 && !numereGrup.Any(n => ClasificarePagini.AcelasiNumar(n!, numar)))
        {
            return false;
        }

        if (p.Data is { } d && curent.Data is { } dg && d != dg)
        {
            return false;
        }

        // A doua e-Factura (sau a doua factură după e-Factura) fără număr citit: o factură are o singură vizualizare SPV.
        if (tip == TipDocumentImport.Factura && numar is null && p.Tip == TipPagina.EFactura && curent.AreEFactura &&
            curent.Pagini[^1].Tip != TipPagina.EFactura)
        {
            return false;
        }

        return true;
    }
}
