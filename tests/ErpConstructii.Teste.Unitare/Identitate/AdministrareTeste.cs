using ErpConstructii.Identitate;

namespace ErpConstructii.Teste.Unitare.Identitate;

public class RevocareAtribuireTeste
{
    private static readonly DateOnly Azi = new(2026, 11, 3);

    private static Atribuire Noua(DateOnly de, DateOnly? pana = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new Domeniu(Guid.NewGuid()), de, pana);

    [Fact]
    public void Atribuirea_inceputa_se_incheie_ieri_si_ramane_in_istoric()
    {
        var a = Noua(new DateOnly(2026, 1, 1));

        Assert.Equal(RezultatRevocare.Incheiata, a.Revoca(Azi));
        Assert.Equal(new DateOnly(2026, 11, 2), a.ValabilPana);
        Assert.False(a.EsteValabila(Azi));
        Assert.True(a.EsteValabila(new DateOnly(2026, 11, 2)));
    }

    [Theory]
    [InlineData(2026, 11, 3)] // începe azi
    [InlineData(2026, 12, 1)] // începe în viitor
    public void Atribuirea_care_nu_a_produs_efecte_se_sterge(int an, int luna, int zi)
    {
        Assert.Equal(RezultatRevocare.DeSters, Noua(new DateOnly(an, luna, zi)).Revoca(Azi));
    }

    [Fact]
    public void Atribuirea_deja_expirata_nu_se_modifica()
    {
        var a = Noua(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(RezultatRevocare.DejaIncheiata, a.Revoca(Azi));
        Assert.Equal(new DateOnly(2026, 6, 30), a.ValabilPana);
    }
}

public class RegulaAdministrareTeste
{
    private static readonly DateOnly Azi = new(2026, 11, 3);
    private static readonly Guid Firma = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Bogdan = Guid.NewGuid();

    private static readonly Rol Admin = new(Firma, "admin_firma", "Administrator", [RegulaAdministrare.PermisiuneAdministrator]);
    private static readonly Rol Ofertant = new(Firma, "ofertant", "Ofertant", ["ofertare.creare"]);
    private static readonly Dictionary<Guid, Rol> Roluri = new() { [Admin.Id] = Admin, [Ofertant.Id] = Ofertant };

    [Fact]
    public void Firma_cu_un_administrator_activ_pe_toata_firma_e_valida()
    {
        var atribuiri = new[] { new Atribuire(Ana, Admin.Id, new Domeniu(Firma), new DateOnly(2026, 1, 1)) };
        Assert.True(RegulaAdministrare.AreAdministrator(Firma, atribuiri, Roluri, new HashSet<Guid> { Ana }, Azi));
    }

    [Fact]
    public void Administratorul_dezactivat_nu_conteaza()
    {
        var atribuiri = new[] { new Atribuire(Ana, Admin.Id, new Domeniu(Firma), new DateOnly(2026, 1, 1)) };
        Assert.False(RegulaAdministrare.AreAdministrator(Firma, atribuiri, Roluri, new HashSet<Guid>(), Azi));
    }

    [Fact]
    public void Administrarea_doar_pe_un_proiect_nu_face_din_utilizator_administrator_al_firmei()
    {
        var atribuiri = new[] { new Atribuire(Ana, Admin.Id, new Domeniu(Firma, ProiectId: Guid.NewGuid()), new DateOnly(2026, 1, 1)) };
        Assert.False(RegulaAdministrare.AreAdministrator(Firma, atribuiri, Roluri, new HashSet<Guid> { Ana }, Azi));
    }

    [Fact]
    public void Revocarea_rolului_ultimului_administrator_lasa_firma_fara_administrator()
    {
        var adminAna = new Atribuire(Ana, Admin.Id, new Domeniu(Firma), new DateOnly(2026, 1, 1));
        var atribuiri = new[] { adminAna, new Atribuire(Bogdan, Ofertant.Id, new Domeniu(Firma), new DateOnly(2026, 1, 1)) };
        var activi = new HashSet<Guid> { Ana, Bogdan };

        adminAna.Revoca(Azi);

        Assert.False(RegulaAdministrare.AreAdministrator(Firma, atribuiri, Roluri, activi, Azi));
    }

    [Fact]
    public void Doar_administratorul_de_sistem_numeste_administratori_de_sistem()
    {
        Assert.False(RegulaAdministrare.PoateGestionaTipul(TipUtilizator.AdministratorFirma, TipUtilizator.AdministratorSistem));
        Assert.True(RegulaAdministrare.PoateGestionaTipul(TipUtilizator.AdministratorSistem, TipUtilizator.AdministratorSistem));
        Assert.True(RegulaAdministrare.PoateGestionaTipul(TipUtilizator.AdministratorFirma, TipUtilizator.Intern));
    }

    [Fact]
    public void Contul_cu_acces_la_alta_firma_poate_fi_resetat_doar_de_administratorul_de_sistem()
    {
        var alta = Guid.NewGuid();
        Assert.True(RegulaAdministrare.PoateModificaContul(TipUtilizator.AdministratorFirma, Firma, [Firma]));
        Assert.False(RegulaAdministrare.PoateModificaContul(TipUtilizator.AdministratorFirma, Firma, [Firma, alta]));
        Assert.True(RegulaAdministrare.PoateModificaContul(TipUtilizator.AdministratorSistem, Firma, [Firma, alta]));
    }
}

public class CatalogPermisiuniTeste
{
    [Fact]
    public void Catalogul_contine_toate_permisiunile_rolurilor_predefinite()
    {
        var folosite = RoluriPredefinite.Toate.SelectMany(r => r.ToatePermisiunile());
        Assert.Empty(CatalogPermisiuni.Necunoscute(folosite));
    }

    [Fact]
    public void Catalogul_are_11_module_cu_cate_6_actiuni_si_3_campuri_sensibile()
    {
        // 11 × 6 + 3 = 69
        Assert.Equal(11, CatalogPermisiuni.Module.Count);
        Assert.All(CatalogPermisiuni.Module, m => Assert.Equal(6, m.Permisiuni.Count));
        Assert.Equal(69, CatalogPermisiuni.Toate.Count);
    }

    [Fact]
    public void Permisiunile_inventate_sunt_semnalate()
    {
        Assert.Equal(["proiecte.stergere"], CatalogPermisiuni.Necunoscute(["proiecte.vizualizare", "proiecte.stergere"]));
    }

    [Fact]
    public void Firma_noua_primeste_toate_rolurile_predefinite()
    {
        var firma = Guid.NewGuid();
        var roluri = RoluriPredefinite.PentruFirma(firma).ToList();

        Assert.Equal(RoluriPredefinite.Toate.Count, roluri.Count);
        Assert.All(roluri, r => Assert.Equal(firma, r.FirmaId));
        Assert.Contains(roluri, r => r.Cod == "admin_firma" && r.Permisiuni.Contains(RegulaAdministrare.PermisiuneAdministrator));
    }
}
