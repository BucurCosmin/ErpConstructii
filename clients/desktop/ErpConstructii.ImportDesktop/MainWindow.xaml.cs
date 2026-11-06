using System.Windows;
using ErpConstructii.ImportDesktop.Infrastructura;
using ErpConstructii.ImportDesktop.Model;

namespace ErpConstructii.ImportDesktop;

public partial class MainWindow : Window
{
    private readonly FereastraModel _model;

    public MainWindow(Setari setari, Uri? server, string? fisier)
    {
        InitializeComponent();

        // Dimensiunea „normală” (după restaurare) încape mereu pe ecran, și pe laptop sau cu scalare 125–150%:
        // fereastra mai mare decât ecranul își pierde bara de titlu (nu se mai poate muta, minimiza, redimensiona).
        var zona = SystemParameters.WorkArea;
        Width = Math.Min(Width, zona.Width * 0.95);
        Height = Math.Min(Height, zona.Height * 0.95);
        MinWidth = Math.Min(MinWidth, zona.Width);
        MinHeight = Math.Min(MinHeight, zona.Height);
        Left = zona.Left + (zona.Width - Width) / 2;
        Top = zona.Top + (zona.Height - Height) / 2;
        _model = new FereastraModel(setari, server, ConfirmaCertificat, (titlu, mesaj) =>
            Dispatcher.Invoke(() => MessageBox.Show(this, mesaj, titlu, MessageBoxButton.OK, MessageBoxImage.Information)));
        DataContext = _model;

        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } fisiere)
            {
                _model.DeschideFisier(fisiere[0]);
            }
        };

        // Pornită din aplicația web (linkul erp-import://): conectarea începe singură; un fișier dat la pornire se deschide după.
        Loaded += (_, _) =>
        {
            if (server is not null)
            {
                _model.Conectare.Execute(null);
            }

            if (fisier is not null)
            {
                _model.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FereastraModel.Conectat) && _model.Conectat && _model.Fisier is null)
                    {
                        _model.DeschideFisier(fisier);
                        fisier = null;
                    }
                };
            }
        };
    }

    /// <summary>
    /// Certificat autosemnat (producția pe calculatorul firmei, D-26): utilizatorul confirmă o dată amprenta, comparând-o
    /// cu cea din browser; după aceea, un alt certificat pe aceeași adresă e refuzat.
    /// </summary>
    private bool ConfirmaCertificat(string gazda, string amprenta) =>
        Dispatcher.Invoke(() => MessageBox.Show(this,
            $"Serverul {gazda} folosește un certificat care nu e emis de o autoritate (autosemnat).{Environment.NewLine}{Environment.NewLine}" +
            $"Amprenta SHA-256:{Environment.NewLine}{Formateaza(amprenta)}{Environment.NewLine}{Environment.NewLine}" +
            "Dacă e serverul firmei tale (aceeași amprentă ca în browser: lacăt → certificat), acceptă-l. Aplicația îl va ține minte.",
            "Certificat necunoscut", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

    private static string Formateaza(string amprenta) =>
        string.Join(":", Enumerable.Range(0, amprenta.Length / 2).Select(i => amprenta.Substring(i * 2, 2)));
}
