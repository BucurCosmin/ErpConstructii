// Aplicația de test a importului (D-28).
//
//   dotnet run --project tools/ErpConstructii.TestImport                       toate fișierele din Sabloane/
//   dotnet run --project tools/ErpConstructii.TestImport -- fact2              doar fișierele care conțin „fact2” în nume
//   dotnet run --project tools/ErpConstructii.TestImport -- --confirma fact2   citirea curentă devine valoarea confirmată
//   dotnet run --project tools/ErpConstructii.TestImport -- --fara-cache       recitește (OCR) și fișierele deja citite
//
// Rezultatul: Sabloane/_rezultate/raport.html (paginile lângă ce s-a citit, diferențele față de valorile confirmate).
// Valorile confirmate: Sabloane/_asteptat/<fișier>.json, verificate pe document de om; un câmp lipsă nu se verifică.
// Codul de ieșire e 1 dacă vreun fișier confirmat se citește altfel: fiecare schimbare a interpretorilor se verifică
// pe toate documentele, nu doar pe cel corectat.
using System.Diagnostics;
using System.Runtime.Versioning;
using ErpConstructii.TestImport;

[assembly: SupportedOSPlatform("windows")]
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macos")]

var radacina = Cai.Radacina();
var sabloane = Path.Combine(radacina, "Sabloane");
var optiuni = Optiuni.Din(args);
if (!Directory.Exists(sabloane))
{
    Console.Error.WriteLine($"Nu există folderul {sabloane}.");
    return 2;
}

var fisiere = Directory.EnumerateFiles(sabloane, "*", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}_", StringComparison.Ordinal))
    .Where(f => Citire.Accepta(f))
    .Where(f => optiuni.Filtru is null || Path.GetFileName(f).Contains(optiuni.Filtru, StringComparison.OrdinalIgnoreCase))
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToList();
if (fisiere.Count == 0)
{
    Console.Error.WriteLine("Niciun fișier de citit.");
    return 2;
}

var citire = new Citire(radacina, Path.Combine(sabloane, "_rezultate", "cache"), optiuni.FaraCache, optiuni.CuiFirma);
var asteptat = Path.Combine(sabloane, "_asteptat");
var rezultate = new List<RezultatFisier>();
foreach (var f in fisiere)
{
    var nume = Path.GetRelativePath(sabloane, f).Replace('\\', '/');
    var ceas = Stopwatch.StartNew();
    Console.Write($"{nume} … ");
    var citit = await citire.CitesteAsync(f, nume);
    var fisierAsteptat = Path.Combine(asteptat, Cai.NumeJson(nume));

    if (optiuni.Confirma && citit.Eroare is null)
    {
        Directory.CreateDirectory(asteptat);
        File.WriteAllText(fisierAsteptat, Json.Scrie(citit.Fisier));
    }

    var confirmat = File.Exists(fisierAsteptat) ? Json.Citeste(File.ReadAllText(fisierAsteptat)) : null;
    var diferente = confirmat is null ? null : Comparare.Diferente(citit.Fisier, confirmat);
    rezultate.Add(new RezultatFisier(nume, f, citit, confirmat, diferente));

    var stare = citit.Eroare is not null ? $"EROARE: {citit.Eroare}"
        : diferente is null ? "neconfirmat"
        : diferente.Count == 0 ? "OK"
        : $"{diferente.Count} diferențe";
    Console.WriteLine($"{stare} ({ceas.Elapsed.TotalSeconds:0.0} s{(citit.DinCache ? ", text din cache" : string.Empty)})");
    foreach (var d in diferente ?? [])
    {
        Console.WriteLine($"    {d.Document} · {d.Camp}: citit „{d.Citit}”, confirmat „{d.Confirmat}”");
    }
}

var raport = Path.Combine(sabloane, "_rezultate", "raport.html");
Raport.Scrie(raport, rezultate, optiuni);
var ok = rezultate.Count(r => r.Diferente is { Count: 0 });
var gresite = rezultate.Count(r => r.Diferente is { Count: > 0 } || r.Citit.Eroare is not null);
var neconfirmate = rezultate.Count(r => r.Confirmat is null && r.Citit.Eroare is null);
Console.WriteLine();
Console.WriteLine($"{rezultate.Count} fișiere: {ok} corecte, {gresite} cu diferențe sau erori, {neconfirmate} fără valori confirmate.");
Console.WriteLine($"Raport: {raport}");
return gresite > 0 ? 1 : 0;
