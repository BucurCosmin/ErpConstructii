using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Facturi.Import;

/// <summary>
/// Factura dintr-un document text (PDF digital sau OCR), pas cu pas, ca în aplicație: clasificarea paginilor, alegerea
/// paginilor facturii (vizualizarea e-Factura din SPV are prioritate față de factura tipărită), interpretorul potrivit.
/// Folosită de importul facturilor și de aplicația de test a importului (tools/ErpConstructii.TestImport). Funcție pură.
/// </summary>
public static class CitireFacturaText
{
    public static (FacturaCitita Factura, IReadOnlyList<PaginaClasificata> Pagini) Citeste(
        TextDocument text, IEnumerable<ProblemaImport> problemeText, string? cuiFirma)
    {
        // Dosar scanat (factură + e-Factura + notă de comandă + aviz…): factura se citește doar din paginile ei.
        var pagini = ClasificarePagini.Clasifica(text);
        var document = text;
        var probleme = problemeText.ToList();
        if (pagini.Count > 1)
        {
            var tip = TipPaginaFactura(pagini);
            if (ClasificarePagini.Pagini(document, pagini, tip) is { } doarFactura)
            {
                document = doarFactura;
                var folosite = pagini.Where(p => p.Tip == tip).Select(p => p.Pagina).ToList();
                var altele = pagini.Where(p => p.Tip != tip).ToList();
                probleme.Add(new ProblemaImport(Severitate.Informare, "pagini",
                    $"PDF-ul are {pagini.Count} pagini; factura a fost citită din {(folosite.Count == 1 ? "pagina" : "paginile")} {string.Join(", ", folosite)}" +
                    (altele.Count == 0 ? "." : $". Mai conține: {string.Join("; ", altele.Select(p => $"p. {p.Pagina} – {p.Descriere}"))}.")));
            }
            else
            {
                probleme.Add(new ProblemaImport(Severitate.Avertisment, "pagini",
                    $"Nu am găsit pagina facturii între cele {pagini.Count} pagini; verifică documentul."));
            }
        }

        var factura = InterpretorVizualizareEFactura.EsteVizualizare(document)
            ? InterpretorVizualizareEFactura.Interpreteaza(document, cuiFirma)
            : InterpretorFacturaPdf.Interpreteaza(document, cuiFirma);
        return (factura with { Probleme = [.. probleme, .. factura.Probleme] }, pagini);
    }

    /// <summary>Vizualizarea SPV are același format la toți furnizorii: dacă există, factura se citește de acolo.</summary>
    public static TipPagina TipPaginaFactura(IReadOnlyList<PaginaClasificata> pagini) =>
        pagini.Any(p => p.Tip == TipPagina.EFactura) ? TipPagina.EFactura : TipPagina.Factura;
}
