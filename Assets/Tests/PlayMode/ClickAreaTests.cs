using System.Collections;
using NUnit.Framework;
using TubeSort.Core;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Tüp tıklama alanı: cam gövde VE yaka tıklanabilir olmalı; ikisinin
    /// dışı (yaka sprite'ının şeffaf köşeleri, tıpa bölgesi, gövdenin yanı)
    /// tıklanamaz kalmalı. ContainsPoint doğrudan test edilir —
    /// BoardView.HandleClick kaba collider elemesinden sonra kararı buna
    /// bırakır, yani tıklanabilirliğin gerçek kaynağı bu fonksiyondur.
    /// </summary>
    public class ClickAreaTests
    {
        private GameObject cameraObject;
        private GameObject tubeObject;
        private TubeView tubeView;

        // Yaka geometrisi (TubeView'daki private sabitlerin aynası):
        // merkez y = gövde tepesi + Width·0.21; boyut 1.2 × (113/244).
        private const float CollarCenterAboveTop = TubeView.Width * 0.21f;
        private const float CollarHalfHeight = 113f / 244f * 0.5f;

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

            var tubeSprite = Resources.Load<Sprite>(TubeView.TubeSpritePath);
            var collarSprite = Resources.Load<Sprite>(TubeView.CollarSpritePath);
            var corkSprite = Resources.Load<Sprite>(TubeView.CorkSpritePath);
            var frontTop = TubeView.CreateCollarFrontTopSprite(collarSprite);
            var frontBottom = TubeView.CreateCollarFrontBottomSprite(collarSprite);

            tubeView.Initialize(0, sourceTube, palette, sprite, liquidMat,
                tubeSprite, collarSprite, frontTop, frontBottom, corkSprite);

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

            // Yaka gövdeden geniştir (1.2 > 0.8): x=±0.55 gövdenin tamamen
            // dışında, yalnız yakanın üstünde — eskiden tıklanamazdı.
            float collarY = tubeView.Height + CollarCenterAboveTop;

            Assert.IsTrue(tubeView.ContainsPoint(new Vector3(0.55f, collarY)),
                "Yakanın sağ kanadı tıklanabilir olmalı");
            Assert.IsTrue(tubeView.ContainsPoint(new Vector3(-0.55f, collarY)),
                "Yakanın sol kanadı tıklanabilir olmalı");
        }

        [UnityTest]
        public IEnumerator ContainsPoint_RejectsOutsideVisuals()
        {
            yield return BuildTubeView(new Tube(4, 0, 0));

            float collarY = tubeView.Height + CollarCenterAboveTop;

            // Yaka sprite sınırının köşesi: kutu içinde ama görünür silüet
            // (stadyum) dışında — tıklama görselden taşmamalı.
            Assert.IsFalse(
                tubeView.ContainsPoint(new Vector3(0.58f, collarY + 0.20f)),
                "Yaka köşesindeki şeffaf bölge tıklanamaz olmalı");

            // Yakanın üstü (tıpa bölgesi): dahil değil.
            Assert.IsFalse(
                tubeView.ContainsPoint(
                    new Vector3(0f, collarY + CollarHalfHeight + 0.07f)),
                "Yaka üstü (tıpa bölgesi) tıklanamaz olmalı");

            // Gövde hizasında, gövdenin yanı: yaka oraya inmez, cam da orada
            // değil — kaba kutu genişlese de SDF reddetmeli.
            Assert.IsFalse(
                tubeView.ContainsPoint(new Vector3(0.55f, tubeView.Height * 0.5f)),
                "Gövde hizasında tüpün yanı tıklanamaz olmalı");
        }
    }
}
