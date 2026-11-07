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

    /// <summary>
    /// Nota de comandă către furnizor (formularul GC 007): antet Beneficiar | COMANDA nr. / dată, lucrare | Furnizor,
    /// condițiile bifate (☐ / ☒ în controalele „bifa_*”), tabelul de materiale cu termen și semnăturile.
    /// </summary>
    public static readonly TipFormular NotaComanda = new(
        Cod: "nota-comanda",
        Denumire: "Notă de comandă către furnizor (GC 007)",
        ModulErp: "Aprovizionare",
        Campuri:
        [
            .. CampuriComune,
            new("firma_denumire", "Beneficiar (firma)"),
            new("firma_adresa", "Adresa firmei"),
            new("firma_cui", "CUI firmă"),
            new("firma_nr_reg_com", "Nr. Reg. Com. firmă"),
            new("firma_cont", "Contul bancar al firmei"),
            new("firma_telefon", "Telefon firmă"),
            new("firma_fax", "Fax firmă"),
            new("firma_email", "E-mail firmă"),
            new("proiect_cod", "Cod proiect"),
            new("lucrare", "Obiect / lucrare"),
            new("furnizor_denumire", "Furnizor", Obligatoriu: true),
            new("furnizor_cui", "CUI furnizor"),
            new("furnizor_adresa", "Adresa furnizorului"),
            new("furnizor_telefon", "Telefon furnizor"),
            new("furnizor_email", "E-mail furnizor"),
            new("bifa_receptie_furnizor", "Recepție la furnizor (☐ / ☒)"),
            new("bifa_receptie_primire", "Recepție la primire (☐ / ☒)"),
            new("bifa_clasa_4", "Clasa de calitate 4 (☐ / ☒)"),
            new("bifa_clasa_best", "Clasa de calitate best comercial (☐ / ☒)"),
            new("bifa_transport_auto", "Transport auto (☐ / ☒)"),
            new("bifa_transport_cfr", "Transport CFR (☐ / ☒)"),
            new("bifa_plata_op", "Plata cu OP (☐ / ☒)"),
            new("bifa_plata_cec", "Plata cu fila CEC (☐ / ☒)"),
            new("bifa_plata_alte", "Plata altfel (☐ / ☒)"),
            new("observatii", "Observații"),
        ],
        Tabele:
        [
            new TabelFormular("linii",
            [
                new("numar_crt", "Nr. crt."),
                new("denumire", "Material / produs", Obligatoriu: true),
                new("um", "UM", Obligatoriu: true),
                new("cantitate", "Cantitate", TipCamp.Numar, Obligatoriu: true),
                new("termen", "Termen"),
            ], CelPutinOLinie: true),
        ],
        SePoateImporta: false);

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

    // ---------------------------------------------------------------- gestiuni (Stocuri)

    private static readonly CampFormular[] CampuriFirma =
    [
        new("firma_denumire", "Unitatea"),
        new("firma_cui", "CUI"),
    ];

    /// <summary>Nota de recepție și constatare de diferențe (cod 14-3-1A).</summary>
    public static readonly TipFormular Nir = new(
        Cod: "nir",
        Denumire: "Notă de recepție și constatare de diferențe (NIR)",
        ModulErp: "Gestiuni",
        Campuri:
        [
            .. CampuriComune, .. CampuriFirma,
            new("gestiune", "Gestiunea", Obligatoriu: true),
            new("furnizor", "Furnizor", Obligatoriu: true),
            new("factura", "Factura (nr. / data)", Obligatoriu: true),
            new("proiect_cod", "Proiect"),
            new("total_valoare", "Total valoare", TipCamp.Suma),
            new("observatii", "Observații"),
        ],
        Tabele:
        [
            new TabelFormular("linii",
            [
                new("numar_crt", "Nr. crt."), new("cod", "Cod"), new("denumire", "Denumire", Obligatoriu: true), new("um", "UM"),
                new("cantitate_factura", "Cantitate cf. document", TipCamp.Numar), new("cantitate_receptionata", "Cantitate recepționată", TipCamp.Numar),
                new("pret_unitar", "Preț unitar", TipCamp.Suma), new("valoare", "Valoare", TipCamp.Suma),
                new("diferenta", "Diferență", TipCamp.Numar), new("observatii", "Observații"),
            ], CelPutinOLinie: true),
        ],
        SePoateImporta: false);

    /// <summary>Bon de consum (cod 14-3-4A), pe articolele devizului.</summary>
    public static readonly TipFormular BonConsum = new(
        Cod: "bon-consum",
        Denumire: "Bon de consum",
        ModulErp: "Gestiuni",
        Campuri:
        [
            .. CampuriComune, .. CampuriFirma,
            new("gestiune", "Gestiunea (șantierul)", Obligatoriu: true),
            new("proiect_cod", "Proiect", Obligatoriu: true),
            new("total_valoare", "Total valoare", TipCamp.Suma),
            new("observatii", "Observații"),
        ],
        Tabele:
        [
            new TabelFormular("linii",
            [
                new("numar_crt", "Nr. crt."), new("cod", "Cod"), new("denumire", "Denumire", Obligatoriu: true), new("um", "UM"),
                new("cantitate", "Cantitate", TipCamp.Numar), new("pret_unitar", "Preț unitar", TipCamp.Suma), new("valoare", "Valoare", TipCamp.Suma),
                new("articol_deviz", "Articol de deviz"),
            ], CelPutinOLinie: true),
        ],
        SePoateImporta: false);

    /// <summary>Bon de predare-transfer-restituire (cod 14-3-3A): transfer în șantier sau retur în depozit.</summary>
    public static readonly TipFormular BonTransfer = new(
        Cod: "aviz-transfer",
        Denumire: "Bon de predare-transfer-restituire",
        ModulErp: "Gestiuni",
        Campuri:
        [
            .. CampuriComune, .. CampuriFirma,
            new("tip", "Tip (transfer / restituire)"),
            new("gestiune_predatoare", "Gestiunea predătoare", Obligatoriu: true),
            new("gestiune_primitoare", "Gestiunea primitoare", Obligatoriu: true),
            new("proiect_cod", "Proiect"),
            new("total_valoare", "Total valoare", TipCamp.Suma),
            new("observatii", "Observații"),
        ],
        Tabele:
        [
            new TabelFormular("linii",
            [
                new("numar_crt", "Nr. crt."), new("cod", "Cod"), new("denumire", "Denumire", Obligatoriu: true), new("um", "UM"),
                new("cantitate", "Cantitate", TipCamp.Numar), new("pret_unitar", "Preț unitar", TipCamp.Suma), new("valoare", "Valoare", TipCamp.Suma),
            ], CelPutinOLinie: true),
        ],
        SePoateImporta: false);

    /// <summary>Restul catalogului; câmpurile specifice se adaugă la implementarea fiecărui modul.</summary>
    public static readonly IReadOnlyList<TipFormular> Toate =
    [
        NecesarMateriale,
        NotaComanda,
        FacturaPrimita,
        Declarat("referat-necesitate", "Referat de necesitate / cerere materiale", "Aprovizionare", true),
        Declarat("cerere-oferta", "Cerere de ofertă către furnizori", "Aprovizionare", false),
        Declarat("comanda-subantreprenor", "Comandă către subantreprenor", "Subantreprenori", true),
        Declarat("contract", "Contract / act adițional", "Contracte", true),
        Nir,
        BonConsum,
        BonTransfer,
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
