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

        private SpriteRenderer FindStreamRenderer()
        {
            var renderers = streamObject.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var r in renderers)
            {
                if (r.gameObject.name == "Stream")
                    return r;
            }

            Assert.Fail("Stream renderer bulunamadı");
            return null;
        }

        [UnityTest]
        public IEnumerator Show_EnablesRenderer()
        {
            yield return BuildStreamView();

            var renderer = FindStreamRenderer();
            Assert.IsFalse(renderer.enabled, "Başlangıçta kapalı olmalı");

            streamView.Show(Color.red, new Vector3(0f, 3f, 0f), new Vector3(1f, 0f, 0f));

            Assert.IsTrue(renderer.enabled, "Show sonrası açık olmalı");
        }

        [UnityTest]
        public IEnumerator Hide_DisablesRenderer()
        {
            yield return BuildStreamView();

            streamView.Show(Color.red, new Vector3(0f, 3f, 0f), new Vector3(1f, 0f, 0f));
            streamView.Hide();

            var renderer = FindStreamRenderer();
            Assert.IsFalse(renderer.enabled, "Hide sonrası kapalı olmalı");
        }

        [UnityTest]
        public IEnumerator Show_SendsColorToShader()
        {
            yield return BuildStreamView();

            streamView.Show(Color.blue, new Vector3(0f, 3f, 0f), new Vector3(1f, 0f, 0f));

            var renderer = FindStreamRenderer();
            var props = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(props);
            Vector4 sentColor = props.GetVector(ColorId);

            // Linear uzayda mavi: gamma değil, linear olmalı.
            Assert.Greater(sentColor.z, 0.1f, "Mavi bileşen sıfıra yakın olmamalı");
            Assert.Less(sentColor.x, 0.1f, "Kırmızı bileşen düşük olmalı");
        }
    }
}
