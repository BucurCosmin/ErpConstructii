using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using ErpConstructii.ImportDesktop.Infrastructura;

namespace ErpConstructii.ImportDesktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Numerele și datele în format românesc în toate câmpurile („47,4”, „02.12.2025”).
        var ro = CultureInfo.GetCultureInfo("ro-RO");
        CultureInfo.DefaultThreadCurrentCulture = ro;
        CultureInfo.DefaultThreadCurrentUICulture = ro;
        CultureInfo.CurrentCulture = ro;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(ro.IetfLanguageTag)));

        Protocol.Inregistreaza();
        var (server, fisier) = Protocol.Citeste(e.Args);
        var fereastra = new MainWindow(Setari.Incarca(), server, fisier);
        fereastra.Show();
    }
}
