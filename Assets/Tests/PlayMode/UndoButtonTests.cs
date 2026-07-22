using System.Collections;
using NUnit.Framework;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Geri al butonunun kuruluşunu doğrular. Girdi simüle edilmez
    /// (ClickDetectionTests ile aynı yaklaşım): collider'ın varlığı ve
    /// konumu input'tan bağımsız test edilir.
    /// </summary>
    public class UndoButtonTests
    {
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

        private IEnumerator BuildBoard(float orthographicSize)
        {
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.aspect = 0.5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            boardObject = new GameObject("BoardView");
            boardObject.AddComponent<BoardView>();

            yield return new WaitForFixedUpdate();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Button_ExistsAndItsColliderIsHittable()
        {
            yield return BuildBoard(orthographicSize: 8f);

            var button = Object.FindAnyObjectByType<UndoButtonView>();
            Assert.IsNotNull(button, "Geri al butonu kurulmamış");

            var collider = button.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(collider, "Butonun collider'ı yok");

            Collider2D hit = Physics2D.OverlapPoint(collider.bounds.center);
            Assert.IsNotNull(hit, "Buton noktasında hiçbir collider yok");
            Assert.AreSame(button.gameObject, hit.gameObject,
                "Buton yerine başka bir nesne yakalandı");
        }

        [UnityTest]
        public IEnumerator Button_StaysInsideCameraView()
        {
            yield return BuildBoard(orthographicSize: 8f);

            var button = Object.FindAnyObjectByType<UndoButtonView>();
            Vector3 pos = button.transform.position;

            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            float halfSize = UndoButtonView.Size * 0.5f;

            Assert.GreaterOrEqual(pos.x - halfSize, -halfWidth, "Buton soldan taşıyor");
            Assert.LessOrEqual(pos.y + halfSize, halfHeight, "Buton yukarıdan taşıyor");
            Assert.Less(pos.x, 0f, "Buton sol üst köşede olmalı");
            Assert.Greater(pos.y, 0f, "Buton sol üst köşede olmalı");
        }

        [UnityTest]
        public IEnumerator Button_KeepsItsSize_WhenBoardIsScaledDown()
        {
            // Küçük görüş alanı: tahta sığmak için ölçeklenir, buton sabit kalmalı.
            yield return BuildBoard(orthographicSize: 1.5f);

            Assert.Less(boardObject.transform.localScale.x, 1f,
                "Test kurgusu: tahta ölçeklenmiş olmalı");

            var button = Object.FindAnyObjectByType<UndoButtonView>();
            Assert.AreEqual(1f, button.transform.lossyScale.x, 0.0001f,
                "Buton tahtayla birlikte ölçeklenmemeli");
        }
    }
}
