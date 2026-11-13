using ErpConstructii.Stocuri;

namespace ErpConstructii.Teste.Unitare.Stocuri;

/// <summary>Ordonarea materialelor din stoc după o denumire liberă (resursa din deviz, D-55).</summary>
public class PropunereMaterialTeste
{
    private static readonly ArticolCandidat Cuie = new(Guid.NewGuid(), "MAT-00374", "CUIE 3.0MMXL 60MM FORTE", "kg");
    private static readonly ArticolCandidat CuieMari = new(Guid.NewGuid(), "MAT-00375", "CUIE 4.0MMXL 100MM FORTE", "kg");
    private static readonly ArticolCandidat Capac = new(Guid.NewGuid(), "MAT-00147", "CAP TRONCONIC D1000 d625 g120h670", "buc");
    private static readonly ArticolCandidat Teava = new(Guid.NewGuid(), "MAT-00119", "TEAVA 1300X12", "ml");

    [Fact]
    public void Cuvintele_intregi_si_dimensiunile_comune_aleg_materialul_potrivit()
    {
        var r = PotrivireArticol.Ordoneaza("Cuie cu cap conic      tip A   3,0 x 60", "kg", [Capac, CuieMari, Teava, Cuie]);

        // CUIE 3.0 x 60: „CUIE” (4) + numerele 3 și 60 (2 × 3) = 10, + aceeași UM. CUIE 4.0 x 100: doar „CUIE” = 4.
        // „CAP” (3) se potrivește, dar „CONIC” nu e cuvântul „TRONCONIC”: 3. Țeava nu are nimic comun.
        Assert.Equal([Cuie, CuieMari, Capac], r);
    }

    [Fact]
    public void Inceputul_de_cuvant_gaseste_materialul_la_cautare() =>
        Assert.Equal([Cuie, CuieMari], PotrivireArticol.Ordoneaza("cui", null, [Teava, CuieMari, Cuie]));

    [Fact]
    public void Fara_cuvinte_semnificative_nu_propune_nimic() =>
        Assert.Empty(PotrivireArticol.Ordoneaza("cu de tip", null, [Cuie, Teava]));
}
