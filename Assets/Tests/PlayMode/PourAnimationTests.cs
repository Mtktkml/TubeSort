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

        // Shader'a gönderilen property'leri okumak için ID'ler.
        private static readonly int FillLevelId = Shader.PropertyToID("_FillLevel");
        private static readonly int TiltAngleId = Shader.PropertyToID("_TiltAngle");

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

        /// <summary>
        /// Liquid renderer'ı bulur. İsme göre arar: sortingOrder
        /// SetSortingOffset ile değişebileceği için güvenilmez.
        /// </summary>
        private SpriteRenderer FindLiquidRenderer()
        {
            var renderers = tubeObject.GetComponentsInChildren<SpriteRenderer>();
            foreach (var r in renderers)
            {
                if (r.gameObject.name == "Liquid")
                    return r;
            }

            Assert.Fail("Liquid renderer bulunamadı");
            return null;
        }

        /// <summary>Liquid renderer'dan shader'a gönderilen _FillLevel'i okur.</summary>
        private float ReadFillLevel()
        {
            var r = FindLiquidRenderer();
            var props = new MaterialPropertyBlock();
            r.GetPropertyBlock(props);
            return props.GetFloat(FillLevelId);
        }

        /// <summary>Liquid renderer'dan shader'a gönderilen _TiltAngle'ı okur.</summary>
        private float ReadTiltAngle()
        {
            var r = FindLiquidRenderer();
            var props = new MaterialPropertyBlock();
            r.GetPropertyBlock(props);
            return props.GetFloat(TiltAngleId);
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
            yield return tubeView.AnimateFill(tubeView.TargetFillLevel, 0.2f);

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
            yield return tubeView.AnimateFill(tubeView.TargetFillLevel, 0.2f);

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
            var routine = tubeView.AnimateFill(tubeView.TargetFillLevel, 0.5f);
            // İlk kareyi çalıştır.
            tubeView.StartCoroutine(routine);
            yield return null;

            float fillMid = ReadFillLevel();

            // Orta karede seviye ne başlangıçta ne sıfırda olmalı:
            // animasyon kademeli ilerliyorsa arada bir yerde durur.
            Assert.Less(fillMid, fillBefore, "Seviye hâlâ başlangıçta — animasyon başlamadı");
            Assert.Greater(fillMid, 0f, "Seviye anında sıfıra düştü — animasyon yok");
        }

        [UnityTest]
        public IEnumerator SetTiltAngle_SendsAngleToShader()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            float before = ReadTiltAngle();
            Assert.AreEqual(0f, before, 0.001f, "Başlangıçta eğim sıfır olmalı");

            tubeView.SetTiltAngle(0.5f);

            float after = ReadTiltAngle();
            Assert.AreEqual(0.5f, after, 0.001f, "Shader'a gönderilen eğim yanlış");
        }

        [UnityTest]
        public IEnumerator SetTiltAngle_RotatesTransform()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            tubeView.SetTiltAngle(30f * Mathf.Deg2Rad);

            float z = tubeView.transform.localRotation.eulerAngles.z;
            // Unity açıyı 0-360 aralığında verir; 30°'ye yakın olmalı.
            Assert.AreEqual(30f, z, 0.5f, "Transform dönmedi");
        }

        [UnityTest]
        public IEnumerator SetTiltAngle_PreservedAfterRefresh()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            tubeView.SetTiltAngle(0.3f);
            tubeView.Refresh();

            float angle = ReadTiltAngle();
            Assert.AreEqual(0.3f, angle, 0.001f,
                "Refresh sonrası eğim kaybolmamalı");
        }

        [UnityTest]
        public IEnumerator SetTiltAngle_PreservedAfterSetFillLevel()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            tubeView.SetTiltAngle(0.4f);
            tubeView.SetFillLevel(0.5f);

            float angle = ReadTiltAngle();
            Assert.AreEqual(0.4f, angle, 0.001f,
                "SetFillLevel sonrası eğim kaybolmamalı");
        }

        [UnityTest]
        public IEnumerator SetSortingOffset_ChangesSortingOrder()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            tubeView.SetSortingOffset(10);

            var renderers = tubeObject.GetComponentsInChildren<SpriteRenderer>();
            bool foundGlass = false, foundLiquid = false;

            foreach (var r in renderers)
            {
                if (r.gameObject.name == "Glass")
                {
                    Assert.AreEqual(10, r.sortingOrder, "Glass sorting order yanlış");
                    foundGlass = true;
                }
                else if (r.gameObject.name == "Liquid")
                {
                    Assert.AreEqual(11, r.sortingOrder, "Liquid sorting order yanlış");
                    foundLiquid = true;
                }
            }

            Assert.IsTrue(foundGlass && foundLiquid, "Renderer bulunamadı");

            // Sıfırla ve kontrol et.
            tubeView.SetSortingOffset(0);
            foreach (var r in renderers)
            {
                if (r.gameObject.name == "Glass")
                    Assert.AreEqual(0, r.sortingOrder);
                else if (r.gameObject.name == "Liquid")
                    Assert.AreEqual(1, r.sortingOrder);
            }
        }
    }
}
