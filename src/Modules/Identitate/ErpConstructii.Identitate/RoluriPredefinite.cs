namespace ErpConstructii.Identitate;

/// <summary>
/// Rolurile predefinite și matricea implicită rol × modul, exact ca în documentul de proiect
/// (secțiunea „Utilizatori, autentificare și drepturi”). La crearea unei firme, rolurile se
/// copiază în firmă și pot fi apoi ajustate de administratorul firmei (REQ-DRP-02).
///
/// Restricțiile din paranteze din document (ex. „E (cereri)” la șeful de șantier) se rafinează
/// la implementarea fiecărui modul, prin permisiuni mai fine; aici se dă nivelul pe modul.
/// </summary>
public static class RoluriPredefinite
{
    public sealed record DefinitieRol(
        string Cod,
        string Denumire,
        IReadOnlyDictionary<ModulErp, NivelAcces> Matrice,
        IReadOnlyList<string> PermisiuniSuplimentare)
    {
        public IEnumerable<string> ToatePermisiunile() =>
            Matrice.SelectMany(m => Permisiuni.DinNivel(m.Key, m.Value))
                .Concat(PermisiuniSuplimentare)
                .Distinct();
    }

    private const NivelAcces F = NivelAcces.Fara;
    private const NivelAcces V = NivelAcces.Vizualizare;
    private const NivelAcces E = NivelAcces.Editare;
    private const NivelAcces A = NivelAcces.Aprobare;

    // Ordinea coloanelor: Ofertare, Contracte, Buget, Santier, Situatii, Aprovizionare, Utilaje, Financiar, HR
    public static readonly IReadOnlyList<DefinitieRol> Toate =
    [
        Rol("director", "Director general", [A, A, A, V, A, V, V, V, V], proiecte: V,
            Permisiuni.PreturiCost, Permisiuni.Salarii),
        Rol("ofertant", "Ofertant / devizier", [E, V, V, F, F, V, F, F, F], proiecte: V,
            Permisiuni.PreturiCost),
        Rol("manager_proiect", "Manager de proiect", [V, E, E, A, E, A, V, V, F], proiecte: E,
            Permisiuni.PreturiCost),
        Rol("sef_santier", "Șef de șantier", [F, F, V, E, V, E, E, F, E], proiecte: V),
        Rol("achizitor", "Achizitor", [F, V, V, F, F, E, F, V, F], proiecte: V,
            Permisiuni.PreturiCost),
        Rol("gestionar", "Gestionar", [F, F, F, V, F, E, F, F, F], proiecte: V),
        Rol("dispecer_utilaje", "Dispecer utilaje", [F, F, F, V, F, F, E, F, F], proiecte: V),
        Rol("contabil", "Contabil", [F, V, V, F, V, V, V, E, V], proiecte: V,
            Permisiuni.PreturiCost),
        Rol("hr", "HR / salarizare", [F, F, F, V, F, F, F, F, E], proiecte: V,
            Permisiuni.Salarii, Permisiuni.DatePersonale),
        Rol("auditor", "Auditor", [V, V, V, V, V, V, V, V, V], proiecte: V,
            Permisiuni.PreturiCost),
        new DefinitieRol("admin_firma", "Administrator firmă",
            new Dictionary<ModulErp, NivelAcces> { [ModulErp.Administrare] = A },
            []),
    ];

    /// <summary>Rolurile unei firme noi, copiate din cele predefinite (REQ-DRP-02).</summary>
    public static IEnumerable<Rol> PentruFirma(Guid firmaId) =>
        Toate.Select(d => new Rol(firmaId, d.Cod, d.Denumire, d.ToatePermisiunile()));

    public static DefinitieRol? Gaseste(string cod) => Toate.FirstOrDefault(r => r.Cod == cod);

    private static DefinitieRol Rol(
        string cod,
        string denumire,
        NivelAcces[] coloane,
        NivelAcces proiecte,
        params string[] suplimentare)
    {
        ModulErp[] ordine =
        [
            ModulErp.Ofertare, ModulErp.Contracte, ModulErp.Buget, ModulErp.Santier, ModulErp.Situatii,
            ModulErp.Aprovizionare, ModulErp.Utilaje, ModulErp.Financiar, ModulErp.HrSalarii,
        ];

        if (coloane.Length != ordine.Length)
        {
            throw new ArgumentException($"Rolul {cod} trebuie să aibă {ordine.Length} coloane.", nameof(coloane));
        }

        var matrice = ordine.Zip(coloane).ToDictionary(p => p.First, p => p.Second);
        matrice[ModulErp.Proiecte] = proiecte;
        return new DefinitieRol(cod, denumire, matrice, suplimentare);
    }
}
