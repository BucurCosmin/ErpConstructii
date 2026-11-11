using ErpConstructii.Identitate;
using Xunit;

namespace ErpConstructii.Teste.Unitare.Identitate;

/// <summary>Diagnosticul codurilor TOTP refuzate: ceasul serverului decalat față de telefon.</summary>
public class DecalajCeasTotpTeste
{
    // Vectorul de test din RFC 6238 (anexa B), SHA-1: cheia ASCII „12345678901234567890” = Base32 GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ.
    // La T = 59 s codul pe 8 cifre e 94287082, deci pe 6 cifre 287082.
    private const string Cheie = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Fact]
    public void Codul_corespunde_vectorului_din_RFC_6238() =>
        Assert.Equal("287082", DecalajCeasTotp.Cod(Cheie, DateTimeOffset.FromUnixTimeSeconds(59)));

    [Fact]
    public void Cheia_scrisa_cu_litere_mici_si_spatii_da_acelasi_cod() =>
        Assert.Equal("287082", DecalajCeasTotp.Cod("gezd gnbv gy3t qojq gezd gnbv gy3t qojq", DateTimeOffset.FromUnixTimeSeconds(59)));

    [Fact]
    public void Serverul_in_urma_cu_5_minute_da_decalaj_pozitiv_de_300_de_secunde()
    {
        var telefon = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var cod = DecalajCeasTotp.Cod(Cheie, telefon)!;

        // Serverul arată 08:55:00: trebuie adăugate 5 × 60 = 300 s.
        Assert.Equal(300, DecalajCeasTotp.Decalaj(Cheie, cod, telefon.AddMinutes(-5)));
        // Serverul înainte cu 3 ore: −10.800 s.
        Assert.Equal(-10800, DecalajCeasTotp.Decalaj(Cheie, cod, telefon.AddHours(3)));
        Assert.Equal(0, DecalajCeasTotp.Decalaj(Cheie, cod, telefon));
    }

    [Fact]
    public void Codul_care_nu_e_numeric_sau_cheia_invalida_nu_dau_decalaj()
    {
        var acum = DateTimeOffset.FromUnixTimeSeconds(59);
        Assert.Null(DecalajCeasTotp.Decalaj(Cheie, "12a456", acum));
        Assert.Null(DecalajCeasTotp.Decalaj("cheie!invalida", "287082", acum));
    }
}
