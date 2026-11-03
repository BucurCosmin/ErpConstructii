using System.Net.Http.Headers;

namespace ErpConstructii.Formulare.Infrastructure;

/// <summary>
/// Conversie DOCX → PDF prin Gotenberg (container cu LibreOffice headless; decizia D-10: gratuit).
/// Rulează ca serviciu separat (docker-compose.yml), ca o conversie lentă sau blocată să nu
/// afecteze procesul aplicației. Endpoint: POST /forms/libreoffice/convert, câmpul „files”.
/// </summary>
public sealed class ConvertorPdfGotenberg(HttpClient http) : IConvertorPdf
{
    private const string TipDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public async Task<byte[]> InPdfAsync(byte[] docx, string numeFisier, CancellationToken ct = default)
    {
        using var formular = new MultipartFormDataContent();
        var fisier = new ByteArrayContent(docx);
        fisier.Headers.ContentType = new MediaTypeHeaderValue(TipDocx);
        var nume = numeFisier.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? numeFisier : numeFisier + ".docx";
        formular.Add(fisier, "files", nume);

        using var raspuns = await http.PostAsync("forms/libreoffice/convert", formular, ct);
        raspuns.EnsureSuccessStatusCode();
        return await raspuns.Content.ReadAsByteArrayAsync(ct);
    }
}
