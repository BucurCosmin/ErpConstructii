using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Devize.Infrastructure;

/// <summary>Un rând al devizului pentru tipărire / export: capitolele (cu adâncimea lor) și articolele, în ordinea arborelui.</summary>
internal sealed record RandExport(int Adancime, bool EsteCapitol, string Cod, string Simbol, string Denumire, string UnitateMasura, decimal? Cantitate,
    ValoriResurse? PretUnitar, ValoriResurse Valoare, decimal? PretOferta, decimal? ValoareOferta);

/// <summary>Devizul ca rânduri plate (tipărire, Excel) și fișierul .xlsx al lui (WBS, simbol, prețuri pe componente, valori, ofertă, recapitulație).</summary>
internal static class ExportDeviz
{
    public static List<RandExport> Randuri(VersiuneDeviz v)
    {
        var p = v.Parametri();
        var randuri = new List<RandExport>();

        (ValoriResurse Total, decimal? Oferta) Capitol(CapitolDeviz c, int adancime)
        {
            var index = randuri.Count;
            randuri.Add(null!); // rândul capitolului, completat după copii (cu totalul lor)
            var total = ValoriResurse.Zero;
            decimal? oferta = null;
            foreach (var a in c.Articole.OrderBy(x => x.Ordine))
            {
                var valoare = CalculatorDeviz.ValoareLinie(a.CaLinie(), p.RotunjireLinie);
                total += valoare;
                if (a.ArePretOferta)
                    oferta = (oferta ?? 0) + a.ValoareOferta.Total;
                randuri.Add(new RandExport(adancime + 1, false, a.CodWbsSursa ?? string.Empty, a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretUnitar,
                    valoare, a.ArePretOferta ? a.PretOfertaUnitar.Total : null, a.ArePretOferta ? a.ValoareOferta.Total : null));
            }

            foreach (var s in v.Copii(c.Id))
            {
                var (t, o) = Capitol(s, adancime + 1);
                total += t;
                if (o is { } x)
                    oferta = (oferta ?? 0) + x;
            }

            randuri[index] = new RandExport(adancime, true, c.CodWbs ?? c.Cod, string.Empty, c.Denumire, string.Empty, null, null, total, null, oferta);
            return (total, oferta);
        }

        foreach (var c in v.Radacini)
            Capitol(c, 0);
        return randuri;
    }

    public static byte[] Excel(VersiuneDeviz v, string proiect)
    {
        var randuri = Randuri(v);
        var r = v.Recapitulatie();
        var p = v.Parametri();
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new Workbook();
            var stiluri = wb.AddNewPart<WorkbookStylesPart>();
            stiluri.Stylesheet = Stiluri();
            var foaie = wb.AddNewPart<WorksheetPart>();
            var date = new SheetData();
            foaie.Worksheet = new Worksheet(
                new Columns(
                    Latime(1, 1, 18), Latime(2, 2, 14), Latime(3, 3, 60), Latime(4, 4, 8), Latime(5, 5, 12), Latime(6, 11, 13), Latime(12, 12, 15),
                    Latime(13, 14, 15)),
                date);
            wb.Workbook.AppendChild(new Sheets(new Sheet { Id = wb.GetIdOfPart(foaie), SheetId = 1, Name = "Deviz" }));

            uint nr = 0;
            Row Rand() { var rand = new Row { RowIndex = ++nr }; date.AppendChild(rand); return rand; }

            Rand().Append(Text(v.Denumire, 1));
            var stare = v.Stare switch { StareVersiuneDeviz.Ciorna => "ciornă", StareVersiuneDeviz.Aprobata => "aprobată", _ => "înlocuită" };
            var curs = v.Curs is { } c ? $" (curs {c.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("ro-RO"))})" : string.Empty;
            Rand().Append(Text($"{(string.IsNullOrWhiteSpace(proiect) ? string.Empty : $"Obiectiv: {proiect} · ")}{TipText(v.Tip)} nr. {v.Numar} · {stare} · costuri directe în lei, ofertă în {v.Moneda}{curs}"));
            Rand();
            Rand().Append(new[] { "WBS / cod", "Simbol", "Denumire", "UM", "Cantitate", "PU material", "PU manoperă", "PU utilaj", "PU transport", "PU echipament",
                "PU total", "Valoare", $"PU ofertă ({v.Moneda})", $"Valoare ofertă ({v.Moneda})" }.Select(t => Text(t, 1)).ToArray());
            foreach (var x in randuri)
            {
                var stil = x.EsteCapitol ? 1u : 0u;
                var rand = Rand();
                rand.Append(Text(x.Cod, stil), Text(x.Simbol, stil), Text(new string(' ', x.Adancime * 2) + x.Denumire, stil), Text(x.UnitateMasura, stil));
                rand.Append(Numar(x.Cantitate, 3), Numar(x.PretUnitar?.Material), Numar(x.PretUnitar?.Manopera), Numar(x.PretUnitar?.Utilaj),
                    Numar(x.PretUnitar?.Transport), Numar(x.PretUnitar?.Echipament), Numar(x.PretUnitar?.Total), Numar(x.Valoare.Total, 2, stil),
                    Numar(x.PretOferta), Numar(x.ValoareOferta, 2, stil));
            }

            Rand();
            void Linie(string eticheta, decimal valoare) { var rand = Rand(); rand.Append(Text(string.Empty), Text(string.Empty), Text(eticheta, 1)); rand.Append(Enumerable.Range(0, 8).Select(_ => Text(string.Empty)).ToArray()); rand.Append(Numar(valoare, 2, 1)); }
            Linie("Material", r.CheltuieliDirecteResurse.Material);
            Linie("Manoperă", r.CheltuieliDirecteResurse.Manopera);
            Linie("Utilaj", r.CheltuieliDirecteResurse.Utilaj);
            Linie("Transport", r.CheltuieliDirecteResurse.Transport);
            if (r.CheltuieliDirecteResurse.Echipament != 0)
                Linie("Echipament", r.CheltuieliDirecteResurse.Echipament);
            Linie("Total cheltuieli directe pe resurse", r.CheltuieliDirecteResurse.Total);
            Linie($"Contribuții manoperă (CAM) {p.CotaContributiiManopera:P2}", r.ContributiiManopera);
            Linie("Total cheltuieli directe", r.TotalCheltuieliDirecte);
            Linie($"Cheltuieli indirecte {p.CotaCheltuieliIndirecte:P2}", r.CheltuieliIndirecte);
            Linie($"Profit {p.CotaProfit:P2}", r.Profit);
            Linie("Total fără TVA", r.TotalFaraTva);
            Linie($"TVA {p.CotaTva:P2}", r.Tva);
            Linie("TOTAL CU TVA", r.TotalCuTva);
            wb.Workbook.Save();
        }

        return ms.ToArray();
    }

    public static string TipText(TipVersiuneDeviz tip) => tip switch
    {
        TipVersiuneDeviz.Oferta => "Ofertă",
        TipVersiuneDeviz.Contract => "Contract",
        TipVersiuneDeviz.BugetExecutie => "Buget de execuție",
        _ => "Act adițional",
    };

    private static Column Latime(uint de, uint panaLa, double latime) => new() { Min = de, Max = panaLa, Width = latime, CustomWidth = true };

    private static Cell Text(string text, uint stil = 0) =>
        new() { DataType = CellValues.InlineString, InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(text ?? string.Empty)), StyleIndex = stil };

    private static Cell Numar(decimal? v, int zecimale = 4, uint stil = 0) => v is not { } x
        ? new Cell()
        : new Cell
        {
            DataType = CellValues.Number,
            CellValue = new CellValue(Math.Round(x, zecimale, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            StyleIndex = stil == 1 ? 3u : 2u,
        };

    /// <summary>Stiluri: 0 normal, 1 aldin, 2 număr #,##0.00##, 3 număr aldin.</summary>
    private static Stylesheet Stiluri() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00##" }) { Count = 1 },
        new Fonts(new Font(), new Font(new Bold())) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, ApplyFont = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true, FontId = 1, ApplyFont = true }) { Count = 4 });
}
