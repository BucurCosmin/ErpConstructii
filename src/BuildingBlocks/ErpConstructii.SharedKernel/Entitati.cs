namespace ErpConstructii.SharedKernel;

/// <summary>
/// Bază pentru toate entitățile. Id-ul este un GUID versiunea 7 (ordonat în timp),
/// generat în aplicație, ca indexurile PostgreSQL să nu se fragmenteze.
/// </summary>
public abstract class Entitate
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}

/// <summary>
/// Entitate care aparține unei firme (tenant). Toate tabelele de business implementează
/// această interfață: coloana <c>firma_id</c> este verificată de politica Row-Level Security
/// din PostgreSQL (vezi db/rls-template.sql) și completată automat la inserare.
/// </summary>
public interface IApartineFirmei
{
    Guid FirmaId { get; }

    /// <summary>Setat o singură dată, la inserare, de interceptorul de salvare.</summary>
    void SeteazaFirma(Guid firmaId);
}

/// <summary>Câmpuri de audit simplu, completate de interceptorul de salvare.</summary>
public interface IAuditabil
{
    DateTimeOffset CreatLa { get; set; }
    Guid? CreatDe { get; set; }
    DateTimeOffset? ModificatLa { get; set; }
    Guid? ModificatDe { get; set; }
}

/// <summary>
/// Bază pentru entitățile de business: aparțin unei firme și sunt auditate.
/// </summary>
public abstract class EntitateFirma : Entitate, IApartineFirmei, IAuditabil
{
    public Guid FirmaId { get; private set; }

    public DateTimeOffset CreatLa { get; set; }
    public Guid? CreatDe { get; set; }
    public DateTimeOffset? ModificatLa { get; set; }
    public Guid? ModificatDe { get; set; }

    public void SeteazaFirma(Guid firmaId)
    {
        if (FirmaId != Guid.Empty && FirmaId != firmaId)
        {
            throw new InvalidOperationException("Firma unei entități nu se poate schimba după creare.");
        }

        FirmaId = firmaId;
    }
}

/// <summary>
/// Documentele aprobate sau facturate devin needitabile indiferent de rolul utilizatorului
/// (cerința REQ-DRP-07). Corecția se face prin storno sau versiune nouă.
/// </summary>
public interface IDocumentCuStare
{
    bool EsteBlocat { get; }
}

/// <summary>
/// Corectura unui document blocat (ex. o factură primită înregistrată cu date greșite): entitate separată, append-only,
/// cu valorile vechi și noi, motivul și autorul. Documentul blocat se poate modifica DOAR în aceeași salvare în care se
/// adaugă corectura lui (verificat de interceptorul de audit); altfel rămâne needitabil.
/// </summary>
public interface ICorectieDocument
{
    Guid DocumentCorectatId { get; }
}

/// <summary>
/// Entitate care NU intră în jurnalul de modificări (date derivate, recalculate din alte înregistrări jurnalizate:
/// ex. soldul de stoc, care rezultă din mișcări).
/// </summary>
public interface IFaraJurnal;
