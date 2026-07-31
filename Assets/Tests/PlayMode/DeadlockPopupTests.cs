using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TubeSort.Core;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Çıkmaz pop-up'ı: tahta çözülemez duruma düşünce açılmalı, kurtarma
    /// aksiyonu tahtayı kurtarınca kapanmalı, kurtaramazsa açık kalmalı.
    /// Pop-up görünürlüğü BoardView.DeadlockPopupVisible ile sorgulanır;
    /// dokunuş yutma (hamle kilidi) HandleClick yönlendirmesinde — burada
    /// veri düzeyindeki davranış test edilir, görsel doğrulama gözle.
    /// </summary>
    public class DeadlockPopupTests
    {
        private GameObject boardObject;
        private GameObject cameraObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (boardObject != null) Object.Destroy(boardObject);
            if (cameraObject != null) Object.Destroy(cameraObject);

            yield return null;
        }

        private BoardView BuildBoard(Board board)
        {
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.aspect = 0.5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            boardObject = new GameObject("BoardView");
            var view = boardObject.AddComponent<BoardView>();
            view.LoadBoard(board);
            return view;
        }

        /// <summary>Çözülemez tahta: iki karışık dolu tüp, boş yok — hiç hamle
        /// yok, Solver.IsSolvable false.</summary>
        private static Board UnsolvableBoard()
        {
            return new Board(new List<Tube>
            {
                new Tube(2, 0, 1),
                new Tube(2, 1, 0),
            });
        }

        [UnityTest]
        public IEnumerator SolvableBoard_PopupHidden()
        {
            // Çözülemez ikilinin boş tüplü hâli: hamleler açık, çözüm var.
            var view = BuildBoard(new Board(new List<Tube>
            {
                new Tube(2, 0, 1),
                new Tube(2, 1, 0),
                new Tube(2),
            }));
            yield return null; // Start çalışsın

            Assert.IsFalse(view.DeadlockPopupVisible,
                "Çözülebilir tahtada pop-up görünmemeli");
        }

        [UnityTest]
        public IEnumerator UnsolvableBoard_PopupShown()
        {
            var view = BuildBoard(UnsolvableBoard());
            yield return null; // Start çalışsın

            Assert.IsTrue(view.DeadlockPopupVisible,
                "Çözülemez tahtada pop-up açılmalı");
        }

        [UnityTest]
        public IEnumerator AddTube_RescuesDeadlock_PopupCloses()
        {
            var view = BuildBoard(UnsolvableBoard());
            yield return null;
            Assert.IsTrue(view.DeadlockPopupVisible, "Ön koşul: pop-up açık olmalı");

            // +1 tüp bu tahtayı kurtarır (boş tüple hamleler açılır).
            view.AddEmptyTube();
            yield return null;

            Assert.IsFalse(view.DeadlockPopupVisible,
                "+tüp çıkmazı çözünce pop-up kapanmalı");
            Assert.AreEqual(3, view.Board.TubeCount, "+tüp tahtaya eklenmiş olmalı");
        }

        [UnityTest]
        public IEnumerator Deadlock_BlocksNewMoves()
        {
            // Çözülemez ama hamlesi OLAN tahta: renk 1'den 3 birim var (kap 2
            // tüpleri asla tam dolduramaz -> çözümsüz), yine de 0->2 dökmesi
            // board kurallarınca geçerli. Çıkmaz kilidi bu hamleyi reddetmeli:
            // çıkmaza girdikten sonra oyuncu yeni hamle yapamaz (bayrak hamle
            // ANINDA güncellendiği için animasyon arasına sıkışan hamle dahil).
            var view = BuildBoard(new Board(new List<Tube>
            {
                new Tube(2, 0, 1),
                new Tube(2, 1, 0),
                new Tube(2, 1),
            }));
            yield return null;
            Assert.IsTrue(view.DeadlockPopupVisible, "Ön koşul: pop-up açık olmalı");

            Assert.IsFalse(view.TryPour(0, 2),
                "Çıkmazda yeni hamle reddedilmeli (kural olarak geçerli olsa da)");
            Assert.AreEqual(2, view.Board[0].Count, "Tahta değişmemiş olmalı");
        }

        [UnityTest]
        public IEnumerator Restart_StillUnsolvable_PopupStays()
        {
            var view = BuildBoard(UnsolvableBoard());
            yield return null;
            Assert.IsTrue(view.DeadlockPopupVisible, "Ön koşul: pop-up açık olmalı");

            // Baştan al: bozulmamış kopya da çözülemez — pop-up KAPANMAMALI
            // (koşullu tazelemenin kanıtı: körü körüne gizleme yok).
            view.RestartLevel();
            yield return null;

            Assert.IsTrue(view.DeadlockPopupVisible,
                "Restart sonrası tahta hâlâ çözülemezse pop-up açık kalmalı");
        }
    }
}
