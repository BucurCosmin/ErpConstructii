using System.Globalization;
using ErpConstructii.SharedKernel.Import;

namespace ErpConstructii.Formulare;

/// <summary>
/// Validează datele unui formular față de schema tipului (REQ-FRM-08, pasul „Validare” la import).
/// Valorile vin ca text din Word, scrise de oameni: acceptăm formatele românești uzuale.
/// </summary>
public static class ValidatorDateFormular
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");
    private static readonly string[] FormateData = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy"];

    public static IReadOnlyList<ProblemaImport> Valideaza(TipFormular tip, DateFormular date)
    {
        var probleme = new List<ProblemaImport>();
        if (date.Campuri is null || date.Tabele is null)
        {
            return [new(Severitate.Eroare, "formular", "Lipsesc câmpurile sau tabelele formularului.")];
        }

        foreach (var camp in tip.Campuri)
        {
            date.Campuri.TryGetValue(camp.Nume, out var valoare);
            ValideazaCamp(camp, valoare, camp.Eticheta, probleme);
        }

        foreach (var tabel in tip.Tabele)
        {
            var linii = date.Tabele.TryGetValue(tabel.Nume, out var l) && l is not null ? l : [];
            if (tabel.CelPutinOLinie && linii.Count == 0)
            {
                probleme.Add(new(Severitate.Eroare, tabel.Nume, "Tabelul nu are nicio linie completată."));
            }

            for (var i = 0; i < linii.Count; i++)
            {
                if (linii[i] is null)
                {
                    probleme.Add(new(Severitate.Eroare, $"{tabel.Nume}, linia {i + 1}", "Linia formularului este invalidă."));
                    continue;
                }

                foreach (var coloana in tabel.Coloane)
                {
                    linii[i].TryGetValue(coloana.Nume, out var valoare);
                    ValideazaCamp(coloana, valoare, $"{tabel.Nume}, linia {i + 1}, {coloana.Eticheta}", probleme);
                }
            }
        }

        return probleme;
    }

    /// <summary>
    /// Citește un număr scris de un om. Reguli (în ordine):
    ///  - are virgulă → format românesc: virgula e zecimală, punctul separă miile („1.234,56”);
    ///  - are un singur punct urmat de alt număr de cifre decât 3 → punct zecimal („1234.56”);
    ///  - altfel punctele separă miile („1.500” = 1500).
    /// ATENȚIE: decimal.TryParse cu ro-RO acceptă „1234.56” ca 123456 (punctul ca separator de mii),
    /// de aceea nu ne bazăm doar pe cultură.
    /// </summary>
    public static bool IncearcaNumar(string text, out decimal valoare)
    {
        var curat = text
            .Replace("lei", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (curat.Contains(',', StringComparison.Ordinal))
        {
            return decimal.TryParse(curat, NumberStyles.Number, Ro, out valoare);
        }

        var puncte = curat.Count(c => c == '.');
        if (puncte == 1 && curat.Length - curat.IndexOf('.', StringComparison.Ordinal) - 1 != 3)
        {
            return decimal.TryParse(curat, NumberStyles.Number, CultureInfo.InvariantCulture, out valoare);
        }

        return decimal.TryParse(curat, NumberStyles.Number, Ro, out valoare);
    }

    public static bool IncearcaData(string text, out DateOnly data) =>
        DateOnly.TryParseExact(text.Trim(), FormateData, Ro, DateTimeStyles.None, out data);

    private static void ValideazaCamp(CampFormular camp, string? valoare, string locatie, List<ProblemaImport> probleme)
    {
        if (string.IsNullOrWhiteSpace(valoare))
        {
            if (camp.Obligatoriu)
            {
                probleme.Add(new(Severitate.Eroare, locatie, "Câmp obligatoriu necompletat."));
            }

            return;
        }

        var ok = camp.Tip switch
        {
            TipCamp.Numar or TipCamp.Suma => IncearcaNumar(valoare, out _),
            TipCamp.Data => IncearcaData(valoare, out _),
            _ => true,
        };

        if (!ok)
        {
            probleme.Add(new(Severitate.Eroare, locatie, $"Valoare invalidă pentru {camp.Tip}: „{valoare}”."));
        }
    }
}
