namespace ErpConstructii.Devize.Import;

/// <summary>
/// Transformă un <see cref="DevizCanonic"/> validat într-o <see cref="VersiuneDeviz"/> nouă (ciornă)
/// (REQ-IMP-03, pasul „Confirmare”). Maparea codurilor de resurse la nomenclatoarele ERP
/// (pasul 4) se face separat, după ce versiunea există.
/// </summary>
public static class ConvertorDevizCanonic
{
    public static VersiuneDeviz CaVersiune(
        DevizCanonic deviz,
        Guid proiectId,
        TipVersiuneDeviz tip,
        int numar,
        string formatId,
        Guid? fisierSursaId,
        Calcul.ParametriRecapitulatie parametriImpliciti)
    {
        var versiune = new VersiuneDeviz(proiectId, tip, numar, deviz.Denumire);
        versiune.SeteazaSursa($"import:{formatId}", fisierSursaId);
        versiune.SeteazaParametri(deviz.Parametri ?? parametriImpliciti);

        foreach (var o in deviz.Obiecte)
        {
            var obiect = versiune.AdaugaObiect(o.Cod, o.Denumire);
            foreach (var c in o.Categorii)
            {
                var categorie = obiect.AdaugaCategorie(c.Cod, c.Denumire);
                foreach (var a in c.Articole)
                {
                    var articol = categorie.AdaugaArticol(a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretUnitar);
                    foreach (var r in a.Consumuri)
                    {
                        articol.AdaugaConsum(r.CodResursa, r.Denumire, r.Tip, r.UnitateMasura, r.ConsumUnitar, r.PretUnitar);
                    }
                }
            }
        }

        return versiune;
    }
}
