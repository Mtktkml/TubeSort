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
    /// Tahta enjeksiyonunu doğrular. Enjeksiyon sayesinde testler tüp sayısını
    /// kesin iddia edebilir; eskiden BoardView kendi test tahtasını kurduğu
    /// için bu mümkün değildi.
    /// </summary>
    public class BoardInjectionTests
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

        /// <summary>İstenen sayıda tüplü basit tahta: yarısı dolu, kalanı boş.</summary>
        private static Board MakeBoard(int tubeCount)
        {
            var tubes = new List<Tube>();
            for (int i = 0; i < tubeCount; i++)
                tubes.Add(i % 2 == 0 ? new Tube(4, 0, 1) : new Tube(4));

            return new Board(tubes);
        }

        [UnityTest]
        public IEnumerator LoadBoard_BeforeStart_BuildsExactlyInjectedTubes()
        {
            BuildCamera();
            boardObject = new GameObject("BoardView");
            var view = boardObject.AddComponent<BoardView>();

            // AddComponent ile ilk kare arasındayız: Start henüz çalışmadı.
            Board injected = MakeBoard(3);
            view.LoadBoard(injected);

            yield return null; // Start çalışsın

            Assert.AreEqual(3, boardObject.GetComponentsInChildren<TubeView>().Length,
                "Kurulum enjekte edilen tahtayla yapılmalıydı");
            Assert.AreSame(injected, view.Board);
        }

        [UnityTest]
        public IEnumerator LoadBoard_AfterStart_ReplacesExistingViews()
        {
            BuildCamera();
            boardObject = new GameObject("BoardView");
            var view = boardObject.AddComponent<BoardView>();

            yield return null; // Start varsayılan test tahtasını kursun

            int defaultCount = boardObject.GetComponentsInChildren<TubeView>().Length;
            Assert.Greater(defaultCount, 0, "Test kurgusu: varsayılan tahta kurulmuş olmalı");

            view.LoadBoard(MakeBoard(3));
            yield return null; // Destroy kare sonunda işlesin

            Assert.AreEqual(3, boardObject.GetComponentsInChildren<TubeView>().Length,
                "Eski görünümler yıkılıp yeni tahta kurulmalıydı");
            Assert.AreNotEqual(defaultCount, 3,
                "Test kurgusu: varsayılan tahta 3 tüplü olmamalı ki değişim görülebilsin");
        }

        [UnityTest]
        public IEnumerator LoadBoard_WithNull_KeepsCurrentBoard()
        {
            BuildCamera();
            boardObject = new GameObject("BoardView");
            var view = boardObject.AddComponent<BoardView>();

            Board injected = MakeBoard(4);
            view.LoadBoard(injected);
            yield return null;

            LogAssert.Expect(LogType.Error,
                "LoadBoard'a null tahta verildi; mevcut tahta korunuyor.");
            view.LoadBoard(null);
            yield return null;

            Assert.AreSame(injected, view.Board, "Null yükleme mevcut tahtayı bozmamalı");
            Assert.AreEqual(4, boardObject.GetComponentsInChildren<TubeView>().Length);
        }
    }
}
