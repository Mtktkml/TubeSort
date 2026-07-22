using System;
using System.Collections.Generic;
using System.Diagnostics;
using TubeSort.Core;

// Performans testi, iki tablo:
//   Tablo 1 — N=3..10: cozulebilir ornek (2 bos) hamle/durum/sure,
//             cozulemez ornek (1 bos) durum/sure.
//   Tablo 2 — 2 bos tuple cozulemez tahta avi: boyut basina en fazla
//             30.000 deneme ya da 45 s; ilk bulunanin kanit maliyeti.
// Tohumlar sabittir: her calistirmada ayni tahtalar, ayni sayilar.
var rng = new Random(7);
const int Budget = 1_000_000;
const int HuntBudget = 2_000_000;

Board Generate(Random r, int colors, int capacity, int empties)
{
    var units = new List<int>();
    for (int c = 0; c < colors; c++)
        for (int u = 0; u < capacity; u++)
            units.Add(c);

    for (int i = units.Count - 1; i > 0; i--)
    {
        int j = r.Next(i + 1);
        (units[i], units[j]) = (units[j], units[i]);
    }

    var tubes = new List<Tube>();
    for (int t = 0; t < colors; t++)
    {
        var tube = new Tube(capacity);
        for (int u = 0; u < capacity; u++)
            tube.Push(units[t * capacity + u]);
        tubes.Add(tube);
    }
    for (int e = 0; e < empties; e++)
        tubes.Add(new Tube(capacity));

    return new Board(tubes);
}

string BoardText(Board b)
{
    var parts = new List<string>();
    for (int t = 0; t < b.TubeCount; t++)
        parts.Add($"[{string.Join(",", b[t].Liquid)}]");
    return string.Join(" ", parts);
}

// Istenen karara sahip bir tahta arar; bulunca (tahta, rapor, sure) doner.
// Sure yalnizca eslesen tahtanin Solve cagrisinin suresidir.
(Board, SolveReport, double)? Find(int colors, int cap, int empties,
    SolveVerdict want, int tries, double timeLimitSec, out int outOfBudgetCount)
{
    outOfBudgetCount = 0;
    var wall = Stopwatch.StartNew();

    for (int i = 0; i < tries && wall.Elapsed.TotalSeconds < timeLimitSec; i++)
    {
        Board board = Generate(rng, colors, cap, empties);
        if (want == SolveVerdict.Unsolvable && !board.HasAnyValidMove) continue;

        var sw = Stopwatch.StartNew();
        SolveReport report = Solver.Solve(board, maxStates: Budget);
        sw.Stop();

        if (report.Verdict == SolveVerdict.OutOfBudget) outOfBudgetCount++;
        if (report.Verdict == want) return (board, report, sw.Elapsed.TotalMilliseconds);
    }

    return null;
}

// JIT isinmasi: ilk cagri derleme maliyeti icermesin.
// (Ana tohumdan uretilir: orijinal tablo 1 kosusuyla ayni dizi korunur.)
Solver.Solve(Generate(rng, 3, 3, 2));

var totalTimer = Stopwatch.StartNew();

Console.WriteLine("================ TABLO 1 — Ana benchmark ================");

for (int n = 3; n <= 10; n++)
{
    Console.WriteLine($"\n=== {n} renk x {n} kapasite ===");

    // --- Cozulebilir ornek: 2 bosla basla, bulunamazsa bos sayisini artir ---
    bool found = false;
    for (int empties = 2; empties <= n && !found; empties++)
    {
        var hit = Find(n, n, empties, SolveVerdict.Solvable,
            tries: 300, timeLimitSec: 60, out int oob);

        if (hit.HasValue)
        {
            var (board, report, ms) = hit.Value;
            Console.WriteLine(
                $"COZULEBILIR  bos={empties}  cozum={report.Solution.Count} hamle  " +
                $"durum={report.StatesVisited}  sure={ms:F1} ms  (butce asan deneme: {oob})");
            Console.WriteLine($"  ornek: {BoardText(board)}");
            found = true;
        }
        else
        {
            Console.WriteLine($"  bos={empties}: cozulebilir bulunamadi (butce asan: {oob})");
        }
    }

    // --- Cozulemez ornek: 1 bos (hamleli olsun) ---
    var uhit = Find(n, n, 1, SolveVerdict.Unsolvable,
        tries: 300, timeLimitSec: 60, out int uoob);

    if (uhit.HasValue)
    {
        var (board, report, ms) = uhit.Value;
        Console.WriteLine(
            $"COZULEMEZ    bos=1  durum={report.StatesVisited}  sure={ms:F1} ms  " +
            $"(butce asan deneme: {uoob})");
        Console.WriteLine($"  ornek: {BoardText(board)}");
    }
    else
    {
        Console.WriteLine($"COZULEMEZ    bulunamadi/kanitlanamadi (butce asan: {uoob})");
    }
}

double table1Seconds = totalTimer.Elapsed.TotalSeconds;
Console.WriteLine($"\nTablo 1 tamamlandi: {table1Seconds:F1} sn");

// ---- Tablo 2: 2 bos tuple cozulemez avi ----
// Ayri ve taze tohum: tablo 1'in rastgele tuketiminden etkilenmesin,
// sayilar tekrar uretilebilir kalsin.
var huntRng = new Random(7);

Console.WriteLine("\n================ TABLO 2 — 2 bos tuple cozulemez avi ================");
Console.WriteLine("(boyut basina en fazla 30.000 deneme ya da 45 s)\n");

for (int n = 3; n <= 10; n++)
{
    var wall = Stopwatch.StartNew();
    int tries = 0;
    bool found = false;

    while (tries < 30000 && wall.Elapsed.TotalSeconds < 45 && !found)
    {
        tries++;
        Board board = Generate(huntRng, n, n, 2);

        var sw = Stopwatch.StartNew();
        SolveReport r = Solver.Solve(board, maxStates: HuntBudget);
        sw.Stop();

        if (r.Verdict == SolveVerdict.Unsolvable)
        {
            found = true;
            Console.WriteLine(
                $"{n}x{n} bos=2  BULUNDU  deneme={tries}  " +
                $"kanit: durum={r.StatesVisited}  sure={sw.Elapsed.TotalMilliseconds:F1} ms");
            Console.WriteLine($"  ornek: {BoardText(board)}");
        }
    }

    if (!found)
        Console.WriteLine(
            $"{n}x{n} bos=2  BULUNAMADI  deneme={tries}  " +
            $"taranan sure={wall.Elapsed.TotalSeconds:F0} s" +
            (n == 3 ? "  (teorik olarak imkansiz: k(3,3)<=2)" : ""));
}

double totalSeconds = totalTimer.Elapsed.TotalSeconds;
Console.WriteLine($"\nTablo 2 tamamlandi: {totalSeconds - table1Seconds:F1} sn");
Console.WriteLine($"BITTI — toplam {totalSeconds:F1} sn (Tablo 1: {table1Seconds:F1} sn, Tablo 2: {totalSeconds - table1Seconds:F1} sn)");
