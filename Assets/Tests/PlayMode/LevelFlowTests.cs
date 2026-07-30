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
    /// +tüp ve restart davranışları — tahta enjeksiyonuyla test edilebilen
    /// kısım. Level navigasyonu (skip/önceki) ve oto-geçiş pilot moduna
    /// (pilot_levels.json) bağlı olduğu için Device Simulator'da elle doğrulanır.
    /// </summary>
    public class LevelFlowTests
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

        private void BuildCamera()
        {
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.aspect = 0.5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        }

        /// <summary>İstenen sayıda tüplü basit tahta: çift indeksliler dolu, kalanı boş.</summary>
        private static Board MakeBoard(int tubeCount)
        {
            var tubes = new List<Tube>();
            for (int i = 0; i < tubeCount; i++)
                tubes.Add(i % 2 == 0 ? new Tube(4, 0, 1) : new Tube(4));

            return new Board(tubes);
        }

        private BoardView SpawnWith(Board board)
        {
            BuildCamera();
            boardObject = new GameObject("BoardView");
            var view = boardObject.AddComponent<BoardView>();
            view.LoadBoard(board);   // Start'tan önce enjekte
            return view;
        }

        [UnityTest]
        public IEnumerator AddEmptyTube_AddsOneEmptyTube()
        {
            BoardView view = SpawnWith(MakeBoard(3));
            yield return null; // Start çalışsın

            view.AddEmptyTube();
            yield return null; // RebuildViews (Destroy) kare sonunda işlesin

            Assert.AreEqual(4, view.Board.TubeCount, "Bir boş tüp eklenmeliydi");
            Assert.AreEqual(4, boardObject.GetComponentsInChildren<TubeView>().Length,
                "Görünümler yeni tüple yeniden kurulmalıydı");
            Assert.IsTrue(view.Board[3].IsEmpty, "Eklenen tüp boş olmalı");
        }

        [UnityTest]
        public IEnumerator RestartLevel_RevertsToLevelStart()
        {
            BoardView view = SpawnWith(MakeBoard(3));
            yield return null;

            view.AddEmptyTube();   // tahtayı değiştir (4 tüp)
            yield return null;
            Assert.AreEqual(4, view.Board.TubeCount, "Test kurgusu: +tüp sonrası 4 tüp");

            view.RestartLevel();   // level başına dön (3 tüp)
            yield return null;

            Assert.AreEqual(3, view.Board.TubeCount, "Restart level başına (3 tüp) dönmeli");
            Assert.AreEqual(3, boardObject.GetComponentsInChildren<TubeView>().Length,
                "Görünümler de level başına dönmeli");
        }
    }
}
