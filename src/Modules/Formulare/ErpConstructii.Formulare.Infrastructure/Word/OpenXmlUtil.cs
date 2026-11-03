using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using CustomProps = DocumentFormat.OpenXml.CustomProperties;
using VariantTypes = DocumentFormat.OpenXml.VariantTypes;

namespace ErpConstructii.Formulare.Infrastructure.Word;

/// <summary>
/// Operații comune pe documentele Word.
///
/// CONVENȚIA ȘABLOANELOR (documentată și pentru firmele care își editează șabloanele):
///  - fiecare câmp este un control de conținut Word (Developer → Plain Text Content Control)
///    cu proprietatea Tag = numele câmpului din schema tipului (ex. <c>furnizor_cui</c>);
///  - tabelele repetate au UN rând-șablon cu controale etichetate „{tabel}.{coloana}”;
///  - textul de placeholder al controalelor este ignorat la citire.
/// </summary>
internal static class OpenXmlUtil
{
    /// <summary>FMTID standard pentru proprietățile personalizate ale documentelor Office.</summary>
    private const string FmtIdProprietati = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";

    public const string PropTip = "erp-tip-formular";
    public const string PropVersiune = "erp-versiune-sablon";
    public const string PropDocument = "erp-document-id";
    public const string PropNumar = "erp-numar-rezervat";

    public static string? Eticheta(SdtElement sdt) => sdt.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value;

    /// <summary>Toate rădăcinile cu conținut: corpul documentului, antetele și subsolurile.</summary>
    public static IEnumerable<OpenXmlElement> Radacini(MainDocumentPart main)
    {
        if (main.Document is not null)
        {
            yield return main.Document;
        }

        foreach (var h in main.HeaderParts)
        {
            if (h.Header is not null)
            {
                yield return h.Header;
            }
        }

        foreach (var f in main.FooterParts)
        {
            if (f.Footer is not null)
            {
                yield return f.Footer;
            }
        }
    }

    public static bool AratePlaceholder(SdtElement sdt) =>
        sdt.SdtProperties?.GetFirstChild<ShowingPlaceholder>() is not null;

    public static string TextDin(SdtElement sdt) =>
        AratePlaceholder(sdt)
            ? string.Empty
            : string.Concat(sdt.Descendants<Text>().Select(t => t.Text)).Trim();

    /// <summary>
    /// Pune textul în control, păstrând formatarea primului run. Valorile pe mai multe rânduri
    /// sunt scrise pe un singur rând (TODO: transformare \n în Break, dacă e nevoie).
    /// </summary>
    public static void SeteazaText(SdtElement sdt, string valoare)
    {
        sdt.SdtProperties?.GetFirstChild<ShowingPlaceholder>()?.Remove();
        foreach (var stil in sdt.Descendants<RunStyle>().Where(s => s.Val?.Value == "PlaceholderText").ToList())
        {
            stil.Remove();
        }

        var texte = sdt.Descendants<Text>().ToList();
        if (texte.Count > 0)
        {
            texte[0].Text = valoare;
            texte[0].Space = SpaceProcessingModeValues.Preserve;
            foreach (var t in texte.Skip(1))
            {
                t.Remove();
            }

            return;
        }

        var run = new Run(new Text(valoare) { Space = SpaceProcessingModeValues.Preserve });
        switch (sdt)
        {
            case SdtRun r:
                (r.SdtContentRun ??= new SdtContentRun()).AppendChild(run);
                break;
            case SdtBlock b:
                (b.SdtContentBlock ??= new SdtContentBlock()).AppendChild(new Paragraph(run));
                break;
            default:
                var paragraf = sdt.Descendants<Paragraph>().FirstOrDefault();
                if (paragraf is not null)
                {
                    paragraf.AppendChild(run);
                }

                break;
        }
    }

    public static void ScrieProprietate(WordprocessingDocument doc, string nume, string valoare)
    {
        var parte = doc.CustomFilePropertiesPart ?? doc.AddCustomFilePropertiesPart();
        parte.Properties ??= new CustomProps.Properties();
        var proprietati = parte.Properties;

        proprietati.Elements<CustomProps.CustomDocumentProperty>()
            .FirstOrDefault(p => p.Name?.Value == nume)
            ?.Remove();

        var urmatorulId = proprietati.Elements<CustomProps.CustomDocumentProperty>()
            .Select(p => p.PropertyId?.Value ?? 1)
            .DefaultIfEmpty(1)
            .Max() + 1;

        proprietati.AppendChild(new CustomProps.CustomDocumentProperty
        {
            FormatId = FmtIdProprietati,
            PropertyId = urmatorulId,
            Name = nume,
            VTLPWSTR = new VariantTypes.VTLPWSTR(valoare),
        });
    }

    public static string? CitesteProprietate(WordprocessingDocument doc, string nume) =>
        doc.CustomFilePropertiesPart?.Properties?
            .Elements<CustomProps.CustomDocumentProperty>()
            .FirstOrDefault(p => p.Name?.Value == nume)?
            .VTLPWSTR?.Text;
}
