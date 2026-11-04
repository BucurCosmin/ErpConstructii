namespace ErpConstructii.Formulare;

/// <summary>
/// Catalogul standard de formulare (documentul de proiect, secțiunea „Formulare și documente Word”).
/// Comenzile către furnizori și facturile primite au scheme explicite; celelalte tipuri se completează
/// când se implementează modulul lor (TODO marcat la fiecare).
/// </summary>
public static class CatalogFormulare
{
    private static readonly CampFormular[] CampuriComune =
    [
        new("numar", "Număr", Obligatoriu: true),
        new("data", "Data", TipCamp.Data, Obligatoriu: true),
        new("proiect_denumire", "Proiect"),
        new("intocmit_de", "Întocmit de"),
    ];

    public static readonly TipFormular NecesarMateriale = new(
        Cod: "necesar-materiale",
        Denumire: "Necesar de materiale",
        ModulErp: "Aprovizionare",
        Campuri:
        [
            .. CampuriComune,
            new("proiect_cod", "Cod proiect"),
            new("lucrare", "Lucrare", Obligatoriu: true),
            new("compartiment_santier", "Compartiment / șantier", Obligatoriu: true),
        ],
        Tabele:
        [
            new TabelFormular("linii",
            [
                new("numar_crt", "Nr. crt."),
                new("denumire", "Denumire", Obligatoriu: true),
                new("um", "UM", Obligatoriu: true),
                new("cantitate", "Cantitate", TipCamp.Numar, Obligatoriu: true),
                new("observatii", "Observații"),
                new("articol_deviz", "Articol deviz"),
            ], CelPutinOLinie: true),
        ],
        SePoateImporta: true);

    public static readonly TipFormular FacturaPrimita = new(
        Cod: "factura-primita",
        Denumire: "Factură primită de la furnizor",
        ModulErp: "Aprovizionare",
        Campuri:
        [
            .. CampuriComune,
            new("proiect_cod", "Cod proiect", Obligatoriu: true),
            new("furnizor_denumire", "Furnizor", Obligatoriu: true),
            new("furnizor_cui", "CUI furnizor"),
            new("furnizor_nr_reg_com", "Nr. Registrul Comerțului furnizor"),
            new("furnizor_adresa", "Adresa furnizorului"),
            new("furnizor_telefon", "Telefon furnizor"),
            new("furnizor_email", "E-mail furnizor"),
            new("furnizor_banca", "Banca furnizorului"),
            new("furnizor_iban", "IBAN furnizor"),
            new("data_scadenta", "Data scadentă", TipCamp.Data),
            new("moneda", "Monedă"),
            new("comanda_numar", "Număr comandă"),
            new("total_fara_tva", "Total fără TVA", TipCamp.Suma),
            new("total_tva", "Total TVA", TipCamp.Suma),
            new("total_cu_tva", "Total de plată", TipCamp.Suma, Obligatoriu: true),
            new("observatii", "Observații"),
        ],
        Tabele:
        [
            new TabelFormular("linii",
            [
                new("cod_articol", "Cod articol"),
                new("denumire", "Denumire", Obligatoriu: true),
                new("um", "UM"),
                new("cantitate", "Cantitate", TipCamp.Numar),
                new("pret_unitar", "Preț unitar", TipCamp.Suma),
                new("valoare", "Valoare", TipCamp.Suma),
                new("cota_tva", "Cotă TVA", TipCamp.Numar),
                new("valoare_tva", "Valoare TVA", TipCamp.Suma),
                new("articol_deviz", "Articol deviz"),
            ]),
        ],
        SePoateImporta: true);

    /// <summary>Restul catalogului; câmpurile specifice se adaugă la implementarea fiecărui modul.</summary>
    public static readonly IReadOnlyList<TipFormular> Toate =
    [
        NecesarMateriale,
        FacturaPrimita,
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
