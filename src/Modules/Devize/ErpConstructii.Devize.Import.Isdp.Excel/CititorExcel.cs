using System.Text;
using ExcelDataReader;

namespace ErpConstructii.Devize.Import.Isdp.Excel;

/// <summary>
/// Transformă un fișier .xls / .xlsx în foi de calcul (<see cref="FoaieCalcul"/>), fără interpretare.
/// Valorile numerice vin ca <see cref="double"/>, textele ca <see cref="string"/>; celulele îmbinate au
/// valoarea doar în colțul stânga-sus (exact ce așteaptă parserele).
///
/// NEVERIFICAT în mediul de generare (fără nuget.org). Test de acceptanță în Claude Code: citirea
/// fișierelor reale din tests/samples-private/isdp/ trebuie să dea aceleași rezultate ca fixture-urile
/// JSON din tests/samples/isdp/ (vezi TesteCititorExcel în docs/import-isdp.md).
/// </summary>
public static class CititorExcel
{
    static CititorExcel()
    {
        // Fișierele .xls (BIFF) folosesc code page 1252; în .NET trebuie înregistrat furnizorul.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<FoaieCalcul> Citeste(byte[] continut)
    {
        using var ms = new MemoryStream(continut, writable: false);
        using var cititor = ExcelReaderFactory.CreateReader(ms);
        var foi = new List<FoaieCalcul>();
        do
        {
            var randuri = new List<IReadOnlyList<object?>>();
            while (cititor.Read())
            {
                var rand = new object?[cititor.FieldCount];
                for (var c = 0; c < cititor.FieldCount; c++)
                {
                    rand[c] = cititor.GetValue(c);
                }

                randuri.Add(rand);
            }

            foi.Add(new FoaieCalcul(cititor.Name, randuri));
        }
        while (cititor.NextResult());

        return foi;
    }
}
