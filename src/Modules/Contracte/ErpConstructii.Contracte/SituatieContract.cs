using ErpConstructii.SharedKernel;

namespace ErpConstructii.Contracte;

/// <summary>O factură legată de contract (totalul fără TVA; storno = negativ).</summary>
public sealed record FacturaPeContract(Guid Id, string Numar, DateOnly Data, decimal TotalFaraTva, string Moneda);

/// <param name="ValoareActualizata">Valoarea inițială plus actele adiționale.</param>
/// <param name="Rest">Cât mai e de facturat (negativ = facturat peste contract).</param>
/// <param name="GarantieRetinuta">Garanția de bună execuție reținută din facturile de până acum (facturat × procentul contractului).</param>
/// <param name="Termen">Termenul de finalizare actualizat (ultimul act adițional care l-a schimbat).</param>
public sealed record SituatieContract(
    decimal ValoareInitiala,
    decimal ValoareActualizata,
    decimal Facturat,
    decimal Rest,
    decimal ProcentFacturat,
    decimal GarantieRetinuta,
    bool Depasit,
    DateOnly? Termen,
    bool TermenDepasit)
{
    /// <summary>Situația contractului: valoare actualizată, facturat, rest, garanție reținută, termen. Funcție pură.</summary>
    public static SituatieContract Calculeaza(
        ContractPartener contract, IEnumerable<ActAditional> acte, IEnumerable<FacturaPeContract> facturi, bool inchis, DateOnly azi)
    {
        var lista = acte.OrderBy(a => a.Numar).ToList();
        var valoare = contract.Valoare + lista.Sum(a => a.DiferentaValoare);
        // Facturile în altă monedă decât contractul nu se adună (se văd în listă).
        var facturat = facturi.Where(f => string.Equals(f.Moneda, contract.Moneda, StringComparison.OrdinalIgnoreCase)).Sum(f => f.TotalFaraTva);
        var termen = lista.LastOrDefault(a => a.DataSfarsitNoua is not null)?.DataSfarsitNoua ?? contract.DataSfarsit;
        return new SituatieContract(
            contract.Valoare,
            valoare,
            facturat,
            valoare - facturat,
            valoare > 0 ? Math.Round(facturat / valoare * 100, 1, MidpointRounding.AwayFromZero) : 0,
            Rotunjire.Lei.Aplica(facturat * contract.ProcentGarantie),
            facturat > valoare,
            termen,
            !inchis && contract.Stare == StareContract.Activ && termen is { } t && azi > t);
    }
}

// ---------------------------------------------------------------- contractele publice (regula 1)

/// <summary>Un contract, pentru legarea facturilor de servicii (modulul Facturi).</summary>
/// <param name="Deschis">Activ și neînchis: se pot lega facturi noi.</param>
public sealed record ContractPentruFactura(
    Guid Id, string Numar, string? NumarExtern, DateOnly DataSemnarii, Guid ProiectId, string ProiectCod, Guid PartenerId, string PartenerDenumire,
    string? PartenerCui, TipContract Tip, string Obiect, string Moneda, decimal ValoareActualizata, decimal ProcentGarantie, bool TaxareInversa, bool Deschis);

/// <summary>Contractul modulului Contracte pentru Facturi: contractele de care se leagă facturile de servicii.</summary>
public interface IInterogareContracte
{
    Task<ContractPentruFactura?> CautaAsync(Guid id, CancellationToken ct = default);

    /// <summary>Contractele active și neînchise ale proiectului.</summary>
    Task<IReadOnlyList<ContractPentruFactura>> DeschiseAsync(Guid proiectId, CancellationToken ct = default);
}

/// <summary>Implementat de Facturi: facturile neanulate legate de contracte. Fără modulul Facturi: niciuna.</summary>
public interface IInterogareFacturiContract
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<FacturaPeContract>>> PeContracteAsync(IReadOnlyCollection<Guid> contracte, CancellationToken ct = default);
}
