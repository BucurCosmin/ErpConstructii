namespace ErpConstructii.Devize;

/// <param name="CheieStabila">Identitatea articolului între versiunile devizului (D-05): consumurile o referă, nu Id-ul versiunii.</param>
/// <param name="PretMaterial">Prețul unitar al materialelor din deviz (pentru comparația buget material / consum real).</param>
public sealed record ArticolDevizReferinta(Guid CheieStabila, string Simbol, string Denumire, string UnitateMasura, decimal Cantitate, decimal PretMaterial, string Obiect, string Categorie);

/// <summary>Devizul după care se urmăresc consumurile unui proiect, cu articolele lui.</summary>
public sealed record DevizReferinta(Guid VersiuneId, string Denumire, TipVersiuneDeviz Tip, StareVersiuneDeviz Stare, IReadOnlyList<ArticolDevizReferinta> Articole);

/// <summary>Contractul public al modulului Devize pentru celelalte module (regula 1). Lucrează pe firma activă (RLS).</summary>
public interface IInterogareDevize
{
    /// <summary>
    /// Versiunea de referință a proiectului: bugetul de execuție aprobat, altfel contractul aprobat, altfel cea mai recentă
    /// versiune aprobată, altfel cea mai recentă ciornă (ca șantierul să poată lucra și înainte de aprobare).
    /// </summary>
    Task<DevizReferinta?> DevizDeReferintaAsync(Guid proiectId, CancellationToken ct = default);
}
