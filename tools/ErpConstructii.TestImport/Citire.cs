using System.Security.Cryptography;
using ErpConstructii.Aprovizionare.Import;
using ErpConstructii.Facturi.Import;
using ErpConstructii.Formulare.Infrastructure.Extragere;
using ErpConstructii.SharedKernel.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ErpConstructii.TestImport;

public sealed record LinieCitita(string? Denumire, string? Um, decimal? Cantitate, decimal? Pret, decimal? Valoare, decimal? Cota);

/// <summary>Ce s-a citit dintr-un document (același model pentru factură, notă de comandă, necesar).</summary>
public sealed record DocumentCitit(
    string Tip,
    List<int> Pagini,
    string? Numar = null,
    DateOnly? Data = null,
    string? Furnizor = null,
    string? FurnizorCui = null,
    string? Lucrare = null,
    decimal? TotalFaraTva = null,
    decimal? TotalTva = null,
    decimal? TotalDePlata = null,
    List<LinieCitita>? Linii = null)
{
    /// <summary>Câmpurile marcate „de verificat” / „de completat” de interpretor (doar în raport).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, Certitudine> Certitudini { get; init; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> Probleme { get; init; } = [];
}

public sealed record FisierCitit(List<DocumentCitit> Documente);

public sealed record RezultatCitire(FisierCitit Fisier, TextDocument? Text, List<PaginaClasificata> Pagini, string? Eroare, bool DinCache);

/// <summary>
/// Citește un fișier exact ca aplicația: extractorii (PDF digital / OCR / Word), clasificarea paginilor, împărțirea în
/// documente (centrul de import), apoi interpretorul fiecărui tip. Textul extras se păstrează pe disc (după SHA-256),
/// ca rularea după o schimbare a interpretorilor să dureze secunde, nu minute de OCR.
/// </summary>
public sealed class Citire(string radacina, string folderCache, bool faraCache, string cuiFirma)
{
    private static readonly string[] Extensii = [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".docx"];

    private readonly List<IExtractorText> _extractori = CreeazaExtractori(radacina);

    public static bool Accepta(string fisier) => Extensii.Contains(Path.GetExtension(fisier).ToLowerInvariant());

    private static List<IExtractorText> CreeazaExtractori(string radacina)
    {
        var ocr = new ExtractorTextOcr(
            Options.Create(new OptiuniOcr { CaleTessdata = Path.Combine(radacina, "tessdata") }),
            NullLogger<ExtractorTextOcr>.Instance);
        return [new ExtractorTextWord(), new ExtractorTextPdf(ocr, NullLogger<ExtractorTextPdf>.Instance), ocr];
    }

    public async Task<RezultatCitire> CitesteAsync(string cale, string nume)
    {
        var continut = await File.ReadAllBytesAsync(cale);
        var (text, eroare, dinCache) = await TextAsync(new SursaImport(Path.GetFileName(cale), continut));
        if (text is null)
        {
            return new RezultatCitire(new FisierCitit([]), null, [], eroare, false);
        }

        var pagini = ClasificarePagini.Clasifica(text).ToList();
        var documente = ImpartireDocumente.Imparte(pagini)
            .Select(d => Interpreteaza(d, text))
            .ToList();
        return new RezultatCitire(new FisierCitit(documente), text, pagini, null, dinCache);
    }

    private DocumentCitit Interpreteaza(DocumentDetectat d, TextDocument text)
    {
        var pagini = d.Pagini.ToHashSet();
        var doc = text with { Randuri = text.Randuri.Where(r => pagini.Contains(r.Pagina)).ToList() };
        var baza = new DocumentCitit(d.Tip.ToString(), [.. d.Pagini]);
        switch (d.Tip)
        {
            case TipDocumentImport.Factura:
            {
                var (f, _) = CitireFacturaText.Citeste(doc, [], cuiFirma);
                return baza with
                {
                    Numar = f.Numar.Valoare,
                    Data = Data(f.DataEmitere),
                    Furnizor = f.FurnizorDenumire.Valoare,
                    FurnizorCui = f.FurnizorCui.Valoare,
                    TotalFaraTva = Suma(f.TotalFaraTva),
                    TotalTva = Suma(f.TotalTva),
                    TotalDePlata = Suma(f.TotalDePlata),
                    Linii = f.Linii.Select(l => new LinieCitita(l.Denumire.Valoare, l.UnitateMasura.Valoare, Suma(l.Cantitate),
                        Suma(l.PretUnitar), Suma(l.ValoareFaraTva), Suma(l.CotaTva))).ToList(),
                    Certitudini = Certitudini(("numar", f.Numar.Certitudine), ("data", f.DataEmitere.Certitudine),
                        ("furnizor", f.FurnizorDenumire.Certitudine), ("furnizorCui", f.FurnizorCui.Certitudine),
                        ("totalFaraTva", f.TotalFaraTva.Certitudine), ("totalTva", f.TotalTva.Certitudine), ("totalDePlata", f.TotalDePlata.Certitudine)),
                    Probleme = f.Probleme.Where(p => p.Locatie != "pagini").Select(p => p.Mesaj).ToList(),
                };
            }

            case TipDocumentImport.NotaComanda:
            {
                var (pentruTip, _) = ClasificarePagini.PentruTip(doc, TipPagina.NotaComanda);
                var n = InterpretorNotaComanda.Interpreteaza(pentruTip, cuiFirma);
                return baza with
                {
                    Numar = n.NumarExtern.Valoare,
                    Data = Data(n.Data),
                    Furnizor = n.FurnizorDenumire.Valoare,
                    FurnizorCui = n.FurnizorCui.Valoare,
                    Lucrare = n.Lucrare.Valoare,
                    Linii = n.Linii.Select(l => new LinieCitita(l.Denumire.Valoare, l.UnitateMasura.Valoare, Suma(l.Cantitate), null, null, null)).ToList(),
                    Certitudini = Certitudini(("numar", n.NumarExtern.Certitudine), ("data", n.Data.Certitudine),
                        ("furnizor", n.FurnizorDenumire.Certitudine), ("lucrare", n.Lucrare.Certitudine)),
                    Probleme = n.Probleme.Select(p => p.Mesaj).ToList(),
                };
            }

            case TipDocumentImport.NecesarMateriale:
            {
                var (pentruTip, _) = ClasificarePagini.PentruTip(doc, TipPagina.NecesarMateriale);
                var n = InterpretorNecesarMateriale.Interpreteaza(pentruTip);
                return baza with
                {
                    Numar = n.NumarExtern.Valoare,
                    Data = Data(n.Data),
                    Lucrare = n.Lucrare.Valoare,
                    Linii = n.Linii.Select(l => new LinieCitita(l.Denumire.Valoare, l.UnitateMasura.Valoare, Suma(l.Cantitate), null, null, null)).ToList(),
                    Certitudini = Certitudini(("numar", n.NumarExtern.Certitudine), ("data", n.Data.Certitudine), ("lucrare", n.Lucrare.Certitudine)),
                    Probleme = n.Probleme.Select(p => p.Mesaj).ToList(),
                };
            }

            default:
                return baza; // bon de consum (fără interpretor încă), anexe
        }
    }

    private static DateOnly? Data(CampCitit<DateOnly> c) => c.Certitudine == Certitudine.Lipsa ? null : c.Valoare;

    private static decimal? Suma(CampCitit<decimal> c) => c.Certitudine == Certitudine.Lipsa ? null : c.Valoare;

    private static Dictionary<string, Certitudine> Certitudini(params (string Camp, Certitudine C)[] campuri) =>
        campuri.ToDictionary(c => c.Camp, c => c.C);

    private async Task<(TextDocument? Text, string? Eroare, bool DinCache)> TextAsync(SursaImport sursa)
    {
        var cache = Path.Combine(folderCache, Convert.ToHexString(SHA256.HashData(sursa.Continut))[..32] + ".json");
        if (!faraCache && File.Exists(cache))
        {
            return (Json.CitesteText(await File.ReadAllTextAsync(cache)), null, true);
        }

        var extractor = _extractori.First(e => e.Accepta(sursa.NumeFisier));
        var r = await extractor.ExtrageAsync(sursa);
        if (r.Valoare is null)
        {
            return (null, r.Probleme.FirstOrDefault()?.Mesaj ?? "Documentul nu poate fi citit.", false);
        }

        Directory.CreateDirectory(folderCache);
        await File.WriteAllTextAsync(cache, Json.ScrieText(r.Valoare));
        return (r.Valoare, null, false);
    }
}
