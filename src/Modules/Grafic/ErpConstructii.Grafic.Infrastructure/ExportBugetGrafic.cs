using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ErpConstructii.Formulare;

namespace ErpConstructii.Grafic.Infrastructure;

/// <summary>Raportul „valoare în grafic vs cost ERP” (D-57, etapa 3) ca fișier Excel (arborele cu niveluri de grupare) și ca PDF standard.</summary>
internal static class ExportBugetGrafic
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static string Stadiu(StadiuActivitate? s) => s switch
    {
        StadiuActivitate.Finalizata => "finalizată",
        StadiuActivitate.InCurs => "în curs",
        StadiuActivitate.Neinceputa => "neîncepută",
        _ => string.Empty,
    };

    /// <summary>Rândurile alese pentru tipărire: nodurile până la nivelul dat (null = tot, cu activitățile), opțional doar cele cu alertă.</summary>
    public static IReadOnlyList<RandBugetGrafic> Filtreaza(RaportBugetGrafic r, int? nivel, bool doarAlerte) =>
        r.Randuri.Where(x => (nivel is null || (x.EsteNod && x.Adancime < nivel)) && (!doarAlerte || x.Alerta is not null)).ToList();

    public static byte[] Excel(RaportBugetGrafic r, string grafic, string actualizare, string moneda, DateTimeOffset generat)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new Workbook();
            wb.AddNewPart<WorkbookStylesPart>().Stylesheet = Stiluri();
            var foaie = wb.AddNewPart<WorksheetPart>();
            var date = new SheetData();
            foaie.Worksheet = new Worksheet(
                new SheetProperties(new OutlineProperties { SummaryBelow = false }),
                new SheetFormatProperties { DefaultRowHeight = 15, OutlineLevelRow = 7 },
                new Columns(Latime(1, 1, 24), Latime(2, 2, 60), Latime(3, 3, 11), Latime(4, 12, 15), Latime(13, 13, 10), Latime(14, 14, 45)),
                date);
            wb.Workbook.AppendChild(new Sheets(new Sheet { Id = wb.GetIdOfPart(foaie), SheetId = 1, Name = "Buget vs cost" }));

            uint nr = 0;
            Row Rand(byte nivel = 0)
            {
                var rand = new Row { RowIndex = ++nr };
                if (nivel > 0)
                    rand.OutlineLevel = nivel;
                date.AppendChild(rand);
                return rand;
            }

            Rand().Append(Text($"Valoare în grafic vs cost ERP — {grafic}", 1));
            Rand().Append(Text($"{actualizare} · valori în {moneda} (costurile ERP la cursul BNR din data documentelor) · generat {generat.ToUniversalTime():dd.MM.yyyy HH:mm} UTC"));
            if (r.CostFaraTinta.Lei != 0)
                Rand().Append(Text($"Costuri pe ținte care nu mai sunt în grafic: {r.CostFaraTinta.MonedaGrafic?.ToString("N2", Ro)} {moneda} ({r.CostFaraTinta.Lei.ToString("N2", Ro)} lei) — doar în total."));
            Rand();
            Rand().Append(new[]
            {
                "Cod / WBS", "Denumire", "Stadiu", $"Valoare planificată ({moneda})", "Execuție %", $"Valoare executată ({moneda})", $"Cost ERP ({moneda})",
                "Cost ERP (lei)", $"Abatere ({moneda})", "Indice cost", $"Prognoză la terminare ({moneda})", "Consumat %", "Documente", "Alertă",
            }.Select(t => Text(t, 1)).ToArray());
            void Scrie(RandBugetGrafic x, byte nivel)
            {
                var stil = x.EsteNod ? 1u : 0u;
                var rand = Rand(nivel);
                rand.Append(Text(x.Cod, stil), Text(new string(' ', x.Adancime * 2) + x.Denumire, stil), Text(Stadiu(x.Stadiu), stil),
                    Numar(x.ValoarePlanificata, stil), Numar(x.Procent, stil), Numar(x.ValoareExecutata, stil), Numar(x.CostErp, stil),
                    Numar(x.CostErpLei, stil), Numar(x.Abatere, x.Abatere < 0 ? 4u : stil), Numar(x.IndiceCost, stil, 3), Numar(x.Prognoza, stil),
                    Numar(x.ProcentConsumat, stil), Numar(x.Documente, stil, 0), Text(x.Alerta ?? string.Empty, x.Alerta is null ? 0u : 5u));
            }

            Scrie(r.Total, 0);
            foreach (var x in r.Randuri)
                Scrie(x, (byte)Math.Min(x.Adancime + 1, 7));
            wb.Workbook.Save();
        }

        return ms.ToArray();
    }

    public static (AspectDocument Aspect, DateFormular Date) Pdf(
        RaportBugetGrafic r, IReadOnlyList<RandBugetGrafic> randuri, string grafic, string proiect, string actualizare, string moneda, string selectie,
        DateTimeOffset generat)
    {
        string N(decimal? v, string format = "N2") => v?.ToString(format, Ro) ?? string.Empty;
        var linii = randuri.Select(x => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
        {
            ["cod"] = x.Cod,
            ["denumire"] = new string(' ', Math.Min(x.Adancime, 5) * 2) + (x.EsteNod ? x.Denumire.ToUpper(Ro) : x.Denumire),
            ["plan"] = N(x.ValoarePlanificata),
            ["procent"] = N(x.Procent, "0.##") + "%",
            ["executat"] = N(x.ValoareExecutata),
            ["cost"] = N(x.CostErp),
            ["abatere"] = N(x.Abatere),
            ["indice"] = N(x.IndiceCost, "0.000"),
            ["prognoza"] = N(x.Prognoza),
            ["alerta"] = x.Alerta ?? string.Empty,
        }).ToList();
        var t = r.Total;
        var aspect = new AspectDocument(
            "Valoare în grafic vs cost ERP", null, $"Generat la {generat.ToUniversalTime():dd.MM.yyyy HH:mm} UTC",
            [new("Proiect", "proiect"), new("Grafic", "grafic"), new("Actualizarea", "actualizare"), new("Selecția", "selectie")],
            "linii",
            [
                new("cod", "Cod / WBS", 1.7), new("denumire", "Denumire", 4.4), new("plan", $"Planificat ({moneda})", 2.2, AliniereColoana.Dreapta),
                new("procent", "Exec. %", 1.0, AliniereColoana.Dreapta), new("executat", "Executat", 2.2, AliniereColoana.Dreapta),
                new("cost", "Cost ERP", 2.0, AliniereColoana.Dreapta), new("abatere", "Abatere", 2.2, AliniereColoana.Dreapta),
                new("indice", "Indice", 1.1, AliniereColoana.Dreapta), new("prognoza", "Prognoză", 2.2, AliniereColoana.Dreapta),
                new("alerta", "Alertă", 2.6),
            ],
            [
                new($"Valoare planificată ({moneda})", "t_plan"), new($"Valoare executată ({moneda})", "t_executat"), new($"Cost ERP ({moneda})", "t_cost"),
                new("Cost ERP (lei)", "t_lei"), new($"Abatere ({moneda})", "t_abatere"), new("Indice cost", "t_indice"),
                new($"Prognoză la terminare ({moneda})", "t_prognoza"),
            ],
            [], "observatii", Peisaj: true);
        var campuri = new Dictionary<string, string>
        {
            ["proiect"] = proiect,
            ["grafic"] = grafic,
            ["actualizare"] = actualizare,
            ["selectie"] = selectie,
            ["t_plan"] = N(t.ValoarePlanificata),
            ["t_executat"] = $"{N(t.ValoareExecutata)} ({N(t.Procent, "0.##")}%)",
            ["t_cost"] = N(t.CostErp) + (t.FaraCurs > 0 ? $" (fără {t.FaraCurs} alocări fără curs BNR)" : string.Empty),
            ["t_lei"] = N(t.CostErpLei),
            ["t_abatere"] = N(t.Abatere),
            ["t_indice"] = N(t.IndiceCost, "0.000"),
            ["t_prognoza"] = N(t.Prognoza),
            ["observatii"] = "Valoarea executată = valoarea planificată × procentul de execuție din grafic. Abaterea = valoarea executată − costul ERP " +
                             "(negativ = cost peste valoarea lucrărilor executate). Indicele de cost = valoarea executată / costul ERP. Prognoza la terminare = " +
                             "costul ERP + valoarea rămasă de executat. Costurile ERP: facturi (fără materialele de stoc) și bonuri de consum alocate, la cursul " +
                             $"BNR din data documentelor. Alertă: cost peste valoarea executată cu mai mult de {r.PragAlerta * 100:0}%." +
                             (r.CostFaraTinta.Lei != 0 ? $" Costuri pe ținte care nu mai sunt în grafic: {N(r.CostFaraTinta.MonedaGrafic)} {moneda} (doar în total)." : string.Empty),
        };
        return (aspect, new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = linii }));
    }

    private static Column Latime(uint de, uint panaLa, double latime) => new() { Min = de, Max = panaLa, Width = latime, CustomWidth = true };

    private static Cell Text(string text, uint stil = 0) =>
        new() { DataType = CellValues.InlineString, InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(text ?? string.Empty)), StyleIndex = stil };

    private static Cell Numar(decimal? v, uint stil, int zecimale = 2) => v is not { } x
        ? new Cell()
        : new Cell
        {
            DataType = CellValues.Number,
            CellValue = new CellValue(Math.Round(x, zecimale, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)),
            StyleIndex = stil switch { 1 => 3u, 4 => 4u, _ => 2u },
        };

    /// <summary>Stiluri: 0 normal, 1 aldin, 2 număr, 3 număr aldin, 4 număr roșu (abatere negativă), 5 text roșu (alertă).</summary>
    private static Stylesheet Stiluri() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00" }) { Count = 1 },
        new Fonts(new Font(), new Font(new Bold()), new Font(new Color { Rgb = "FFC00000" })) { Count = 3 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, ApplyFont = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true, FontId = 1, ApplyFont = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true, FontId = 2, ApplyFont = true },
            new CellFormat { FontId = 2, ApplyFont = true }) { Count = 6 },
        new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
}
