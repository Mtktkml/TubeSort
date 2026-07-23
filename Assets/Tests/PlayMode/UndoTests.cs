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
    /// Geri alma akışını BoardView üzerinden test eder: hamle kaydı,
    /// geri alma sonrası tahta ve görsel durumu, geçmişin sınırları.
    /// </summary>
    public class UndoTests
    {
        private const int Red = 0;
        private const int Yellow = 1;

        private GameObject boardObject;
        private GameObject cameraObject;
        private BoardView view;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (boardObject != null) Object.Destroy(boardObject);
            if (cameraObject != null) Object.Destroy(cameraObject);

            yield return null;
        }

        /// <summary>Kamera + verilen tahtayla BoardView kurar, Start'ı bekler.</summary>
        private IEnumerator BuildBoard(Board board)
        {
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.aspect = 0.5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            boardObject = new GameObject("BoardView");
            view = boardObject.AddComponent<BoardView>();
            view.LoadBoard(board);

            yield return null;
        }

        /// <summary>Süren dökme animasyonunun bitmesini bekler (emniyetli tavanla).</summary>
        private IEnumerator WaitForAnimation()
        {
            float deadline = Time.time + 5f;
            while (view.IsAnimating && Time.time < deadline)
                yield return null;

            Assert.IsFalse(view.IsAnimating, "Animasyon zaman tavanında bitmedi");
        }

        private TubeView FindTube(int index)
        {
            foreach (TubeView tube in boardObject.GetComponentsInChildren<TubeView>())
                if (tube.name == $"Tube{index}")
                    return tube;

            Assert.Fail($"Tube{index} bulunamadı");
            return null;
        }

        [UnityTest]
        public IEnumerator UndoLastMove_RestoresBoardAndVisuals()
        {
            yield return BuildBoard(new Board(new[]
            {
                new Tube(4, Red, Red),
                new Tube(4)
            }));

            Assert.IsTrue(view.TryPour(0, 1), "Test kurgusu: hamle geçerli olmalı");
            yield return WaitForAnimation();

            Assert.IsTrue(view.Board[0].IsEmpty, "Test kurgusu: dökme gerçekleşmiş olmalı");
            Assert.AreEqual(2, view.Board[1].Count);

            view.UndoLastMove();

            Assert.AreEqual(2, view.Board[0].Count, "Kaynak tüp eski haline dönmeli");
            Assert.IsTrue(view.Board[1].IsEmpty, "Hedef tüp boşalmalı");

            // Görsel de tahtayla aynı hizada olmalı: Refresh seviyeyi anında kurar.
            Assert.AreEqual(FindTube(0).TargetFillLevel, FindTube(0).CurrentFill, 0.001f,
                "Kaynak tüpün görseli tahtayla eşleşmeli");
            Assert.AreEqual(FindTube(1).TargetFillLevel, FindTube(1).CurrentFill, 0.001f,
                "Hedef tüpün görseli tahtayla eşleşmeli");
        }

        [UnityTest]
        public IEnumerator UndoLastMove_WithEmptyHistory_DoesNothing()
        {
            yield return BuildBoard(new Board(new[]
            {
                new Tube(4, Red),
                new Tube(4)
            }));

            view.UndoLastMove();

            Assert.AreEqual(1, view.Board[0].Count, "Boş geçmişte tahta değişmemeli");
            Assert.IsTrue(view.Board[1].IsEmpty);
        }

        [UnityTest]
        public IEnumerator TryPour_InvalidMove_RecordsNothing()
        {
            yield return BuildBoard(new Board(new[]
            {
                new Tube(4, Red),
                new Tube(4, Yellow)
            }));

            Assert.IsFalse(view.TryPour(0, 1), "Farklı renk üstüne dökme geçersiz olmalı");

            view.UndoLastMove();

            Assert.AreEqual(1, view.Board[0].Count, "Geçersiz hamle geçmişe yazılmamalı");
            Assert.AreEqual(1, view.Board[1].Count);
        }

        [UnityTest]
        public IEnumerator LoadBoard_ClearsHistory()
        {
            yield return BuildBoard(new Board(new[]
            {
                new Tube(4, Red, Red),
                new Tube(4)
            }));

            Assert.IsTrue(view.TryPour(0, 1));
            yield return WaitForAnimation();

            // Yeni tahta yüklendi: eski hamle bu tahtada geri alınamamalı.
            var fresh = new Board(new List<Tube> { new Tube(4, Yellow), new Tube(4) });
            view.LoadBoard(fresh);
            yield return null;

            view.UndoLastMove();

            Assert.AreEqual(1, fresh[0].Count,
                "Eski tahtanın hamlesi yeni tahtaya uygulanmamalı");
            Assert.IsTrue(fresh[1].IsEmpty);
        }
    }
}
