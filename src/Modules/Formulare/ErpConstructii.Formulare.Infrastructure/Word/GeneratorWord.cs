using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ErpConstructii.Formulare.Infrastructure.Word;

/// <summary>
/// Completează un șablon Word cu datele unui formular (REQ-FRM-03..05), fără Word instalat.
/// Rezultatul conține proprietățile ascunse (tip, versiune șablon, document) folosite la import.
/// </summary>
public sealed class GeneratorWord : IGeneratorFormular
{
    public byte[] Genereaza(byte[] sablonDocx, TipFormular tip, DateFormular date, MetadateFormular metadate, int randuriGoale = 0)
    {
        using var ms = new MemoryStream();
        ms.Write(sablonDocx, 0, sablonDocx.Length);
        ms.Position = 0;

        // AutoSave implicit: modificările se scriu în stream la Dispose.
        using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
        {
            var main = doc.MainDocumentPart ?? throw new InvalidOperationException("Șablonul nu are document principal.");

            foreach (var radacina in OpenXmlUtil.Radacini(main))
            {
                foreach (var tabel in tip.Tabele)
                {
                    ExpandeazaTabel(radacina, tabel, date, randuriGoale);
                }

                foreach (var sdt in radacina.Descendants<SdtElement>().ToList())
                {
                    var eticheta = OpenXmlUtil.Eticheta(sdt);
                    if (eticheta is null || eticheta.Contains('.', StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (date.Campuri.TryGetValue(eticheta, out var valoare))
                    {
                        OpenXmlUtil.SeteazaText(sdt, valoare);
                    }
                }
            }

            OpenXmlUtil.ScrieProprietate(doc, OpenXmlUtil.PropTip, metadate.CodTip);
            OpenXmlUtil.ScrieProprietate(doc, OpenXmlUtil.PropVersiune, metadate.VersiuneSablon.ToString(CultureInfo.InvariantCulture));
            if (metadate.DocumentId is { } id)
            {
                OpenXmlUtil.ScrieProprietate(doc, OpenXmlUtil.PropDocument, id.ToString());
            }

            if (metadate.NumarRezervat is { } numar)
            {
                OpenXmlUtil.ScrieProprietate(doc, OpenXmlUtil.PropNumar, numar);
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Găsește rândul-șablon al tabelului (rândul cu controale „{tabel}.*”) și îl clonează per linie.
    /// Fără linii: păstrează <paramref name="randuriGoale"/> rânduri goale (formular de completat manual)
    /// sau elimină rândul-șablon.
    /// </summary>
    private static void ExpandeazaTabel(OpenXmlElement radacina, TabelFormular tabel, DateFormular date, int randuriGoale)
    {
        var prefix = tabel.Nume + ".";
        var randSablon = radacina.Descendants<TableRow>()
            .FirstOrDefault(r => r.Descendants<SdtElement>().Any(s => OpenXmlUtil.Eticheta(s)?.StartsWith(prefix, StringComparison.Ordinal) == true));
        if (randSablon?.Parent is not { } parinte)
        {
            return;
        }

        IReadOnlyList<IReadOnlyDictionary<string, string>> linii = date.Tabele.TryGetValue(tabel.Nume, out var l) && l.Count > 0
            ? l
            : Enumerable.Range(0, randuriGoale).Select(_ => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()).ToList();

        foreach (var linie in linii)
        {
            var rand = (TableRow)randSablon.CloneNode(true);

            // Id-urile controalelor trebuie să fie unice în document; la clonare le eliminăm (sunt opționale).
            foreach (var idControl in rand.Descendants<SdtId>().ToList())
            {
                idControl.Remove();
            }

            foreach (var sdt in rand.Descendants<SdtElement>().ToList())
            {
                var eticheta = OpenXmlUtil.Eticheta(sdt);
                if (eticheta is null || !eticheta.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var coloana = eticheta[prefix.Length..];
                OpenXmlUtil.SeteazaText(sdt, linie.TryGetValue(coloana, out var v) ? v : string.Empty);
            }

            parinte.InsertBefore(rand, randSablon);
        }

        randSablon.Remove();
    }
}
