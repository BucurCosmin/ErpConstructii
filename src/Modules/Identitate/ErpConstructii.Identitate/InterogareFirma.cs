using ErpConstructii.SharedKernel;

namespace ErpConstructii.Identitate;

/// <summary>Datele firmei active folosite de documentele altor module (antet, verificarea cumpărătorului din facturi).</summary>
/// <param name="Cont">Contul bancar principal (în lei, dacă există), pentru antetul documentelor.</param>
public sealed record DateFirmaDocument(
    Guid Id, string Denumire, string? Cui, string? NrRegCom, Adresa Adresa,
    string? Telefon = null, string? Fax = null, string? Email = null, ContBancar? Cont = null);

/// <summary>Contract public: firma activă a cererii, fără dependență de infrastructura Identitate.</summary>
public interface IInterogareFirma
{
    Task<DateFirmaDocument?> FirmaActivaAsync(CancellationToken ct = default);
}
