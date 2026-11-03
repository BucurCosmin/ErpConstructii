using System.Security.Cryptography;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Aprovizionare;

/// <summary>
/// Documentul original al unei note importate (scanare PDF, poză, Word), arhivat neschimbat, cu amprenta SHA-256
/// (REQ-FRM-07): dovada vizei de pe hârtie și protecție la importul dublu. Se mută în modulul Documente când există.
/// </summary>
public sealed class DocumentOriginal : EntitateFirma
{
    public const int DimensiuneMaxima = 15_000_000;

    private DocumentOriginal() { }

    public DocumentOriginal(string numeFisier, string tipContinut, byte[] continut)
    {
        NumeFisier = Path.GetFileName(numeFisier);
        TipContinut = tipContinut;
        Continut = continut;
        DimensiuneOcteti = continut.Length;
        HashSha256 = Amprenta(continut);
    }

    public string NumeFisier { get; private set; } = string.Empty;
    public string TipContinut { get; private set; } = string.Empty;
    public string HashSha256 { get; private set; } = string.Empty;
    public long DimensiuneOcteti { get; private set; }
    public byte[] Continut { get; private set; } = [];

    public static string Amprenta(byte[] continut) => Convert.ToHexString(SHA256.HashData(continut));
}
