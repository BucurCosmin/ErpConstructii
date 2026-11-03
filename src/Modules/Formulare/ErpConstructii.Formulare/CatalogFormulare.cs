namespace ErpConstructii.Formulare;

/// <summary>
/// Catalogul standard de formulare (documentul de proiect, secțiunea „Formulare și documente Word”).
/// Doar „comanda-furnizor” are schema completă, ca model; celelalte tipuri sunt declarate cu câmpurile
/// comune și se completează când se implementează modulul lor (TODO marcat la fiecare).
/// </summary>
public static class CatalogFormulare
{
    private static readonly CampFormular[] CampuriComune =
    [
        new("numar", "Număr", Obligatoriu: true),
        new("data", "Data", TipCamp.Data, Obligatoriu: true),
        new("proiect_cod", "Cod proiect", Obligatoriu: true),
        new("proiect_denumire", "Proiect"),
        new("intocmit_de", "Întocmit de"),
    ];

    public static readonly TipFormular ComandaFurnizor = new(
        Cod: "comanda-furnizor",
        Denumire: "Comandă către furnizor",
        ModulErp: "Aprovizionare",
        Campuri:
        [
            .. CampuriComune,
            new("furnizor_denumire", "Furnizor", Obligatoriu: true),
            new("furnizor_cui", "CUI furnizor", Obligatoriu: true),
            new("termen_livrare", "Termen de livrare", TipCamp.Data),
            new("adresa_livrare", "Adresa de livrare (șantier)"),
            new("conditii_plata", "Condiții de plată"),
            new("total_fara_tva", "Total fără TVA", TipCamp.Suma),
            new("observatii", "Observații"),
        ],
        Tabele:
        [
            new TabelFormular("linii",
            [
                new("cod_articol", "Cod articol"),
                new("denumire", "Denumire", Obligatoriu: true),
                new("um", "UM", Obligatoriu: true),
                new("cantitate", "Cantitate", TipCamp.Numar, Obligatoriu: true),
                new("pret_unitar", "Preț unitar", TipCamp.Suma),
                new("valoare", "Valoare", TipCamp.Suma),
                new("articol_deviz", "Articol deviz", Obligatoriu: true),
            ], CelPutinOLinie: true),
        ],
        SePoateImporta: true);

    /// <summary>Restul catalogului; câmpurile specifice se adaugă la implementarea fiecărui modul.</summary>
    public static readonly IReadOnlyList<TipFormular> Toate =
    [
        ComandaFurnizor,
        Declarat("referat-necesitate", "Referat de necesitate / cerere materiale", "Aprovizionare", true),
        Declarat("cerere-oferta", "Cerere de ofertă către furnizori", "Aprovizionare", false),
        Declarat("comanda-subantreprenor", "Comandă către subantreprenor", "Subantreprenori", true),
        Declarat("contract", "Contract / act adițional", "Contracte", true),
        Declarat("nir", "Notă de recepție (NIR)", "Gestiuni", true),
        Declarat("bon-consum", "Bon de consum", "Gestiuni", true),
        Declarat("aviz-transfer", "Aviz de transfer între gestiuni", "Gestiuni", true),
        Declarat("pv-lucrari-ascunse", "Proces-verbal de lucrări ascunse", "Executie", true),
        Declarat("pv-predare-amplasament", "Proces-verbal de predare-primire amplasament", "Executie", true),
        Declarat("pv-receptie", "Proces-verbal de recepție (terminare / finală)", "Executie", true),
        Declarat("dispozitie-santier", "Dispoziție de șantier", "Executie", true),
        Declarat("raport-zilnic", "Raport zilnic / jurnal de șantier", "Executie", true),
        Declarat("foaie-utilaj", "Foaie de parcurs / foaie de lucru utilaj", "Utilaje", true),
        Declarat("situatie-lucrari", "Situație de lucrări cu anexe", "Situatii", false),
        Declarat("oferta-beneficiar", "Ofertă către beneficiar", "Ofertare", false),
    ];

    public static TipFormular? DupaCod(string cod) => Toate.FirstOrDefault(t => t.Cod == cod);

    // TODO: câmpurile specifice ale fiecărui tip, la implementarea modulului respectiv.
    private static TipFormular Declarat(string cod, string denumire, string modul, bool importabil) =>
        new(cod, denumire, modul, CampuriComune, [], importabil);
}
