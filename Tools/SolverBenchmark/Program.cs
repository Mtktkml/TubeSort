using System;
using System.Collections.Generic;
using System.Diagnostics;
using TubeSort.Core;

// Performans testi: N renk x N kapasite tahtalar (N=3..10).
// Her boyut icin bir COZULEBILIR ve bir COZULEMEZ ornek bulunur,
// solver'in suresi ve gezdigi durum sayisi olculur.
var rng = new Random(7);
const int Budget = 1_000_000;

Board Generate(int colors, int capacity, int empties)
{
    var units = new List<int>();
    for (int c = 0; c < colors; c++)
        for (int u = 0; u < capacity; u++)
            units.Add(c);

    for (int i = units.Count - 1; i > 0; i--)
    {
        int j = rng.Next(i + 1);
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
        Board board = Generate(colors, cap, empties);
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
Solver.Solve(Generate(3, 3, 2));

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
