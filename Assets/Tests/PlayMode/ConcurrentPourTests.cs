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
    /// Eşzamanlı dökme: bir dökme sürerken, ona dahil olmayan (serbest) tüpler
    /// arasında ikinci bir dökme başlayabilmeli. Dökmeye dahil (meşgul) bir tüpten
    /// ya da meşgul bir hedefe yeni dökme, board açısından geçerli olsa bile
    /// reddedilmeli.
    /// </summary>
    public class ConcurrentPourTests
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

        [UnityTest]
        public IEnumerator DisjointTubes_PourConcurrently_BusyTubeRejected()
        {
            // 0->1 (kırmızı) ve 2->3 (sarı) ayrık çiftler; aynı anda dökülebilmeli.
            // 4->1 board açısından geçerli (kırmızı üstüne kırmızı, yer var) ama
            // hedef (1) meşgul olduğu için reddedilmeli.
            const int Red = 0, Yellow = 1;
            var view = BuildBoard(new Board(new List<Tube>
            {
                new Tube(4, Red, Red),
                new Tube(4),
                new Tube(4, Yellow, Yellow),
                new Tube(4),
                new Tube(4, Red, Red),
            }));
            yield return null; // Start çalışsın

            Assert.IsTrue(view.TryPour(0, 1), "ilk dökme başlamalıydı");
            Assert.IsTrue(view.IsAnimating, "animasyon başlamalıydı");

            // Boşalan (kaynak) tüpe dökme: board-geçerli olsa da reddedilmeli.
            // (0 şu an 1'e boşalıyor; 4'ün kırmızısı boş 0'a board-geçerli ama
            // 0 meşgul kaynak olduğu için reddedilir.)
            Assert.IsFalse(view.TryPour(4, 0),
                "boşalan (kaynak) tüpe dökme reddedilmeliydi");

            // Ayrık çift: ilk dökme sürerken eşzamanlı kabul edilmeliydi
            // (eski seri kilitte bu false dönerdi).
            Assert.IsTrue(view.TryPour(2, 3),
                "ayrık tüpler arası dökme eşzamanlı kabul edilmeliydi");

            float elapsed = 0f;
            while (view.IsAnimating && elapsed < 6f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsFalse(view.IsAnimating,
                $"animasyonlar {elapsed:F1} sn'de bitmedi");

            Board result = view.Board;
            Assert.IsTrue(result[0].IsEmpty, "0: kaynak boşalmalı");
            Assert.AreEqual(2, result[1].TopSegmentLength,
                "1: yalnız ilk dökmeyi almalı (2 birim kırmızı)");
            Assert.IsTrue(result[2].IsEmpty, "2: kaynak boşalmalı");
            Assert.AreEqual(2, result[3].TopSegmentLength, "3: sarı dökmeyi almalı");
            Assert.IsFalse(result[4].IsEmpty, "4: reddedilen kaynak değişmemeli");
        }

        [UnityTest]
        public IEnumerator TwoSources_PourIntoOneTarget_ThirdRejected()
        {
            // Üç kırmızı kaynak, bir boş hedef (kapasite 4). İlk iki dökme hedefe
            // kabul edilmeli (karşı taraflardan); hedef 2 gelenle dolunca üçüncü
            // reddedilmeli.
            const int Red = 0;
            var view = BuildBoard(new Board(new List<Tube>
            {
                new Tube(4, Red, Red),
                new Tube(4),
                new Tube(4, Red, Red),
                new Tube(4, Red, Red),
            }));
            yield return null; // Start çalışsın

            Assert.IsTrue(view.TryPour(0, 1), "1. gelen kabul edilmeliydi");
            Assert.IsTrue(view.TryPour(2, 1),
                "2. gelen (karşı taraf) kabul edilmeliydi");
            Assert.IsFalse(view.TryPour(3, 1),
                "3. gelen reddedilmeliydi (hedef 2 gelenle dolu)");

            float elapsed = 0f;
            while (view.IsAnimating && elapsed < 8f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsFalse(view.IsAnimating, $"animasyonlar {elapsed:F1} sn'de bitmedi");

            Board result = view.Board;
            Assert.IsTrue(result[0].IsEmpty, "0: kaynak boşalmalı");
            Assert.IsTrue(result[1].IsComplete,
                "1: iki gelenle dolup tamamlanmalı (4 kırmızı)");
            Assert.IsTrue(result[2].IsEmpty, "2: kaynak boşalmalı");
            Assert.IsFalse(result[3].IsEmpty, "3: reddedilen kaynak değişmemeli");
        }
    }
}
