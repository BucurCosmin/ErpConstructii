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
        versiune.SeteazaPoliticaRotunjire(deviz.PoliticaRotunjire);
        var moneda = versiune.SeteazaMoneda(deviz.Moneda, deviz.Curs);
        if (moneda.Esec)
        {
            throw new ArgumentException(moneda.Eroare.Mesaj, nameof(deviz));
        }

        versiune.SeteazaAntet(deviz.Antet?.Obiectiv, deviz.Antet?.Obiect, deviz.Antet?.StadiuFizic, deviz.TotaluriDeclarateJson);

        foreach (var c in deviz.Capitole)
        {
            AdaugaCapitol(versiune, c, null);
        }

        return versiune;
    }

    private static void AdaugaCapitol(VersiuneDeviz versiune, CapitolCanonic c, CapitolDeviz? parinte)
    {
        var capitol = versiune.AdaugaCapitol(c.Cod, c.Denumire, c.Tip, parinte, c.CodWbs, c.Subcontractat);
        foreach (var a in c.Articole)
        {
            var articol = capitol.AdaugaArticol(a.Simbol, a.Denumire, a.UnitateMasura, a.Cantitate, a.PretUnitar);
            articol.SeteazaSursa(a.Tip, a.SimbolBaza, a.CodWbs);
            if (a.PretOfertaUnitar is { } pu)
            {
                articol.SeteazaPretOferta(pu, a.ValoareOferta ?? pu * a.Cantitate);
            }

            foreach (var r in a.Consumuri)
            {
                articol.AdaugaConsum(r.CodResursa, r.Denumire, r.Tip, r.UnitateMasura, r.ConsumUnitar, r.PretUnitar, r.CantitateTotala, r.TipDedus);
            }
        }

        foreach (var s in c.Subcapitole)
        {
            AdaugaCapitol(versiune, s, capitol);
        }
    }
}
