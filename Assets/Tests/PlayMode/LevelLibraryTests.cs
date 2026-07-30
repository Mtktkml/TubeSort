using System.Collections.Generic;
using NUnit.Framework;
using TubeSort.Core;
using TubeSort.Game;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Python'un ürettiği pilot_levels.json'un (öğretici + köprü + ranked;
    /// 15 tier x 2 = 30 tahta) Unity tarafında doğru okunduğunu doğrular.
    /// Sahne gerekmez ama Resources yüklemesi oyun ortamı istediği için
    /// PlayMode'dadır.
    /// </summary>
    public class LevelLibraryTests
    {
        private const string Pilot = "pilot_levels";
        private const int ExpectedCount = 30;

        [Test]
        public void Pilot_HasThirtyLevels()
        {
            Assert.AreEqual(ExpectedCount, LevelLibrary.LevelCount(Pilot),
                "pilot_levels.json 30 tahta içermeli (15 tier x 2).");
        }

        [Test]
        public void Pilot_AllLevels_LoadAndHaveLabel()
        {
            for (int level = 1; level <= ExpectedCount; level++)
            {
                Board board = LevelLibrary.LoadFrom(Pilot, level);
                Assert.IsNotNull(board, $"Level {level} yüklenemedi");
                Assert.Greater(board.TubeCount, 0, $"Level {level}: tüp yok");

                string label = LevelLibrary.LabelOf(Pilot, level);
                Assert.IsFalse(string.IsNullOrEmpty(label),
                    $"Level {level}: label boş olmamalı (ör. \"1.1\")");
            }
        }

        [Test]
        public void Pilot_AllLevels_AreSolvableByCSharpSolver()
        {
            // Python üretimi çözülebilir dedi; oyunun kendi solver'ı da doğrulamalı.
            // İki bağımsız implementasyonun aynı karara varması, JSON aktarımının
            // bozulmadığını da gösterir.
            for (int level = 1; level <= ExpectedCount; level++)
            {
                Board board = LevelLibrary.LoadFrom(Pilot, level);
                SolveReport report = Solver.Solve(board);

                Assert.AreEqual(SolveVerdict.Solvable, report.Verdict,
                    $"Level {level} ({LevelLibrary.LabelOf(Pilot, level)}) çözülebilir olmalı");
            }
        }

        [Test]
        public void Pilot_FirstTutorial_IsSingleColorTwoTubes()
        {
            // Tier 1 (level 1-2): tek renk, 2 tüp — en yumuşak giriş.
            Board board = LevelLibrary.LoadFrom(Pilot, 1);
            Assert.AreEqual("1.1", LevelLibrary.LabelOf(Pilot, 1));
            Assert.AreEqual(2, board.TubeCount, "Öğretici 1: 2 tüp olmalı");
            Assert.AreEqual(1, DistinctColors(board).Count, "Öğretici 1: tek renk olmalı");
        }

        [Test]
        public void Pilot_SecondTutorial_IsTwoColorWithTwoEmpties()
        {
            // Tier 2 (level 3-4): 2 renk, 2 boş, 4 tüp. İki boş tüp öğreticiyi
            // çıkmaz-güvenli tutar (tek boşla oyuncu kilitlenebilir).
            Board board = LevelLibrary.LoadFrom(Pilot, 3);
            Assert.AreEqual("2.1", LevelLibrary.LabelOf(Pilot, 3));
            Assert.AreEqual(4, board.TubeCount, "Öğretici 2: 4 tüp olmalı");
            Assert.AreEqual(2, DistinctColors(board).Count, "Öğretici 2: 2 renk olmalı");

            int emptyCount = 0;
            foreach (Tube tube in board.Tubes)
                if (tube.IsEmpty) emptyCount++;
            Assert.AreEqual(2, emptyCount, "Öğretici 2: 2 boş tüp olmalı");
        }

        [Test]
        public void Pilot_MissingLevel_ReturnsNull()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                "Level 999 pilot_levels.json içinde yok.");

            Assert.IsNull(LevelLibrary.LoadFrom(Pilot, 999));
        }

        private static HashSet<int> DistinctColors(Board board)
        {
            var colors = new HashSet<int>();
            foreach (Tube tube in board.Tubes)
                foreach (int unit in tube.Liquid)
                    colors.Add(unit);
            return colors;
        }
    }
}
