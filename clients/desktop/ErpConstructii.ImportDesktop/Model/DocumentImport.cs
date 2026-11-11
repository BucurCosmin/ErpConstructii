using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ErpConstructii.ImportDesktop.Infrastructura;

namespace ErpConstructii.ImportDesktop.Model;

public enum StareDocument { Necitit, SeCiteste, DeVerificat, SeImporta, Importat, Eroare, Sarit, Neimportabil }

/// <summary>O linie editabilă (factură, notă de comandă sau necesar: fiecare tip folosește coloanele lui).</summary>
public sealed class LinieEditabila : Observabil
{
    private string _denumire = string.Empty;
    private string _um = string.Empty;
    private decimal? _cantitate;
    private decimal? _pret;
    private decimal? _valoare;
    private decimal? _cota;
    private bool _intraInStoc = true;
    private string? _termen;
    private string? _observatii;

    public string Denumire { get => _denumire; set => Seteaza(ref _denumire, value); }
    public string Um { get => _um; set => Seteaza(ref _um, value); }
    public decimal? Cantitate { get => _cantitate; set => Seteaza(ref _cantitate, value); }
    public decimal? Pret { get => _pret; set => Seteaza(ref _pret, value); }
    public decimal? Valoare { get => _valoare; set => Seteaza(ref _valoare, value); }

    /// <summary>Cota TVA în procente (21), cum o scrie utilizatorul.</summary>
    public decimal? CotaProcent { get => _cota; set => Seteaza(ref _cota, value); }

    public bool IntraInStoc { get => _intraInStoc; set => Seteaza(ref _intraInStoc, value); }

    private Guid? _articolId;
    private Guid? _articolDevizCheie;

    /// <summary>Bon de consum: materialul din stocul șantierului care se consumă (propus, ales de utilizator).</summary>
    public Guid? ArticolId { get => _articolId; set { if (Seteaza(ref _articolId, value)) Notifica(nameof(FaraMaterial)); } }

    /// <summary>Bon de consum: articolul de deviz pe care se consumă (null = cheltuieli generale ale proiectului).</summary>
    public Guid? ArticolDevizCheie { get => _articolDevizCheie; set => Seteaza(ref _articolDevizCheie, value); }

    public bool FaraMaterial => ArticolId is null;

    /// <summary>Bon de consum: codul materialului scris pe bon (ex. „R20681”).</summary>
    public string? Cod { get; init; }
    public string? Termen { get => _termen; set => Seteaza(ref _termen, value); }
    public string? Observatii { get => _observatii; set => Seteaza(ref _observatii, value); }

    /// <summary>Interpretorul a marcat ceva pe linie „de verificat” / „de completat”.</summary>
    public bool DeVerificat { get; init; }
}

/// <summary>
/// Un document din fișier (paginile lui, tipul) și formularul de verificare completat din ce a citit serverul.
/// Utilizatorul corectează, apoi importă; documentul trece prin stările din <see cref="StareDocument"/>.
/// </summary>
public sealed class DocumentImport : Observabil
{
    private string _tip;
    private StareDocument _stare = StareDocument.Necitit;
    private string? _mesaj;
    private Guid? _proiectId;
    private string? _numar;
    private DateTime? _data;
    private DateTime? _dataScadenta;
    private string _moneda = "RON";
    private string? _furnizor;
    private string? _furnizorCui;
    private bool _adaugaFurnizor = true;
    private string? _lucrare;
    private string? _compartiment;
    private string? _observatii;
    private decimal? _totalFaraTva;
    private decimal? _totalTva;
    private decimal? _totalDePlata;
    private bool _dinUrma;

    public DocumentImport(string tip, List<int> pagini, string? numar, DateOnly? data, int? zona = null)
    {
        _tip = tip;
        Zona = zona;
        Pagini = pagini;
        NumarPropus = numar;
        DataPropusa = data;
        Linii.CollectionChanged += LiniiSchimbate;
        if (!Importabil)
        {
            _stare = StareDocument.Neimportabil;
        }
    }

    public List<int> Pagini { get; private set; }

    /// <summary>Al câtelea document de pe aceste pagini (bonurile de consum, câte două pe pagină).</summary>
    public int? Zona { get; private set; }
    public string? NumarPropus { get; private set; }
    public DateOnly? DataPropusa { get; }

    public string Tip
    {
        get => _tip;
        set
        {
            if (Seteaza(ref _tip, value))
            {
                Reseteaza();
                Notifica(nameof(Titlu), nameof(EsteFactura), nameof(EsteNota), nameof(EsteNecesar), nameof(EsteBon), nameof(AreFurnizor), nameof(Importabil), nameof(EticheteTip), nameof(PoateFiDinUrma));
            }
        }
    }

    public static IReadOnlyList<string> Tipuri { get; } = ["Factura", "NotaComanda", "NecesarMateriale", "BonConsum", "Anexe"];

    public static string Denumire(string tip) => tip switch
    {
        "Factura" => "Factură",
        "NotaComanda" => "Notă de comandă",
        "NecesarMateriale" => "Necesar de materiale",
        "BonConsum" => "Bon de consum",
        _ => "Pagini fără document",
    };

    public string EticheteTip => Denumire(Tip);
    public bool EsteFactura => Tip == "Factura";
    public bool EsteNota => Tip == "NotaComanda";
    public bool EsteNecesar => Tip == "NecesarMateriale";
    public bool EsteBon => Tip == "BonConsum";
    public bool AreFurnizor => EsteFactura || EsteNota;
    public bool Importabil => EsteFactura || EsteNota || EsteNecesar || EsteBon;

    /// <summary>Bonul de consum nu are documente de dinainte; celelalte se pot importa „din urmă” (D-45).</summary>
    public bool PoateFiDinUrma => EsteFactura || EsteNota || EsteNecesar;

    /// <summary>
    /// Document din urmă (D-45): vechi, din arhivă. Factura nu cere nota de comandă / contractul, nota nu cere necesarul,
    /// necesarul intră aprobat, fără nota „de acceptat”.
    /// </summary>
    public bool DinUrma { get => _dinUrma; set => Seteaza(ref _dinUrma, value); }

    public string IntervalPagini => (Pagini.Count == 1 ? $"p. {Pagini[0]}"
        : Pagini[^1] - Pagini[0] == Pagini.Count - 1 ? $"p. {Pagini[0]}–{Pagini[^1]}" : $"p. {string.Join(", ", Pagini)}")
        + (Zona is { } z && EsteBon ? (z == 1 ? " (sus)" : z == 2 ? " (jos)" : $" (#{z})") : string.Empty);

    public string Titlu => Denumire(Tip) + ((Numar ?? NumarPropus) is { Length: > 0 } n ? $" nr. {n}" : string.Empty);

    public StareDocument Stare
    {
        get => _stare;
        set
        {
            if (Seteaza(ref _stare, value))
            {
                Notifica(nameof(TextStare), nameof(Editabil), nameof(PoateImporta), nameof(ProiectEditabil), nameof(SeCiteste));
            }
        }
    }

    public string TextStare => Stare switch
    {
        StareDocument.Necitit => "necitit",
        StareDocument.SeCiteste => "se citește…",
        StareDocument.DeVerificat => Existent is null ? "de verificat" : "deja în aplicație",
        StareDocument.SeImporta => "se importă…",
        StareDocument.Importat => "importat",
        StareDocument.Eroare => "eroare",
        StareDocument.Sarit => "sărit",
        _ => "nu se importă",
    };

    /// <summary>Mesajul stării: eroarea serverului sau ce s-a creat la import.</summary>
    public string? Mesaj { get => _mesaj; set => Seteaza(ref _mesaj, value); }

    public bool Editabil => Stare == StareDocument.DeVerificat && !EsteXml;

    /// <summary>Proiectul se alege și pentru XML-ul e-Factura (restul datelor lui sunt fixe: documentul fiscal oficial).</summary>
    public bool ProiectEditabil => Stare == StareDocument.DeVerificat;

    public bool SeCiteste => Stare is StareDocument.SeCiteste or StareDocument.SeImporta;
    public bool PoateImporta => Stare == StareDocument.DeVerificat && Existent is null;

    // ------------------------------------------------------------------ fișierul documentului (paginile lui)

    public byte[]? Fisier { get; set; }
    public string? NumeFisier { get; set; }

    // ------------------------------------------------------------------ formular

    public Guid? ProiectId { get => _proiectId; set { if (Seteaza(ref _proiectId, value)) Notifica(nameof(ProiectDeVerificat)); } }
    public string? Numar { get => _numar; set { if (Seteaza(ref _numar, value)) Notifica(nameof(Titlu)); } }
    public DateTime? Data { get => _data; set => Seteaza(ref _data, value); }
    public DateTime? DataScadenta { get => _dataScadenta; set => Seteaza(ref _dataScadenta, value); }
    public string Moneda { get => _moneda; set => Seteaza(ref _moneda, value); }
    public string? Furnizor { get => _furnizor; set => Seteaza(ref _furnizor, value); }
    public string? FurnizorCui { get => _furnizorCui; set => Seteaza(ref _furnizorCui, value); }
    public bool AdaugaFurnizor { get => _adaugaFurnizor; set => Seteaza(ref _adaugaFurnizor, value); }
    public string? Lucrare { get => _lucrare; set => Seteaza(ref _lucrare, value); }
    /// <summary>Compartimentul / șantierul scris pe document (doar orientativ; în aplicație se alege din <see cref="Locuri"/>).</summary>
    public string? Compartiment { get => _compartiment; set { if (Seteaza(ref _compartiment, value)) Notifica(nameof(CompartimentPeDocument)); } }

    public string CompartimentPeDocument => string.IsNullOrWhiteSpace(_compartiment) ? "Nu apare în document; alege-l." : $"Pe document: „{_compartiment.Trim()}”";

    /// <summary>Necesar: șantierele proiectului ales.</summary>
    public ObservableCollection<LocSolicitant> Locuri { get; } = [];

    private LocSolicitant? _loc;

    /// <summary>Șantierul care cere materialele, ales din listă (propus după textul de pe document).</summary>
    public LocSolicitant? Loc { get => _loc; set => Seteaza(ref _loc, value); }
    public string? Observatii { get => _observatii; set => Seteaza(ref _observatii, value); }
    public decimal? TotalFaraTva { get => _totalFaraTva; set { if (Seteaza(ref _totalFaraTva, value)) Notifica(nameof(VerificareTotaluri)); } }
    public decimal? TotalTva { get => _totalTva; set { if (Seteaza(ref _totalTva, value)) Notifica(nameof(VerificareTotaluri)); } }
    public decimal? TotalDePlata { get => _totalDePlata; set { if (Seteaza(ref _totalDePlata, value)) Notifica(nameof(VerificareTotaluri)); } }

    public ObservableCollection<LinieEditabila> Linii { get; } = [];

    // Bon de consum: stocul șantierului proiectului ales și articolele devizului lui.
    public ObservableCollection<MaterialSantier> Materiale { get; } = [];
    public ObservableCollection<ArticolDeviz> ArticoleDeviz { get; } = [];

    private string? _mesajMateriale;

    /// <summary>De ce nu sunt materiale de ales (proiect fără stoc în șantier) sau cum s-au propus.</summary>
    public string? MesajMateriale { get => _mesajMateriale; set => Seteaza(ref _mesajMateriale, value); }

    public string? Gestiune { get; private set; }
    public string? Primitor { get; private set; }
    public decimal? TotalBon { get; private set; }
    public ObservableCollection<string> Probleme { get; } = [];

    // Citite din document, de afișat lângă formular.
    public bool EsteXml { get; private set; }
    public bool DinOcr { get; private set; }
    public string? FurnizorInNomenclator { get; private set; }
    public string? FurnizorAdresa { get; private set; }
    public string? FurnizorTelefon { get; private set; }
    public string? Referinta { get; private set; }
    public string? Existent { get; private set; }
    public Guid? ProiectPropusId { get; private set; }
    public bool ProiectPropusSigur { get; private set; }

    // Câmpurile marcate de interpretor.
    public bool NumarDeVerificat { get; private set; }
    public bool DataDeVerificat { get; private set; }
    public bool FurnizorDeVerificat { get; private set; }
    public bool CuiDeVerificat { get; private set; }
    public bool LucrareDeVerificat { get; private set; }
    public bool TotaluriDeVerificat { get; private set; }
    public bool ProiectDeVerificat => ProiectId is not null && ProiectId == ProiectPropusId && !ProiectPropusSigur;

    /// <summary>Suma liniilor față de totaluri, recalculată la fiecare corectură (ca în aplicația web).</summary>
    public string? VerificareTotaluri
    {
        get
        {
            if (!EsteFactura || Stare is StareDocument.Necitit or StareDocument.SeCiteste)
            {
                return null;
            }

            var suma = decimal.Round(Linii.Sum(l => l.Valoare ?? 0), 2);
            var probleme = new List<string>();
            if (TotalFaraTva is { } f && Math.Abs(suma - f) > 0.01m * Math.Max(1, Linii.Count))
            {
                probleme.Add($"Suma liniilor ({suma:N2}) diferă de totalul fără TVA ({f:N2}).");
            }

            if (TotalFaraTva is { } a && TotalTva is { } b && TotalDePlata is { } c && Math.Abs(a + b - c) > 0.01m)
            {
                probleme.Add($"Total fără TVA + TVA ({a + b:N2}) diferă de totalul de plată ({c:N2}).");
            }

            return probleme.Count == 0 ? "✓ Totalurile corespund liniilor." : string.Join(" ", probleme);
        }
    }

    public bool TotaluriCorecte => VerificareTotaluri?.StartsWith('✓') == true;

    private void LiniiSchimbate(object? s, NotifyCollectionChangedEventArgs e)
    {
        foreach (var l in e.NewItems?.OfType<LinieEditabila>() ?? [])
        {
            l.PropertyChanged += LinieSchimbata;
        }

        Notifica(nameof(VerificareTotaluri), nameof(TotaluriCorecte));
    }

    private void LinieSchimbata(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LinieEditabila.Valoare))
        {
            Notifica(nameof(VerificareTotaluri), nameof(TotaluriCorecte));
        }
    }

    // ------------------------------------------------------------------ încărcarea citirii

    public void Reseteaza()
    {
        Linii.Clear();
        Probleme.Clear();
        Materiale.Clear();
        ArticoleDeviz.Clear();
        MesajMateriale = null;
        Fisier = null;
        Existent = null;
        EsteXml = false;
        Mesaj = null;
        Numar = null;
        Data = DataScadenta = null;
        Furnizor = FurnizorCui = Lucrare = Compartiment = Observatii = null;
        Loc = null;
        Locuri.Clear();
        TotalFaraTva = TotalTva = TotalDePlata = null;
        Stare = Importabil ? StareDocument.Necitit : StareDocument.Neimportabil;
        NotificaCitire();
    }

    public void SeteazaPagini(List<int> pagini)
    {
        Pagini = pagini;
        NumarPropus = null;
        Reseteaza();
        Notifica(nameof(Pagini), nameof(IntervalPagini), nameof(Titlu));
    }

    public void Incarca(InterpretareFactura f)
    {
        EsteXml = f.Sursa == "EFactura";
        DinOcr = f.DinOcr;
        Numar = f.Numar.Valoare;
        Data = Zi(f.DataEmitere);
        DataScadenta = Zi(f.DataScadenta);
        Moneda = f.Moneda.Valoare ?? "RON";
        Furnizor = f.FurnizorInNomenclator?.Denumire ?? f.FurnizorDenumire.Valoare;
        FurnizorCui = f.FurnizorCui.Valoare;
        FurnizorInNomenclator = f.FurnizorInNomenclator?.Denumire;
        AdaugaFurnizor = f.FurnizorInNomenclator is null;
        Referinta = f.Referinta;
        TotalFaraTva = f.TotalFaraTva.Valoare;
        TotalTva = f.TotalTva.Valoare;
        TotalDePlata = f.TotalDePlata.Valoare;
        Existent = f.FacturaExistenta is { } e ? $"Factura a fost deja înregistrată: {e}." : null;
        Propune(f.ProiectPropus);
        (NumarDeVerificat, DataDeVerificat, FurnizorDeVerificat, CuiDeVerificat) =
            (f.Numar.DeVerificat, f.DataEmitere.DeVerificat, f.FurnizorDenumire.DeVerificat, f.FurnizorCui.DeVerificat);
        TotaluriDeVerificat = f.TotalDePlata.DeVerificat || f.TotalFaraTva.DeVerificat;
        foreach (var l in f.Linii)
        {
            Linii.Add(new LinieEditabila
            {
                Denumire = l.Denumire.Valoare ?? string.Empty,
                Um = l.UnitateMasura.Valoare ?? string.Empty,
                Cantitate = l.Cantitate.Valoare,
                Pret = l.PretUnitar.Valoare,
                Valoare = l.ValoareFaraTva.Valoare,
                CotaProcent = l.CotaTva.Valoare is { } k ? decimal.Round(k * 100, 2) : null,
                DeVerificat = l.Denumire.DeVerificat || l.Cantitate.DeVerificat || l.ValoareFaraTva.DeVerificat,
            });
        }

        Finalizeaza(f.Probleme);
    }

    public void Incarca(InterpretareNotaComanda n)
    {
        DinOcr = n.DinOcr;
        Numar = n.NumarExtern.Valoare;
        Data = Zi(n.Data);
        Furnizor = n.FurnizorGasit?.Denumire ?? n.FurnizorDenumire.Valoare;
        FurnizorCui = n.FurnizorGasit?.Cui ?? n.FurnizorCui.Valoare;
        FurnizorInNomenclator = n.FurnizorGasit?.Denumire;
        FurnizorAdresa = n.FurnizorAdresa;
        FurnizorTelefon = n.FurnizorTelefon;
        Lucrare = n.Lucrare.Valoare;
        Existent = n.ComandaExistenta is { } e ? $"Nota de comandă a fost deja importată: {e}." : null;
        Propune(n.ProiectPropus);
        (NumarDeVerificat, DataDeVerificat, FurnizorDeVerificat, LucrareDeVerificat) =
            (n.NumarExtern.DeVerificat, n.Data.DeVerificat, n.FurnizorDenumire.DeVerificat, n.Lucrare.DeVerificat);
        foreach (var l in n.Linii)
        {
            Linii.Add(new LinieEditabila
            {
                Denumire = l.Denumire.Valoare ?? string.Empty,
                Um = l.UnitateMasura.Valoare ?? string.Empty,
                Cantitate = l.Cantitate.Valoare,
                Termen = l.Termen,
                DeVerificat = l.Denumire.DeVerificat || l.Cantitate.DeVerificat || l.UnitateMasura.DeVerificat,
            });
        }

        Finalizeaza(n.Probleme);
    }

    public void Incarca(InterpretareBonConsum b)
    {
        DinOcr = b.DinOcr;
        Numar = b.Numar.Valoare;
        Data = Zi(b.Data);
        Lucrare = b.Lucrare.Valoare;
        Gestiune = b.Gestiune.Valoare;
        Primitor = b.Primitor;
        TotalBon = b.Total.Valoare;
        Observatii = b.ObservatiiPropuse;
        Existent = b.BonExistent is { } e ? $"Bonul a fost deja importat: {e}." : null;
        Propune(b.ProiectPropus);
        (NumarDeVerificat, DataDeVerificat, LucrareDeVerificat) = (b.Numar.DeVerificat, b.Data.DeVerificat, b.Lucrare.DeVerificat);
        foreach (var l in b.Linii)
        {
            Linii.Add(new LinieEditabila
            {
                Denumire = l.Denumire.Valoare ?? string.Empty,
                Um = l.UnitateMasura.Valoare ?? string.Empty,
                Cantitate = l.Cantitate.Valoare,
                Pret = l.PretUnitar.Valoare,
                Valoare = l.Valoare.Valoare,
                Cod = l.Cod,
                DeVerificat = l.Denumire.DeVerificat || l.Cantitate.DeVerificat,
            });
        }

        Finalizeaza(b.Probleme.Where(p => p.Locatie != "bon" || !p.Mesaj.StartsWith("Bonul a fost deja", StringComparison.Ordinal)).ToList());
        Notifica(nameof(Gestiune), nameof(Primitor), nameof(TotalBon));
        if (b.Gol)
        {
            Stare = StareDocument.Sarit;
            Mesaj = "Bon necompletat (formular fără materiale): nu se importă.";
        }
    }

    public void Incarca(InterpretareNecesar n)
    {
        DinOcr = n.DinOcr;
        Numar = n.NumarExtern.Valoare;
        Data = Zi(n.Data);
        Lucrare = n.Lucrare.Valoare;
        Compartiment = n.CompartimentSantier.Valoare;
        Existent = n.NecesarExistent is { } e ? $"Necesarul a fost deja importat: {e}." : null;
        Propune(n.ProiectPropus);
        (NumarDeVerificat, DataDeVerificat, LucrareDeVerificat) = (n.NumarExtern.DeVerificat, n.Data.DeVerificat, n.Lucrare.DeVerificat);
        foreach (var l in n.Linii)
        {
            Linii.Add(new LinieEditabila
            {
                Denumire = l.Denumire.Valoare ?? string.Empty,
                Um = l.UnitateMasura.Valoare ?? string.Empty,
                Cantitate = l.Cantitate.Valoare,
                Observatii = l.Observatii.Valoare,
                DeVerificat = l.Denumire.DeVerificat || l.Cantitate.DeVerificat || l.UnitateMasura.DeVerificat,
            });
        }

        Finalizeaza(n.Probleme);
    }

    private void Propune(ProiectPropus? p)
    {
        ProiectPropusId = p?.Id;
        ProiectPropusSigur = p?.Certitudine == "Sigur";
        ProiectId = p?.Id;
    }

    private void Finalizeaza(List<Problema> probleme)
    {
        foreach (var p in probleme.Where(p => p.Locatie != "pagini"))
        {
            Probleme.Add(p.Mesaj);
        }

        if (Existent is not null)
        {
            Probleme.Insert(0, Existent);
        }

        Stare = StareDocument.DeVerificat;
        NotificaCitire();
    }

    private void NotificaCitire() => Notifica(
        nameof(EsteXml), nameof(DinOcr), nameof(FurnizorInNomenclator), nameof(Existent), nameof(Referinta), nameof(NumarDeVerificat),
        nameof(DataDeVerificat), nameof(FurnizorDeVerificat), nameof(CuiDeVerificat), nameof(LucrareDeVerificat), nameof(TotaluriDeVerificat),
        nameof(ProiectDeVerificat), nameof(VerificareTotaluri), nameof(TotaluriCorecte), nameof(Editabil), nameof(PoateImporta), nameof(TextStare));

    private static DateTime? Zi(CampCitit<DateOnly?> c) =>
        c.Valoare?.ToDateTime(TimeOnly.MinValue);
}
