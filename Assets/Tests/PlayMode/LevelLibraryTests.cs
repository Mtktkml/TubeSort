using System.Collections.Generic;
using NUnit.Framework;
using TubeSort.Core;
using TubeSort.Game;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Python'un ürettiği pilot_levels.json'un (300-level sistemi: öğretici
    /// girişler + zorluğu artan sırada dengeli GRID; etiketler düz "1".."300")
    /// Unity tarafında doğru okunduğunu doğrular. Sahne gerekmez ama Resources
    /// yüklemesi oyun ortamı istediği için PlayMode'dadır.
    /// </summary>
    public class LevelLibraryTests
    {
        private const string Pilot = "pilot_levels";
        private const int ExpectedCount = 300;

        [Test]
        public void Pilot_HasThreeHundredLevels()
        {
            Assert.AreEqual(ExpectedCount, LevelLibrary.LevelCount(Pilot),
                "pilot_levels.json 300 tahta içermeli (300-level sistemi).");
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
            // bozulmadığını da gösterir. IsSolvable kullanılır (ilk çözümde durur):
            // 300 tahtada tam Solve raporu (durum sayımı) testi dakikalara çeker;
            // oyun da hamle başına aynı IsSolvable'ı koşuyor.
            for (int level = 1; level <= ExpectedCount; level++)
            {
                Board board = LevelLibrary.LoadFrom(Pilot, level);

                Assert.IsTrue(Solver.IsSolvable(board),
                    $"Level {level} ({LevelLibrary.LabelOf(Pilot, level)}) çözülebilir olmalı");
            }
        }

        [Test]
        public void Pilot_FirstTutorial_IsSingleColorTwoTubes()
        {
            // Level 1-2: tek renk, 2 kısmi tüp — en yumuşak giriş (dök, bitir).
            // 300-level sisteminde etiketler düz sayı ("1").
            Board board = LevelLibrary.LoadFrom(Pilot, 1);
            Assert.AreEqual("1", LevelLibrary.LabelOf(Pilot, 1));
            Assert.AreEqual(2, board.TubeCount, "Öğretici 1: 2 tüp olmalı");
            Assert.AreEqual(1, DistinctColors(board).Count, "Öğretici 1: tek renk olmalı");
        }

        [Test]
        public void Pilot_SecondTutorial_IsTwoColorWithTwoEmpties()
        {
            // Level 3-4: 2 renk, 2 boş, 4 tüp. İki boş tüp öğreticiyi
            // çıkmaz-güvenli tutar (tek boşla oyuncu kilitlenebilir).
            Board board = LevelLibrary.LoadFrom(Pilot, 3);
            Assert.AreEqual("3", LevelLibrary.LabelOf(Pilot, 3));
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
