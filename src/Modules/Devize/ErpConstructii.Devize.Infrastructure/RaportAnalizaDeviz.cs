using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErpConstructii.Devize.Analize;
using ErpConstructii.Formulare;

namespace ErpConstructii.Devize.Infrastructure;

/// <summary>Construiește previzualizarea verificabilă și datele raportului PDF, fără a salva rezultatul.</summary>
public static class RaportAnalizaDeviz
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static PrevizualizareAnalizaDevizDto Previzualizeaza(VersiuneDeviz deviz, RegistruAnalizori registru)
    {
        var versiune = new VersiuneAnalizaDto(deviz.Id, deviz.ProiectId, deviz.Denumire, deviz.Tip.ToString(), deviz.Numar, deviz.Stare.ToString());
        var articole = deviz.Articole().OrderBy(a => a.CheieStabila)
            .Select(a => new ArticolAnalizaDto(a.CheieStabila, a.Simbol, a.CodWbsSursa, a.UnitateMasura)).ToArray();
        var analizori = registru.Analizeaza(new ContextAnaliza(deviz, new ConfiguratieAnaliza()),
                new HashSet<string>(StringComparer.Ordinal) { "A2", "A5" })
            .OrderBy(r => r.Cod, StringComparer.Ordinal)
            .Select(r => new RezultatAnalizorDto(r.Cod, r.Versiune,
                r.Constatari.Select(c => new ConstatareAnalizaDto(c.Cod, c.Severitate, c.Incredere,
                        c.Articole.Order().ToArray(), c.Explicatie,
                        c.Dovezi.OrderBy(d => d.Nume, StringComparer.Ordinal).ThenBy(d => d.Valoare, StringComparer.Ordinal).ToArray(),
                        c.Corectura is { } corectura ? new CorecturaAnalizaDto(corectura.Articol, corectura.CantitateVeche, corectura.CantitateNoua) : null,
                        c.ImpactCostDirect, c.ImpactOferta, c.Amprenta()))
                    .OrderBy(c => JsonSerializer.Serialize(c), StringComparer.Ordinal).ToArray(),
                r.Limitari.Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
        var date = JsonSerializer.SerializeToUtf8Bytes(new { versiune, articole, analizori });
        return new(versiune, articole, analizori, Convert.ToHexString(SHA256.HashData(date)));
    }

    public static (AspectDocument Aspect, DateFormular Date) PentruPdf(PrevizualizareAnalizaDevizDto rezultat, DateTimeOffset moment)
    {
        var articole = rezultat.Articole.ToDictionary(a => a.CheieStabila);
        string Articol(Guid cheie) => articole.TryGetValue(cheie, out var a)
            ? $"{a.Simbol}{(string.IsNullOrEmpty(a.CodWbs) ? string.Empty : $" ({a.CodWbs})")} [{cheie}]"
            : cheie.ToString();
        var randuri = new List<IReadOnlyDictionary<string, string>>();

        void Adauga(string categorie, string text)
        {
            // MigraDoc nu împarte un rând între pagini și nu poate încadra cuvinte arbitrar de lungi.
            var pregatit = new StringBuilder();
            var lungimeCuvant = 0;
            foreach (var caracter in text)
            {
                if (char.IsWhiteSpace(caracter))
                {
                    pregatit.Append(' ');
                    lungimeCuvant = 0;
                    continue;
                }
                if (lungimeCuvant >= 35 && !char.IsLowSurrogate(caracter))
                {
                    pregatit.Append(' ');
                    lungimeCuvant = 0;
                }
                pregatit.Append(caracter);
                lungimeCuvant++;
            }
            var continut = pregatit.ToString();
            if (continut.Length == 0)
                randuri.Add(new Dictionary<string, string> { ["categorie"] = categorie, ["detalii"] = string.Empty });
            for (var start = 0; start < continut.Length;)
            {
                var lungime = Math.Min(160, continut.Length - start);
                if (start + lungime < continut.Length && char.IsHighSurrogate(continut[start + lungime - 1]))
                    lungime--;
                randuri.Add(new Dictionary<string, string>
                {
                    ["categorie"] = start == 0 ? categorie : string.Empty,
                    ["detalii"] = continut.Substring(start, lungime),
                });
                start += lungime;
            }
        }

        Adauga("Atenție", "Verificare experimentală, parțială și nevalidată pentru producție. Constatările sunt suspiciuni, nu greșeli confirmate. Nu modifică devizul și nu blochează aprobarea.");
        foreach (var analizor in rezultat.Analizori)
        {
            Adauga("Analizor", $"{analizor.Cod} v{analizor.Versiune}: {analizor.Constatari.Count} constatări");
            foreach (var limitare in analizor.Limitari)
                Adauga("Limitare", limitare);
            foreach (var c in analizor.Constatari)
            {
                Adauga("Constatare", $"{c.Cod} · {c.Severitate} · încredere {c.Incredere.ToString("G29", Ro)}");
                Adauga("Explicație", c.Explicatie);
                foreach (var cheie in c.Articole)
                    Adauga("Articol", Articol(cheie));
                if (c.Corectura is { } corectura)
                {
                    var um = articole.TryGetValue(corectura.Articol, out var articol) ? articol.UnitateMasura : string.Empty;
                    Adauga("De verificat", $"{Articol(corectura.Articol)}: {corectura.CantitateVeche.ToString("G29", Ro)} → {corectura.CantitateNoua.ToString("G29", Ro)} {um}");
                }
                if (c.ImpactCostDirect is { } cost)
                    Adauga("Impact", $"Cost direct estimat: {cost.ToString("N2", Ro)} lei");
                if (c.ImpactOferta is { } oferta)
                    Adauga("Impact", $"Ofertă estimată: {oferta.ToString("N2", Ro)} lei");
                foreach (var dovada in c.Dovezi)
                    Adauga($"Dovadă: {dovada.Nume}", dovada.Valoare);
            }
        }
        if (rezultat.Analizori.All(a => a.Constatari.Count == 0))
            Adauga("Rezultat", "Nu au fost găsite suspiciuni de regulile rulate.");

        var aspect = new AspectDocument("Raport de verificare a devizului", null, $"Generat la {moment.ToUniversalTime():dd.MM.yyyy HH:mm} UTC",
            [new("Deviz", "deviz"), new("Versiune", "versiune"), new("Proiect ID", "proiect"), new("Versiune ID", "id")],
            "linii", [new("categorie", "Categorie", 2), new("detalii", "Detalii", 8)], [], []);
        var campuri = new Dictionary<string, string>
        {
            ["deviz"] = rezultat.Versiune.Denumire,
            ["versiune"] = $"{rezultat.Versiune.Tip} nr. {rezultat.Versiune.Numar} · {rezultat.Versiune.Stare}",
            ["proiect"] = rezultat.Versiune.ProiectId.ToString(),
            ["id"] = rezultat.Versiune.Id.ToString(),
        };
        return (aspect, new DateFormular(campuri, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["linii"] = randuri }));
    }
}
