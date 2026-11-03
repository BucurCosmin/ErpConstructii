using ErpConstructii.Identitate;

namespace ErpConstructii.Teste.Unitare.Identitate;

public class RoluriPredefiniteTeste
{
    private static RoluriPredefinite.DefinitieRol Rol(string cod) => RoluriPredefinite.Toate.Single(r => r.Cod == cod);

    [Fact]
    public void Codurile_rolurilor_sunt_unice()
    {
        Assert.Equal(RoluriPredefinite.Toate.Count, RoluriPredefinite.Toate.Select(r => r.Cod).Distinct().Count());
    }

    [Fact]
    public void Matricea_respecta_documentul()
    {
        var aprobareSituatii = Permisiuni.Pentru(ModulErp.Situatii, Actiune.Aprobare);
        var creareSituatii = Permisiuni.Pentru(ModulErp.Situatii, Actiune.Creare);

        // Directorul aprobă situațiile, managerul de proiect le întocmește (E), nu le aprobă.
        Assert.Contains(aprobareSituatii, Rol("director").ToatePermisiunile());
        Assert.Contains(creareSituatii, Rol("manager_proiect").ToatePermisiunile());
        Assert.DoesNotContain(aprobareSituatii, Rol("manager_proiect").ToatePermisiunile());

        // Șeful de șantier editează șantierul, nu vede ofertarea.
        Assert.Contains(Permisiuni.Pentru(ModulErp.Santier, Actiune.Editare), Rol("sef_santier").ToatePermisiunile());
        Assert.DoesNotContain(Permisiuni.Pentru(ModulErp.Ofertare, Actiune.Vizualizare), Rol("sef_santier").ToatePermisiunile());
    }

    [Fact]
    public void Salariile_le_vad_doar_directorul_si_HR()
    {
        var cuSalarii = RoluriPredefinite.Toate.Where(r => r.ToatePermisiunile().Contains(Permisiuni.Salarii)).Select(r => r.Cod).Order().ToList();
        Assert.Equal(new[] { "director", "hr" }, cuSalarii);
    }
}

public class EvaluatorPermisiuniTeste
{
    private static readonly Guid Firma = Guid.NewGuid();
    private static readonly Guid AltaFirma = Guid.NewGuid();
    private static readonly Guid P1 = Guid.NewGuid();
    private static readonly Guid P2 = Guid.NewGuid();
    private const string Vede = "proiecte.vizualizare";

    private static EvaluatorPermisiuni Evaluator(params Domeniu[] domenii) =>
        new(domenii.Select(d => new AtribuireEfectiva(d, new HashSet<string> { Vede })).ToList());

    [Fact]
    public void Atribuirea_pe_firma_acopera_orice_proiect_al_firmei()
    {
        var e = Evaluator(new Domeniu(Firma));
        Assert.True(e.Are(Vede, new Domeniu(Firma, P1)));
        Assert.False(e.Are(Vede, new Domeniu(AltaFirma, P1)));
        Assert.Null(e.ProiecteAccesibile(Vede, Firma));
    }

    [Fact]
    public void Atribuirea_pe_proiect_acopera_doar_acel_proiect()
    {
        var e = Evaluator(new Domeniu(Firma, P1));
        Assert.True(e.Are(Vede, new Domeniu(Firma, P1)));
        Assert.False(e.Are(Vede, new Domeniu(Firma, P2)));
        Assert.Equal(new[] { P1 }, e.ProiecteAccesibile(Vede, Firma)!.ToArray());
        Assert.Empty(e.ProiecteAccesibile(Vede, AltaFirma)!);
    }

    [Fact]
    public void Atribuirile_expirate_sunt_ignorate()
    {
        var rol = new Rol(Firma, "test", "Test", [Vede]);
        var utilizator = Guid.NewGuid();
        var expirata = new Atribuire(utilizator, rol.Id, new Domeniu(Firma), new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var e = EvaluatorPermisiuni.Construieste([expirata], new Dictionary<Guid, Rol> { [rol.Id] = rol }, new DateOnly(2026, 11, 3));

        Assert.False(e.Are(Vede, new Domeniu(Firma)));
    }

    [Fact]
    public void Cine_a_creat_documentul_nu_il_poate_aproba()
    {
        var u = Guid.NewGuid();
        Assert.False(EvaluatorPermisiuni.PoateAprobaDocumentCreatDe(u, u));
        Assert.True(EvaluatorPermisiuni.PoateAprobaDocumentCreatDe(Guid.NewGuid(), u));
    }
}
