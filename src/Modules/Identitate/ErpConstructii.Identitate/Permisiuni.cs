namespace ErpConstructii.Identitate;

/// <summary>Modulele funcționale pe care se dau drepturi (coloanele matricei rol × modul).</summary>
public enum ModulErp
{
    Proiecte,
    Ofertare,
    Contracte,
    Buget,
    Santier,
    Situatii,
    Aprovizionare,
    Utilaje,
    Financiar,
    HrSalarii,
    Administrare,
}

/// <summary>Acțiunile standard asupra unei resurse (REQ-DRP-01).</summary>
public enum Actiune
{
    Vizualizare,
    Creare,
    Editare,
    Anulare,
    Aprobare,
    Export,
}

/// <summary>
/// Nivelul de acces dintr-o celulă a matricei rol × modul.
/// Aprobare include Editare, Editare include Vizualizare.
/// </summary>
public enum NivelAcces
{
    Fara = 0,
    Vizualizare = 1,
    Editare = 2,
    Aprobare = 3,
}

/// <summary>
/// Permisiunile sunt șiruri „modul.actiune” (ex. <c>situatii.aprobare</c>), stocate în baza de date
/// pe rol. Pe lângă acestea există permisiuni pentru câmpuri sensibile, verificate la serializare:
/// fără ele, API-ul trimite valorile mascate (REQ-DRP-04).
/// </summary>
public static class Permisiuni
{
    public const string PreturiCost = "date.preturi_cost";
    public const string Salarii = "date.salarii";
    public const string DatePersonale = "date.personale";

    public static string Pentru(ModulErp modul, Actiune actiune) =>
        $"{Nume(modul)}.{Nume(actiune)}";

    public static IEnumerable<string> DinNivel(ModulErp modul, NivelAcces nivel)
    {
        if (nivel >= NivelAcces.Vizualizare)
        {
            yield return Pentru(modul, Actiune.Vizualizare);
            yield return Pentru(modul, Actiune.Export);
        }

        if (nivel >= NivelAcces.Editare)
        {
            yield return Pentru(modul, Actiune.Creare);
            yield return Pentru(modul, Actiune.Editare);
            yield return Pentru(modul, Actiune.Anulare);
        }

        if (nivel >= NivelAcces.Aprobare)
        {
            yield return Pentru(modul, Actiune.Aprobare);
        }
    }

    private static string Nume(ModulErp modul) => modul switch
    {
        ModulErp.HrSalarii => "hr",
        _ => modul.ToString().ToLowerInvariant(),
    };

    private static string Nume(Actiune actiune) => actiune.ToString().ToLowerInvariant();
}
