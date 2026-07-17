using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Tahtanın ekrana sığdığını doğrular. Sığdırma kameranın görüş alanına
    /// bağlı olduğu için testler kamerayı kendileri kurar.
    /// </summary>
    public class LayoutFitTests
    {
        private const float Aspect = 0.5f;   // dikey telefon oranı

        private GameObject boardObject;
        private GameObject cameraObject;
        private Camera camera;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (boardObject != null) Object.Destroy(boardObject);
            if (cameraObject != null) Object.Destroy(cameraObject);

            yield return null;
        }

        /// <summary>
        /// Verilen görüş büyüklüğünde bir kamera ve tahta kurar.
        /// Kameranın oranı elle veriliyor: batchmode'da ekran boyutu belirsiz
        /// olduğundan aksi halde testin sonucu makineye göre değişirdi.
        /// </summary>
        private IEnumerator BuildBoard(float orthographicSize, float aspect = Aspect)
        {
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.aspect = aspect;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            boardObject = new GameObject("BoardView");
            boardObject.AddComponent<BoardView>();

            yield return new WaitForFixedUpdate();
            yield return null;
        }

        /// <summary>Tahtadaki her şeyi kapsayan kutu.</summary>
        private Bounds MeasureRenderedBoard()
        {
            var renderers = boardObject.GetComponentsInChildren<Renderer>();
            Assert.Greater(renderers.Length, 0, "Tahtada hiç çizilen nesne yok");

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            return bounds;
        }

        private void AssertFitsOnScreen()
        {
            Bounds board = MeasureRenderedBoard();

            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;

            Assert.LessOrEqual(board.max.x, halfWidth, "Tahta sağdan taşıyor");
            Assert.GreaterOrEqual(board.min.x, -halfWidth, "Tahta soldan taşıyor");
            Assert.LessOrEqual(board.max.y, halfHeight, "Tahta yukarıdan taşıyor");
            Assert.GreaterOrEqual(board.min.y, -halfHeight, "Tahta aşağıdan taşıyor");
        }

        [UnityTest]
        public IEnumerator BoardFitsOnScreen_AtDefaultCameraSize()
        {
            yield return BuildBoard(orthographicSize: 8f);
            AssertFitsOnScreen();
        }

        [UnityTest]
        public IEnumerator BoardFitsOnScreen_WhenViewIsFarTooSmall()
        {
            // Tahta bu görüş alanına asla olduğu gibi sığmaz; sığdırma
            // çalışmıyorsa test burada patlar.
            yield return BuildBoard(orthographicSize: 1.5f);
            AssertFitsOnScreen();
        }

        [UnityTest]
        public IEnumerator BoardRefits_WhenViewShrinksWhilePlaying()
        {
            // Cihaz döndüğünde, katlanabilir telefon açıldığında ya da ekran
            // bölündüğünde görüş alanı oyun sürerken değişir. Sığdırma yalnızca
            // açılışta yapılsaydı tahta taşar ve öyle kalırdı.
            yield return BuildBoard(orthographicSize: 8f);

            camera.orthographicSize = 1.5f;
            yield return null;

            AssertFitsOnScreen();
        }

        [UnityTest]
        public IEnumerator BoardRefits_WhenAspectChanges()
        {
            // Dikeyden yataya geçiş: yükseklik aynı kalır, genişlik değişir.
            yield return BuildBoard(orthographicSize: 1.5f);

            camera.aspect = 0.2f;
            yield return null;

            AssertFitsOnScreen();
        }

        /// <summary>Tüplerin dizildiği satır sayısı: farklı yükseklikte kaç grup var.</summary>
        private int CountRows()
        {
            var rows = new HashSet<int>();
            foreach (TubeView view in boardObject.GetComponentsInChildren<TubeView>())
                rows.Add(Mathf.RoundToInt(view.transform.localPosition.y * 100f));

            return rows.Count;
        }

        [UnityTest]
        public IEnumerator BoardSpreadsSideways_WhenViewIsWide()
        {
            // Yatay ekranda yanlarda bol yer var; tüpler oraya yayılmalı.
            // Satır sayısı sabit olsaydı dikey alan tükenir, tüpler gereksiz
            // yere küçülür, yanlardaki boşluk kullanılmazdı.
            yield return BuildBoard(orthographicSize: 8f, aspect: 0.5f);
            int portraitRows = CountRows();

            camera.aspect = 2f;
            yield return null;
            int landscapeRows = CountRows();

            Assert.Less(landscapeRows, portraitRows,
                $"Yatay ekranda satır azalmalıydı (dikey {portraitRows}, yatay {landscapeRows})");
        }

        [UnityTest]
        public IEnumerator BoardIsNotScaledUp_WhenItAlreadyFits()
        {
            // Kocaman bir görüş alanı. Tahta rahatça sığıyor, dokunulmamalı:
            // büyütmek tüp boyunu level'dan level'a zıplatırdı.
            yield return BuildBoard(orthographicSize: 40f);

            Assert.AreEqual(1f, boardObject.transform.localScale.x, 0.0001f);
        }
    }
}
