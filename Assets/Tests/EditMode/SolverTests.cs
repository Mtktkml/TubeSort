using NUnit.Framework;
using TubeSort.Core;

namespace TubeSort.Tests
{
    public class SolverTests
    {
        private const int Red = 0;
        private const int Yellow = 1;
        private const int Blue = 2;

        [Test]
        public void Solve_SolvedBoardNeedsNoMoves()
        {
            var board = new Board(new[]
            {
                new Tube(4, Red, Red, Red, Red),
                new Tube(4)
            });

            SolveReport report = Solver.Solve(board);

            Assert.AreEqual(SolveVerdict.Solvable, report.Verdict);
            Assert.AreEqual(0, report.Solution.Count, "Çözülmüş tahta için hamle gerekmemeli");
            Assert.AreEqual(0, report.SolutionCount, "Çözülü başlayan tahtada çözüme düşen kenar yok");
            Assert.IsTrue(report.CountIsExact);
        }

        [Test]
        public void Solve_FindsSolutionAndItReplaysToWin()
        {
            // İki renk iç içe, iki boş tüp: çözülebilir ama birkaç hamle ister.
            var board = new Board(new[]
            {
                new Tube(4, Red, Yellow, Red, Yellow),
                new Tube(4, Yellow, Red, Yellow, Red),
                new Tube(4),
                new Tube(4)
            });

            SolveReport report = Solver.Solve(board);

            Assert.AreEqual(SolveVerdict.Solvable, report.Verdict);
            Assert.IsNotNull(report.Solution);
            Assert.Greater(report.Solution.Count, 0);
            Assert.Greater(report.SolutionCount, 0, "En az bir çözüm kenarı sayılmalı");
            Assert.IsTrue(report.CountIsExact, "Bütçe aşılmadı: sayım kesin olmalı");

            // Solve orijinal tahtayı değiştirmemeli.
            Assert.AreEqual(4, board[0].Count);
            Assert.AreEqual(4, board[1].Count);
            Assert.IsTrue(board[2].IsEmpty);

            // Çözüm gerçek olmalı: baştan oynandığında tahta kazanılmalı.
            foreach (PourResult move in report.Solution)
            {
                PourResult replayed = board.Pour(move.FromIndex, move.ToIndex);
                Assert.IsTrue(replayed.Success, "Çözümdeki her hamle oynanabilir olmalı");
                Assert.AreEqual(move.Amount, replayed.Amount, "Hamle miktarı çözümle aynı olmalı");
            }

            Assert.IsTrue(board.IsSolved);
        }

        [Test]
        public void Solve_ProvesDeadlockWhenNoMoveExists()
        {
            // Hiçbir üst renk eşleşmiyor ve boş tüp yok.
            var board = new Board(new[]
            {
                new Tube(2, Red, Yellow),
                new Tube(2, Yellow, Blue),
                new Tube(2, Blue, Red)
            });

            SolveReport report = Solver.Solve(board);

            Assert.AreEqual(SolveVerdict.Unsolvable, report.Verdict);
            Assert.IsNull(report.Solution);
            Assert.AreEqual(0, report.SolutionCount);
            Assert.IsTrue(report.CountIsExact, "Uzay tüketildi: 'sıfır çözüm' kesin bir sayımdır");
        }

        [Test]
        public void Solve_ProvesDeadlockDespiteAvailableMoves()
        {
            // HasAnyValidMove'un yakalayamadığı asıl durum: hamle var ama
            // kazanmak imkansız (sarı 3 birim, hiçbir tüpü dolduramaz).
            var board = new Board(new[]
            {
                new Tube(4, Red, Yellow),
                new Tube(4, Yellow, Red, Yellow)
            });

            Assert.IsTrue(board.HasAnyValidMove, "Test kurgusu: hamle mevcut olmalı");

            SolveReport report = Solver.Solve(board);

            Assert.AreEqual(SolveVerdict.Unsolvable, report.Verdict);
            Assert.AreEqual(0, report.SolutionCount);
        }

        [Test]
        public void Solve_ReportsOutOfBudgetInsteadOfUnsolvable()
        {
            // Çözülebilir bir tahta, ama bütçe tek duruma yetiyor:
            // sonuç "çözülemez" DEĞİL "bilinmiyor" olmalı.
            var board = new Board(new[]
            {
                new Tube(4, Red, Yellow, Red, Yellow),
                new Tube(4, Yellow, Red, Yellow, Red),
                new Tube(4),
                new Tube(4)
            });

            SolveReport report = Solver.Solve(board, maxStates: 1);

            Assert.AreEqual(SolveVerdict.OutOfBudget, report.Verdict);
            Assert.AreEqual(0, report.SolutionCount);
            Assert.IsFalse(report.CountIsExact, "Bütçe aşıldı: sayım kesin sayılamaz");
        }

        [Test]
        public void Solve_CountsAllSolutionEdges()
        {
            // Elle sayılabilir kurgu (kapasite 2): [R] [R] [Y] [Y].
            // Kanonik durumlar: S0 = başlangıç, S1 = kırmızılar birleşmiş,
            // S2 = sarılar birleşmiş. Çözüme düşen kenarlar: S1'den sarıları
            // birleştiren 2 hamle (2→3, 3→2) + S2'den kırmızıları birleştiren
            // 2 hamle (0→1, 1→0) = toplam 4. Gezilen durum: S0, S1, S2 = 3.
            var board = new Board(new[]
            {
                new Tube(2, Red),
                new Tube(2, Red),
                new Tube(2, Yellow),
                new Tube(2, Yellow)
            });

            SolveReport report = Solver.Solve(board);

            Assert.AreEqual(SolveVerdict.Solvable, report.Verdict);
            Assert.AreEqual(4, report.SolutionCount, "El ile sayım: 4 çözüm kenarı");
            Assert.AreEqual(3, report.StatesVisited, "El ile sayım: S0, S1, S2");
            Assert.IsTrue(report.CountIsExact);
        }

        [Test]
        public void Solve_StaysSolvableWhenBudgetHitsAfterFirstSolution()
        {
            // Aynı elle sayılabilir kurgu, ama bütçe 2 duruma yetiyor: S1'deki
            // çözümler bulunur, S2 genişletilemeden bütçe dolar. Karar Solvable
            // KALMALI; yalnız sayım "en az" düzeyine iner (CountIsExact=false).
            var board = new Board(new[]
            {
                new Tube(2, Red),
                new Tube(2, Red),
                new Tube(2, Yellow),
                new Tube(2, Yellow)
            });

            SolveReport report = Solver.Solve(board, maxStates: 2);

            Assert.AreEqual(SolveVerdict.Solvable, report.Verdict,
                "Çözüm bulunduysa bütçe aşımı kararı OutOfBudget'a çevirmemeli");
            Assert.Greater(report.SolutionCount, 0);
            Assert.Less(report.SolutionCount, 4, "Kesilen arama tüm kenarları sayamamalı");
            Assert.IsFalse(report.CountIsExact);
            Assert.IsNotNull(report.Solution, "İlk çözümün yolu yine de raporlanmalı");
        }

        [Test]
        public void Solve_AgreesWithNaiveSolverOnRandomBoards()
        {
            // Budamaların ve kanonikleştirmenin çözüm kaçırmadığının asıl kanıtı:
            // budamasız, kanonik anahtarsız naif DFS ile 200 rastgele tahtada
            // aynı karara varılmalı. Tohum sabit: test deterministik.
            var rng = new System.Random(12345);
            int solvableCount = 0;
            int unsolvableCount = 0;

            for (int i = 0; i < 200; i++)
            {
                int colors = 2 + rng.Next(2);   // 2-3 renk
                int capacity = 2 + rng.Next(2); // 2-3 kapasite
                int empties = rng.Next(3);      // 0-2 boş tüp

                Board board = RandomBoard(rng, colors, capacity, empties);

                SolveReport report = Solver.Solve(board);
                Assert.AreNotEqual(SolveVerdict.OutOfBudget, report.Verdict,
                    "Küçük tahtada bütçe aşılmamalı");

                bool naive = NaiveSolvable(board.Clone());

                Assert.AreEqual(naive, report.IsSolvable,
                    $"Tahta #{i} (renk={colors}, kapasite={capacity}, boş={empties}): " +
                    $"naif={naive}, solver={report.IsSolvable} — bir budama çözüm kaçırıyor olabilir");

                if (report.IsSolvable) solvableCount++;
                else unsolvableCount++;
            }

            // Test boşa dönmesin: iki karar da gerçekten üretilmiş olmalı.
            Assert.Greater(solvableCount, 0, "Hiç çözülebilir tahta üretilmedi");
            Assert.Greater(unsolvableCount, 0, "Hiç çözülemez tahta üretilmedi");
        }

        /// <summary>
        /// Rastgele tahta: her renkten tam 'capacity' birim karıştırılıp dolu
        /// tüplere dağıtılır, sonuna boş tüpler eklenir. Oyunun üreteceği
        /// "hepsi dolu ya da boş" başlangıç biçimiyle aynı.
        /// </summary>
        private static Board RandomBoard(System.Random rng, int colors, int capacity, int emptyTubes)
        {
            var units = new System.Collections.Generic.List<int>();
            for (int c = 0; c < colors; c++)
                for (int u = 0; u < capacity; u++)
                    units.Add(c);

            // Fisher-Yates karıştırma
            for (int i = units.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (units[i], units[j]) = (units[j], units[i]);
            }

            var tubes = new System.Collections.Generic.List<Tube>();
            for (int t = 0; t < colors; t++)
            {
                var tube = new Tube(capacity);
                for (int u = 0; u < capacity; u++)
                    tube.Push(units[t * capacity + u]);
                tubes.Add(tube);
            }

            for (int e = 0; e < emptyTubes; e++)
                tubes.Add(new Tube(capacity));

            return new Board(tubes);
        }

        /// <summary>
        /// Referans çözücü: budama yok, kanonikleştirme yok, hamle sıralaması yok.
        /// Tek koruma, birebir durum tekrarını engelleyen ziyaret kümesi
        /// (sonlanma garantisi). Yavaş ama tartışmasız doğru.
        /// </summary>
        private static bool NaiveSolvable(Board board)
        {
            return NaiveDfs(board, new System.Collections.Generic.HashSet<string>());
        }

        private static bool NaiveDfs(Board board, System.Collections.Generic.HashSet<string> visited)
        {
            if (board.IsSolved) return true;
            if (!visited.Add(RawKey(board))) return false;

            for (int from = 0; from < board.TubeCount; from++)
            {
                for (int to = 0; to < board.TubeCount; to++)
                {
                    if (!board.IsValidMove(from, to)) continue;

                    PourResult move = board.Pour(from, to);
                    bool solved = NaiveDfs(board, visited);
                    board.UndoPour(move);
                    if (solved) return true;
                }
            }

            return false;
        }

        /// <summary>Ham durum anahtarı: tüp sırası korunur, kanonikleştirme yapılmaz.</summary>
        private static string RawKey(Board board)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < board.TubeCount; i++)
            {
                sb.Append(board[i].Capacity).Append(':');
                for (int u = 0; u < board[i].Count; u++)
                    sb.Append(board[i].Liquid[u]).Append(',');
                sb.Append('|');
            }

            return sb.ToString();
        }

        [Test]
        public void Solve_TreatsEquivalentEmptyTubesAsOneState()
        {
            // Üç özdeş boş tüp durum uzayını katlamamalı. Aynı tahtanın tek
            // boş tüplü hâline göre gezilen durum sayısı patlamıyorsa
            // kanonikleştirme çalışıyor demektir.
            var oneEmpty = new Board(new[]
            {
                new Tube(4, Red, Yellow, Red, Yellow),
                new Tube(4, Yellow, Red, Yellow, Red),
                new Tube(4)
            });
            var threeEmpties = new Board(new[]
            {
                new Tube(4, Red, Yellow, Red, Yellow),
                new Tube(4, Yellow, Red, Yellow, Red),
                new Tube(4),
                new Tube(4),
                new Tube(4)
            });

            SolveReport small = Solver.Solve(oneEmpty);
            SolveReport large = Solver.Solve(threeEmpties);

            Assert.AreEqual(SolveVerdict.Solvable, large.Verdict);
            // Boş tüp eklemek durum uzayını büyütür ama kanonikleştirme sayesinde
            // kontrollü kalmalı: körlemesine permütasyon patlaması olsaydı fark
            // yüzlerce kat olurdu.
            Assert.Less(large.StatesVisited, small.StatesVisited * 50 + 1000,
                "Eşdeğer boş tüpler tek duruma inmeliydi");
        }
    }
}
