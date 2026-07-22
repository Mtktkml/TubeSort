using System;
using System.Collections.Generic;
using System.Text;

namespace TubeSort.Core
{
    /// <summary>Çözülebilirlik testinin sonucu.</summary>
    public enum SolveVerdict
    {
        /// <summary>Çözüm bulundu; Solution listesi dolu.</summary>
        Solvable,

        /// <summary>Erişilebilir tüm durumlar tarandı, çözüm yok. Kanıtlanmış çıkmaz.</summary>
        Unsolvable,

        /// <summary>
        /// Bütçe (düğüm/derinlik) aşıldı; çözülebilirlik bilinmiyor.
        /// "Çözülemez" ile karıştırılmamalı: bütçeyle elenen tahtalar ayrı
        /// raporlanmazsa level havuzu sessizce kolaya yamulur.
        /// </summary>
        OutOfBudget,
    }

    /// <summary>Solver'ın raporu: karar, gezilen durum sayısı ve varsa çözüm yolu.</summary>
    public readonly struct SolveReport
    {
        public readonly SolveVerdict Verdict;

        /// <summary>Genişletilen benzersiz durum sayısı (performans gözlemi için).</summary>
        public readonly int StatesVisited;

        /// <summary>
        /// Bulunan çözüm yolu; yalnız Solvable'da dolu, diğerlerinde null.
        /// En kısa çözüm DEĞİLDİR: DFS ilk bulduğu yolu döner.
        /// </summary>
        public readonly IReadOnlyList<PourResult> Solution;

        public SolveReport(SolveVerdict verdict, int statesVisited, IReadOnlyList<PourResult> solution)
        {
            Verdict = verdict;
            StatesVisited = statesVisited;
            Solution = solution;
        }

        public bool IsSolvable => Verdict == SolveVerdict.Solvable;
    }

    /// <summary>
    /// Tahtanın çözülebilir olup olmadığını karar veren arama: DFS + budama +
    /// kanonik durum önbelleği. Level üreticinin generate-and-test akışında
    /// kullanılır: üret → Solve → Unsolvable/OutOfBudget ise at, yeniden üret.
    ///
    /// Çözülebilirlik testi herhangi bir çözüm arar, en kısasını değil;
    /// bu yüzden BFS/A* değil DFS. Durum uzayı sonlu ve önbellek tekrar
    /// ziyareti engellediği için arama derinlik sınırı olmadan da sonlanır;
    /// sınırlar yalnızca bütçe görevi görür.
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
            var context = new SearchContext(maxStates, maxDepth);
            bool found = Dfs(board.Clone(), context);

            if (found)
                return new SolveReport(SolveVerdict.Solvable, context.StatesVisited, context.Path.ToArray());

            SolveVerdict verdict = context.BudgetHit ? SolveVerdict.OutOfBudget : SolveVerdict.Unsolvable;
            return new SolveReport(verdict, context.StatesVisited, null);
        }

        private sealed class SearchContext
        {
            public readonly HashSet<string> Visited = new HashSet<string>();
            public readonly List<PourResult> Path = new List<PourResult>();
            public readonly int MaxStates;
            public readonly int MaxDepth;
            public int StatesVisited;
            public bool BudgetHit;

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

        private static bool Dfs(Board board, SearchContext context)
        {
            if (board.IsSolved) return true;

            if (context.Path.Count >= context.MaxDepth)
            {
                context.BudgetHit = true;
                return false;
            }

            // Ters hamle ve döngüler burada elenir: geri dönülen her durum
            // önbellekte zaten vardır.
            if (!context.Visited.Add(CanonicalKey(board))) return false;

            if (context.StatesVisited >= context.MaxStates)
            {
                context.BudgetHit = true;
                return false;
            }
            context.StatesVisited++;

            var moves = new List<Move>();
            CollectMoves(board, moves);

            foreach (Move move in moves)
            {
                PourResult result = board.Pour(move.From, move.To);
                context.Path.Add(result);

                if (Dfs(board, context)) return true;

                board.UndoPour(result);
                context.Path.RemoveAt(context.Path.Count - 1);
            }

            return false;
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
