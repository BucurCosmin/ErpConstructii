using System.Security.Cryptography;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Facturi;

/// <summary>
/// Originalul unei facturi primite, păstrat neschimbat (REQ-FCT-11): arhiva ZIP / XML-ul din SPV (cu semnătura ANAF)
/// sau PDF-ul furnizorului, cu amprenta SHA-256 (protecție la importul dublu). Se mută în modulul Documente când există.
/// </summary>
public sealed class DocumentFactura : EntitateFirma
{
    public const int DimensiuneMaxima = 15_000_000;

    private DocumentFactura() { }

    public DocumentFactura(string numeFisier, string tipContinut, byte[] continut)
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
