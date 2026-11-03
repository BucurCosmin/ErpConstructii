using ErpConstructii.SharedKernel;

namespace ErpConstructii.Formulare;

/// <summary>O versiune imuabilă a unui șablon Word aparținând unei firme.</summary>
public sealed class SablonFormular : EntitateFirma
{
    private SablonFormular() { }

    public string CodTip { get; private set; } = string.Empty;
    public int Versiune { get; private set; }
    public string NumeFisier { get; private set; } = string.Empty;
    public string HashSha256 { get; private set; } = string.Empty;
    public byte[] Continut { get; private set; } = [];
    public bool Activ { get; private set; }

    public SablonFormular(string codTip, int versiune, string numeFisier, byte[] continut, string hashSha256)
    {
        if (string.IsNullOrWhiteSpace(codTip))
            throw new ArgumentException("Codul tipului de formular este obligatoriu.", nameof(codTip));
        if (versiune < 1)
            throw new ArgumentOutOfRangeException(nameof(versiune));
        if (string.IsNullOrWhiteSpace(numeFisier))
            throw new ArgumentException("Numele fișierului este obligatoriu.", nameof(numeFisier));
        if (continut.Length == 0)
            throw new ArgumentException("Șablonul nu poate fi gol.", nameof(continut));
        if (string.IsNullOrWhiteSpace(hashSha256))
            throw new ArgumentException("Amprenta SHA-256 este obligatorie.", nameof(hashSha256));

        CodTip = codTip;
        Versiune = versiune;
        NumeFisier = Path.GetFileName(numeFisier);
        Continut = continut.ToArray();
        HashSha256 = hashSha256;
        Activ = true;
    }

    public void Activeaza() => Activ = true;

    public void Dezactiveaza() => Activ = false;
}

/// <summary>Document extern păstrat ca ciornă pentru verificare ulterioară în modulul de destinație.</summary>
public sealed class ImportFormular : EntitateFirma
{
    private ImportFormular() { }

    public string CodTip { get; private set; } = string.Empty;
    public string NumeFisier { get; private set; } = string.Empty;
    public string HashSha256 { get; private set; } = string.Empty;
    public byte[] Original { get; private set; } = [];
    public string DateJson { get; private set; } = "{}";
    public string Stare { get; private set; } = "DeVerificat";

    public ImportFormular(string codTip, string numeFisier, string hashSha256, byte[] original, string dateJson)
    {
        CodTip = codTip;
        NumeFisier = Path.GetFileName(numeFisier);
        HashSha256 = hashSha256;
        Original = original.ToArray();
        DateJson = dateJson;
    }
}
