using System.Collections;
using NUnit.Framework;
using TubeSort.Core;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Dökme animasyonunun doğruluğunu test eder. Gerçek girdi simüle etmez:
    /// TubeView.AnimateFill'i doğrudan çağırarak animasyonun pürüzsüz
    /// başlayıp doğru seviyede bittiğini doğrular.
    /// </summary>
    public class PourAnimationTests
    {
        private GameObject cameraObject;
        private GameObject tubeObject;
        private TubeView tubeView;
        private Tube tube;

        // Shader'a gönderilen _FillLevel'i okumak için property ID.
        private static readonly int FillLevelId = Shader.PropertyToID("_FillLevel");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // TubeView shader'lara ihtiyaç duyar; Resources'tan yüklenirler.
            // Kamera olmasa BoardView Start'ta hata verir ama biz BoardView
            // kullanmıyoruz. Yine de Liquid shader'ın çalışması için sahne lazım.
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (tubeObject != null) Object.Destroy(tubeObject);
            if (cameraObject != null) Object.Destroy(cameraObject);

            yield return null;
        }

        /// <summary>
        /// Verilen tüpü gösteren bir TubeView oluşturur. BoardView'ın
        /// BuildViews'ta yaptığının aynısı, ama tek tüp için.
        /// </summary>
        private IEnumerator BuildTubeView(Tube sourceTube)
        {
            tube = sourceTube;
            tubeObject = new GameObject("TestTube");

            tubeView = tubeObject.AddComponent<TubeView>();

            var palette = new ColorPalette();

            // BoardView'ın yaptığı gibi 1x1 beyaz sprite.
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);

            var glassShader = Resources.Load<Shader>("Glass");
            var liquidShader = Resources.Load<Shader>("Liquid");
            var glassMat = new Material(glassShader);
            var liquidMat = new Material(liquidShader);

            tubeView.Initialize(0, tube, palette, sprite, glassMat, liquidMat);

            yield return null;
        }

        /// <summary>Liquid renderer'dan shader'a gönderilen _FillLevel'i okur.</summary>
        private float ReadFillLevel()
        {
            // TubeView iki çocuk oluşturur: Glass (order 0) ve Liquid (order 1).
            // Liquid'in MaterialPropertyBlock'undan okuyoruz.
            var renderers = tubeObject.GetComponentsInChildren<SpriteRenderer>();
            var props = new MaterialPropertyBlock();

            // sortingOrder'ı 1 olan Liquid renderer'ı bul.
            foreach (var r in renderers)
            {
                if (r.sortingOrder == 1)
                {
                    r.GetPropertyBlock(props);
                    return props.GetFloat(FillLevelId);
                }
            }

            Assert.Fail("Liquid renderer bulunamadı");
            return 0f;
        }

        [UnityTest]
        public IEnumerator AnimateFill_ReachesCorrectLevel_WhenTubeLosesTwoUnits()
        {
            // 4 kapasiteli, 4 birim dolu bir tüp.
            yield return BuildTubeView(new Tube(4, 0, 0, 1, 1));

            float fillBefore = ReadFillLevel();

            // Board.Pour normalde bunu yapar: üstten 2 birim çıkar.
            tube.Pop(2);

            // Animasyonu başlat ve tamamlanmasını bekle.
            yield return tubeView.AnimateFill(0.2f);

            float fillAfter = ReadFillLevel();

            // Tüp 4/4'ten 2/4'e düştü: seviye yarıya inmeli.
            Assert.Less(fillAfter, fillBefore, "Seviye düşmedi");
            Assert.AreEqual(fillAfter, fillBefore * 0.5f, 0.01f,
                "Seviye beklenen değere ulaşmadı (yarısına inmeli)");
        }

        [UnityTest]
        public IEnumerator AnimateFill_ReachesCorrectLevel_WhenTubeGainsTwoUnits()
        {
            // 4 kapasiteli, 2 birim dolu bir tüp.
            yield return BuildTubeView(new Tube(4, 0, 0));

            float fillBefore = ReadFillLevel();

            // Board.Pour normalde bunu yapar: üstten 2 birim eklenir.
            tube.Push(0, 2);

            // Animasyonu başlat ve tamamlanmasını bekle.
            yield return tubeView.AnimateFill(0.2f);

            float fillAfter = ReadFillLevel();

            // Tüp 2/4'ten 4/4'e çıktı: seviye iki katına çıkmalı.
            Assert.Greater(fillAfter, fillBefore, "Seviye yükselmedi");
            Assert.AreEqual(fillAfter, fillBefore * 2f, 0.01f,
                "Seviye beklenen değere ulaşmadı (iki katına çıkmalı)");
        }

        [UnityTest]
        public IEnumerator AnimateFill_LevelChangesGradually_NotInstantly()
        {
            // 4 kapasiteli, 4 birim dolu bir tüp.
            yield return BuildTubeView(new Tube(4, 0, 0, 0, 0));

            float fillBefore = ReadFillLevel();

            // Tüpü tamamen boşalt.
            tube.Pop(4);

            // Animasyonu uzun tutuyoruz ki ara kareleri yakalayabilelim.
            var routine = tubeView.AnimateFill(0.5f);
            // İlk kareyi çalıştır.
            tubeView.StartCoroutine(routine);
            yield return null;

            float fillMid = ReadFillLevel();

            // Orta karede seviye ne başlangıçta ne sıfırda olmalı:
            // animasyon kademeli ilerliyorsa arada bir yerde durur.
            Assert.Less(fillMid, fillBefore, "Seviye hâlâ başlangıçta — animasyon başlamadı");
            Assert.Greater(fillMid, 0f, "Seviye anında sıfıra düştü — animasyon yok");
        }
    }
}
