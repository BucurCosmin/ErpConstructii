namespace ErpConstructii.Devize.Import.Isdp;

public enum TipRaportIsdp
{
    Necunoscut,
    DevizCuResurse,
    ListaCantitatiF3,
}

/// <summary>
/// Recunoaște tipul raportului ISDP dintr-o foaie (folosit de PoateCiti al importatorului).
/// Nu se bazează pe numele fișierului: utilizatorii le redenumesc.
/// </summary>
public static class DetectorIsdp
{
    public static (TipRaportIsdp Tip, double Incredere, string Motiv) Detecteaza(FoaieCalcul f)
    {
        bool wbs = false, simbol = false, devizOferta = false, f3 = false, capitol = false, isdp = false, sectiuneFinanciara = false;
        for (var r = 0; r < f.NrRanduri; r++)
        {
            for (var c = 0; c < f.NrColoane(r); c++)
            {
                var t = TextIsdp.Normalizeaza(f.Text(r, c));
                if (t.Length == 0) continue;
                wbs |= t == "wbs";
                simbol |= t == "simbol";
                devizOferta |= t.StartsWith("deviz oferta", StringComparison.Ordinal);
                f3 |= t.StartsWith("f3 - lista", StringComparison.Ordinal);
                capitol |= t.StartsWith("capitolul de lucrari", StringComparison.Ordinal);
                sectiuneFinanciara |= t == "sectiunea financiara";
                isdp |= t.Contains("isdp", StringComparison.Ordinal) || t.Contains("devize.ro", StringComparison.Ordinal);
            }
        }

        if (wbs && simbol)
        {
            return (TipRaportIsdp.DevizCuResurse, devizOferta ? 0.95 : 0.7,
                devizOferta ? "Antet WBS/Simbol și titlu „Deviz Oferta”." : "Antet WBS/Simbol.");
        }

        if (f3 && capitol)
        {
            return (TipRaportIsdp.ListaCantitatiF3, isdp || sectiuneFinanciara ? 0.95 : 0.75, "Formular F3 - Lista cu cantități de lucrări.");
        }

        return (TipRaportIsdp.Necunoscut, 0, "Nu este un raport ISDP cunoscut.");
    }
}
