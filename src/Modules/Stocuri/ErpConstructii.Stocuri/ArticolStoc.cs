using System.Globalization;
using System.Text;
using ErpConstructii.SharedKernel;

namespace ErpConstructii.Stocuri;

/// <summary>
/// Un material din nomenclatorul de stoc (cod, denumire, UM). Furnizorii scriu același material diferit pe facturi;
/// legătura dintre textul de pe factură și articol se memorează în <see cref="AliasArticol"/>.
/// </summary>
public sealed class ArticolStoc : EntitateFirma
{
    private ArticolStoc() { }

    /// <summary>Cod generat (MAT-00001) sau dat de utilizator.</summary>
    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public string UnitateMasura { get; private set; } = string.Empty;
    public bool Activ { get; private set; } = true;

    public static Rezultat<ArticolStoc> Creeaza(string cod, string denumire, string unitateMasura)
    {
        var r = Valideaza(denumire, unitateMasura);
        if (r.Esec)
            return Rezultat.Esuat<ArticolStoc>(r.Eroare);
        if (string.IsNullOrWhiteSpace(cod) || cod.Trim().Length > 30)
            return Rezultat.Esuat<ArticolStoc>(Eroare.Validare("Codul articolului este obligatoriu (cel mult 30 de caractere)."));
        return Rezultat.Ok(new ArticolStoc { Cod = cod.Trim().ToUpperInvariant(), Denumire = denumire.Trim(), UnitateMasura = unitateMasura.Trim().ToLowerInvariant() });
    }

    /// <summary>Corectează denumirea sau UM (ex. articol creat automat de la recepție). Mișcările vechi își păstrează textul.</summary>
    public Rezultat Modifica(string denumire, string unitateMasura, bool activ)
    {
        var r = Valideaza(denumire, unitateMasura);
        if (r.Esec)
            return r;
        (Denumire, UnitateMasura, Activ) = (denumire.Trim(), unitateMasura.Trim().ToLowerInvariant(), activ);
        return Rezultat.Ok();
    }

    public static string CodGenerat(int numar) => $"MAT-{numar:D5}";

    private static Rezultat Valideaza(string denumire, string unitateMasura)
    {
        if (string.IsNullOrWhiteSpace(denumire) || denumire.Trim().Length > 300)
            return Rezultat.Esuat(Eroare.Validare("Denumirea articolului este obligatorie (cel mult 300 de caractere)."));
        if (string.IsNullOrWhiteSpace(unitateMasura) || unitateMasura.Trim().Length > 20)
            return Rezultat.Esuat(Eroare.Validare("Unitatea de măsură este obligatorie (cel mult 20 de caractere)."));
        return Rezultat.Ok();
    }
}

/// <summary>
/// Textul de pe factura unui furnizor (cheia normalizată) → articolul de stoc ales la recepție. Data viitoare,
/// aceeași linie de la același furnizor se potrivește singură.
/// </summary>
public sealed class AliasArticol : EntitateFirma
{
    private AliasArticol() { }

    public AliasArticol(Guid? furnizorId, string denumireFactura, Guid articolId)
    {
        FurnizorId = furnizorId;
        Cheie = PotrivireArticol.Cheie(denumireFactura);
        ArticolId = articolId;
    }

    public Guid? FurnizorId { get; private set; }
    public string Cheie { get; private set; } = string.Empty;
    public Guid ArticolId { get; private set; }
}

public sealed record ArticolCandidat(Guid Id, string Cod, string Denumire, string UnitateMasura);

public sealed record AliasCandidat(Guid? FurnizorId, string Cheie, Guid ArticolId);

public enum SursaPotrivire
{
    /// <summary>Aceeași denumire de la același furnizor, aleasă la o recepție anterioară.</summary>
    AliasFurnizor,

    /// <summary>Aceeași denumire, aleasă la recepția facturii altui furnizor.</summary>
    Alias,

    /// <summary>Denumirea articolului din nomenclator coincide (după normalizare).</summary>
    Denumire,
}

public sealed record ArticolPotrivit(ArticolCandidat Articol, SursaPotrivire Sursa);

/// <summary>Propune articolul de stoc pentru o linie de factură. Funcție pură.</summary>
public static class PotrivireArticol
{
    /// <summary>Cheia de comparare: majuscule, fără diacritice, doar litere și cifre, spații unice („Ciment CEM II/A-S 42,5R” → „CIMENT CEM II A S 42 5R”).</summary>
    public static string Cheie(string denumire)
    {
        var fd = denumire.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(fd.Length);
        foreach (var ch in fd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }

        var cheie = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cheie.Length > 300 ? cheie[..300] : cheie;
    }

    public static ArticolPotrivit? Propune(string denumireFactura, Guid? furnizorId, IReadOnlyList<AliasCandidat> aliasuri, IReadOnlyList<ArticolCandidat> articole)
    {
        var cheie = Cheie(denumireFactura);
        if (cheie.Length == 0)
            return null;

        var dupaId = articole.ToDictionary(a => a.Id);
        if (furnizorId is not null && aliasuri.FirstOrDefault(a => a.FurnizorId == furnizorId && a.Cheie == cheie) is { } af && dupaId.TryGetValue(af.ArticolId, out var a1))
            return new ArticolPotrivit(a1, SursaPotrivire.AliasFurnizor);
        if (aliasuri.FirstOrDefault(a => a.Cheie == cheie) is { } al && dupaId.TryGetValue(al.ArticolId, out var a2))
            return new ArticolPotrivit(a2, SursaPotrivire.Alias);
        if (articole.FirstOrDefault(a => Cheie(a.Denumire) == cheie) is { } a3)
            return new ArticolPotrivit(a3, SursaPotrivire.Denumire);
        return null;
    }
}
