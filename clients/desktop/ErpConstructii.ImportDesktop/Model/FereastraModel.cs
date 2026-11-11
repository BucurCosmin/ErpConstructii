using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media.Imaging;
using ErpConstructii.ImportDesktop.Infrastructura;
using Microsoft.Win32;

namespace ErpConstructii.ImportDesktop.Model;

public sealed record PaginaAfisata(int Numar, string Descriere, BitmapSource? Imagine);

/// <summary>
/// Fluxul aplicației: conectare la server (login prin browser) → fișier → documentele propuse de server → fiecare
/// document citit în fundal, verificat de utilizator lângă imaginea paginilor, apoi importat (sau sărit). Toate
/// operațiile trec prin API-ul aplicației principale: aceleași drepturi și verificări ca în aplicația web.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FereastraModel : Observabil
{
    private readonly Setari _setari;
    private readonly Func<string, string, bool> _confirmaCertificat;
    private readonly Action<string, string> _arataMesaj;
    private ClientErp? _client;
    private string _server;
    private string? _utilizator;
    private Firma? _firmaSelectata;
    private bool _seIncarcaContextul;
    private string? _fisier;
    private AnalizaImport? _analiza;
    private Previzualizare? _previzualizare;
    private DocumentImport? _documentSelectat;
    private string? _ocupat;
    private bool _preluareInCurs;
    private HashSet<string> _permisiuni = [];

    public FereastraModel(Setari setari, Uri? server, Func<string, string, bool> confirmaCertificat, Action<string, string> arataMesaj)
    {
        _setari = setari;
        _confirmaCertificat = confirmaCertificat;
        _arataMesaj = arataMesaj;
        _server = (server ?? Protocol.ServerValid(setari.Server))?.ToString().TrimEnd('/') ?? "https://";

        Conectare = new Comanda(_ => Ruleaza(ConecteazaAsync), _ => Ocupat is null);
        Deschide = new Comanda(_ => Ruleaza(() => DeschideAsync(null)), _ => Conectat && Ocupat is null);
        Importa = new Comanda(_ => Ruleaza(() => ImportaAsync(DocumentSelectat!)), _ => DocumentSelectat?.PoateImporta == true);
        SariPeste = new Comanda(_ => SariPesteDocument(), _ => DocumentSelectat is { Stare: StareDocument.DeVerificat or StareDocument.Eroare or StareDocument.Necitit });
        Reciteste = new Comanda(_ => Ruleaza(() => RecitesteAsync(DocumentSelectat!)),
            _ => DocumentSelectat is { Importabil: true, Stare: StareDocument.DeVerificat or StareDocument.Eroare or StareDocument.Sarit });
        Uneste = new Comanda(_ => Ruleaza(() => UnesteAsync(DocumentSelectat!)),
            _ => DocumentSelectat is { } d && Documente.IndexOf(d) > 0 && d.Stare != StareDocument.Importat && Documente[Documente.IndexOf(d) - 1].Stare != StareDocument.Importat);
        Desparte = new Comanda(p => Ruleaza(() => DesparteAsync(DocumentSelectat!, Convert.ToInt32(p, System.Globalization.CultureInfo.InvariantCulture))),
            p => DocumentSelectat is { Pagini.Count: > 1, Stare: not StareDocument.Importat } && p is int);
        Urmatorul = new Comanda(_ => SelecteazaUrmatorul(), _ => Documente.Count > 0);
        AdaugaLinie = new Comanda(_ => DocumentSelectat!.Linii.Add(new LinieEditabila()), _ => DocumentSelectat?.Editabil == true);
        StergeLinie = new Comanda(l => DocumentSelectat!.Linii.Remove((LinieEditabila)l!), l => l is LinieEditabila && DocumentSelectat?.Editabil == true);
    }

    // ------------------------------------------------------------------ stare afișată

    public string Server { get => _server; set => Seteaza(ref _server, value); }

    public bool Conectat => _client?.Autentificat == true && Utilizator is not null;

    public string? Utilizator { get => _utilizator; private set { if (Seteaza(ref _utilizator, value)) Notifica(nameof(Conectat)); } }

    public ObservableCollection<Firma> Firme { get; } = [];

    public Firma? FirmaSelectata
    {
        get => _firmaSelectata;
        set
        {
            var anterioara = _firmaSelectata;
            if (Seteaza(ref _firmaSelectata, value) && !_seIncarcaContextul && value is not null && anterioara is not null)
            {
                Ruleaza(() => SchimbaFirmaAsync(value));
            }
        }
    }

    public ObservableCollection<Proiect> Proiecte { get; } = [];

    public string? Fisier { get => _fisier; private set { if (Seteaza(ref _fisier, value)) Notifica(nameof(TitluFisier)); } }

    public string TitluFisier => Fisier is null ? "Niciun fișier deschis" : Path.GetFileName(Fisier);

    public ObservableCollection<DocumentImport> Documente { get; } = [];

    private bool _dinUrma;

    /// <summary>
    /// D-45: documentele din fișier sunt din arhivă. Se propune fiecărui document (și celor adăugate ulterior, la împărțire);
    /// pe fiecare document bifa se poate schimba separat.
    /// </summary>
    public bool DinUrma
    {
        get => _dinUrma;
        set
        {
            if (Seteaza(ref _dinUrma, value))
            {
                foreach (var d in Documente.Where(d => d.Stare is not StareDocument.Importat))
                {
                    d.DinUrma = value;
                }
            }
        }
    }

    public ObservableCollection<PaginaAfisata> PaginiAfisate { get; } = [];

    public ObservableCollection<string> ProblemeFisier { get; } = [];

    public DocumentImport? DocumentSelectat
    {
        get => _documentSelectat;
        set
        {
            if (Seteaza(ref _documentSelectat, value))
            {
                Notifica(nameof(AreDocumentSelectat), nameof(PaginiDeDespartire));
                _ = AfiseazaPaginiAsync(value);
                if (value is { Stare: StareDocument.Necitit })
                {
                    Ruleaza(() => CitesteAsync(value), elibereaza: false);
                }
            }
        }
    }

    public bool AreDocumentSelectat => DocumentSelectat is not null;

    /// <summary>Paginile de la care documentul selectat se poate despărți (toate, în afară de prima).</summary>
    public IEnumerable<int> PaginiDeDespartire => DocumentSelectat?.Pagini.Skip(1) ?? [];

    /// <summary>Operația în curs (afișată peste fereastră); null = liber.</summary>
    public string? Ocupat { get => _ocupat; private set => Seteaza(ref _ocupat, value); }

    public string Rezumat
    {
        get
        {
            if (Documente.Count == 0)
            {
                return string.Empty;
            }

            var importabile = Documente.Count(d => d.Importabil);
            var importate = Documente.Count(d => d.Stare == StareDocument.Importat);
            var sarite = Documente.Count(d => d.Stare == StareDocument.Sarit);
            return $"{Documente.Count} documente · {importate} din {importabile} importate" + (sarite > 0 ? $" · {sarite} sărite" : string.Empty);
        }
    }

    private int _rotire;

    /// <summary>Rotirea imaginilor paginilor în previzualizare (0 / 90 / 180 / 270), doar pentru citit.</summary>
    public int Rotire { get => _rotire; private set => Seteaza(ref _rotire, value); }

    public Comanda Roteste => _roteste ??= new Comanda(_ => Rotire = (Rotire + 90) % 360);

    private Comanda? _roteste;

    public Comanda Conectare { get; }
    public Comanda Deschide { get; }
    public Comanda Importa { get; }
    public Comanda SariPeste { get; }
    public Comanda Reciteste { get; }
    public Comanda Uneste { get; }
    public Comanda Desparte { get; }
    public Comanda Urmatorul { get; }
    public Comanda AdaugaLinie { get; }
    public Comanda StergeLinie { get; }

    // ------------------------------------------------------------------ conectare

    private async Task ConecteazaAsync()
    {
        var server = Protocol.ServerValid(Server)
                     ?? throw new InvalidOperationException("Scrie adresa aplicației web, de exemplu https://sdaq.home.ro sau http://localhost:5173.");
        Server = server.ToString().TrimEnd('/');
        _client?.Dispose();
        _client = new ClientErp(server, _setari, _confirmaCertificat);
        Utilizator = null;

        Ocupat = "Autentificare în browser… (se deschide pagina de login a aplicației)";
        using (var limita = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
        {
            await _client.AutentificaAsync(limita.Token);
        }

        _setari.Server = Server;
        _setari.Salveaza();
        Application.Current.MainWindow?.Activate();
        await IncarcaContextulAsync();
    }

    private async Task IncarcaContextulAsync()
    {
        Ocupat = "Se încarcă firma și proiectele…";
        var profil = await _client!.ProfilAsync(CancellationToken.None);
        _permisiuni = [.. await _client.PermisiuniAsync(CancellationToken.None)];
        var proiecte = await _client.ProiecteAsync(CancellationToken.None);

        _seIncarcaContextul = true;
        try
        {
            Firme.Clear();
            foreach (var f in profil.Firme)
            {
                Firme.Add(f);
            }

            FirmaSelectata = Firme.FirstOrDefault(f => f.Id == profil.FirmaActiva) ?? Firme.FirstOrDefault();
        }
        finally
        {
            _seIncarcaContextul = false;
        }

        Proiecte.Clear();
        foreach (var p in proiecte.OrderBy(p => p.Cod, StringComparer.OrdinalIgnoreCase))
        {
            Proiecte.Add(p);
        }

        Utilizator = $"{profil.Nume} ({profil.Email})";
        if (!_permisiuni.Contains("financiar.creare") && !_permisiuni.Contains("aprovizionare.creare"))
        {
            _arataMesaj("Fără drepturi de import",
                "Contul tău nu are dreptul de a crea facturi (financiar.creare) sau documente de aprovizionare (aprovizionare.creare). Cere-l administratorului firmei.");
        }
    }

    private async Task SchimbaFirmaAsync(Firma firma)
    {
        Ocupat = $"Se schimbă firma activă: {firma.Denumire}…";
        await _client!.SchimbaFirmaAsync(firma.Id, CancellationToken.None);
        InchideFisierul(); // documentele citite țin de firma anterioară (cumpărătorul, proiectele)
        await IncarcaContextulAsync();
    }

    // ------------------------------------------------------------------ fișierul

    public async Task DeschideAsync(string? cale)
    {
        if (!Conectat)
        {
            throw new InvalidOperationException("Conectează-te întâi la aplicație.");
        }

        if (cale is null)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Alege fișierul cu documente",
                Filter = "Documente (PDF, imagini, Word, e-Factura)|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.docx;*.xml;*.zip|Toate fișierele|*.*",
                InitialDirectory = _setari.UltimulFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            cale = dialog.FileName;
        }

        if (Documente.Any(d => d.Stare == StareDocument.DeVerificat) &&
            MessageBox.Show("Mai sunt documente neimportate în fișierul curent. Deschizi alt fișier?", "Import documente",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        InchideFisierul();
        _setari.UltimulFolder = Path.GetDirectoryName(cale);
        _setari.Salveaza();
        Fisier = cale;
        _previzualizare = new Previzualizare(cale);

        Ocupat = "Se citește fișierul… Paginile scanate trec prin recunoașterea textului (câteva secunde pe pagină).";
        _analiza = await _client!.AnalizeazaAsync(cale, CancellationToken.None);
        foreach (var p in _analiza.Probleme)
        {
            ProblemeFisier.Add(p.Mesaj);
        }

        foreach (var d in _analiza.Documente)
        {
            Adauga(new DocumentImport(d.Tip, d.Pagini, d.Numar, d.Data, d.Zona));
        }

        Ocupat = null;
        Notifica(nameof(Rezumat));
        DocumentSelectat = Documente.FirstOrDefault(d => d.Importabil) ?? Documente.FirstOrDefault();
        _ = PreiaToateAsync();
    }

    private void InchideFisierul()
    {
        DocumentSelectat = null;
        Documente.Clear();
        PaginiAfisate.Clear();
        ProblemeFisier.Clear();
        _analiza = null;
        _previzualizare = null;
        Fisier = null;
        Notifica(nameof(Rezumat));
    }

    private void Adauga(DocumentImport d, int? pozitie = null)
    {
        d.DinUrma = DinUrma;
        d.PropertyChanged += DocumentSchimbat;
        if (pozitie is { } i)
        {
            Documente.Insert(i, d);
        }
        else
        {
            Documente.Add(d);
        }
    }

    private void DocumentSchimbat(object? s, PropertyChangedEventArgs e)
    {
        if (s is not DocumentImport d)
        {
            return;
        }

        if (e.PropertyName == nameof(DocumentImport.Stare))
        {
            Notifica(nameof(Rezumat));
        }
        else if (e.PropertyName == nameof(DocumentImport.ProiectId) && d is { EsteBon: true, Stare: StareDocument.DeVerificat })
        {
            Ruleaza(() => IncarcaMaterialeAsync(d), elibereaza: false); // stocul șantierului noului proiect
        }
        else if (e.PropertyName == nameof(DocumentImport.ProiectId) && d is { EsteNecesar: true, Stare: StareDocument.DeVerificat })
        {
            Ruleaza(() => IncarcaLocuriAsync(d), elibereaza: false); // șantierele noului proiect
        }
        else if (e.PropertyName == nameof(DocumentImport.Tip) && d.Importabil)
        {
            Ruleaza(() => CitesteAsync(d), elibereaza: false); // tipul schimbat de utilizator: altă interpretare
        }
    }

    /// <summary>Citește în fundal documentele încă necitite, ca utilizatorul să treacă de la unul la altul fără așteptare.</summary>
    private async Task PreiaToateAsync()
    {
        if (_preluareInCurs)
        {
            return;
        }

        _preluareInCurs = true;
        try
        {
            foreach (var d in Documente.ToList())
            {
                if (d.Stare == StareDocument.Necitit && Documente.Contains(d))
                {
                    await CitesteSigurAsync(d);
                }
            }
        }
        finally
        {
            _preluareInCurs = false;
        }
    }

    private async Task CitesteSigurAsync(DocumentImport d)
    {
        try
        {
            await CitesteAsync(d);
        }
        catch (Exception ex) when (ex is EroareServer or InvalidOperationException or IOException or System.Net.Http.HttpRequestException)
        {
            // starea documentului arată deja eroarea
        }
    }

    private async Task CitesteAsync(DocumentImport d)
    {
        if (_analiza is null || d.Stare is not (StareDocument.Necitit or StareDocument.Eroare) || !d.Importabil)
        {
            return;
        }

        var drept = d.EsteFactura ? "financiar.creare" : "aprovizionare.creare";
        if (!_permisiuni.Contains(drept))
        {
            d.Stare = StareDocument.Eroare;
            d.Mesaj = $"Nu ai dreptul {drept}, necesar pentru acest tip de document.";
            return;
        }

        d.Stare = StareDocument.SeCiteste;
        d.Mesaj = null;
        try
        {
            var fisier = await _client!.DocumentAsync(_analiza.Cheie, d.Pagini, CancellationToken.None);
            var nume = NumeFisierDocument(_analiza, d);
            if (d.EsteFactura)
            {
                var f = await _client.InterpreteazaFacturaAsync(fisier, nume, CancellationToken.None);
                d.Fisier = fisier;
                d.NumeFisier = nume;
                d.Incarca(f);
            }
            else if (d.EsteNota)
            {
                var n = await _client.InterpreteazaNotaAsync(fisier, nume, CancellationToken.None);
                d.Fisier = fisier;
                d.NumeFisier = nume;
                d.Incarca(n);
            }
            else if (d.EsteBon)
            {
                var b = await _client.InterpreteazaBonAsync(fisier, nume, d.Zona ?? 1, CancellationToken.None);
                d.Fisier = fisier;
                d.NumeFisier = nume;
                d.Incarca(b);
                if (d is { Stare: StareDocument.DeVerificat, ProiectId: not null })
                {
                    await IncarcaMaterialeAsync(d);
                }
            }
            else
            {
                var n = await _client.InterpreteazaNecesarAsync(fisier, nume, CancellationToken.None);
                d.Fisier = fisier;
                d.NumeFisier = nume;
                d.Incarca(n);
                if (d is { Stare: StareDocument.DeVerificat, ProiectId: not null })
                {
                    await IncarcaLocuriAsync(d);
                }
            }
        }
        catch (Exception ex) when (ex is EroareServer or IOException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            d.Stare = StareDocument.Eroare;
            d.Mesaj = ex is EroareServer ? ex.Message : $"Serverul nu răspunde: {ex.Message}";
            throw;
        }
    }

    /// <summary>
    /// Necesar: șantierele proiectului ales, cu cel scris pe document propus, ca în
    /// aplicația web: denumire identică sau, una singură, cuprinsă pe cuvinte întregi.
    /// </summary>
    private async Task IncarcaLocuriAsync(DocumentImport d)
    {
        if (d.ProiectId is not { } proiect)
        {
            d.Locuri.Clear();
            d.Loc = null;
            return;
        }

        var locuri = await _client!.LocuriSolicitantAsync(proiect, CancellationToken.None);
        if (d.ProiectId != proiect)
        {
            return; // între timp s-a ales alt proiect
        }

        var ales = d.Loc;
        d.Locuri.Clear();
        foreach (var l in locuri)
        {
            d.Locuri.Add(l);
        }

        d.Loc = locuri.FirstOrDefault(l => l.Id == ales?.Id && l.EsteSantier == ales.EsteSantier) ?? PotrivesteLoc(d.Compartiment, locuri);
    }

    private static LocSolicitant? PotrivesteLoc(string? text, List<LocSolicitant> locuri)
    {
        static string Cheie(string s)
        {
            var fd = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(fd.Length);
            foreach (var ch in fd)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
            }

            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var t = Cheie(text ?? string.Empty);
        if (t.Length == 0)
        {
            return null;
        }

        var identice = locuri.Where(l => Cheie(l.Denumire) == t).ToList();
        if (identice.Count > 0)
        {
            return identice.Count == 1 ? identice[0] : null;
        }

        var partiale = locuri.Where(l =>
        {
            var c = Cheie(l.Denumire);
            return c.Length >= 3 && ($" {t} ".Contains($" {c} ", StringComparison.Ordinal) || $" {c} ".Contains($" {t} ", StringComparison.Ordinal));
        }).ToList();
        return partiale.Count == 1 ? partiale[0] : null;
    }

    /// <summary>
    /// Bon de consum: materialele din stocul șantierului proiectului ales (cu materialul propus pentru fiecare linie) și
    /// articolele devizului. Consumul se face doar din ce e în șantier, la CMP.
    /// </summary>
    private async Task IncarcaMaterialeAsync(DocumentImport d)
    {
        if (d.ProiectId is not { } proiect)
        {
            d.Materiale.Clear();
            d.ArticoleDeviz.Clear();
            return;
        }

        d.MesajMateriale = "Se caută materialele în stocul șantierului…";
        var potrivire = await _client!.PotrivesteBonAsync(proiect, d.Linii.Select(l => (l.Denumire, (string?)l.Um)), CancellationToken.None);
        var deviz = await _client.DevizAsync(proiect, CancellationToken.None);
        if (d.ProiectId != proiect)
        {
            return; // între timp s-a ales alt proiect
        }

        d.Materiale.Clear();
        foreach (var m in potrivire.Materiale)
        {
            d.Materiale.Add(m);
        }

        d.ArticoleDeviz.Clear();
        foreach (var a in deviz?.Articole ?? [])
        {
            d.ArticoleDeviz.Add(a);
        }

        var asemanatoare = 0;
        foreach (var (linie, i) in d.Linii.Select((l, i) => (l, i)))
        {
            var propunere = potrivire.Propuneri.ElementAtOrDefault(i);
            linie.ArticolId = propunere?.ArticolId;
            asemanatoare += propunere?.Sursa == "Asemanare" ? 1 : 0;
        }

        var propuse = d.Linii.Count(l => l.ArticolId is not null);
        d.MesajMateriale = potrivire.Mesaj ?? (propuse == d.Linii.Count && asemanatoare == 0
            ? "Materialele au fost găsite în stocul șantierului."
            : $"Material propus pentru {propuse} din {d.Linii.Count} linii" +
              (asemanatoare > 0 ? $" ({asemanatoare} după o denumire asemănătoare: verifică)" : string.Empty) +
              ". Alege materialul din stocul șantierului pentru fiecare linie.");
    }

    /// <summary>Numele PDF-ului tăiat (ca în aplicația web); fișierele care nu se pot tăia își păstrează numele.</summary>
    private static string NumeFisierDocument(AnalizaImport a, DocumentImport d)
    {
        if (!a.Separabil || d.Pagini.Count == a.Pagini.Count)
        {
            return a.NumeFisier;
        }

        var interval = d.Pagini.Count == 1 ? $"{d.Pagini[0]}" : $"{d.Pagini[0]}-{d.Pagini[^1]}";
        return $"{Path.GetFileNameWithoutExtension(a.NumeFisier)}_p{interval}.pdf";
    }

    private async Task AfiseazaPaginiAsync(DocumentImport? d)
    {
        PaginiAfisate.Clear();
        if (d is null || _previzualizare is not { AreImagini: true } previzualizare || _analiza is null)
        {
            return;
        }

        var descrieri = _analiza.Pagini.ToDictionary(p => p.Pagina, p => p.Descriere);
        foreach (var p in d.Pagini)
        {
            var imagine = await Task.Run(() => previzualizare.Pagina(p));
            if (DocumentSelectat != d)
            {
                return; // între timp s-a ales alt document
            }

            PaginiAfisate.Add(new PaginaAfisata(p, descrieri.GetValueOrDefault(p, "pagină"), imagine));
        }
    }

    // ------------------------------------------------------------------ verificare și import

    private async Task ImportaAsync(DocumentImport d)
    {
        var lipsuri = Valideaza(d);
        if (lipsuri.Count > 0)
        {
            _arataMesaj("Completează înainte de import", string.Join(Environment.NewLine, lipsuri.Select(l => "• " + l)));
            return;
        }

        d.Stare = StareDocument.SeImporta;
        Ocupat = $"Se importă {d.Titlu}…";
        try
        {
            var proiect = d.ProiectId!.Value;
            var data = DateOnly.FromDateTime(d.Data!.Value);
            var cod = Proiecte.FirstOrDefault(p => p.Id == proiect)?.Cod;
            if (d.EsteFactura)
            {
                var f = await _client!.ImportaFacturaAsync(proiect, d.Fisier!, d.NumeFisier!, new ConfirmaFactura(
                    (d.Numar ?? string.Empty).Trim(),
                    data,
                    d.DataScadenta is { } s ? DateOnly.FromDateTime(s) : null,
                    d.Moneda.Trim().ToUpperInvariant(),
                    (d.Furnizor ?? string.Empty).Trim(),
                    string.IsNullOrWhiteSpace(d.FurnizorCui) ? null : d.FurnizorCui.Trim(),
                    d.AdaugaFurnizor,
                    d.Linii.Select(l => new LinieFacturaConfirmata(l.Denumire.Trim(), l.Um.Trim(), l.Cantitate ?? 0, l.Pret ?? 0,
                        l.Valoare ?? 0, (l.CotaProcent ?? 0) / 100m, l.IntraInStoc)).ToList(),
                    d.TotalFaraTva ?? 0, d.TotalTva ?? 0, d.TotalDePlata ?? 0,
                    d.Referinta, string.IsNullOrWhiteSpace(d.Observatii) ? null : d.Observatii.Trim(), d.DinUrma), CancellationToken.None);
                d.Mesaj = $"Factura {f.Numar} ({f.FurnizorDenumire}, {f.TotalDePlata:N2} lei) înregistrată pe proiectul {f.ProiectCod}.";
            }
            else if (d.EsteNota)
            {
                var n = await _client!.ImportaNotaAsync(proiect, d.Fisier!, d.NumeFisier!, new ConfirmaNotaComanda(
                    Gol(d.Numar), data, Gol(d.Lucrare), (d.Furnizor ?? string.Empty).Trim(), Gol(d.FurnizorCui), d.FurnizorAdresa, d.FurnizorTelefon,
                    d.Linii.Select(l => new LinieNotaConfirmata(l.Denumire.Trim(), l.Um.Trim(), l.Cantitate ?? 0, Gol(l.Termen))).ToList(),
                    Gol(d.Observatii), d.DinUrma), CancellationToken.None);
                d.Mesaj = $"Nota de comandă {n.Numar} înregistrată pe proiectul {cod}{(d.DinUrma ? " (document din urmă)" : string.Empty)}.";
            }
            else if (d.EsteBon)
            {
                var n = await _client!.ImportaBonAsync(proiect, new ConsumCerere(data,
                    d.Linii.Select(l => new LinieConsumCerere(l.ArticolId!.Value, l.Cantitate!.Value, l.ArticolDevizCheie)).ToList(),
                    Gol(d.Observatii), Gol(d.Numar)), CancellationToken.None);
                d.Mesaj = $"Bonul de consum {n.Numar} înregistrat pe proiectul {cod}: materialele au ieșit din stocul șantierului, la CMP.";
            }
            else
            {
                var n = await _client!.ImportaNecesarAsync(proiect, d.Fisier!, d.NumeFisier!, new ConfirmaNecesar(
                    Gol(d.Numar), data, (d.Compartiment ?? string.Empty).Trim(),
                    d.Linii.Select(l => new LinieNecesarConfirmata(l.Denumire.Trim(), l.Um.Trim(), l.Cantitate ?? 0, Gol(l.Observatii), null)).ToList(),
                    d.DinUrma,
                    d.Loc is { EsteSantier: true } s ? s.Id : null,
                    d.Loc is { EsteSantier: false } c ? c.Id : null),
                    CancellationToken.None);
                d.Mesaj = d.DinUrma
                    ? $"Necesarul {n.Numar} înregistrat pe proiectul {cod} ca document din urmă (aprobat)."
                    : $"Necesarul {n.Numar} înregistrat pe proiectul {cod}, în așteptarea aprobării.";
            }

            d.Stare = StareDocument.Importat;
            Ocupat = null;
            SelecteazaUrmatorul();
        }
        catch
        {
            d.Stare = StareDocument.DeVerificat;
            throw;
        }
    }

    private static List<string> Valideaza(DocumentImport d)
    {
        var l = new List<string>();
        if (d.ProiectId is null) l.Add("Proiectul (orice cost / document se leagă de un proiect).");
        if (d.Data is null) l.Add(d.EsteFactura ? "Data facturii." : "Data documentului.");
        if (d.EsteFactura && string.IsNullOrWhiteSpace(d.Numar)) l.Add("Seria și numărul facturii.");
        if ((d.EsteFactura || d.EsteNota) && string.IsNullOrWhiteSpace(d.Furnizor)) l.Add("Furnizorul.");
        if (d.EsteNecesar && d.Loc is null) l.Add("Șantierul proiectului care cere materialele.");
        if (d.Linii.Count == 0) l.Add("Cel puțin o linie.");
        foreach (var (linie, i) in d.Linii.Select((x, i) => (x, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(linie.Denumire)) l.Add($"Linia {i}: denumirea.");
            if (linie.Cantitate is null or 0) l.Add($"Linia {i}: cantitatea.");
            if (d.EsteFactura && linie.Valoare is null) l.Add($"Linia {i}: valoarea.");
            if (d.EsteFactura && linie.CotaProcent is null) l.Add($"Linia {i}: cota TVA.");
            if (d.EsteBon && linie.ArticolId is null) l.Add($"Linia {i}: materialul din stocul șantierului.");
        }

        if (d.EsteFactura && (d.TotalFaraTva is null || d.TotalTva is null || d.TotalDePlata is null)) l.Add("Totalurile facturii.");
        return l;
    }

    private void SariPesteDocument()
    {
        DocumentSelectat!.Stare = StareDocument.Sarit;
        DocumentSelectat.Mesaj = "Sărit: nu se importă din acest fișier.";
        SelecteazaUrmatorul();
    }

    private async Task RecitesteAsync(DocumentImport d)
    {
        d.Reseteaza();
        await CitesteAsync(d);
    }

    private async Task UnesteAsync(DocumentImport d)
    {
        var i = Documente.IndexOf(d);
        var sus = Documente[i - 1];
        d.PropertyChanged -= DocumentSchimbat;
        Documente.RemoveAt(i);
        sus.SeteazaPagini([.. sus.Pagini.Concat(d.Pagini).Distinct().Order()]);
        DocumentSelectat = sus;
        await CitesteAsync(sus);
    }

    private async Task DesparteAsync(DocumentImport d, int dela)
    {
        var jos = new DocumentImport(d.Tip, [.. d.Pagini.Where(p => p >= dela)], null, null);
        d.SeteazaPagini([.. d.Pagini.Where(p => p < dela)]);
        Adauga(jos, Documente.IndexOf(d) + 1);
        Notifica(nameof(Rezumat), nameof(PaginiDeDespartire));
        await CitesteAsync(d);
        await CitesteSigurAsync(jos);
    }

    private void SelecteazaUrmatorul()
    {
        if (Documente.Count == 0)
        {
            return;
        }

        var start = DocumentSelectat is null ? 0 : Documente.IndexOf(DocumentSelectat) + 1;
        var urmatorul = Documente.Skip(start).Concat(Documente.Take(start))
            .FirstOrDefault(d => d.Stare is StareDocument.Necitit or StareDocument.SeCiteste or StareDocument.Eroare ||
                                 d is { Stare: StareDocument.DeVerificat, Existent: null });
        if (urmatorul is not null)
        {
            DocumentSelectat = urmatorul;
        }
        else if (Documente.Any(d => d.Importabil))
        {
            _arataMesaj("Fișier terminat", $"Toate documentele din fișier au fost tratate. {Rezumat}.");
        }
    }

    // ------------------------------------------------------------------ utilitare

    /// <summary>Rulează o operație și arată eroarea ei utilizatorului (mesajele serverului sunt deja în română).</summary>
    /// <param name="elibereaza">false pentru citirile din fundal: nu închid mesajul „ocupat” al altei operații.</param>
    private async void Ruleaza(Func<Task> operatie, bool elibereaza = true)
    {
        try
        {
            await operatie();
        }
        catch (EroareServer ex)
        {
            _arataMesaj("Serverul a refuzat operația", ex.Message);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _arataMesaj("Serverul nu răspunde", $"Nu mă pot conecta la {Server}. Verifică adresa și că aplicația rulează.{Environment.NewLine}{ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TaskCanceledException or OperationCanceledException)
        {
            _arataMesaj("Import documente", ex is OperationCanceledException ? "Operația a durat prea mult și a fost oprită." : ex.Message);
        }
        finally
        {
            if (elibereaza)
            {
                Ocupat = null;
            }
        }
    }

    /// <summary>Deschiderea unui fișier tras peste fereastră sau primit la pornire.</summary>
    public void DeschideFisier(string cale) => Ruleaza(() => DeschideAsync(cale));

    private static string? Gol(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
