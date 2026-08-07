using System.Collections;
using NUnit.Framework;
using TubeSort.Core;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Tüp tıklama alanı: cam gövde VE halka tıklanabilir olmalı; ikisinin
    /// dışı (halkanın şeffaf köşeleri, tıpa bölgesi, gövdenin yanı)
    /// tıklanamaz kalmalı. ContainsPoint doğrudan test edilir —
    /// BoardView.HandleClick kaba collider elemesinden sonra kararı buna
    /// bırakır, yani tıklanabilirliğin gerçek kaynağı bu fonksiyondur.
    /// </summary>
    public class ClickAreaTests
    {
        private GameObject cameraObject;
        private GameObject tubeObject;
        private TubeView tubeView;

        // Halka geometrisi (TubeView'daki private sabitlerin aynası):
        // halka 60 satır, dikiş tüp tepesinin 4 satır altında, PPU 126.67 →
        // merkez y = tüp tepesi + (30−4)/PPU; yarı boy 30/PPU.
        private const float RingCenterAboveTop = 26f / 126.67f;
        private const float RingHalfHeight = 30f / 126.67f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
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

        /// <summary>PourAnimationTests.BuildTubeView ile aynı kurulum.</summary>
        private IEnumerator BuildTubeView(Tube sourceTube)
        {
            tubeObject = new GameObject("TestTube");
            tubeView = tubeObject.AddComponent<TubeView>();

            var palette = new ColorPalette();

            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);

            var liquidShader = Resources.Load<Shader>("Liquid");
            var liquidMat = new Material(liquidShader);

            var bodySprite = Resources.Load<Sprite>(TubeView.TubeBodySpritePath);
            var ringSprite = Resources.Load<Sprite>(TubeView.TubeRingSpritePath);
            var corkSprite = Resources.Load<Sprite>(TubeView.CorkSpritePath);
            var seatedCorkSprite = Resources.Load<Sprite>(TubeView.CorkSeatedSpritePath);

            tubeView.Initialize(0, sourceTube, palette, sprite, liquidMat,
                bodySprite, ringSprite, corkSprite, seatedCorkSprite);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ContainsPoint_AcceptsGlassBody()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            // Gövdenin ortası: her zaman tıklanabilir olmalıydı, hâlâ öyle.
            Assert.IsTrue(
                tubeView.ContainsPoint(new Vector3(0f, tubeView.Height * 0.5f)),
                "Cam gövdenin ortası tıklanabilir olmalı");
        }

        [UnityTest]
        public IEnumerator ContainsPoint_AcceptsCollarSides()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            // Halka gövdeden geniştir (1.2 > 0.82): x=±0.55 gövdenin tamamen
            // dışında, yalnız halkanın üstünde — eskiden tıklanamazdı.
            float ringY = tubeView.Height + RingCenterAboveTop;

            Assert.IsTrue(tubeView.ContainsPoint(new Vector3(0.55f, ringY)),
                "Halkanın sağ kanadı tıklanabilir olmalı");
            Assert.IsTrue(tubeView.ContainsPoint(new Vector3(-0.55f, ringY)),
                "Halkanın sol kanadı tıklanabilir olmalı");
        }

        [UnityTest]
        public IEnumerator ContainsPoint_RejectsOutsideVisuals()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            float ringY = tubeView.Height + RingCenterAboveTop;

            // Halka sprite sınırının köşesi: kutu içinde ama görünür silüet
            // (stadyum) dışında — tıklama görselden taşmamalı.
            Assert.IsFalse(
                tubeView.ContainsPoint(new Vector3(0.58f, ringY + 0.20f)),
                "Halka köşesindeki şeffaf bölge tıklanamaz olmalı");

            // Halkanın üstü (tıpa bölgesi): dahil değil.
            Assert.IsFalse(
                tubeView.ContainsPoint(
                    new Vector3(0f, ringY + RingHalfHeight + 0.07f)),
                "Halka üstü (tıpa bölgesi) tıklanamaz olmalı");

            // Gövde hizasında, gövdenin yanı: halka oraya inmez, cam da orada
            // değil — kaba kutu genişlese de SDF reddetmeli.
            Assert.IsFalse(
                tubeView.ContainsPoint(new Vector3(0.55f, tubeView.Height * 0.5f)),
                "Gövde hizasında tüpün yanı tıklanamaz olmalı");
        }
    }
}
