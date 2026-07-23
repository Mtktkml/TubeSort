using System;
using System.Collections.Generic;
using System.Text;

namespace TubeSort.Core
{
    /// <summary>Çözülebilirlik testinin sonucu.</summary>
    public enum SolveVerdict
    {
        /// <summary>En az bir çözüm bulundu; Solution ilk bulunan yolu taşır.</summary>
        Solvable,

        /// <summary>Erişilebilir tüm durumlar tarandı, çözüm yok. Kanıtlanmış çıkmaz.</summary>
        Unsolvable,

        /// <summary>
        /// Bütçe (düğüm/derinlik) hiç çözüm bulunamadan aşıldı; çözülebilirlik
        /// bilinmiyor. "Çözülemez" ile karıştırılmamalı: bütçeyle elenen tahtalar
        /// ayrı raporlanmazsa level havuzu sessizce kolaya yamulur.
        /// (Çözüm bulunduktan sonra aşılan bütçe kararı değiştirmez: sonuç
        /// Solvable kalır, yalnız sayım kesinliğini yitirir; bkz. CountIsExact.)
        /// </summary>
        OutOfBudget,
    }

    /// <summary>Solver'ın raporu: karar, gezilen durum ve çözüm sayısı, örnek çözüm.</summary>
    public readonly struct SolveReport
    {
        public readonly SolveVerdict Verdict;

        /// <summary>
        /// Genişletilen benzersiz durum sayısı. Uzay tüketildiği için gezinme
        /// sırasından bağımsızdır (bütçe aşılmadıysa).
        /// </summary>
        public readonly int StatesVisited;

        /// <summary>
        /// Çözüme düşen kenar sayısı: genişletilen durumlardan yapılan ve tahtayı
        /// bitiren (budanmış) hamlelerin toplamı. Aynı öz çözümün hamle-sıralaması
        /// varyantlarını tekrar saymaz; zorluk/esneklik metriği olarak kullanılır.
        /// </summary>
        public readonly int SolutionCount;

        /// <summary>
        /// true: sayım kesin (erişilebilir uzay tüketildi). false: bütçe aşıldı,
        /// SolutionCount yalnızca alt sınırdır ("en az N").
        /// </summary>
        public readonly bool CountIsExact;

        /// <summary>
        /// İlk bulunan çözüm yolu; yalnız Solvable'da dolu, diğerlerinde null.
        /// En kısa çözüm DEĞİLDİR: DFS'in ilk rastladığı yoldur (örnek/replay için).
        /// </summary>
        public readonly IReadOnlyList<PourResult> Solution;

        public SolveReport(SolveVerdict verdict, int statesVisited, int solutionCount,
            bool countIsExact, IReadOnlyList<PourResult> solution)
        {
            Verdict = verdict;
            StatesVisited = statesVisited;
            SolutionCount = solutionCount;
            CountIsExact = countIsExact;
            Solution = solution;
        }

        public bool IsSolvable => Verdict == SolveVerdict.Solvable;
    }

    /// <summary>
    /// Tahtanın çözülebilirliğine karar veren ve çözüm sayısını ölçen arama:
    /// DFS + budama + kanonik durum önbelleği. Arama ilk çözümde DURMAZ:
    /// erişilebilir durum uzayının tamamını gezer (her kanonik durum en fazla
    /// bir kez genişletilir) ve çözüme düşen her kenarı sayar. Level üreticinin
    /// generate-and-test akışında kullanılır: üret → Solve →
    /// Unsolvable/OutOfBudget ise at; SolutionCount zorluk metriğine girer.
    ///
    /// Uzay tüketildiği için sonuçlar (karar, durum ve çözüm sayısı) gezinme
    /// sırasından bağımsızdır. Durum uzayı sonlu ve önbellek tekrar ziyareti
    /// engellediği için arama derinlik sınırı olmadan da sonlanır; sınırlar
    /// yalnızca bütçe görevi görür.
    /// </summary>
    public static class Solver
    {
        /// <summary>Varsayılan düğüm bütçesi. Aşılırsa OutOfBudget döner.</summary>
        public const int DefaultMaxStates = 200_000;

        /// <summary>
        /// Varsayılan derinlik (hamle zinciri) bütçesi. Her çözülebilir tahtanın
        /// polinom uzunlukta çözümü kanıtlı olduğundan bu sınır cömert bir tavandır;
        /// pratikte çözümler yüzlerce hamleyi geçmez.
        /// </summary>
        public const int DefaultMaxDepth = 1024;

        /// <summary>Tahtayı klonlayıp arar; verilen tahta değişmez.</summary>
        public static SolveReport Solve(
            Board board,
            int maxStates = DefaultMaxStates,
            int maxDepth = DefaultMaxDepth)
        {
            // Baştan çözülmüş tahta: çözüme düşen kenar yok ama tahta çözülü.
            if (board.IsSolved)
                return new SolveReport(SolveVerdict.Solvable, 0, 0, true, Array.Empty<PourResult>());

            var context = new SearchContext(maxStates, maxDepth);
            Dfs(board.Clone(), context);

            bool exact = !context.BudgetHit;
            if (context.SolutionCount > 0)
            {
                return new SolveReport(SolveVerdict.Solvable, context.StatesVisited,
                    context.SolutionCount, exact, context.FirstSolution);
            }

            SolveVerdict verdict = context.BudgetHit ? SolveVerdict.OutOfBudget : SolveVerdict.Unsolvable;
            return new SolveReport(verdict, context.StatesVisited, 0, exact, null);
        }

        private sealed class SearchContext
        {
            public readonly HashSet<string> Visited = new HashSet<string>();
            public readonly List<PourResult> Path = new List<PourResult>();
            public readonly int MaxStates;
            public readonly int MaxDepth;
            public int StatesVisited;
            public int SolutionCount;
            public bool BudgetHit;

            /// <summary>İlk bulunan çözümün fotoğrafı (Path o anda kopyalanır).</summary>
            public PourResult[] FirstSolution;

            public SearchContext(int maxStates, int maxDepth)
            {
                MaxStates = maxStates;
                MaxDepth = maxDepth;
            }
        }

        private readonly struct Move
        {
            public readonly int From;
            public readonly int To;

            public Move(int from, int to)
            {
                From = from;
                To = to;
            }
        }

        private static void Dfs(Board board, SearchContext context)
        {
            if (context.Path.Count >= context.MaxDepth)
            {
                context.BudgetHit = true;
                return;
            }

            // Ters hamle ve döngüler burada elenir: geri dönülen her durum
            // önbellekte zaten vardır.
            if (!context.Visited.Add(CanonicalKey(board))) return;

            if (context.StatesVisited >= context.MaxStates)
            {
                context.BudgetHit = true;
                return;
            }
            context.StatesVisited++;

            var moves = new List<Move>();
            CollectMoves(board, moves);

            foreach (Move move in moves)
            {
                PourResult result = board.Pour(move.From, move.To);
                context.Path.Add(result);

                // Çözüm KENARDA tespit edilir ve çözülmüş durum genişletilmez:
                // sayım "çözüme düşen kenar" tanımıyla bire bir kalır. Arama
                // ilk çözümde durmaz; uzayın kalanını gezmeye devam eder.
                if (board.IsSolved)
                {
                    context.SolutionCount++;
                    if (context.FirstSolution == null)
                        context.FirstSolution = context.Path.ToArray();
                }
                else
                {
                    Dfs(board, context);
                }

                board.UndoPour(result);
                context.Path.RemoveAt(context.Path.Count - 1);
            }
        }

        /// <summary>
        /// Geçerli hamleleri budayarak toplar. Sıralama bilinçli: renk
        /// birleştiren dolu hedefler önce, boş tüpe taşıma sona — DFS çözümü
        /// böyle daha erken bulur.
        /// </summary>
        private static void CollectMoves(Board board, List<Move> moves)
        {
            var emptyMoves = new List<Move>();
            var seenEmptyCapacities = new HashSet<int>();

            for (int from = 0; from < board.TubeCount; from++)
            {
                Tube source = board[from];
                if (source.IsEmpty) continue;

                // Budama: tamamlanmış tüpten dökmek çözümü ancak uzatır.
                if (source.IsComplete) continue;

                // Tüm içerik tek renk mi? (boş hedefe taşıma budaması için)
                bool singleColor = source.TopSegmentLength == source.Count;

                seenEmptyCapacities.Clear();

                for (int to = 0; to < board.TubeCount; to++)
                {
                    if (to == from) continue;
                    Tube target = board[to];

                    if (target.IsEmpty)
                    {
                        // Aynı kapasiteli boş tüpler birbirinin yerine geçer:
                        // her kapasiteden yalnız ilki denenir.
                        if (!seenEmptyCapacities.Add(target.Capacity)) continue;

                        // Budama: tek renkli içeriği aynı kapasiteli boş tüpe
                        // taşımak kanonik olarak aynı durumu üretir.
                        if (singleColor && target.Capacity == source.Capacity) continue;

                        if (board.IsValidMove(from, to)) emptyMoves.Add(new Move(from, to));
                    }
                    else if (board.IsValidMove(from, to))
                    {
                        moves.Add(new Move(from, to));
                    }
                }
            }

            moves.AddRange(emptyMoves);
        }

        /// <summary>
        /// Tahtanın kanonik anahtarı: tüp içerikleri metinleştirilir, sıralanır,
        /// birleştirilir. Tüp sırası permütasyonları ve eşdeğer boş tüpler böylece
        /// tek duruma iner — durum uzayı azaltmasının asıl kaynağı budur.
        /// </summary>
        private static string CanonicalKey(Board board)
        {
            var tubeKeys = new string[board.TubeCount];
            var sb = new StringBuilder();

            for (int i = 0; i < board.TubeCount; i++)
            {
                sb.Length = 0;
                Tube tube = board[i];
                sb.Append(tube.Capacity).Append(':');
                for (int u = 0; u < tube.Count; u++)
                    sb.Append(tube.Liquid[u]).Append(',');

                tubeKeys[i] = sb.ToString();
            }

            Array.Sort(tubeKeys, StringComparer.Ordinal);
            return string.Join("|", tubeKeys);
        }
    }
}
