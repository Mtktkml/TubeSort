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
