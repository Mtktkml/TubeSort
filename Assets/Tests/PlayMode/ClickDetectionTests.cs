using System.Collections;
using NUnit.Framework;
using TubeSort.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TubeSort.Tests.PlayMode
{
    /// <summary>
    /// Tıklama zincirini parça parça doğrular. Fare girdisi olmadan çalışır:
    /// amaç "collider'lar doğru yerde mi" sorusunu input'tan ayırmak.
    /// </summary>
    public class ClickDetectionTests
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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Test sahnesi bomboş açılır; BoardView Camera.main beklediği için kurmalıyız.
            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            boardObject = new GameObject("BoardView");
            boardObject.AddComponent<BoardView>();

            // Start() çalışsın ve fizik dünyası collider'ları kaydetsin.
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        [Test]
        public void BoardView_CreatesOneColliderPerTube()
        {
            var views = boardObject.GetComponentsInChildren<TubeView>();
            Assert.AreEqual(6, views.Length, "Altı tüp görünümü oluşmalıydı");

            foreach (TubeView view in views)
            {
                var box = view.GetComponent<BoxCollider2D>();
                Assert.IsNotNull(box, $"Tüp {view.Index} collider'sız");
            }
        }

        [Test]
        public void OverlapPoint_HitsTubeAtItsOwnPosition()
        {
            var views = boardObject.GetComponentsInChildren<TubeView>();

            foreach (TubeView view in views)
            {
                // Tüpün gövdesinin ortası: dibin biraz yukarısı.
                Vector2 point = view.transform.position + Vector3.up * 1f;
                Collider2D hit = Physics2D.OverlapPoint(point);

                Assert.IsNotNull(hit, $"Tüp {view.Index} noktasında hiçbir collider yok");
                Assert.AreSame(view.gameObject, hit.gameObject,
                    $"Tüp {view.Index} yerine başka bir nesne yakalandı");
            }
        }
    }
}
