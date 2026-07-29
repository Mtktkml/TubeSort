using System.Collections;
using NUnit.Framework;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// StreamView'ın Show/Hide ve renk gönderimini test eder.
    /// </summary>
    public class StreamViewTests
    {
        private GameObject cameraObject;
        private GameObject streamObject;
        private StreamView streamView;

        private static readonly int ColorId = Shader.PropertyToID("_Color");

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
            if (streamObject != null) Object.Destroy(streamObject);
            if (cameraObject != null) Object.Destroy(cameraObject);

            yield return null;
        }

        private IEnumerator BuildStreamView()
        {
            streamObject = new GameObject("TestStream");
            streamView = streamObject.AddComponent<StreamView>();

            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);

            var shader = Resources.Load<Shader>("Stream");
            Assert.IsNotNull(shader, "Stream shader bulunamadı");
            var material = new Material(shader);

            streamView.Initialize(sprite, material);

            yield return null;
        }

        /// <summary>Akış artık iki parça: üst (kaynak önünde) + alt (hedef
        /// sandviçinde). İsimle bulunur; sortingOrder güvenilmez.</summary>
        private SpriteRenderer FindStreamRenderer(string name)
        {
            var renderers = streamObject.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var r in renderers)
            {
                if (r.gameObject.name == name)
                    return r;
            }

            Assert.Fail($"{name} renderer bulunamadı");
            return null;
        }

        /// <summary>Show'un yeni imzası: kaynak ağız, hedef delik, hedef yüzey.</summary>
        private void ShowSample(Color color)
        {
            streamView.Show(color,
                new Vector3(1f, 3f, 0f), new Vector3(1f, 1f, 0f), new Vector3(1f, 0f, 0f));
        }

        [UnityTest]
        public IEnumerator Show_EnablesRenderers()
        {
            yield return BuildStreamView();

            var top = FindStreamRenderer("StreamTop");
            var bottom = FindStreamRenderer("StreamBottom");
            Assert.IsFalse(top.enabled, "Başlangıçta üst parça kapalı olmalı");
            Assert.IsFalse(bottom.enabled, "Başlangıçta alt parça kapalı olmalı");

            ShowSample(Color.red);

            Assert.IsTrue(top.enabled, "Show sonrası üst parça açık olmalı");
            Assert.IsTrue(bottom.enabled, "Show sonrası alt parça açık olmalı");
        }

        [UnityTest]
        public IEnumerator Hide_DisablesRenderers()
        {
            yield return BuildStreamView();

            ShowSample(Color.red);
            streamView.Hide();

            Assert.IsFalse(FindStreamRenderer("StreamTop").enabled,
                "Hide sonrası üst parça kapalı olmalı");
            Assert.IsFalse(FindStreamRenderer("StreamBottom").enabled,
                "Hide sonrası alt parça kapalı olmalı");
        }

        [UnityTest]
        public IEnumerator Show_SendsColorToShader()
        {
            yield return BuildStreamView();

            ShowSample(Color.blue);

            var renderer = FindStreamRenderer("StreamTop");
            var props = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(props);
            Vector4 sentColor = props.GetVector(ColorId);

            // Linear uzayda mavi: gamma değil, linear olmalı.
            Assert.Greater(sentColor.z, 0.1f, "Mavi bileşen sıfıra yakın olmamalı");
            Assert.Less(sentColor.x, 0.1f, "Kırmızı bileşen düşük olmalı");
        }
    }
}
