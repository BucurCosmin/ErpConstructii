using ErpConstructii.Devize.Calcul;

namespace ErpConstructii.Devize.Import.Boq;

/// <summary>Câmpurile unui rând de BOQ, recunoscute după antetul coloanei.</summary>
public enum CampBoq
{
    Numar,
    Cod,
    Denumire,
    Specificatie,
    UnitateMasura,
    Cantitate,
    Total,
    Observatii,
}

/// <param name="Prefixe">Începuturile antetului (după <see cref="ErpConstructii.SharedKernel.ValoriFoaie.Cheie"/>: litere mici, fără diacritice).</param>
/// <param name="Exact">Antete care trebuie să fie exact așa (ex. „no”, „unit”), ca să nu prindă alte coloane.</param>
public sealed record ColoanaProfil(CampBoq Camp, IReadOnlyList<string> Prefixe, IReadOnlyList<string>? Exact = null);

/// <summary>O componentă de preț a BOQ-ului: coloanele „preț unitar / valoare” sub un antet comun (ex. „Material (A)”).</summary>
/// <param name="Cheie">Identificatorul stabil al componentei (ex. „A”), folosit de alegerea utilizatorului.</param>
/// <param name="Tip">Componenta devizului; null = nu e clar (utilizatorul alege la import).</param>
public sealed record ComponentaProfil(string Cheie, string Eticheta, IReadOnlyList<string> Prefixe, TipResursa? Tip);

/// <summary>
/// Profilul unui format de BOQ (D-58): cum se recunosc antetul, coloanele, componentele de preț, foaia de rezumat și
/// rândurile de total. Motorul (<see cref="CititorBoq"/>) e același; un format nou = un profil nou.
/// </summary>
/// <param name="FoiRezumat">Numele foii de rezumat (ex. „BOQ”), după cheie.</param>
/// <param name="EticheteTotal">Textul rândului de total al unei secțiuni.</param>
/// <param name="EticheteTotalGeneral">Textul rândului de total al rezumatului.</param>
/// <param name="PrefixTitlu">Începutul titlului foii (ex. „Bill of Quantities - ”), înaintea numelui obiectului / secțiunii.</param>
public sealed record ProfilBoq(
    string Id,
    string Denumire,
    IReadOnlyList<ColoanaProfil> Coloane,
    IReadOnlyList<ComponentaProfil> Componente,
    IReadOnlyList<string> FoiRezumat,
    IReadOnlyList<string> EticheteTotal,
    IReadOnlyList<string> EticheteTotalGeneral,
    string PrefixTitlu,
    string MonedaImplicita)
{
    /// <summary>
    /// BOQ-ul de ofertă pe obiecte cu prețuri Material (A) / Labour (B) / Expense (C), rate + amount (formatul EPC al
    /// pachetului 703C1R: B5, B6, B10, PCA16A). Materialul și manopera sunt clare; „Expense” se alege la import.
    /// </summary>
    public static readonly ProfilBoq MaterialLabourExpense = new(
        "boq-mle",
        "BOQ ofertă (Material / Labour / Expense)",
        [
            new(CampBoq.Numar, [], ["no", "no.", "nr", "nr."]),
            new(CampBoq.Cod, ["work ite", "item code", "cod articol", "code"]),
            new(CampBoq.Denumire, ["item description", "description", "denumire"]),
            new(CampBoq.Specificatie, ["specification", "specificatie"]),
            new(CampBoq.UnitateMasura, ["unit"], ["um", "u.m."]),
            new(CampBoq.Cantitate, ["quantity", "qty", "cantitate"]),
            new(CampBoq.Total, ["total"]),
            new(CampBoq.Observatii, ["remark", "observat"]),
        ],
        [
            new("A", "Material (A)", ["material"], TipResursa.Material),
            new("B", "Labour (B)", ["labour", "labor", "manopera"], TipResursa.Manopera),
            new("C", "Expense (C)", ["expense", "cheltuieli"], null),
        ],
        ["boq", "summary", "rezumat"],
        ["total"],
        ["grand total", "total general"],
        "bill of quantities",
        "EUR");

    /// <summary>Profilurile cunoscute, în ordinea încercării.</summary>
    public static IReadOnlyList<ProfilBoq> Toate { get; } = [MaterialLabourExpense];
}
