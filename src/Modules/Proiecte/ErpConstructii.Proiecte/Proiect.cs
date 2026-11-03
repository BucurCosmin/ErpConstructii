using ErpConstructii.SharedKernel;

namespace ErpConstructii.Proiecte;

/// <summary>Ciclul de viață al unui proiect (șantier).</summary>
public enum StareProiect
{
    Ofertare,
    Contractat,
    InExecutie,
    ReceptieLaTerminare,
    PerioadaGarantie,
    Inchis,
    Pierdut,
}

/// <summary>
/// Proiectul este axa costurilor (REQ-GEN-01): oferta, bugetul, consumurile, pontajul,
/// situațiile și facturile se leagă toate de un proiect și de articolele lui de deviz.
/// Structura obiect → categorie → articol stă în modulul Devize, versionată.
/// </summary>
public sealed class Proiect : EntitateFirma
{
    private Proiect() { }

    public Proiect(string cod, string denumire, string beneficiar, string? cuiBeneficiar, string adresaSantier)
    {
        if (string.IsNullOrWhiteSpace(cod))
        {
            throw new ArgumentException("Codul proiectului este obligatoriu.", nameof(cod));
        }

        Cod = cod.Trim().ToUpperInvariant();
        Denumire = denumire.Trim();
        Beneficiar = beneficiar.Trim();
        CuiBeneficiar = cuiBeneficiar?.Trim();
        AdresaSantier = adresaSantier.Trim();
        Stare = StareProiect.Ofertare;
    }

    public string Cod { get; private set; } = string.Empty;
    public string Denumire { get; private set; } = string.Empty;
    public string Beneficiar { get; private set; } = string.Empty;
    public string? CuiBeneficiar { get; private set; }
    public string AdresaSantier { get; private set; } = string.Empty;
    public StareProiect Stare { get; private set; }
    public Guid? ManagerProiectId { get; private set; }
    public DateOnly? DataInceput { get; private set; }
    public DateOnly? TermenContractual { get; private set; }

    public void AlocaManager(Guid utilizatorId) => ManagerProiectId = utilizatorId;

    public Rezultat Contracteaza(DateOnly dataInceput, DateOnly termen)
    {
        if (Stare != StareProiect.Ofertare)
        {
            return Rezultat.Esuat(Eroare.Validare("Doar un proiect în ofertare poate fi contractat."));
        }

        if (termen < dataInceput)
        {
            return Rezultat.Esuat(Eroare.Validare("Termenul contractual este înaintea datei de început."));
        }

        DataInceput = dataInceput;
        TermenContractual = termen;
        Stare = StareProiect.Contractat;
        return Rezultat.Ok();
    }

    public void IncepeExecutia() => Stare = StareProiect.InExecutie;
}
