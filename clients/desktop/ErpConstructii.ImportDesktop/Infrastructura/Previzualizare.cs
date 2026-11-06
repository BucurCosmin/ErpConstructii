using System.IO;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using PDFtoImage;

namespace ErpConstructii.ImportDesktop.Infrastructura;

/// <summary>Imaginile paginilor fișierului deschis, randate local (PDFium), ca utilizatorul să compare cu ce s-a citit.</summary>
[SupportedOSPlatform("windows")]
public sealed class Previzualizare
{
    private readonly byte[] _continut;
    private readonly string _extensie;
    private readonly Dictionary<int, BitmapSource> _pagini = [];

    public Previzualizare(string cale)
    {
        _continut = File.ReadAllBytes(cale);
        _extensie = Path.GetExtension(cale).ToLowerInvariant();
        NumarPagini = _extensie == ".pdf" ? SafePageCount() : EsteImagine ? 1 : 0;
    }

    public int NumarPagini { get; }

    public bool AreImagini => NumarPagini > 0;

    private bool EsteImagine => _extensie is ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp";

    /// <summary>Pagina (de la 1), la rezoluția de ecran; null dacă nu se poate randa.</summary>
    public BitmapSource? Pagina(int pagina)
    {
        if (pagina < 1 || pagina > NumarPagini)
        {
            return null;
        }

        if (_pagini.TryGetValue(pagina, out var gata))
        {
            return gata;
        }

        try
        {
            using var ms = new MemoryStream();
            if (_extensie == ".pdf")
            {
                Conversion.SavePng(ms, _continut, page: pagina - 1, options: new RenderOptions(Dpi: 110, WithAnnotations: true));
            }
            else
            {
                ms.Write(_continut);
            }

            ms.Position = 0;
            var imagine = new BitmapImage();
            imagine.BeginInit();
            imagine.CacheOption = BitmapCacheOption.OnLoad;
            imagine.StreamSource = ms;
            imagine.EndInit();
            imagine.Freeze();
            _pagini[pagina] = imagine;
            return imagine;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private int SafePageCount()
    {
        try
        {
            return Conversion.GetPageCount(_continut);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 0;
        }
    }
}
