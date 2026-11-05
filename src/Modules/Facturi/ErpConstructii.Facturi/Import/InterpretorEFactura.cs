using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ErpConstructii.SharedKernel;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Facturi.Import;

/// <summary>
/// Citește XML-ul e-Factura (UBL 2.1, CIUS-RO): factură (Invoice) sau notă de credit (CreditNote, storno).
/// Totul e sigur: XML-ul e documentul fiscal oficial (D-11). Funcție pură; arhiva ZIP din SPV se despachetează în infrastructură.
/// </summary>
public static class InterpretorEFactura
{
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    /// <summary>Codurile UN/ECE (Recomandarea 20) uzuale în facturile de construcții → UM afișată.</summary>
    private static readonly Dictionary<string, string> Unitati = new(StringComparer.OrdinalIgnoreCase)
    {
        ["H87"] = "buc", ["C62"] = "buc", ["EA"] = "buc", ["NAR"] = "buc", ["XPP"] = "buc",
        ["KGM"] = "kg", ["TNE"] = "t", ["GRM"] = "g", ["MTR"] = "m", ["MTK"] = "mp", ["MTQ"] = "mc",
        ["LTR"] = "l", ["SET"] = "set", ["PR"] = "per", ["XBX"] = "cut", ["XRO"] = "rola", ["XSA"] = "sac",
        ["XPK"] = "pac", ["XPX"] = "palet", ["HUR"] = "ora", ["DAY"] = "zi", ["MON"] = "luna", ["KWH"] = "kWh",
        ["KMT"] = "km", ["LS"] = "forfetar",
    };

    /// <summary>UM afișată pentru un cod UN/ECE (ex. H87 → buc); codurile necunoscute rămân ca atare.</summary>
    internal static string UnitateDinCod(string cod)
    {
        var c = cod.Trim().Trim('"', '“', '”', '„', '\'');
        // OCR: „HB7” / „HS7” / „H 87” în loc de H87 (cel mai frecvent cod: bucată).
        if (c.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant() is "HB7" or "HS7" or "H8" or "HBT" or "H87")
        {
            c = "H87";
        }

        return Unitati.GetValueOrDefault(c, c);
    }

    /// <summary>Recunoaște un XML e-Factura după elementul rădăcină.</summary>
    public static bool EsteEFactura(byte[] continut)
    {
        try
        {
            using var r = XmlReader.Create(new MemoryStream(continut), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            r.MoveToContent();
            return r.LocalName is "Invoice" or "CreditNote" && r.NamespaceURI.Contains("ubl:schema:xsd", StringComparison.Ordinal);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public static RezultatImport<FacturaCitita> Interpreteaza(byte[] continut)
    {
        XDocument doc;
        try
        {
            // Fără DTD (protecție XXE): XML-ul vine din afara sistemului.
            using var r = XmlReader.Create(new MemoryStream(continut), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            doc = XDocument.Load(r);
        }
        catch (XmlException)
        {
            return RezultatImport<FacturaCitita>.Esuat("Fișierul XML nu este valid.");
        }

        var rad = doc.Root;
        if (rad is null || rad.Name.LocalName is not ("Invoice" or "CreditNote"))
        {
            return RezultatImport<FacturaCitita>.Esuat("Fișierul XML nu este o factură e-Factura (UBL Invoice / CreditNote).");
        }

        var esteNotaCredit = rad.Name.LocalName == "CreditNote";
        var probleme = new List<ProblemaImport>();
        var moneda = Text(rad, Cbc + "DocumentCurrencyCode") ?? "RON";

        var furnizor = rad.Element(Cac + "AccountingSupplierParty")?.Element(Cac + "Party");
        var cumparator = rad.Element(Cac + "AccountingCustomerParty")?.Element(Cac + "Party");

        var semn = esteNotaCredit ? -1m : 1m;
        var linii = rad.Elements(Cac + (esteNotaCredit ? "CreditNoteLine" : "InvoiceLine"))
            .Select((l, i) => Linie(l, i + 1, esteNotaCredit, semn, probleme))
            .ToList();

        var totaluri = rad.Element(Cac + "LegalMonetaryTotal");
        var tvaTotal = rad.Elements(Cac + "TaxTotal")
            .Select(t => t.Element(Cbc + "TaxAmount"))
            .FirstOrDefault(a => a is not null && (string?)a.Attribute("currencyID") is var c && (c is null || c == moneda));

        var scadenta = Data(Text(rad, Cbc + "DueDate"))
            ?? Data(rad.Elements(Cac + "PaymentMeans").Select(p => Text(p, Cbc + "PaymentDueDate")).FirstOrDefault(d => d is not null));
        var referinta = Text(rad.Element(Cac + "OrderReference"), Cbc + "ID")
            ?? string.Join(" ", rad.Elements(Cbc + "Note").Select(n => n.Value.Trim()).Where(n => n.Length > 0)).NullDacaGol();

        if (rad.Descendants(Cac + "ClassifiedTaxCategory").Any(c => Text(c, Cbc + "ID") == "AE"))
        {
            probleme.Add(new ProblemaImport(Severitate.Informare, "TVA", "Factura are taxare inversă (cod AE): TVA-ul se înregistrează de cumpărător."));
        }

        var numar = Text(rad, Cbc + "ID");
        var data = Data(Text(rad, Cbc + "IssueDate"));
        return RezultatImport<FacturaCitita>.Ok(new FacturaCitita(
            SursaFactura.EFactura,
            Sigur(numar),
            data is { } d ? new CampCitit<DateOnly>(d, Certitudine.Sigur) : CampCitit<DateOnly>.Lipsa(),
            scadenta is { } s ? new CampCitit<DateOnly>(s, Certitudine.Sigur) : CampCitit<DateOnly>.Lipsa(),
            Sigur(moneda),
            Sigur(NumeParte(furnizor)),
            Sigur(CuiParte(furnizor)),
            Sigur(CuiParte(cumparator)),
            Suma(totaluri, "TaxExclusiveAmount", semn),
            tvaTotal is not null && Numar(tvaTotal.Value) is { } tva ? new CampCitit<decimal>(tva * semn, Certitudine.Sigur) : CampCitit<decimal>.Lipsa(),
            Suma(totaluri, "PayableAmount", semn),
            linii,
            referinta,
            probleme), probleme);
    }

    private static LinieFacturaCitita Linie(XElement l, int rand, bool esteNotaCredit, decimal semn, List<ProblemaImport> probleme)
    {
        var cantitateEl = l.Element(Cbc + (esteNotaCredit ? "CreditedQuantity" : "InvoicedQuantity"));
        var cod = (string?)cantitateEl?.Attribute("unitCode");
        var um = cod is null ? null : UnitateDinCod(cod);
        var item = l.Element(Cac + "Item");
        var pret = l.Element(Cac + "Price");
        var bazaPret = Numar(Text(pret, Cbc + "BaseQuantity")) is { } b and > 0 ? b : 1m;
        var procent = Numar(Text(item?.Element(Cac + "ClassifiedTaxCategory"), Cbc + "Percent"));

        var cantitate = Numar(cantitateEl?.Value);
        var valoare = Numar(Text(l, Cbc + "LineExtensionAmount"));
        if (cantitate is null || valoare is null)
        {
            probleme.Add(new ProblemaImport(Severitate.Avertisment, $"linia {rand}", "Linia nu are cantitate sau valoare în XML."));
        }

        // La storno (CreditNote) cantitățile și valorile sunt pozitive în XML; în ERP le înregistrăm negative.
        return new LinieFacturaCitita(
            rand,
            Sigur(Text(item, Cbc + "Name") ?? Text(item, Cbc + "Description")),
            Sigur(um),
            cantitate is { } c ? new CampCitit<decimal>(c * semn, Certitudine.Sigur) : CampCitit<decimal>.Lipsa(),
            Numar(Text(pret, Cbc + "PriceAmount")) is { } p ? new CampCitit<decimal>(p / bazaPret, Certitudine.Sigur) : CampCitit<decimal>.Lipsa(),
            valoare is { } v ? new CampCitit<decimal>(v * semn, Certitudine.Sigur) : CampCitit<decimal>.Lipsa(),
            procent is { } pr ? new CampCitit<decimal>(pr / 100m, Certitudine.Sigur) : CampCitit<decimal>.Lipsa());
    }

    private static string? NumeParte(XElement? parte) =>
        Text(parte?.Element(Cac + "PartyLegalEntity"), Cbc + "RegistrationName")
        ?? Text(parte?.Element(Cac + "PartyName"), Cbc + "Name");

    /// <summary>CUI-ul (fără „RO”): din PartyTaxScheme (plătitor TVA) sau din PartyLegalEntity.</summary>
    private static string? CuiParte(XElement? parte)
    {
        var cod = parte?.Elements(Cac + "PartyTaxScheme").Select(t => Text(t, Cbc + "CompanyID")).FirstOrDefault(c => c is not null)
                  ?? Text(parte?.Element(Cac + "PartyLegalEntity"), Cbc + "CompanyID");
        return cod is null ? null : ValidareFiscala.NormalizeazaCui(cod);
    }

    private static CampCitit<decimal> Suma(XElement? parinte, string element, decimal semn) =>
        Numar(Text(parinte, Cbc + element)) is { } v ? new CampCitit<decimal>(v * semn, Certitudine.Sigur) : CampCitit<decimal>.Lipsa();

    private static CampCitit<string> Sigur(string? valoare) =>
        string.IsNullOrWhiteSpace(valoare) ? CampCitit<string>.Lipsa() : new CampCitit<string>(valoare.Trim(), Certitudine.Sigur);

    private static string? Text(XElement? parinte, XName nume) =>
        parinte?.Element(nume)?.Value is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>Numerele din UBL folosesc punctul zecimal (xsd:decimal).</summary>
    private static decimal? Numar(string? text) =>
        decimal.TryParse(text?.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateOnly? Data(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string? NullDacaGol(this string s) => s.Length == 0 ? null : s;
}
