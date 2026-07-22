using NUnit.Framework;
using TubeSort.Core;
using TubeSort.Game;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Python'un ürettiği levels.json'un Unity tarafında doğru okunduğunu
    /// doğrular. Sahne gerekmez ama Resources yüklemesi oyun ortamı istediği
    /// için PlayMode'dadır.
    /// </summary>
    public class LevelLibraryTests
    {
        [Test]
        public void Load_AllFiveLevels_HaveExpectedShape()
        {
            for (int level = 1; level <= 5; level++)
            {
                Board board = LevelLibrary.Load(level);
                Assert.IsNotNull(board, $"Level {level} yüklenemedi");

                int n = level + 2; // level 1 = 3 renk x 3 kapasite
                Assert.AreEqual(n + 2, board.TubeCount,
                    $"Level {level}: {n} dolu + 2 boş tüp bekleniyordu");

                int emptyCount = 0;
                foreach (Tube tube in board.Tubes)
                {
                    Assert.AreEqual(n, tube.Capacity, $"Level {level}: kapasite {n} olmalı");
                    if (tube.IsEmpty) emptyCount++;
                }

                Assert.AreEqual(2, emptyCount, $"Level {level}: 2 boş tüp olmalı");
            }
        }

        [Test]
        public void Load_AllFiveLevels_AreSolvableByCSharpSolver()
        {
            // Python "çözülebilir" dedi; son sözü oyunun kendi solver'ı söylesin.
            // İki bağımsız implementasyonun aynı levelde aynı karara varması,
            // veri aktarımının da (JSON) bozulmadığının kanıtı.
            for (int level = 1; level <= 5; level++)
            {
                Board board = LevelLibrary.Load(level);
                SolveReport report = Solver.Solve(board);

                Assert.AreEqual(SolveVerdict.Solvable, report.Verdict,
                    $"Level {level} oyunun solver'ına göre çözülebilir olmalı");
            }
        }

        [Test]
        public void Load_MissingLevel_ReturnsNull()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                "Level 999 levels.json içinde yok.");

            Assert.IsNull(LevelLibrary.Load(999));
        }
    }
}
