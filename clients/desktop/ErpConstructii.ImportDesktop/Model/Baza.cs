using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ErpConstructii.ImportDesktop.Model;

public abstract class Observabil : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Seteaza<T>(ref T camp, T valoare, [CallerMemberName] string? nume = null)
    {
        if (EqualityComparer<T>.Default.Equals(camp, valoare))
        {
            return false;
        }

        camp = valoare;
        Notifica(nume!);
        return true;
    }

    protected void Notifica(params string[] nume)
    {
        foreach (var n in nume)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}

/// <summary>Comandă pentru butoane; acțiunile asincrone își raportează singure erorile (vezi FereastraModel.RuleazaAsync).</summary>
public sealed class Comanda(Action<object?> executa, Func<object?, bool>? poate = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => poate?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => executa(parameter);
}

/// <summary>true → fundal portocaliu deschis („de verificat”), ca în aplicația web.</summary>
public sealed class DeVerificatLaFundal : IValueConverter
{
    private static readonly Brush Portocaliu = Inghetat(Color.FromRgb(0xFF, 0xF1, 0xD6));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Portocaliu : Brushes.White;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    internal static Brush Inghetat(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

public sealed class BoolLaVizibilitate : IValueConverter
{
    public bool Invers { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invers ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NulLaVizibilitate : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class TipLaDenumire : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string t ? DocumentImport.Denumire(t) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Culoarea stării unui document în listă.</summary>
public sealed class StareLaCuloare : IValueConverter
{
    private static readonly Dictionary<StareDocument, Brush> Culori = new()
    {
        [StareDocument.Necitit] = DeVerificatLaFundal.Inghetat(Color.FromRgb(0x99, 0x99, 0x99)),
        [StareDocument.SeCiteste] = DeVerificatLaFundal.Inghetat(Color.FromRgb(0x2F, 0x54, 0xEB)),
        [StareDocument.DeVerificat] = DeVerificatLaFundal.Inghetat(Color.FromRgb(0xFA, 0x8C, 0x16)),
        [StareDocument.SeImporta] = DeVerificatLaFundal.Inghetat(Color.FromRgb(0x2F, 0x54, 0xEB)),
        [StareDocument.Importat] = DeVerificatLaFundal.Inghetat(Color.FromRgb(0x38, 0x9E, 0x0D)),
        [StareDocument.Eroare] = DeVerificatLaFundal.Inghetat(Color.FromRgb(0xCF, 0x13, 0x22)),
        [StareDocument.Sarit] = DeVerificatLaFundal.Inghetat(Color.FromRgb(0x99, 0x99, 0x99)),
        [StareDocument.Neimportabil] = DeVerificatLaFundal.Inghetat(Color.FromRgb(0xBF, 0xBF, 0xBF)),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is StareDocument s ? Culori[s] : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
